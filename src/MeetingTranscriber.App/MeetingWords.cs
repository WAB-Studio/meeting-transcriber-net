using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Presentation;

namespace MeetingTranscriber.App;

/// <summary>
/// What a screen says about how far a meeting has got, where that stands, and what it would do to
/// it next.
/// </summary>
/// <remarks>
/// <para>
/// Two screens ask this now — the list every meeting is on, and the screen one meeting is read
/// from — and they have to answer it the same way. A meeting that reads <em>transcribed</em> on
/// the list and something else on its own screen is the application disagreeing with itself about
/// the one thing both screens are for. So the table is here rather than on either of them, and
/// each screen looks the answer up.
/// </para>
/// <para>
/// Every arm stops rather than substituting, and that is what the tables are for. A stage added to
/// <see cref="MeetingStage"/> and not given a text here would otherwise be shown to somebody as
/// one of the others — a meeting with no audio reading as one ready to be paid for.
/// <c>MeetingCardTextTests</c> is what catches it before it can be thrown.
/// </para>
/// </remarks>
internal static class MeetingWords
{
    /// <summary>What a screen says about the stage a meeting has got to.</summary>
    public static UiText Reached(MeetingStage stage) => stage switch
    {
        MeetingStage.Recording => UiTexts.NoAudioYet,
        MeetingStage.Recorded => UiTexts.Recorded,
        MeetingStage.Transcribed => UiTexts.Transcribed,
        MeetingStage.Summarised => UiTexts.Summarised,
        _ => throw new InvalidOperationException($"No screen has text for meeting stage '{stage}'."),
    };

    /// <summary>
    /// What a screen says about where that stage stands, or nothing when the stage has no action
    /// for anything to be standing over.
    /// </summary>
    public static UiText? Standing(StageStanding standing) => standing switch
    {
        StageStanding.Offered => UiTexts.WaitingToBeTold,
        StageStanding.Underway => UiTexts.AlreadyInTheQueue,
        StageStanding.StoppedOnAPerson => UiTexts.StoppedWaitingForAPerson,
        StageStanding.Declined => UiTexts.IgnoredForNow,
        StageStanding.NothingToDo => null,
        _ => throw new InvalidOperationException($"No screen has text for stage standing '{standing}'."),
    };

    /// <summary>
    /// What the button offering a stage's action says.
    /// </summary>
    /// <remarks>
    /// Only the kinds a stage can offer are here, and the throw is what keeps it that way. The one
    /// this must never grow is <see cref="JobKind.Render"/>: the rendered files cost nothing and
    /// can be made again, so they are never a press, and a screen that had a word for the button
    /// would be one edit from showing it.
    /// </remarks>
    public static UiText Action(JobKind kind) => kind switch
    {
        JobKind.Transcribe => UiTexts.Transcribe,
        JobKind.Extract => UiTexts.Summarise,
        _ => throw new InvalidOperationException($"No screen offers anything for job kind '{kind}'."),
    };
}
