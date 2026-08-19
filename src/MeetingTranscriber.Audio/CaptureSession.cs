using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The two sources of one recording, open at the same time: what the machine is playing on
/// channel 0 and what the chosen microphone hears on channel 1.
/// </summary>
/// <remarks>
/// <para>
/// Both or neither. Half a meeting is not a smaller meeting — it is a recording that looks
/// complete and has lost one side of the conversation — so a session that cannot open its second
/// source closes the first one and says so.
/// </para>
/// <para>
/// Channel 0 is either everything the machine plays or one program and what it started, and that
/// is the only thing the two shapes of recording disagree about. Which of the two it is is
/// somebody's, start to finish: a program Windows will not follow stops the recording rather than
/// quietly opening the whole machine, and a program that turns out to be silent is moved to the
/// whole machine only when somebody says to. Recording every notification and every other
/// application on the machine is not something this decides on a person's behalf.
/// </para>
/// </remarks>
public sealed class CaptureSession : IDisposable
{
    private readonly CaptureSource[] sources;
    private readonly RecordingPause pause;
    private readonly DirectoryInfo folder;
    private readonly AudioDevice playback;

    /// <summary>
    /// One caller's, and it is what keeps a recording from being moved while it is being stopped.
    /// Pausing does not take it — that one crosses threads by design and is one write — and neither
    /// does anything the devices do: what this covers is the two things a person can press.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// What is played into the playback endpoint so that it keeps handing packets over, or nothing
    /// while channel 0 is following a program. Not readonly, because moving channel 0 to the whole
    /// machine is where a recording that never needed it starts needing it.
    /// </summary>
    private SilentPlayback? silence;

    private bool over;

    private CaptureSession(
        CaptureSource[] sources,
        SilentPlayback? silence,
        SpoolCard card,
        RecordingPause pause,
        DirectoryInfo folder,
        AudioDevice playback)
    {
        this.sources = sources;
        this.silence = silence;
        this.pause = pause;
        this.folder = folder;
        this.playback = playback;
        Card = card;
    }

    /// <summary>Both sources, in channel order.</summary>
    public IReadOnlyList<CaptureSource> Sources => sources;

    /// <summary>
    /// What this recording's folder says about it, as it was written when the devices opened.
    /// </summary>
    public SpoolCard Card { get; }

    /// <summary>
    /// How channel 0 is being obtained now, read off what it is actually listening to. The card
    /// says how it was obtained when the devices opened, and the two differ from the moment
    /// somebody moves it — see <see cref="RecordTheWholeMachine"/>.
    /// </summary>
    public CaptureMode Mode => On(AudioChannel.Loopback).Listening is CaptureTarget.Program
        ? CaptureMode.ProcessLoopback
        : CaptureMode.FullLoopback;

