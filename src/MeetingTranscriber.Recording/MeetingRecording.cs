using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// One meeting being recorded: record, pause, resume, stop. What a record button presses.
/// </summary>
/// <remarks>
/// <para>
/// As thin as it can be, deliberately. Every rule it composes is in <see cref="MeetingRecordings"/>
/// or in the audio engine, both of which can be exercised on a machine with no microphone; what is
/// here and nowhere else is the ordering, and the ordering is the one thing that needs a device to
/// see it go wrong. A build agent has neither device, so anything of substance living here would
/// be something nothing ever runs.
/// </para>
/// <para>
/// It is not a state machine and does not need to be. Pausing is a question anyone can ask the
/// devices at any moment, and the two states a recording has — going and over — are the object
/// existing and having been stopped.
/// </para>
/// <para>
/// <b>It holds the corpus it was handed for as long as the meeting lasts, and does not own it.</b>
/// Whoever starts a recording keeps that context open until they stop it — a window that opened one
/// inside its record handler and let it go would have a disposed context by the time somebody
/// pressed stop, an hour later. That is a real constraint and not a preference, and it is the first
/// thing a screen built on this has to decide about: an hour is a long time for one unit of work,
/// and the alternative shape — this holding what it needs to reopen a short one per step — is a
/// change to make when there is a window to make it against rather than guessed at now.
/// </para>
/// </remarks>
public sealed class MeetingRecording : IDisposable
{
    private readonly CorpusDbContext corpus;
    private readonly CaptureSession session;
    private bool stopped;

    private MeetingRecording(CorpusDbContext corpus, CaptureSession session, PreparedRecording prepared)
    {
        this.corpus = corpus;
        this.session = session;
        Prepared = prepared;
    }

    /// <summary>The meeting and the folder it is being recorded into.</summary>
    public PreparedRecording Prepared { get; }

    /// <summary>The meeting being recorded, settled before any audio of it existed.</summary>
    public Guid MeetingId => Prepared.MeetingId;

    /// <summary>What the recording wrote about itself when its devices opened.</summary>
    public SpoolCard Card => session.Card;

    /// <summary>The two sources, for a meter to read while the meeting runs.</summary>
    public IReadOnlyList<CaptureSource> Sources => session.Sources;

    /// <summary>What channel 0 is listening to now, which is what it opened with until it is moved.</summary>
    public CaptureMode Mode => session.Mode;

    /// <summary>Whether the meeting is paused.</summary>
    public bool IsPaused => session.IsPaused;

    /// <summary>
    /// Presses record: the meeting exists in <paramref name="corpus"/> and has its folder before
    /// either device is opened, and the run is written down as soon as they are.
    /// </summary>
    /// <remarks>
    /// The corpus comes first and the devices second, which is the ordering this whole type is for.
    /// Doing it the other way round would leave audio on disk belonging to no meeting for as long
    /// as it took to write a row — and that window is exactly when a person is pressing a button
    /// on a machine that might be about to be closed.
    /// </remarks>
    /// <param name="corpus">The corpus to record into.</param>
    /// <param name="language">What the meeting is expected to be spoken in.</param>
    /// <param name="microphone">The device channel 1 listens to.</param>
    /// <param name="follow">The program channel 0 should follow, or nothing for the whole machine.</param>
    /// <param name="now">When record was pressed.</param>
    public static MeetingRecording Start(
        CorpusDbContext corpus,
        string language,
        AudioDevice microphone,
        AudioProcess? follow,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var prepared = MeetingRecordings.Open(corpus, language, now);

        // Nothing catches this. A recording that could not open its devices leaves the meeting row
        // where it is: the row is the only thing that would say the attempt happened at all, and
        // taking it back here would be this type deleting from the corpus on a path where nothing
        // went wrong with the corpus. What it leaves is a meeting with no audio, which the meeting
        // list and recovery both already have to be able to show.
        var session = CaptureSession.Start(prepared.Spool, prepared.MeetingId, microphone, follow);

        try
        {
            MeetingRecordings.Began(corpus, session.Card);
        }
        catch
        {
            // Both devices are recording by now, so the meeting is happening: it is not thrown away
            // over the row that describes the run, which the card beside the blocks can be used to
            // write again. Letting go stops the devices rather than erasing what they caught.
            //
            // Whatever letting go throws is swallowed, and only here: what the caller has to hear is
            // why the recording could not be started, and a device that then refused to close would
            // otherwise replace that with a message about the device. The devices are released
            // either way, which is the part that matters, and a wedged one is already something
            // CaptureSession reports through its own deadline rather than through this path.
            try
            {
                session.Dispose();
            }
            catch (AudioCaptureException)
            {
            }

            throw;
        }

        return new MeetingRecording(corpus, session, prepared);
    }

