namespace MeetingTranscriber.Domain.Meetings;

// The thirteen meetings arquitectura.md §5.3 lists, as a rule rather than as words: what choosing
// one of them opens on the screen that files a meeting, and it only ever opens — never fills.
//
// Nothing in this file is a stored value, and that matters because it lands beside Classification.cs
// and looks like a fourth name set. It is not: nothing here is a column, nothing has a CHECK,
// CorpusDbContext never writes it and WireNames never sees it. A shape is a pre-fill somebody
// presses and the meeting carries no record of which one it was — what is stored is the links and
// the namings the person ended up with. If storing the shape is ever wanted, that is a column, a
// CHECK, a migration and a change to .claude/audit-floor.md, not another member of this enum.

/// <summary>
/// One of the thirteen meetings §5.3 lists, plus the answer for a meeting that is none of them.
/// </summary>
/// <remarks>
/// The last member is not a fourteenth story. <see cref="CasualCatchUp"/> and
/// <see cref="FilledByHand"/> open the same nothing and both stay, because they are two different
/// answers a person gives — <em>this was a casual catch-up</em> and <em>none of these fits, I will
/// fill it in</em> — and a meeting that is the first is classified as having nothing on it while a
/// meeting that is the second is about to be classified by hand.
/// </remarks>
public enum MeetingShape
{
    Class = 1,
    CasualCatchUp = 2,
    InterviewAsCandidate = 3,
    InterviewAsInterviewer = 4,
    TwoProjects = 5,
    SellingToAClient = 6,
    TeamMeeting = 7,
    Conference = 8,
    BetweenTwoCompanies = 9,
    HumanResources = 10,
    RecurringOneToOne = 11,
    Daily = 12,
    AfterSalesSupport = 13,
    FilledByHand = 14,
}

/// <summary>A place on the screen for somebody, and how the meeting would name them.</summary>
/// <remarks>
/// It carries the two roles and never a person. §5.3 is explicit that a shape pre-fills nothing —
/// <em>siempre va a pre-llenar nada más</em> — so a slot is a row waiting for a name, with the two
/// toggles already set the way that story sets them.
/// </remarks>
public sealed record PersonSlot(bool Attended, bool Subject);

/// <summary>What choosing a shape opens: how many empty places, and never what goes in one.</summary>
/// <param name="WorkOf">Empty paths in the column for what the meeting is work of.</param>
/// <param name="Counterpart">Empty paths in the column for the other side of the table.</param>
/// <param name="About">Empty paths in the column for what it was about without being work of.</param>
/// <param name="Somebody">One place per person the story puts on the meeting.</param>
public sealed record ShapeOpens(
    int WorkOf,
    int Counterpart,
    int About,
    IReadOnlyList<PersonSlot> Somebody);

/// <summary>The table saying what each shape opens, and the only thing that answers that.</summary>
public static class MeetingShapes
{
    /// <summary>
    /// What choosing <paramref name="shape"/> puts on the screen.
    /// </summary>
    /// <remarks>
    /// The last arm throws for the reason every table in <c>MeetingWords</c> does: a shape added
    /// later and given no row here would silently open some other shape's slots, and somebody
    /// filing a meeting would be handed a column the story they picked never asked for.
    /// </remarks>
    public static ShapeOpens Opens(MeetingShape shape) => shape switch
    {
        // Row 1. The course carries it and the faculty is reached through the tree, unnamed.
        MeetingShape.Class => new ShapeOpens(1, 0, 0, []),

        // Row 2. Nothing at all, and it is found again by text.
        MeetingShape.CasualCatchUp => new ShapeOpens(0, 0, 0, []),

        // Row 3. A company I do not work for, and no project inside it to invent.
        MeetingShape.InterviewAsCandidate => new ShapeOpens(0, 1, 0, []),

        // Row 4. My own company's work, and the candidate on it.
        MeetingShape.InterviewAsInterviewer =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: false)]),

        // Row 5. Two links, because one project column made this a choice and the answer was a
        // project called "varios".
        MeetingShape.TwoProjects => new ShapeOpens(2, 0, 0, []),

        // Row 6. Both sides of the table at once, each saying which side it is.
        MeetingShape.SellingToAClient => new ShapeOpens(1, 1, 0, []),

        MeetingShape.TeamMeeting =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: false)]),

        // Row 8. Attending a conference is not work of it, and the organiser is not across the
        // table.
        MeetingShape.Conference => new ShapeOpens(0, 0, 1, []),

        // Row 9. Not "of" either company, and no third link inventing an owner.
        MeetingShape.BetweenTwoCompanies => new ShapeOpens(0, 2, 0, []),

        // Row 10. The person is what it is about and was not in the room, which is why the slot
        // opens with one toggle on and the other off.
        MeetingShape.HumanResources =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: false, Subject: true)]),

        // Row 11. Both, and both true.
        MeetingShape.RecurringOneToOne =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: true)]),

        MeetingShape.Daily => new ShapeOpens(1, 0, 0, []),

        // Row 13. The subject is a ticket, so the path runs three deep, and the client is still
        // the other side.
        MeetingShape.AfterSalesSupport => new ShapeOpens(1, 1, 0, []),

        // None of the thirteen. It opens nothing and every column is filled by hand.
        MeetingShape.FilledByHand => new ShapeOpens(0, 0, 0, []),

        _ => throw new InvalidOperationException($"No shape table has an answer for '{shape}'."),
    };
}
