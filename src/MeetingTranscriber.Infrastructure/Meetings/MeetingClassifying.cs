using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>One node with everything above it, root first — what a column draws as pills.</summary>
public sealed record NodePath(IReadOnlyList<Node> Nodes);

/// <summary>
/// Somebody the screen may put on a meeting, with where they belonged the day it happened.
/// </summary>
/// <remarks>
/// The day of the meeting and never today, which is why <see cref="Affiliation"/> carries a period
/// at all: hiring tomorrow somebody interviewed today must not rewrite the interview into a meeting
/// with your own employee. Somebody at two organizations at once has two entries here and not one.
/// </remarks>
public sealed record PersonAsOfTheMeeting(
    Person Person,
    IReadOnlyList<(Node Organization, UtcTimestamp? Since)> Belonged);

/// <summary>
/// The screen that files a meeting, read: the meeting, what it is already filed under, the tree the
/// pickers offer, and everybody who could be put on it.
/// </summary>
/// <param name="Chosen">
/// The filing as it stands, which is the one authority on it. There is no second copy in nodes: the
/// screen resolves the ids out of <paramref name="Tree"/>, which it needs for the pickers anyway,
/// and two representations of one answer is one place for them to disagree.
/// </param>
/// <param name="Tree">Every node in the corpus, which is what the pickers offer.</param>
/// <param name="Everybody">Everybody in the corpus, each with where they belonged that day.</param>
/// <param name="Me">
/// Whoever is using this install, or nobody while nothing has said. They are drawn on every meeting
/// and stored on none — a row saying the owner of the corpus was at their own meeting says nothing.
/// </param>
public sealed record MeetingAsClassified(
    Meeting Meeting,
    MeetingFiling Chosen,
    IReadOnlyList<Node> Tree,
    IReadOnlyList<PersonAsOfTheMeeting> Everybody,
    Person? Me);

/// <summary>
/// The corpus side of the screen a meeting is filed from: what to offer, what it is filed under
/// now, and the one thing that screen writes back.
/// </summary>
/// <remarks>
/// <para>
/// Beside <see cref="MeetingReading"/> and not inside <see cref="HumanLayer"/>, which is where the
/// writes are and would have been the shorter answer. That type is on the audit floor because it
/// holds <c>SettleTheMicrophone</c>, the one row where <em>a channel is never a person</em> reaches
/// disk; growing it with a walk that has nothing to do with that invariant makes its own sentence
/// about being the single place less true. This composes it, the way <c>MeetingReading</c> composes
/// <c>MeetingWork</c>.
/// </para>
/// <para>
/// Nothing here caches, for the reason <c>MeetingsDrawer</c> gives about opening a context per
/// read: what is on screen has to be what is on disk.
/// </para>
/// </remarks>
public sealed class MeetingClassifying(CorpusDbContext context, TimeProvider clock)
{
    /// <summary>One meeting, as the screen that files it needs it.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public MeetingAsClassified Of(Guid meetingId)
    {
        var meeting = context.Meetings.AsNoTracking().FirstOrDefault(row => row.Id == meetingId)
            ?? throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");

        var tree = context.Nodes
            .AsNoTracking()
            .OrderBy(node => node.Name)
            .ToArray();

        var byId = tree.ToDictionary(node => node.Id);

        var links = context.MeetingNodes
            .AsNoTracking()
            .Where(link => link.MeetingId == meetingId)
            .ToArray();

        var named = context.MeetingPeople
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId)
            .ToArray();

        var everybody = Everybody(meeting.StartedAt, byId);

        var chosen = new MeetingFiling(
            // No shape, and there is nothing to read one off: what a shape opened is a pre-fill and
            // the meeting carries no record of it. Re-opening a filed meeting shows the filing with
            // no chip lit, which is honest about what the corpus holds.
            null,
            Paths(links, MeetingNodeRole.WorkOf, byId),
            Paths(links, MeetingNodeRole.Counterpart, byId),
            Paths(links, MeetingNodeRole.About, byId),
            Slots(named, everybody));