    /// <summary>
    /// Opens both streams into <paramref name="folder"/>, one spool each, writes the card saying
    /// what the recording is, and starts recording.
    /// </summary>
    /// <remarks>
    /// The card is written once both devices are open, because until then nothing knows what each
    /// channel really got. A recording whose folder cannot be made to say
    /// what it is does not go on: what it would leave behind is blocks nobody can attach to a
    /// meeting, which is the case this whole path exists to prevent.
    /// </remarks>
    /// <param name="folder">Where the two spools and the card go. Made if it is not there.</param>
    /// <param name="meetingId">The meeting being recorded, fixed before any of this is called.</param>
    /// <param name="playback">
    /// The endpoint channel 0 listens to when it is not following a program, and the one it is
    /// moved onto if somebody later chooses the whole machine.
    /// </param>
    /// <param name="microphone">The device channel 1 listens to.</param>
    /// <param name="follow">
    /// The program channel 0 should follow, or nothing to record the whole machine.
    /// </param>
    public static CaptureSession Start(
        DirectoryInfo folder,
        Guid meetingId,
        AudioDevice playback,
        AudioDevice microphone,
        AudioProcess? follow = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(microphone);

        folder.Create();
        BlockSpool.EnsureNothingRecordedIn(folder);

        SilentPlayback? silence = null;
        var opened = new List<CaptureSource>();

        // One for the recording, handed to both sources: pausing has to be a single write, or one
        // channel records the room for as long as it takes to set the second flag.
        var pause = new RecordingPause();

        CaptureSource Open(AudioChannel channel, CaptureTarget target)
        {
            // Only ever for the whole machine's loopback, and before it opens, so the endpoint is
            // already handing packets over by the time anything is listening for them. A program's
            // audio needs none of it: silence played by this process is not that program's.
            if (channel == AudioChannel.Loopback && target is CaptureTarget.Endpoint)
            {
                silence ??= SilentPlayback.On(playback);
            }

            return CaptureSource.Open(channel, target, BlockSpool.FileFor(folder, channel), pause);
        }

        try
        {
            foreach (var (channel, target) in InChannelOrder(Others(follow, playback), microphone))
            {
                try
                {
                    opened.Add(Open(channel, target));
                }
                // A program Windows will not follow used to open the whole machine instead and note
                // it on the card, and that is the one thing this must never do on its own: what it
                // produces is a file carrying every notification and every other application on the
                // machine, which is a different recording from the one somebody asked for and the
                // kind of difference nobody notices until the meeting is over. So the recording does
                // not start, and the one thing added here is the sentence saying what the choice
                // costs — said once, rather than by each screen that has to say it.
                //
                // A device that never answered is not this and is not caught: there is nothing to
                // offer somebody whose machine has stopped answering about a device, and the thread
                // holding it cannot be stopped.
                catch (AudioCaptureException cannot)
                    when (target is CaptureTarget.Program && cannot is not AudioDeviceWedgedException)
                {
                    throw new AudioCaptureException(
                        $"{cannot.Message} Nothing was recorded. Recording the whole machine "
                        + "instead is somebody's choice and not this one's: it puts notifications "
                        + "and every other application into the file.",
                        cannot);
                }
            }
        }
        catch
        {
            // A session that cannot open both of its sources is not a half recording, so the
            // source that did open takes its file with it. That is the one place a spool is
            // erased, and it is erased because there is no recording — the other source never
            // started, and what this one caught is the moment before Windows refused.
            foreach (var source in opened)
            {
                source.Discard();
            }

            silence?.Dispose();
            throw;
        }

        var card = new SpoolCard(
            meetingId,
            Guid.NewGuid(),
            opened.Min(source => source.StartedAt),
            CapturedAudio.Profile,
            [.. opened
                .OrderBy(source => CapturedAudio.IndexOf(source.Channel))
                .Select(source => new SpooledSource(
                    source.Channel,
                    source.Listening.Name,
                    (source.Listening as CaptureTarget.Endpoint)?.Device.Id))]);

        try
        {
            SpoolManifest.Write(folder, card);
        }
        catch
        {
            // Both devices are recording by the time this runs, so what is on disk is a meeting
            // and not a failed attempt: it is let go of and left, and it comes back as a recording
            // with nothing saying what it is — a case recovery already has. Erasing it here would
            // be this product deleting audio nobody decided about.
            foreach (var source in opened)
            {
                source.LetGo();
            }

            silence?.Dispose();
            throw;
        }

        return new CaptureSession([.. opened], silence, card, pause, folder, playback);
    }

    /// <summary>
    /// Whether channel 0 has heard nothing at all since it opened, for long enough that following
    /// that program is what is wrong rather than nobody having spoken yet.
    /// </summary>
    /// <remarks>
    /// Asked rather than announced, and it goes on being true until somebody does something about
    /// it: a program that cannot be followed throws nothing and ends nothing, so there is no moment
    /// to raise and the only thing that says so is the level. What is done about it is
    /// <see cref="RecordTheWholeMachine"/>, and doing nothing about it is an answer too — a meeting
    /// where only the room is worth recording is a meeting this records.
    /// </remarks>
    public bool HeardNothingFromTheProgram()
    {
        var others = On(AudioChannel.Loopback);
        return SilentProgram.HeardNothing(others.Listening, others.Delivered, others.OpenFor);
    }

