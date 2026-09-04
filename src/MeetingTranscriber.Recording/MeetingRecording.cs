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
            LetGoOf(session);
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
    /// Somebody asking for the microphone to be opened again, after its stream ended by itself. The
    /// meeting goes on either way — what this can win back is the channel, and what it cannot is
    /// the stretch between.
    /// </summary>
    /// <remarks>
    /// Like <see cref="RecordTheWholeMachine"/>, not on a thread somebody is looking at, and the
    /// meeting is being recorded on channel 0 the whole time it runs.
    /// </remarks>
    public void OpenTheMicrophoneAgain() => session.OpenTheMicrophoneAgain();

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
    /// They are released <b>however the stop itself went</b>, and with them the block files and the
    /// mark over the folder, because <see cref="CaptureSession.Dispose"/> is the only thing that
    /// lets any of the three go. A session nobody disposes holds all three until this process ends,
    /// and the one that costs somebody something before then is the blocks: a spool still open is
    /// one <see cref="MeetingRecordings.Finish"/> cannot read, so the meeting the paragraph below
    /// says can be finished again could not be, until the application was restarted.
    /// </para>
    /// <para>
    /// If finishing throws, the recording is over and the meeting can be finished again from the
    /// blocks with <see cref="MeetingRecordings.Finish"/>. If <b>stopping</b> throws, that is true
    /// of a stream that ended by itself and not of one that was given up on: that one keeps its
    /// block file open while a thread may still be writing through it, which is
    /// <see cref="CaptureSource.Dispose"/>'s rule and not this method's to break. Either way this
    /// object is done, which is why the flag goes up before the work rather than after it.
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

        try
        {
            session.Stop();
        }
        catch
        {
            // The recording is over whatever the devices did. The flag goes up first so a second
            // stop meets the sentence about MeetingRecordings.Finish rather than re-entering a
            // session whose sources are already finished and let go, and so that this object is
            // done even if letting go were ever to throw its way out of here.
            stopped = true;
            LetGoOf(session);
            throw;
        }

        // The devices are let go of before the spools are read: a recording still being written is
        // a file this build refuses to read, which is the same refusal that keeps somebody from
        // being told a meeting still going on had ended. Not folded into a finally around the stop:
        // a failure to let go on the way out of a stop that worked is the caller's own news, and
        // there is nothing above it for it to drown out.
        //
        // The flag goes up before it for the same reason it goes up first in the catch. Letting go
        // can throw here too — the same IOException out of the mark's own handle LetGoOf is written
        // around — and by then the sources are finished and the recording is over whether or not
        // the last handle closed. Set after the call, that throw would leave this object saying it
        // was still going, and a second stop would re-enter a session whose sources are already
        // finished rather than meeting the sentence about MeetingRecordings.Finish.
        stopped = true;
        session.Dispose();

        return MeetingRecordings.Finish(corpus, MeetingId, now, told);
    }

    /// <summary>
    /// Lets go of the devices without finishing anything. What is on disk stays there, and comes
    /// back as a recording somebody has to decide about — which is what happens to a meeting the
    /// application was closed in the middle of.
    /// </summary>
    /// <remarks>
    /// The flag goes up before the work, as it does in <see cref="Stop"/> and for the same reason:
    /// letting go can throw, and a second call meeting a flag that never went up would ask a
    /// session that is already finished to let go again. This one reports what it throws rather
    /// than swallowing it — it is the deliberate exit path, where a handle that would not close is
    /// the news and there is nothing above it for that news to drown out.
    /// </remarks>
    public void Dispose()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        session.Dispose();
    }

    /// <summary>
    /// Lets go of <paramref name="session"/> while a failure is already on its way up, and keeps a
    /// handle that would not close from replacing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both ways a recording fails run through here — a start whose row could not be written, and a
    /// stop a device refused — and both owe the same thing.
    /// <see cref="CaptureSession.Dispose"/> asks every source to stop, lets each go, and releases
    /// the mark over the folder in a <c>finally</c>, so the mark goes however the devices went. It
    /// is the only thing that lets any of them go, so a session nobody disposes holds two devices,
    /// its block files and its mark until this process ends — and a meeting whose blocks are still
    /// held is one <see cref="MeetingRecordings.Finish"/> cannot read back, so the recording that
    /// just failed could not be salvaged either until the application was restarted.
    /// </para>
    /// <para>
    /// Everything it throws is swallowed, and broadly rather than by name on purpose. A source
    /// letting go already swallows what a device does, so the throw that actually arrives here is
    /// an <see cref="IOException"/> out of the mark's own handle — and an
    /// <see cref="AudioCaptureException"/> is the one thing this can never be, because that is what
    /// taking a mark throws and never what letting one go throws. A catch naming it would read like
    /// a guarantee and hold nothing, and what it let through would be a sentence about cleanup
    /// standing where the caller needed to hear why the meeting could not be started or stopped.
    /// </para>
    /// </remarks>
    /// <param name="session">The session to let go of, however it ended.</param>
    private static void LetGoOf(CaptureSession session)
    {
        try
        {
            session.Dispose();
        }
        catch
        {
            // Swallowed on purpose: see the remarks.
        }
    }
}