    /// <summary>
    /// Whether channel 0 has heard nothing at all since it opened, for long enough that the program
    /// it is following is the wrong one. Nothing is done about it until somebody does.
    /// </summary>
    public bool HeardNothingFromTheProgram() => session.HeardNothingFromTheProgram();

    /// <summary>
    /// Somebody choosing the whole machine's audio in place of the program channel 0 is following.
    /// The meeting goes on, and every notification and other application is in the file from here.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Stop"/>, not on a thread somebody is looking at: it opens one device and
    /// stops another, each with its own deadline for a driver that does not answer. Unlike
    /// <see cref="Stop"/>, the meeting is still being recorded the whole time it runs.
    /// </remarks>
    public void RecordTheWholeMachine() => session.RecordTheWholeMachine();

    /// <summary>
    /// Pauses the meeting. The clock keeps running: what the pause costs the recording is silence
    /// of exactly the length it lasted, and never a shorter meeting.
    /// </summary>
    public void Pause() => session.Pause();

    /// <summary>Resumes it.</summary>
    public void Resume() => session.Resume();

    /// <summary>
    /// Stops the meeting and finishes it: the devices are let go of, the spools become the
    /// meeting's audio and its length, and nothing is set going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It does all of that before it comes back, and for a long meeting that is minutes of work —
    /// the spools are poured onto the timeline, the recording is read back to check it, and it is
    /// hashed on the way into the corpus. <b>Do not call this on a thread somebody is looking at.</b>
    /// A window doing so freezes for the length of it, and the thing it is freezing over is not
    /// stopping the devices, which is quick, but making the meeting, which is not.
    /// </para>
    /// <para>
    /// The devices are released before any of that, so nothing is being recorded while it runs and
    /// somebody who closed the application in the middle has a spool that recovery already handles.
    /// If finishing does throw, the recording is over and the meeting can be finished again from
    /// the blocks with <see cref="MeetingRecordings.Finish"/> — this object is done either way,
    /// which is why the flag goes up before the work rather than after it.
    /// </para>
    /// </remarks>
    /// <param name="now">When stop was pressed.</param>
    /// <param name="told">
    /// Whoever is watching the save, when anybody is. The first report goes out before the devices
    /// are touched, because letting them go is the first step of the save and a screen that only
    /// heard once they were gone would have nothing to show for the one stretch that can wait on a
    /// device. It reads nothing and opens nothing, so a stop nobody is watching and a stop somebody
    /// is do the same work in the same order — including when a device refuses to close.
    /// </param>
    public FinishedRecording Stop(UtcTimestamp now, IProgress<SavingWork>? told = null)
    {
        if (stopped)
        {
            throw new RecordingException(
                $"Meeting {MeetingId} has already been stopped. Finishing it again is "
                + "MeetingRecordings.Finish, which reads the blocks and needs no device.");
        }

        told?.Report(SavingWork.LettingTheSourcesGo);

        session.Stop();

        // The devices are let go of before the spools are read: a recording still being written is
        // a file this build refuses to read, which is the same refusal that keeps somebody from
        // being told a meeting still going on had ended.
        session.Dispose();
        stopped = true;

        return MeetingRecordings.Finish(corpus, MeetingId, now, told);
    }

    /// <summary>
    /// Lets go of the devices without finishing anything. What is on disk stays there, and comes
    /// back as a recording somebody has to decide about — which is what happens to a meeting the
    /// application was closed in the middle of.
    /// </summary>
    public void Dispose()
    {
        if (stopped)
        {
            return;
        }

        session.Dispose();
        stopped = true;
    }
}