        return new MeetingAsClassified(
            meeting,
            chosen,
            tree,
            everybody,
            new HumanLayer(context, clock).Me());
    }

    /// <summary>
    /// What one meeting is filed under, each link as the whole path up to its root.
    /// </summary>
    /// <remarks>
    /// The short read, for the screen a meeting is read from: it draws a row of pills and has no
    /// use for everybody or the affiliations, which is what <see cref="Of"/> pays for. It shares
    /// <see cref="PathTo"/> with that method, because a second walk up the tree is a second place
    /// to get the order wrong and the two screens would then disagree about one meeting.
    /// <para>
    /// It reads every node rather than the linked ones and their ancestors, which was two queries
    /// and a loop. The tree stops at three levels and belongs to one person, so the whole of it is
    /// the cheapest read in the schema — and <see cref="Of"/> reads it whole two methods away.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(MeetingNodeRole Role, NodePath Path)> Filing(Guid meetingId)
    {
        var links = context.MeetingNodes
            .AsNoTracking()
            .Where(link => link.MeetingId == meetingId)
            .ToArray();

        if (links.Length == 0)
        {
            return [];
        }

        var byId = context.Nodes.AsNoTracking().ToDictionary(node => node.Id);

        return
        [
            .. links
                .Select(link => (link.Role, Path: PathTo(byId[link.NodeId], byId)))
                .OrderBy(filed => filed.Role)
                .ThenBy(filed => filed.Path.Nodes[^1].Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Files the meeting under exactly what is on the screen, and takes off what is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One transaction around the whole walk. Somebody pressed <em>Guardar</em> once, and half a
    /// classification is a meeting filed wrong — which is a meeting nobody finds again.
    /// <see cref="HumanLayer"/>'s methods each save; inside an open transaction on the same context
    /// they enlist in it, so the boundary moves here without that type changing, exactly as
    /// <c>HumanLayer.Describe</c> already spans more than one write.
    /// </para>
    /// <para>
    /// The order inside is load-bearing and is not the cheapest arrangement by accident: the nodes
    /// are resolved and the links written <em>before</em> the people are resolved, so a person the
    /// corpus does not hold is discovered after links are already on disk. That is what makes the
    /// transaction provable rather than merely present — with the resolution hoisted above every
    /// write, the test that drops the transaction would still pass.
    /// </para>
    /// <para>
    /// There is no <c>Unclassify</c> beside this. <c>Save(meetingId, MeetingFiling.Nothing)</c> is
    /// it: the same diff with an empty left-hand side takes every link and every naming off.
    /// </para>
    /// <para>
    /// That the meeting is there at all is asked here rather than left to a foreign key, and it is
    /// asked first, so nothing is written on the way to finding out. The screen asks a second
    /// question of its own — <see cref="MeetingScreen.ItMayBeFiled"/>, which keeps the press off a
    /// meeting whose recording is still being written — and that one stays a screen's: it is about
    /// a window being open at the wrong moment, and the corpus has no half-written meeting to see.
    /// What the corpus can answer for itself is whether the meeting exists.
    /// </para>
    /// </remarks>
    /// <exception cref="MeetingStageException">This corpus holds no such meeting.</exception>
    /// <exception cref="ArgumentException">
    /// The screen offered a node or a person this corpus does not hold, which is a defect in the
    /// screen rather than something a person did — so it stops rather than being reported as a
    /// corpus that could not be read.
    /// </exception>
    public void Save(Guid meetingId, MeetingFiling chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        var human = new HumanLayer(context, clock);
        using var filing = context.Database.BeginTransaction();

        if (!context.Meetings.Any(row => row.Id == meetingId))
        {
            throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");
        }

        var wanted = chosen.Links;
        var nodes = NodesHeld([.. wanted.Select(link => link.Node)]);
        var stored = context.MeetingNodes.Where(link => link.MeetingId == meetingId).ToArray();

        // Adding is unconditional because HumanLayer.Link finds the row first and hands back the
        // one that is there, untouched — so saving the same classification twice moves nothing,
        // and a diff written out here would be a second implementation of that rule.
        foreach (var (node, role) in wanted)
        {
            human.Link(meetingId, nodes[node], role);
        }

        // What is coming off was read from disk a moment ago, so it is looked up and not demanded:
        // a link whose node the `nodes` table no longer holds is a foreign key the database
        // already refuses, and taking the application down over one on the way to deleting it
        // would be the refusal doing harm rather than good.
        var gone = stored.Where(link => !wanted.Contains((link.NodeId, link.Role))).ToArray();
        var unlinking = NodesBy([.. gone.Select(link => link.NodeId)]);

        foreach (var link in gone.Where(link => unlinking.ContainsKey(link.NodeId)))
        {
            human.Unlink(meetingId, unlinking[link.NodeId], link.Role);
        }

        var naming = chosen.Named;
        var people = PeopleHeld([.. naming.Select(named => named.Person)]);
        var known = context.MeetingPeople.Where(row => row.MeetingId == meetingId).ToArray();

        foreach (var (person, role) in naming)
        {
            human.Name(meetingId, people[person], role);
        }

        var unnamed = known.Where(row => !naming.Contains((row.PersonId, row.Role))).ToArray();
        var taking = PeopleBy([.. unnamed.Select(row => row.PersonId)]);

        foreach (var row in unnamed.Where(row => taking.ContainsKey(row.PersonId)))
        {
            human.Unname(meetingId, taking[row.PersonId], row.Role);
        }

        filing.Commit();
    }

    /// <summary>The nodes with these ids, whichever of them the corpus holds.</summary>
    /// <remarks>One query and not one per id.</remarks>
    private Dictionary<Guid, Node> NodesBy(IReadOnlyList<Guid> ids)
    {
        var wanted = ids.Distinct().ToArray();

        return context.Nodes.Where(node => wanted.Contains(node.Id)).ToDictionary(node => node.Id);
    }

    private Dictionary<Guid, Person> PeopleBy(IReadOnlyList<Guid> ids)
    {
        var wanted = ids.Distinct().ToArray();

        return context.People.Where(person => wanted.Contains(person.Id)).ToDictionary(person => person.Id);
    }

    /// <summary>The nodes with these ids, and a refusal for any the corpus does not hold.</summary>
    /// <remarks>
    /// The refusing half, for the ids that came off a screen rather than off disk. The ordering in
    /// <see cref="Save"/> is what lets a whole batch be resolved at once without the refusal moving
    /// in front of every write.
    /// </remarks>
    private Dictionary<Guid, Node> NodesHeld(IReadOnlyList<Guid> ids)
    {
        var found = NodesBy(ids);

        Refuse(ids, found.Keys, "node");
        return found;
    }

    /// <summary>The people with these ids, and a refusal for any the corpus does not hold.</summary>
    private Dictionary<Guid, Person> PeopleHeld(IReadOnlyList<Guid> ids)
    {
        var found = PeopleBy(ids);

        Refuse(ids, found.Keys, "person");
        return found;
    }

    /// <summary>
    /// Stops on the first id the corpus does not hold.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentException"/> and not <c>ClassificationException</c>, and that is a
    /// decision rather than a consequence: a row the screen offered that no <c>nodes</c> or
    /// <c>people</c> row carries is a defect in the screen, not something a person did.
    /// <c>ScreenFailures.Reportable</c> names neither, so it stops the application rather than
    /// reading to somebody as a corpus that could not be opened.
    /// </remarks>
    private static void Refuse(IReadOnlyList<Guid> wanted, ICollection<Guid> found, string what)
    {
        foreach (var id in wanted.Where(id => !found.Contains(id)))
        {
            throw new ArgumentException($"This corpus holds no {what} {id}.", nameof(wanted));
        }
    }

    /// <summary>
    /// The path from a node's root down to it, root first.
    /// </summary>
    /// <remarks>
    /// It stops on a node it has already walked through as well as on one with no parent, and that
    /// is not a hypothetical guard: a walk up a cycle never ends, and this one runs on the thread a
    /// window is drawn on. Nothing in the application can write a cycle — the factories set a
    /// parent once and a depth with it — but the importer writes nodes straight into the tables,
    /// and a read that hangs the window is a worse answer than a path that stops early.
    /// </remarks>
    private static NodePath PathTo(Node deepest, IReadOnlyDictionary<Guid, Node> byId)
    {
        var up = new List<Node>();
        var walked = new HashSet<Guid>();
        var node = deepest;

        while (walked.Add(node.Id))
        {
            up.Add(node);

            if (node.ParentId is not { } parent || !byId.TryGetValue(parent, out var above))
            {
                break;
            }

            node = above;
        }

        up.Reverse();
        return new NodePath(up);
    }

    /// <summary>One column of the filing, as the paths the screen draws it from.</summary>
    private static IReadOnlyList<ChosenPath> Paths(
        IReadOnlyList<MeetingNode> links,
        MeetingNodeRole role,
        IReadOnlyDictionary<Guid, Node> byId) =>
    [
        .. links
            .Where(link => link.Role == role)
            .Select(link => PathTo(byId[link.NodeId], byId))
            .OrderBy(path => path.Nodes[^1].Name, StringComparer.Ordinal)
            .Select(path => new ChosenPath([.. path.Nodes.Select(node => node.Id)])),
    ];

    /// <summary>
    /// One place per person the meeting names, carrying every role it names them under.
    /// </summary>
    /// <remarks>
    /// Somebody named twice — attended and the subject, which is a one to one — is one row on the
    /// screen with both toggles on, and not two rows of the same person.
    /// </remarks>
    private static IReadOnlyList<ChosenPerson> Slots(
        IReadOnlyList<MeetingPerson> named,
        IReadOnlyList<PersonAsOfTheMeeting> everybody)
    {
        var byName = everybody.ToDictionary(
            found => found.Person.Id,
            found => found.Person.DisplayName);

        return
        [
            .. named
                .GroupBy(row => row.PersonId)
                .OrderBy(person => byName.GetValueOrDefault(person.Key, string.Empty), StringComparer.Ordinal)
                .ThenBy(person => person.Key)
                .Select(person => new ChosenPerson(
                    person.Key,
                    person.Any(row => row.Role is MeetingPersonRole.Attended),
                    person.Any(row => row.Role is MeetingPersonRole.Subject))),
        ];
    }

    /// <summary>
    /// Everybody in the corpus, each with the organizations they belonged to on the day of the
    /// meeting.
    /// </summary>
    /// <remarks>
    /// Everybody and not only the people already on this meeting, because the screen offers all of
    /// them: somebody chosen on a pill has to show their affiliation there and then, and a read
    /// covering only what is already filed would leave that line empty until the meeting was saved
    /// and opened again.
    /// </remarks>
    private IReadOnlyList<PersonAsOfTheMeeting> Everybody(
        UtcTimestamp startedAt,
        IReadOnlyDictionary<Guid, Node> byId)
    {
        var affiliations = context.Affiliations
            .AsNoTracking()
            .ToArray()
            .Where(affiliation => affiliation.Held(startedAt))
            .ToLookup(affiliation => affiliation.PersonId);

        return
        [
            .. context.People
                .AsNoTracking()
                .OrderBy(person => person.DisplayName)
                .ToArray()
                .Select(person => new PersonAsOfTheMeeting(
                    person,
                    [
                        .. affiliations[person.Id]
                            .Where(affiliation => byId.ContainsKey(affiliation.OrganizationId))
                            .Select(affiliation => (
                                Organization: byId[affiliation.OrganizationId],
                                Since: affiliation.StartedAt))
                            .OrderBy(at => at.Organization.Name, StringComparer.Ordinal),
                    ])),
        ];
    }
}