    /// <summary>
    /// Somebody choosing the whole machine's audio in place of the program channel 0 is following,
    /// while the meeting is still running. The recording goes on being one recording.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only way channel 0 ever moves. Nothing here reads a level and decides for itself: what
    /// this costs is every notification and every other application landing in a file somebody will
    /// send to a transcription service, and a recording is not allowed to take that on quietly
    /// because a meter looked wrong for ten seconds.
    /// </para>
    /// <para>
    /// The silence goes into the playback endpoint before the channel is moved onto it. An endpoint
    /// hands nothing over while nothing is playing into it, so the other order would move the
    /// recording onto a device that is not yet producing and open a hole at the seam.
    /// </para>
    /// <para>
    /// <b>Not on a thread somebody is looking at.</b> Three devices are dealt with here — one
    /// opened, one stopped, one let go of — and every one of them has its own deadline for a
    /// driver that does not answer. The meeting carries on being recorded throughout, because the
    /// devices doing it are not this thread; what a window would freeze is itself.
    /// </para>
    /// <para>
    /// What the folder then says is on the card until the move and beside it afterwards: the card
    /// is what was true when the devices opened and is never rewritten, and the change is one line
    /// appended to <see cref="SpoolChanges"/>. That line lands before the new device's audio does,
    /// so a machine dying in the middle of this leaves a folder that overstates what is in the file
    /// rather than one that hides it.
    /// </para>
    /// </remarks>
    public void RecordTheWholeMachine()
    {
        lock (gate)
        {
            if (over)
            {
                throw new AudioCaptureException(
                    "This recording has been stopped, so there is nothing left to move: what its "
                    + "channel 0 holds is what it recorded.");
            }

            var others = On(AudioChannel.Loopback);
            if (others.Listening is not CaptureTarget.Program program)
            {
                throw new AudioCaptureException(
                    $"The {AudioChannel.Loopback} channel is already recording "
                    + $"'{others.Listening.Name}', which is the whole machine's audio.");
            }

            // Nothing is playing into the endpoint yet and this is where that starts: the silence
            // goes in only for a channel 0 that is on the endpoint, and the guard above says this
            // one is on a program until the move below. A refusal puts it back, so the ask can be
            // made again and find the field as it is here.
            silence = SilentPlayback.On(playback);

            var destination = new CaptureTarget.Endpoint(playback);

            try
            {
                // The moment is read where the move happens rather than where somebody pressed.
                // What separates the two is three devices being dealt with, each with a deadline of
                // its own, and a folder saying the machine's audio began seconds before it did is a
                // folder that is wrong about the one thing it was written to be right about.
                others.MoveTo(destination, () => SpoolChanges.Append(
                    folder,
                    new SourceChanged(
                        UtcTimestamp.From(TimeProvider.System.GetUtcNow()),
                        AudioChannel.Loopback,
                        destination.Name,
                        playback.Id,
                        program.Name)));
            }
            catch
            {
                // Put back the way it was found. MoveTo leaves the channel where it was whatever
                // went wrong, so a refusal that kept the silence would leave this process playing
                // into a device the recording is not on for the rest of the meeting — and the ask
                // can be made again, which would then find it already playing and never start it.
                silence.Dispose();
                silence = null;

                throw;
            }
        }
    }

    /// <summary>
    /// Whether the meeting is paused, so both sources are spooling silence rather than what their
    /// devices are hearing.
    /// </summary>
    public bool IsPaused => pause.IsPaused;

