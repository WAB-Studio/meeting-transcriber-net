using MeetingTranscriber.Domain.Jobs;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What one press on one row of the meetings list is called, so that it can be told from the same
/// press on another row and from the other press on its own row.
/// </summary>
/// <remarks>
/// <para>
/// Two things want that. A redraw takes every card off the list and builds new ones, so the button
/// somebody had the keyboard on is gone and its replacement is a different object: an id is what
/// says which of the new buttons is the one they were on. And a tool driving the window presses one
/// row of twelve, which is the same question asked from outside. Nobody hears an id — what a
/// narrator reads out is the name beside it, in the reader's own language — so nothing here is a
/// word anybody has to understand.
/// </para>
/// <para>
/// An id says which row <em>and what the press on it buys</em>, and never where the row sits. A
/// list re-read under somebody is a list whose rows have moved, so a position would hand the
/// keyboard to another meeting's button. What the act is doing in <see cref="ToTake"/> is the
/// same rule one step finer, and that method says it.
/// </para>
/// <para>
/// The meetings' ids and the recordings' cannot collide even though one list holds both. A colon is
/// not legal in a Windows directory name, so no folder name ends in one of the suffixes below; and
/// a recording and the meeting it is of are never drawn together, because the list draws the
/// recording instead of that meeting's card.
/// </para>
/// <para>
/// It is here for the reason <c>MeetingsWatch</c> is: the subject is the rows of that list, whose
/// two halves are the corpus's meetings and the spool's folders, and this is the only project that
/// can see both. <c>Presentation</c> was not the alternative — it holds what a screen says, and an
/// id is not said.
/// </para>
/// </remarks>
public static class RowPresses
{
    /// <summary>The press that opens a meeting, which is the card's own name.</summary>
    /// <remarks>
    /// The meeting's id and nothing appended, and it may never gain a suffix: the probe runs
    /// written down in <c>ISA.md</c> press a meeting by exactly this string, matching an automation
    /// id with an ordinal equality rather than a contains, so an open press that grew a suffix
    /// would silently stop resolving while the two presses below started to.
    /// </remarks>
    public static string ToOpen(Guid meeting) => meeting.ToString();

    /// <summary>The press that leaves this meeting's next act for somebody else to ask for.</summary>
    /// <remarks>
    /// No act in it, unlike <see cref="ToTake"/>: ignoring is ignoring at every stage — the words
    /// on it do not change and it spends nothing — so a stage that advanced under a reader who was
    /// on this press has not changed what their Enter does.
    /// </remarks>
    public static string ToLeave(Guid meeting) => $"{meeting}:leave";

    /// <summary>The press that asks for this meeting's next act, named for the act it buys.</summary>
    /// <param name="meeting">Which meeting the row is of.</param>
    /// <param name="act">
    /// What the press buys, which is the only thing on a card that can change while the row, the
    /// position and the standing all stay as they were: a meeting whose stage advances between two
    /// reads is drawn again in the same place with <em>Transcribir</em> replaced by
    /// <em>Resumir</em>. So it is part of the id, and two acts on one row are two presses — a
    /// redraw then finds nothing to give the keyboard back to, instead of handing it to a button
    /// that has started buying a summary nobody chose.
    /// </param>
    public static string ToTake(Guid meeting, JobKind act) => $"{meeting}:take:{Word(act)}";

    /// <summary>One of the two answers about a recording the application never finished.</summary>
    /// <param name="folder">
    /// The name of the spool folder holding it, not its path. Every waiting row in one list comes
    /// from one spool root, so a name tells them apart inside the list that draws them — and an
    /// absolute path would be an id no run on another machine or another corpus could replay,
    /// which is half of what an id is for. It claims nothing wider than that one root.
    /// </param>
    /// <param name="answer">Which of the two answers this press gives.</param>
    public static string ToAnswer(string folder, WaitingAnswer answer)
    {
        // An id of nothing is what GetAutomationId already answers for every element carrying no
        // id at all, so a blank one here would make a redraw match a control that was never on
        // this list.
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return $"{folder}:{Word(answer)}";
    }

    /// <summary>What an act is spelled as inside an id.</summary>
    /// <remarks>
    /// A switch and not the member's own spelling, so the id is a decision this file takes rather
    /// than a consequence of how an enum was written, and so that an act added to
    /// <see cref="JobKind"/> stops rather than quietly minting an id for a press no card draws. The
    /// closed set is the one a card can offer, which is the same one <c>MeetingWords.Action</c>
    /// closes over; <see cref="JobKind.Render"/> is the one that must never join it, for the reason
    /// that method gives. It is the second of the two to refuse and never the first — a screen
    /// building a card asks for the press's words before its id — so what a stop here really
    /// protects is this type's own contract, and a card offering an act nobody wrote words for is
    /// caught over there.
    /// </remarks>
    private static string Word(JobKind act) => act switch
    {
        JobKind.Transcribe => "transcribe",
        JobKind.Extract => "extract",
        _ => throw new ArgumentOutOfRangeException(
            nameof(act),
            act,
            "No card offers this act, so no press on this list is named for it."),
    };

    /// <summary>What an answer about a recording is spelled as inside an id.</summary>
    /// <remarks>Same reason as the act's, and the same shape.</remarks>
    private static string Word(WaitingAnswer answer) => answer switch
    {
        WaitingAnswer.Discard => "discard",
        WaitingAnswer.Keep => "keep",
        _ => throw new ArgumentOutOfRangeException(
            nameof(answer),
            answer,
            "No row offers this answer, so no press on this list is named for it."),
    };
}
