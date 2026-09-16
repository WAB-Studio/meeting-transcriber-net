namespace MeetingTranscriber.Recording;

/// <summary>What saving a meeting does, in the order it does it.</summary>
/// <remarks>
/// These are the whole of it, and the order they are declared in is the order they happen in —
/// which is what <see cref="SavingTheMeeting.Steps"/> reads. <b>Stopping does queue what the person
/// settled, and there is still no member for it.</b> The reason changed with the fact: it used to
/// be that nothing was ever queued, and it is now that queueing is one row written after the last
/// byte of audio is on disk — inside <see cref="WritingTheMeetingDown"/> by every reading a person
/// could have of it, and over in the time it takes to say it. These two are what a person watches
/// for minutes while a long meeting is made; a step that could only ever be drawn already finished
/// is not progress, it is a line that appears and goes in one frame. What would bring a member here
/// is work a save waits on — something that runs the job rather than writing it down.
/// </remarks>
public enum SavingWork
{
    /// <summary>
    /// Both sources are being let go of. Quick when the devices answer and bounded when one does
    /// not, and nothing is being recorded from the moment it is over.
    /// </summary>
    LettingTheSourcesGo,

    /// <summary>
    /// The spools are being poured onto one timeline, read back, hashed and filed, and the corpus
    /// is being told how long the meeting turned out to be. Minutes of it for a long meeting.
    /// </summary>
    WritingTheMeetingDown,
}

/// <summary>Where one step of a save has got to.</summary>
public enum StepStanding
{
    /// <summary>It has not started.</summary>
    NotYet,

    /// <summary>It is what is happening now.</summary>
    Underway,

    /// <summary>It is over.</summary>
    Done,
}

/// <summary>
/// Saving a meeting, as the steps it runs and where each of them stands while one is under way.
/// </summary>
/// <remarks>
/// <para>
/// The one rule here is that a save goes forward: whatever is behind the step under way is done
/// and whatever is ahead of it has not started. It lives in a project a build agent runs rather
/// than beside a window, for the reason <c>RecorderStates</c> and <c>RecordingMeters</c> do — a
/// screen reads the answer and decides nothing, so what a person sees is provable with no device
/// and no UI thread.
/// </para>
/// <para>
/// It carries no state of its own. What is under way is the one thing that changes, and it travels
/// as the step itself through the <see cref="IProgress{T}"/> a caller hands
/// <c>MeetingRecording.Stop</c> — so there is nothing here that a screen could find stale, and
/// nothing that two saves running against one screen could get wrong.
/// </para>
/// </remarks>
public static class SavingTheMeeting
{
    /// <summary>The steps a save runs, in the order it runs them.</summary>
    /// <remarks>
    /// Read off <see cref="SavingWork"/> rather than listed again, so the steps a screen draws and
    /// the steps a save has are one fact. A second list here is how a screen comes to draw a step
    /// nothing runs, which is the one thing this is for.
    /// </remarks>
    public static IReadOnlyList<SavingWork> Steps => InOrder;

    private static readonly SavingWork[] InOrder = Enum.GetValues<SavingWork>();

    /// <summary>
    /// Where <paramref name="step"/> stands while <paramref name="underway"/> is what is happening.
    /// </summary>
    /// <exception cref="RecordingException">Either of them is not a step a save runs.</exception>
    public static StepStanding StandingOf(SavingWork step, SavingWork underway)
    {
        var where = Array.IndexOf(InOrder, step);
        var now = Array.IndexOf(InOrder, underway);

        if (where < 0 || now < 0)
        {
            throw new RecordingException(
                $"Saving a meeting runs '{string.Join("', '", Steps)}', so there is nothing to say "
                + $"about '{step}' while '{underway}' is happening.");
        }

        return where == now ? StepStanding.Underway
            : where < now ? StepStanding.Done
            : StepStanding.NotYet;
    }
}
