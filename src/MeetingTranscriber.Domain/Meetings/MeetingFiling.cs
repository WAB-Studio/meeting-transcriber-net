namespace MeetingTranscriber.Domain.Meetings;

/// <summary>One row of a column: the path down the tree, root first, deepest last.</summary>
/// <remarks>
/// A path and not a node, because the screen draws the whole way down — <em>TechSed › Soporte ›
/// ticket #4312</em> — and each pill is chosen against the one to its left. What the meeting is
/// filed under is <see cref="Deepest"/> alone; §5.3 row 1 is the rule stated, where the class links
/// the course and the faculty is reached through the tree without being named.
/// </remarks>
public sealed record ChosenPath(IReadOnlyList<Guid> Nodes)
{
    /// <summary>A place in a column with nothing in it, which is a slot and not a link.</summary>
    public static readonly ChosenPath Empty = new([]);

    /// <summary>The node this path files the meeting under, or nothing while it is empty.</summary>
    public Guid? Deepest => Nodes.Count == 0 ? null : Nodes[^1];
}

/// <summary>A place for somebody, filled or not, and how the meeting would name them.</summary>
/// <remarks>
/// Two ways and not one of two: somebody in their own one to one attended it and is what it was
/// about, and §5.3 row 10 is a dismissal discussed before the person is in the room. The two
/// answers below walk <see cref="MeetingPersonRole"/> rather than naming the fields, so a third way
/// of being named is a failure here instead of a toggle nobody drew.
/// </remarks>
public sealed record ChosenPerson(Guid? PersonId, bool Attended, bool Subject)
{
    /// <summary>Whether this place names somebody under that role.</summary>
    /// <remarks>
    /// The last arm throws for the reason every table in this file does: a role added to the closed
    /// vocabulary and not answered here would be dropped from every save without a word.
    /// </remarks>
    public bool Carries(MeetingPersonRole role) => role switch
    {
        MeetingPersonRole.Attended => Attended,
        MeetingPersonRole.Subject => Subject,
        _ => throw new InvalidOperationException($"A place for somebody has no answer for '{role}'."),
    };

    /// <summary>The same place with that answer turned the other way.</summary>
    /// <remarks>
    /// Here rather than on the screen pressing the toggle, and beside <see cref="Carries"/> for the
    /// reason it exists at all: which field a role reads and which field it writes are one fact,
    /// and a screen holding the second copy of it is where the two come to disagree.
    /// </remarks>
    public ChosenPerson Flipped(MeetingPersonRole role) => role switch
    {
        MeetingPersonRole.Attended => this with { Attended = !Attended },
        MeetingPersonRole.Subject => this with { Subject = !Subject },
        _ => throw new InvalidOperationException($"A place for somebody has no answer for '{role}'."),
    };
}