    /// <summary>
    /// Pauses the meeting: neither device stops, and neither one's audio reaches the recording
    /// until <see cref="Resume"/>. Saying it twice is saying it once.
    /// </summary>
    /// <remarks>
    /// Both sources or neither, the way everything else here is — and here that is a property of
    /// there being one pause rather than of this method visiting two. One channel paused and the
    /// other still recording would be a stretch of the meeting with one side of the conversation
    /// on it, and unlike a source that failed to open, nothing downstream could tell that from a
    /// room where only one person was talking.
    /// </remarks>
    public void Pause() => pause.Pause();

    /// <summary>Lets both devices reach the recording again.</summary>
    public void Resume() => pause.Resume();

    /// <summary>The source feeding <paramref name="channel"/>.</summary>
    public CaptureSource On(AudioChannel channel) =>
        Array.Find(sources, source => source.Channel == channel)
        ?? throw new AudioContractException($"This capture has no {channel} source.");

    /// <summary>
    /// Stops both streams and finishes both spools. Every source is asked to stop before either is
    /// waited on, and every one of them is stopped even when another has something to say about
    /// how it ended — the other one's file is a recording somebody wants either way.
    /// </summary>
    public void Stop()
    {
        // Said before the first source is asked, and under the gate a move takes, so that a move
        // already underway finishes and one that has not started never does. What it stops is a
        // channel being moved onto a device while this is letting the other one go.
        lock (gate)
        {
            over = true;
        }

        var failures = new List<Exception>();

        // Both asked, then both waited on. The other way round leaves the second source recording
        // for however long the first one took to let go, which is a difference between the two
        // files that nothing in the meeting put there.
        foreach (var source in sources)
        {
            Collect(source.AskToStop, failures);
        }

        foreach (var source in sources)
        {
            Collect(source.Finish, failures);
        }

        if (failures.Count == 1)
        {
            throw failures[0] as AudioCaptureException
                ?? new AudioCaptureException(failures[0].Message, failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AudioCaptureException(
                string.Join(" ", failures.Select(failure => failure.Message)), failures[0]);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            over = true;
        }

        // Asked first, all of them, for the same reason Stop asks before it waits — and here it
        // matters more, because disposing is an exit path in its own right. A caller that never
        // reached Stop, or whose Stop threw on the first source, arrives with both devices still
        // recording; letting go of the first before the second has even been asked leaves the
        // second recording for however long the first takes to be given up on.
        foreach (var source in sources)
        {
            source.AskToStop();
        }

        foreach (var source in sources)
        {
            // Every source, whatever the one before it did with its last block: this is where the
            // handles are let go, and Stop is where a failure is reported.
            source.LetGo();
        }

        silence?.Dispose();
    }

    /// <summary>
    /// Runs one step of stopping and keeps what it threw, so that the step after it still runs.
    /// Broad on purpose: a source left recording because another one failed is the outcome this
    /// exists to prevent, and nothing is lost — what was caught is what gets thrown.
    /// </summary>
    private static void Collect(Action step, List<Exception> failures)
    {
        try
        {
            step();
        }
        catch (Exception failed)
        {
            failures.Add(failed);
        }
    }

    /// <summary>What channel 0 was asked to listen to, before anything says whether it can.</summary>
    private static CaptureTarget Others(AudioProcess? follow, AudioDevice playback) =>
        follow is null ? new CaptureTarget.Endpoint(playback) : new CaptureTarget.Program(follow);

    /// <summary>
    /// What feeds which channel, ordered by the contract rather than by the order these two are
    /// written in, so channel 0 is opened first because it is channel 0.
    /// </summary>
    private static IEnumerable<(AudioChannel Channel, CaptureTarget Target)> InChannelOrder(
        CaptureTarget others,
        AudioDevice microphone) =>
        new[]
        {
            (Channel: AudioChannel.Microphone, Target: (CaptureTarget)new CaptureTarget.Endpoint(microphone)),
            (Channel: AudioChannel.Loopback, Target: others),
        }.OrderBy(source => CapturedAudio.IndexOf(source.Channel));
}
