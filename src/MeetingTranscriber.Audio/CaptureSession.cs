using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

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
/// <para>
/// Neither of the two is a device. Both are the same process loopback under two modes, so nothing
/// here opens a playback stream, names an endpoint for channel 0 or has anything to put back if
/// one refuses — which is why a session takes a microphone and no second device.
/// </para>
/// </remarks>
public sealed class CaptureSession : IDisposable
{
    private readonly CaptureSource[] sources;
    private readonly RecordingPause pause;
    private readonly DirectoryInfo folder;

    /// <summary>
    /// One caller's, and it is what keeps a recording from being moved while it is being stopped.
    /// Pausing does not take it — that one crosses threads by design and is one write — and neither
    /// does anything the devices do: what this covers is the two things a person can press.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// How long the thread that follows a device Windows took away sleeps between looks.
    /// </summary>
    /// <remarks>
    /// Not a measurement: what it bounds is how much of the meeting is missing off a channel
    /// between the device going away and the recording being back on one, and two seconds is where
    /// a person would put that rather than where a run put it. A tick with both sources recording
    /// takes the gate, looks at two fields and sleeps again — <see cref="FollowWhateverReplacedIt"/>
    /// comes back before it asks the audio stack anything. It is once a source has ended that
    /// every tick enumerates the machine's endpoints, and what bounds the moving onto them is
    /// <see cref="ReplacedDevice.IsWorthTrying"/> rather than this number.
    /// </remarks>
    private static readonly TimeSpan Looks = TimeSpan.FromSeconds(2);

    /// <summary>What ends the thread that follows, and what it sleeps on so it ends at once.</summary>
    private readonly CancellationTokenSource stopping = new();

    /// <summary>
    /// What the thread that follows last moved a channel onto, and how many blocks that channel had
    /// taken when it did — which together say whether opening the same endpoint again is worth
    /// doing. See <see cref="ReplacedDevice.IsWorthTrying"/>, which is the rule; this is only what
    /// it is asked about.
    /// </summary>
    private readonly Dictionary<AudioChannel, (string Device, long Packets)> followed = [];

    private Thread? following;
    private bool followingCameBack;
    private bool over;