/// <summary>
/// What the screen that files a meeting is holding, and what pressing <em>Guardar</em> would write.
/// </summary>
/// <remarks>
/// <para>
/// A draft and never the corpus. Nothing here reaches a database, nothing here is stored as it
/// stands, and a filing with a slot nobody filled in files nothing under that slot — which is what
/// makes choosing a shape safe to do twice and what makes leaving the screen cost nothing.
/// </para>
/// <para>
/// It is named for what it is rather than for the screen holding it, and that is not only style:
/// the control is <c>MeetingTranscriber.App.ClassifyingAMeeting</c>, and inside its code-behind the
/// containing class would shadow this type's name at every use.
/// </para>
/// </remarks>
/// <param name="Shape">
/// Which of the fourteen the columns were opened from, or none. Drawn as the chip that is lit and
/// stored nowhere: a shape is a pre-fill, so a meeting read back shows its filing with no chip on.
/// </param>
public sealed record MeetingFiling(
    MeetingShape? Shape,
    IReadOnlyList<ChosenPath> WorkOf,
    IReadOnlyList<ChosenPath> Counterpart,
    IReadOnlyList<ChosenPath> About,
    IReadOnlyList<ChosenPerson> Somebody)
{
    /// <summary>A meeting filed under nothing on purpose, which is what §5.3 row 2 stores.</summary>
    public static readonly MeetingFiling Nothing = new(null, [], [], [], []);

    /// <summary>
    /// The columns a shape opens, empty, with the person slots that story sets.
    /// </summary>
    /// <remarks>
    /// It replaces rather than merges: the card's <em>what each one fills is seen when it is
    /// chosen</em> only reads true if choosing changes the screen. What is lost is a draft, and the
    /// corpus is not touched either way.
    /// </remarks>
    public static MeetingFiling AsShapedBy(MeetingShape shape)
    {
        var opens = MeetingShapes.Opens(shape);

        return new MeetingFiling(
            shape,
            Slots(opens.WorkOf),
            Slots(opens.Counterpart),
            Slots(opens.About),
            [.. opens.Somebody.Select(slot => new ChosenPerson(null, slot.Attended, slot.Subject))]);
    }

    /// <summary>The column a link of this role is drawn in.</summary>
    /// <remarks>
    /// Public, and it is what the screen draws the three columns from: it walks
    /// <see cref="MeetingNodeRole"/> and asks this, so the screen needs one table of its own — the
    /// heading's words — rather than a second one deciding which list goes where.
    /// </remarks>
    public IReadOnlyList<ChosenPath> Column(MeetingNodeRole role) => role switch
    {
        MeetingNodeRole.WorkOf => WorkOf,
        MeetingNodeRole.Counterpart => Counterpart,
        MeetingNodeRole.About => About,
        _ => throw new InvalidOperationException($"A filing has no column for the role '{role}'."),
    };

    /// <summary>The same filing with one column replaced.</summary>
    /// <remarks>
    /// Beside <see cref="Column"/> and not on the screen that edits one, for the same reason
    /// <see cref="ChosenPerson.Flipped"/> is beside <see cref="ChosenPerson.Carries"/>: which field
    /// a role is read out of and which field it is written into are one fact.
    /// </remarks>
    public MeetingFiling With(MeetingNodeRole column, IReadOnlyList<ChosenPath> paths) => column switch
    {
        MeetingNodeRole.WorkOf => this with { WorkOf = paths },
        MeetingNodeRole.Counterpart => this with { Counterpart = paths },
        MeetingNodeRole.About => this with { About = paths },
        _ => throw new InvalidOperationException($"A filing has no column for the role '{column}'."),
    };

    /// <summary>
    /// Every link this filing would write: the deepest node of each filled path, under its role.
    /// </summary>
    /// <remarks>
    /// The deepest and not every node on the way to it, which is §5.3 row 1: the class links the
    /// course, and linking the faculty as well would file the meeting under something nobody said
    /// it was about. A path with nothing in it is a slot somebody has not answered and is no link.
    /// </remarks>
    public IReadOnlyList<(Guid Node, MeetingNodeRole Role)> Links =>
    [
        .. Enum.GetValues<MeetingNodeRole>()
            .SelectMany(role => Column(role)
                .Select(path => path.Deepest)
                .Where(node => node is not null)
                .Select(node => (Node: node!.Value, Role: role)))
            .Distinct(),
    ];

    /// <summary>
    /// Every naming this filing would write: each filled slot under each role it carries.
    /// </summary>
    /// <remarks>
    /// Somebody who attended and is what the meeting was about is two rows, which is what one
    /// column made a choice between and lost. The roles come from walking
    /// <see cref="MeetingPersonRole"/> rather than from two lines, so a third member of that
    /// vocabulary is a failure here and not a silent omission.
    /// </remarks>
    public IReadOnlyList<(Guid Person, MeetingPersonRole Role)> Named =>
    [
        .. Somebody
            .Where(slot => slot.PersonId is not null)
            .SelectMany(slot => Enum.GetValues<MeetingPersonRole>()
                .Where(slot.Carries)
                .Select(role => (Person: slot.PersonId!.Value, Role: role)))
            .Distinct(),
    ];

    /// <summary>
    /// Whether this would file the meeting under nothing at all.
    /// </summary>
    /// <remarks>
    /// Read off what would be written and never off <see cref="Shape"/>. A shape chosen and left
    /// unanswered opens slots and fills none of them, so a meeting somebody merely looked at is
    /// still unclassified.
    /// </remarks>
    public bool IsUnclassified => Links.Count == 0 && Named.Count == 0;

    private static IReadOnlyList<ChosenPath> Slots(int howMany) =>
        [.. Enumerable.Repeat(ChosenPath.Empty, howMany)];
}
