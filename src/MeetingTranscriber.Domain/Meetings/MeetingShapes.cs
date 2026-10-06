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

/// <summary>
/// What a place on the filing screen is called for the meeting it is in: the word a person uses
/// for it, and never a stored value.
/// </summary>
/// <remarks>
/// <para>
/// The corpus stores an organization, a project, a topic and a person; the screen used to say those
/// four, or the role a level plays, and somebody filing a class was asked for an <em>organización</em>
/// and a <em>proyecto</em>. This is the screen saying the same four in the meeting's own words —
/// <em>Universidad</em> over an organization, <em>Materia</em> over a project, <em>Profesor</em>
/// over a person. <see cref="MeetingShapes.Holds"/> is what each of them is of the four.
/// </para>
/// <para>
/// Like <see cref="MeetingShape"/>, nothing here is a column or has a CHECK: renaming a member is
/// not a migration, and adding one does not change what a corpus can hold.
/// </para>
/// </remarks>
public enum PlaceName
{
    /// <summary>An organization with nothing more said about it: the generic name of a first level.</summary>
    Organization = 1,

    /// <summary>A body of work with nothing more said about it: the generic name of a second level.</summary>
    Project = 2,

    /// <summary>One concrete subject with nothing more said about it: the generic name of a third level.</summary>
    Topic = 3,

    /// <summary>Somebody, with nothing more said about them. Never a node.</summary>
    Person = 4,

    /// <summary>The organization a class belongs to.</summary>
    University = 5,

    /// <summary>The course inside it.</summary>
    Course = 6,

    /// <summary>Who teaches it. Never a node.</summary>
    Teacher = 7,

    /// <summary>A company: the one somebody works at, interviews for or deals with.</summary>
    Company = 8,

    /// <summary>Who interviews the person filing. Never a node.</summary>
    Interviewer = 9,

    /// <summary>Who is interviewed. Never a node.</summary>
    Candidate = 10,

    /// <summary>The company on the other side of a sale.</summary>
    Client = 11,

    /// <summary>The person at the client. Never a node.</summary>
    Contact = 12,

    /// <summary>The organization that holds a conference.</summary>
    Organizer = 13,

    /// <summary>A conference, inside its organizer.</summary>
    Conference = 14,

    /// <summary>A team inside a company.</summary>
    Team = 15,

    /// <summary>A client's case: a ticket, an incident.</summary>
    Case = 16,

    /// <summary>The organization across the table when no kind of meeting says what it is.</summary>
    OtherOrganization = 17,
}

/// <summary>What each level of a path and each place for somebody is called, for one kind of meeting.</summary>
/// <param name="Levels">
/// By role, the name of each level of a path from the root down. A role the meeting does not open is
/// absent, and a path deeper than the names runs on in the generic ones.
/// </param>
/// <param name="Somebody">The name of each place for somebody, in the order they open.</param>
public sealed record ShapeNames(
    IReadOnlyDictionary<MeetingNodeRole, IReadOnlyList<PlaceName>> Levels,
    IReadOnlyList<PlaceName> Somebody);

/// <summary>A place on the screen for somebody, and how the meeting would name them.</summary>
/// <remarks>
/// It carries the two roles and never a person. §5.3 is explicit that a shape pre-fills nothing —
/// <em>siempre va a pre-llenar nada más</em> — so a slot is a row waiting for a name, with the two
/// toggles already set the way that story sets them.
/// </remarks>
/// <param name="Attended">Whether the slot opens with <em>was there</em> on.</param>
/// <param name="Subject">Whether the slot opens with <em>the meeting is about them</em> on.</param>
/// <param name="Name">What that kind of meeting calls the person: a teacher, a candidate, a contact.</param>
public sealed record PersonSlot(bool Attended, bool Subject, PlaceName Name);