    private CaptureSession(
        CaptureSource[] sources,
        SpoolCard card,
        RecordingPause pause,
        DirectoryInfo folder)
    {
        this.sources = sources;
        this.pause = pause;
        this.folder = folder;
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
    public CaptureMode Mode => ObtainedAs(On(AudioChannel.Loopback).Listening);

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
    /// <param name="microphone">The device channel 1 listens to.</param>
    /// <param name="follow">
    /// The program channel 0 should follow, or nothing to record the whole machine.
    /// </param>
    public static CaptureSession Start(
        DirectoryInfo folder,
        Guid meetingId,
        AudioDevice microphone,
        AudioProcess? follow = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(microphone);

        folder.Create();
        BlockSpool.EnsureNothingRecordedIn(folder);

        var opened = new List<CaptureSource>();

        // One for the recording, handed to both sources: pausing has to be a single write, or one
        // channel records the room for as long as it takes to set the second flag.
        var pause = new RecordingPause();

        try
        {
            foreach (var target in InChannelOrder(Others(follow), microphone))
            {
                try
                {
                    opened.Add(CaptureSource.Open(
                        target, BlockSpool.FileFor(folder, target.Channel), pause));
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

            throw;
        }

        var card = new SpoolCard(
            meetingId,
            Guid.NewGuid(),
            opened.Min(source => source.StartedAt),
            CapturedAudio.Profile,
            ObtainedAs(opened.Single(source => source.Channel == AudioChannel.Loopback).Listening),
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

            throw;
        }

        var session = new CaptureSession([.. opened], card, pause, folder);
        session.Follow();
        return session;
    }

    /// <summary>
    /// Somebody choosing another microphone for channel 1 while the meeting is running. The
    /// recording goes on being one recording, with everything the device that is leaving caught
    /// still in the file and everything the one taking over catches behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the two devices number their frames in has nothing in common, so this is a stretch and
    /// not a continuation: the channel comes to name two devices over one meeting, and what stands
    /// between them is the audio nobody was handed while the changeover happened, recorded as the
    /// gap it really was. <see cref="CaptureSource.MoveTo"/> is where that is decided.
    /// </para>
    /// <para>
    /// <b>Not on a thread somebody is looking at</b>, for the reason
    /// <see cref="RecordTheWholeMachine"/> is not: two devices are dealt with, each with its own
    /// deadline for a driver that does not answer.
    /// </para>
    /// </remarks>
    /// <param name="microphone">The endpoint channel 1 listens to from here on.</param>
    public void RecordFrom(AudioDevice microphone)
    {
        ArgumentNullException.ThrowIfNull(microphone);

        lock (gate)
        {
            EnsureRunning(AudioChannel.Microphone);

            var mine = On(AudioChannel.Microphone);

            // The same device again is refused only while it is still recording. A channel whose
            // stream died and whose endpoint Windows still names is the case this must not refuse:
            // a driver that reset is the same id and a different open, and refusing it would leave
            // the meeting with no microphone for the sake of comparing two strings.
            if (!mine.HasEnded
                && mine.Listening is CaptureTarget.Endpoint on
                && on.Device.Id == microphone.Id)
            {
                throw new AudioCaptureException(
                    $"The {AudioChannel.Microphone} channel is already recording "
                    + $"'{microphone.Name}'.");
            }

            Move(AudioChannel.Microphone, new CaptureTarget.Endpoint(microphone), microphone.Id);
        }
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
    /// <b>Not on a thread somebody is looking at.</b> Two devices are dealt with here — one opened
    /// and one stopped and let go of — and each has its own deadline for a driver that does not
    /// answer. The meeting carries on being recorded throughout, because the devices doing it are
    /// not this thread; what a window would freeze is itself.
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
            EnsureRunning(AudioChannel.Loopback);

            var others = On(AudioChannel.Loopback);
            if (others.Listening is not CaptureTarget.Program)
            {
                throw new AudioCaptureException(
                    $"The {AudioChannel.Loopback} channel is already recording "
                    + $"'{others.Listening.Name}', which is the whole machine's audio.");
            }

            Move(AudioChannel.Loopback, new CaptureTarget.TheWholeMachine(), deviceId: null);
        }
    }

    /// <summary>
    /// Moves one channel onto <paramref name="destination"/> and writes the move down, which is the
    /// whole of what every move here is made of.
    /// </summary>
    /// <remarks>
    /// The moment is read where the move happens rather than where somebody pressed or where a
    /// device was noticed gone. What separates the two is two devices being dealt with, each with a
    /// deadline of its own, and a folder saying a channel changed seconds before it did is a folder
    /// that is wrong about the one thing it was written to be right about.
    /// <para>
    /// Nothing is caught. <see cref="CaptureSource.MoveTo"/> leaves the channel exactly where it
    /// was whatever went wrong, and there is nothing else here to put back: a move opens one device
    /// and starts nothing else, so a refusal is a recording still on what it was on and an ask that
    /// can be made again.
    /// </para>
    /// </remarks>
    private void Move(AudioChannel channel, CaptureTarget destination, string? deviceId)
    {
        var source = On(channel);
        var was = source.Listening.Name;

        source.MoveTo(destination, () => SpoolChanges.Append(
            folder,
            new SourceChanged(
                UtcTimestamp.From(TimeProvider.System.GetUtcNow()),
                channel,
                destination.Name,
                was,
                deviceId)));
    }

    /// <summary>Refuses a move onto a recording that has already been stopped.</summary>
    private void EnsureRunning(AudioChannel channel)
    {
        if (over)
        {
            throw new AudioCaptureException(
                "This recording has been stopped, so there is nothing left to move: what its "
                + $"{channel} channel holds is what it recorded.");
        }
    }

    /// <summary>
    /// Starts the thread that follows Windows when a device this recording is on is taken away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than in a screen's tick, because a meeting recorded at a prompt is as much a
    /// meeting as one recorded in the application: a microphone somebody unplugged has to be
    /// followed by whoever is doing the recording, and that is this. A background thread, so a
    /// process that is being torn down is not held open by it, and one thread for the session
    /// rather than one per source — what it does is a move, and a move takes the gate anyway.
    /// </para>
    /// <para>
    /// Started once both devices are open and the folder has its card, so there is nothing it can
    /// find half built.
    /// </para>
    /// </remarks>
    private void Follow()
    {
        following = new Thread(Watching)
        {
            IsBackground = true,
            Name = "capture follows the device",
        };

        following.Start();
    }

    /// <summary>Looks, moves, and never lets either take the recording down.</summary>
    /// <remarks>
    /// Every failure is swallowed and looked at again, and this is one of the few places that is
    /// right: there is nobody to throw to. A machine that will not open its replacement endpoint,
    /// a folder that cannot be written, a device that answers a second later — each of them leaves
    /// the channel exactly where it was, which is stopped, and what says so is the reading a screen
    /// already shows. Throwing from here would end the process and the meeting with it, over a
    /// channel that was already silent.
    /// </remarks>
    private void Watching()
    {
        while (!stopping.Token.WaitHandle.WaitOne(Looks))
        {
            try
            {
                FollowWhateverReplacedIt();
            }
            catch (Exception left)
            {
                // Every one of them, and broad on purpose: this is the top of a thread, so what is
                // not caught here ends the process and the meeting with it — which is the outcome
                // the summary says this exists to prevent, and it would be reached by exactly the
                // machine this feature is for. Windows refusing an endpoint another application has
                // in exclusive mode arrives as a COMException and is the ordinary way in.
                _ = left;
            }
        }
    }

    /// <summary>
    /// Moves a channel whose device went away onto whatever Windows now names in its place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which sources those are is <see cref="ReplacedDevice"/>'s and not this method's, because it
    /// is the boundary between two opposite promises and it is decided here on a thread no probe
    /// reaches.
    /// </para>
    /// <para>
    /// The machine is asked outside the gate and the move is made inside it. Asking is a
    /// synchronous call into the audio stack with no deadline of its own, and the gate is what
    /// somebody pressing stop is waiting on — so what is held while an audio service is thinking is
    /// nothing at all.
    /// </para>
    /// </remarks>
    private void FollowWhateverReplacedIt()
    {
        CaptureSource? waiting;
        lock (gate)
        {
            waiting = over ? null : Array.Find(sources, Went);
        }

        if (waiting is null)
        {
            return;
        }

        var replacement = AudioDevices.Choose(AudioDevices.Microphones(), wanted: null);

        lock (gate)
        {
            // Asked again with the gate held. Between the two the recording may have been stopped,
            // or somebody may have chosen a microphone themselves — and a channel that is recording
            // again is not one to move off whatever it is now on.
            if (over || !Went(waiting) || !WorthTrying(waiting, replacement))
            {
                return;
            }

            // Written down before the move rather than after it, so that a move which throws still
            // counts as having been tried: what would otherwise happen is the same endpoint being
            // opened again every two seconds for the rest of the meeting.
            followed[waiting.Channel] = (replacement.Id, waiting.Packets.Packets);
            Move(waiting.Channel, new CaptureTarget.Endpoint(replacement), replacement.Id);
        }
    }

    /// <summary>Whether this source's device went away and is one to follow onto another.</summary>
    /// <remarks>
    /// Asked of <see cref="ReplacedDevice"/> rather than decided here: which channel may move on
    /// its own is the one decision on this thread worth holding a build over, and nothing on this
    /// thread can be run on a machine with no devices.
    /// </remarks>
    private static bool Went(CaptureSource source) =>
        ReplacedDevice.IsFollowed(source.Listening, source.HasEnded);

    /// <summary>
    /// Whether opening <paramref name="replacement"/> for <paramref name="source"/> is worth doing,
    /// given what following last moved it onto and what that brought.
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="ReplacedDevice.IsWorthTrying"/>'s; what is here is the two numbers it
    /// is asked about, which are the ones only a running source has. A move that brought nothing is
    /// one where the block count has not moved since it was made.
    /// </remarks>
    private bool WorthTrying(CaptureSource source, AudioDevice replacement) =>
        !followed.TryGetValue(source.Channel, out var last)
        || ReplacedDevice.IsWorthTrying(
            replacement, last.Device, broughtNothing: source.Packets.Packets == last.Packets);

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

        Stopped();

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

        Stopped();

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

        // Last, and only for a thread that really came back. What it sleeps on is what would be
        // let go of here, and a thread given up on is one that may still be about to read it —
        // disposing it under that thread throws where nothing is catching, which is the process and
        // the meeting rather than a handle held to the end of it.
        if (followingCameBack)
        {
            stopping.Dispose();
        }
    }

    /// <summary>
    /// Ends the thread that follows and waits for it, so that nothing opens a device while the
    /// sources are being let go of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What keeps a look that is still running from touching a source is not this wait: it is the
    /// gate. <c>over</c> is set under it before this runs, so a move already underway finished
    /// first and one that has not started never will — the same guarantee somebody pressing stop
    /// mid move already had.
    /// </para>
    /// <para>
    /// So the wait is bounded, and it is bounded for the reason every other wait on a device in
    /// this file is: the thread can be inside a synchronous call into the audio stack, and a
    /// machine whose audio service has wedged after an unplug is exactly the machine this feature
    /// exists for. Waiting for it without a deadline would make stopping a meeting wait on the
    /// thing that broke. What being given up on costs is one background thread left sleeping and
    /// the handle it sleeps on kept, which is the same answer a device given up on gets.
    /// </para>
    /// </remarks>
    private void Stopped()
    {
        if (following is null)
        {
            return;
        }

        stopping.Cancel();
        followingCameBack = following.Join(CaptureLoop.StopsWithin);
        following = null;
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
    private static CaptureTarget Others(AudioProcess? follow) =>
        follow is null ? new CaptureTarget.TheWholeMachine() : new CaptureTarget.Program(follow);

    /// <summary>
    /// How channel 0 is being obtained, asked of the thing it is listening to — the one type that
    /// knows the ways in. Asked both while the meeting runs and when the card is written, so a
    /// recording cannot be described one way in its folder and another way on a screen.
    /// </summary>
    /// <remarks>
    /// Refused rather than defaulted. A channel 0 listening to something that is no way of
    /// obtaining channel 0 is a recording nothing could describe, and the answer that would be
    /// invented here is the one nobody would go looking for.
    /// </remarks>
    private static CaptureMode ObtainedAs(CaptureTarget others) =>
        others.Mode ?? throw new AudioContractException(
            $"The {AudioChannel.Loopback} channel is listening to '{others.Name}', which is not "
            + "something channel 0 is ever obtained as.");

    /// <summary>
    /// What feeds each channel, ordered by the contract rather than by the order these two are
    /// written in, so channel 0 is opened first because it is channel 0. Which channel each one
    /// feeds is its own and is not paired up here — a pairing is a second answer, and the day it
    /// disagreed with the stream a source would be recording one channel and labelled the other.
    /// </summary>
    private static IEnumerable<CaptureTarget> InChannelOrder(
        CaptureTarget others,
        AudioDevice microphone) =>
        new CaptureTarget[] { new CaptureTarget.Endpoint(microphone), others }
            .OrderBy(target => CapturedAudio.IndexOf(target.Channel));
}