/// <summary>What choosing a shape opens: how many empty places, and never what goes in one.</summary>
/// <param name="WorkOf">Empty paths in the column for what the meeting is work of.</param>
/// <param name="Counterpart">Empty paths in the column for the other side of the table.</param>
/// <param name="About">Empty paths in the column for what it was about without being work of.</param>
/// <param name="Somebody">One place per person the story puts on the meeting.</param>
public sealed record ShapeOpens(
    int WorkOf,
    int Counterpart,
    int About,
    IReadOnlyList<PersonSlot> Somebody)
{
    /// <summary>How many empty paths this opens in the column for one role.</summary>
    public int Paths(MeetingNodeRole role) => role switch
    {
        MeetingNodeRole.WorkOf => WorkOf,
        MeetingNodeRole.Counterpart => Counterpart,
        MeetingNodeRole.About => About,
        _ => throw new InvalidOperationException($"A shape opens nothing for the role '{role}'."),
    };
}

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
        // Row 1. The course carries it and the faculty is reached through the tree, unnamed. The
        // teacher is somebody who was there.
        MeetingShape.Class =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: false, PlaceName.Teacher)]),

        // Row 2. Nothing at all, and it is found again by text.
        MeetingShape.CasualCatchUp => new ShapeOpens(0, 0, 0, []),

        // Row 3. A company I do not work for, and no project inside it to invent. Whoever
        // interviewed me was there.
        MeetingShape.InterviewAsCandidate =>
            new ShapeOpens(0, 1, 0, [new PersonSlot(Attended: true, Subject: false, PlaceName.Interviewer)]),

        // Row 4. My own company's work, and the candidate on it.
        MeetingShape.InterviewAsInterviewer =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: false, PlaceName.Candidate)]),

        // Row 5. Two links, because one project column made this a choice and the answer was a
        // project called "varios".
        MeetingShape.TwoProjects => new ShapeOpens(2, 0, 0, []),

        // Row 6. Both sides of the table at once, each saying which side it is, and a contact
        // at the client.
        MeetingShape.SellingToAClient =>
            new ShapeOpens(1, 1, 0, [new PersonSlot(Attended: true, Subject: false, PlaceName.Contact)]),

        MeetingShape.TeamMeeting =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: false, PlaceName.Person)]),

        // Row 8. Attending a conference is not work of it, and the organiser is not across the
        // table.
        MeetingShape.Conference => new ShapeOpens(0, 0, 1, []),

        // Row 9. Not "of" either company, and no third link inventing an owner.
        MeetingShape.BetweenTwoCompanies => new ShapeOpens(0, 2, 0, []),

        // Row 10. The person is what it is about and was not in the room, which is why the slot
        // opens with one toggle on and the other off.
        MeetingShape.HumanResources =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: false, Subject: true, PlaceName.Person)]),

        // Row 11. Both, and both true.
        MeetingShape.RecurringOneToOne =>
            new ShapeOpens(1, 0, 0, [new PersonSlot(Attended: true, Subject: true, PlaceName.Person)]),

        MeetingShape.Daily => new ShapeOpens(1, 0, 0, []),

        // Row 13. The subject is a ticket, so the path runs three deep, and the client is still
        // the other side.
        MeetingShape.AfterSalesSupport => new ShapeOpens(1, 1, 0, []),

        // None of the thirteen. It opens nothing and every column is filled by hand.
        MeetingShape.FilledByHand => new ShapeOpens(0, 0, 0, []),

        _ => throw new InvalidOperationException($"No shape table has an answer for '{shape}'."),
    };

    /// <summary>
    /// What each level of a path, and each place for somebody, is called for a kind of meeting.
    /// </summary>
    /// <param name="shape">The kind lit on the screen, or nothing when none is.</param>
    /// <remarks>
    /// <para>
    /// Nothing is stored: these are the words a person uses for what is already stored, and
    /// <see cref="Holds"/> is the only thing connecting a word back to the class a node is written
    /// as. With nothing lit, and for <see cref="MeetingShape.FilledByHand"/>, the generic names
    /// stand in every column — <em>Organización › Proyecto › Asunto</em>, and <em>Otra
    /// organización</em> at the top of the other side — and so they do for a role the lit kind does
    /// not open.
    /// </para>
    /// <para>
    /// The places for somebody are read off <see cref="Opens"/>, which already says how many there
    /// are and how each is named, so the two cannot disagree. The last arm throws for the reason
    /// <see cref="Opens"/>' does.
    /// </para>
    /// </remarks>
    public static ShapeNames Names(MeetingShape? shape)
    {
        if (shape is not { } lit || lit is MeetingShape.FilledByHand)
        {
            return new ShapeNames(
                new Dictionary<MeetingNodeRole, IReadOnlyList<PlaceName>>
                {
                    [MeetingNodeRole.WorkOf] = [PlaceName.Organization, PlaceName.Project, PlaceName.Topic],
                    [MeetingNodeRole.Counterpart] =
                        [PlaceName.OtherOrganization, PlaceName.Project, PlaceName.Topic],
                    [MeetingNodeRole.About] = [PlaceName.Organization, PlaceName.Project, PlaceName.Topic],
                },
                []);
        }

        var somebody = Opens(lit).Somebody.Select(slot => slot.Name).ToArray();

        return lit switch
        {
            MeetingShape.Class => Of(somebody, workOf: [PlaceName.University, PlaceName.Course]),
            MeetingShape.CasualCatchUp => Of(somebody),
            MeetingShape.InterviewAsCandidate => Of(somebody, counterpart: [PlaceName.Company]),
            MeetingShape.InterviewAsInterviewer =>
                Of(somebody, workOf: [PlaceName.Company, PlaceName.Project]),
            MeetingShape.TwoProjects =>
                Of(somebody, workOf: [PlaceName.Organization, PlaceName.Project]),
            MeetingShape.SellingToAClient => Of(
                somebody,
                workOf: [PlaceName.Company, PlaceName.Project],
                counterpart: [PlaceName.Client]),
            MeetingShape.TeamMeeting => Of(somebody, workOf: [PlaceName.Company, PlaceName.Team]),
            MeetingShape.Conference =>
                Of(somebody, about: [PlaceName.Organizer, PlaceName.Conference]),
            MeetingShape.BetweenTwoCompanies => Of(somebody, counterpart: [PlaceName.Company]),
            MeetingShape.HumanResources => Of(somebody, workOf: [PlaceName.Company]),
            MeetingShape.RecurringOneToOne => Of(somebody, workOf: [PlaceName.Company]),
            MeetingShape.Daily => Of(somebody, workOf: [PlaceName.Company, PlaceName.Project]),
            MeetingShape.AfterSalesSupport => Of(
                somebody,
                workOf: [PlaceName.Company, PlaceName.Project, PlaceName.Case],
                counterpart: [PlaceName.Client]),
            _ => throw new InvalidOperationException($"No shape table has names for '{lit}'."),
        };
    }

    /// <summary>
    /// The class of node a level of this name is: what a pill under it offers, and what naming a
    /// new one there writes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The name is one for somebody, which is never a node, or is not a name at all.
    /// </exception>
    public static NodeKind Holds(PlaceName name) => name switch
    {
        PlaceName.Organization or PlaceName.University or PlaceName.Company or PlaceName.Client
            or PlaceName.Organizer or PlaceName.OtherOrganization => NodeKind.Organization,
        PlaceName.Project or PlaceName.Course or PlaceName.Team or PlaceName.Conference => NodeKind.Initiative,
        PlaceName.Topic or PlaceName.Case => NodeKind.Topic,
        _ => throw new ArgumentOutOfRangeException(
            nameof(name), name, "That name is for somebody, or is not a name, and is never a node."),
    };

    private static ShapeNames Of(
        IReadOnlyList<PlaceName> somebody,
        PlaceName[]? workOf = null,
        PlaceName[]? counterpart = null,
        PlaceName[]? about = null)
    {
        var levels = new Dictionary<MeetingNodeRole, IReadOnlyList<PlaceName>>();

        if (workOf is not null)
        {
            levels[MeetingNodeRole.WorkOf] = workOf;
        }

        if (counterpart is not null)
        {
            levels[MeetingNodeRole.Counterpart] = counterpart;
        }

        if (about is not null)
        {
            levels[MeetingNodeRole.About] = about;
        }

        return new ShapeNames(levels, somebody);
    }
}
