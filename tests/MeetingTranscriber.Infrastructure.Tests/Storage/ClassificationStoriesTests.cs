using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The thirteen meetings arquitectura.md §5.3 lists, all in one corpus. They are what the
/// classification vocabulary was closed against: three classes of node, three roles for a link to
/// one, two for a person, and templates that carry only a name.
/// </summary>
/// <remarks>
/// <para>
/// Two things are asked of each, and the second is what catches a vocabulary too small. It has to
/// store — anybody can force that — and it has to store <em>only</em> what somebody knows, with no
/// invented owner, no project called "several" and no organization standing in for a role there is
/// no word for. The shape this replaced passed the first and failed the second on five of them.
/// </para>
/// <para>
/// Then it has to be found again — by organization, by initiative and by person — because a
/// classification nothing can query is a column somebody fills in and never reads.
/// </para>
/// </remarks>
public class ClassificationStoriesTests
{
    /// <summary>
    /// Every link in the thirteen, and nothing else in the corpus. The counts are the "no invented
    /// rows" half: a story that only fitted by adding a node nobody would name, or by collapsing
    /// two links into one, moves one of these numbers.
    /// </summary>
    [Fact]
    public void The_thirteen_go_in_and_the_corpus_holds_exactly_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        context.Meetings.Count().ShouldBe(Stories.All.Count);
        context.Nodes.Count().ShouldBe(Stories.Tree.Count);
        context.MeetingNodes.Count().ShouldBe(Stories.Links.Count);
        context.MeetingPeople.Count().ShouldBe(Stories.Named.Count);

        foreach (var title in Stories.All)
        {
            var expected = Stories.Links
                .Where(link => link.Meeting == title)
                .Select(link => (link.Node, link.Role));
            var stored = context.MeetingNodes
                .Where(link => link.MeetingId == stories.MeetingId(title))
                .ToArray()
                .Select(link => (Node: stories.NodeName(link.NodeId), link.Role));

            stored.ShouldBe(expected, ignoreOrder: true, customMessage: title);
        }
    }

    /// <summary>
    /// Who each meeting names, and how. Everything not in the list has nobody on it, which is the
    /// conference: two hundred people were there and none of them is a row anybody will type.
    /// </summary>
    [Fact]
    public void Every_story_names_the_people_it_names_and_no_others()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        foreach (var title in Stories.All)
        {
            var expected = Stories.Named
                .Where(named => named.Meeting == title)
                .Select(named => (named.Person, named.Role));
            var stored = context.MeetingPeople
                .Where(named => named.MeetingId == stories.MeetingId(title))
                .ToArray()
                .Select(named => (Person: stories.PersonName(named.PersonId), named.Role));

            stored.ShouldBe(expected, ignoreOrder: true, customMessage: title);
        }
    }

    /// <summary>
    /// An organization finds everything under it, however deep the meeting was linked. The faculty
    /// is the point: nothing links that meeting to it, and searching it finds the meeting anyway.
    /// </summary>
    [Theory]
    [InlineData("Facultad de Ingeniería", Stories.Class)]
    [InlineData("AcmeCo", Stories.Candidate)]
    [InlineData("Python Software Foundation", Stories.Conference)]
    [InlineData("Northwind", Stories.TwoCompanies)]
    [InlineData("Orchard", Stories.Selling, Stories.TwoCompanies, Stories.Support)]
    [InlineData(
        "TechSed",
        Stories.Interviewing,
        Stories.TwoProjects,
        Stories.Selling,
        Stories.Team,
        Stories.Dismissal,
        Stories.OneToOne,
        Stories.Daily,
        Stories.Support)]
    public void Searching_an_organization_finds_the_meetings_under_it(string organization, params string[] expected)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        stories.Under(context, organization).ShouldBe(expected, ignoreOrder: true);
    }

    /// <summary>
    /// The same query one level down. Support is the one that matters: that meeting hangs off a
    /// ticket, and the initiative above finds it without being told the ticket exists.
    /// </summary>
    [Theory]
    [InlineData("Coati", Stories.TwoProjects, Stories.Team, Stories.Daily)]
    [InlineData("Huemul", Stories.TwoProjects)]
    [InlineData("Soporte", Stories.Support)]
    [InlineData("PyCon 2026", Stories.Conference)]
    [InlineData("Algoritmos II 2026-1", Stories.Class)]
    public void Searching_an_initiative_finds_the_meetings_under_it(string initiative, params string[] expected)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        stories.Under(context, initiative).ShouldBe(expected, ignoreOrder: true);
    }

    /// <summary>
    /// By person, whichever way they are on it. Dana never attended and is found; Jo is on one
    /// meeting under two roles and is found once, not twice.
    /// </summary>
    [Theory]
    [InlineData("Vikram", Stories.Interviewing)]
    [InlineData("Sam", Stories.Team)]
    [InlineData("Dana", Stories.Dismissal)]
    [InlineData("Jo", Stories.OneToOne)]
    public void Searching_a_person_finds_the_meetings_they_are_on(string person, params string[] expected)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        var found = context.MeetingPeople
            .Where(named => named.PersonId == stories.PersonId(person))
            .Select(named => named.MeetingId)
            .Distinct()
            .ToArray()
            .Select(stories.MeetingName);

        found.ShouldBe(expected, ignoreOrder: true);
    }

    /// <summary>
    /// The meeting with nothing on it. It is stored, and it is the one a listing of the
    /// unclassified turns up — which is the whole of what that story asks for.
    /// </summary>
    [Fact]
    public void A_meeting_classified_as_nothing_is_still_a_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Stories.Write(context);

        var unclassified = context.Meetings
            .Where(meeting => !context.MeetingNodes.Any(link => link.MeetingId == meeting.Id))
            .Select(meeting => meeting.Title)
            .ToArray();

        unclassified.ShouldBe([Stories.Casual]);
    }

    /// <summary>
    /// Somebody at two organizations at once, and a candidate whose employer at the time of the
    /// interview is still readable after they were hired. Both are what one organization on a
    /// person could not hold, and both are stories rather than hypotheticals.
    /// </summary>
    [Fact]
    public void A_person_belongs_to_as_many_organizations_as_they_do()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.Write(context);

        var contractor = context.Affiliations
            .Where(affiliation => affiliation.PersonId == stories.PersonId("Sam"))
            .ToArray()
            .Select(affiliation => stories.NodeName(affiliation.OrganizationId));

        contractor.ShouldBe(["Orchard", "TechSed"], ignoreOrder: true);

        var candidate = context.Affiliations
            .Where(affiliation => affiliation.PersonId == stories.PersonId("Vikram"))
            .ToArray();

        // Where they worked the day of the interview, which hiring them did not overwrite.
        stories.NodeName(candidate.Single(spell => spell.Held(Stories.Interviewed)).OrganizationId)
            .ShouldBe("Northwind");
        stories.NodeName(candidate.Single(spell => spell.Held(Stories.Now)).OrganizationId)
            .ShouldBe("TechSed");
    }
}

/// <summary>
/// The thirteen, written into a corpus. Everything goes in through the model rather than through
/// SQL, so the factories that place a node and open an affiliation run on the way in.
/// </summary>
internal sealed class Stories
{
    public const string Class = "una clase de la facultad";
    public const string Casual = "una junta casual";
    public const string Candidate = "una entrevista, yo candidato";
    public const string Interviewing = "una entrevista, yo entrevistando";
    public const string TwoProjects = "una reunion sobre dos proyectos";
    public const string Selling = "un vendedor con un cliente";
    public const string Team = "un PM con su equipo";
    public const string Conference = "una conferencia";
    public const string TwoCompanies = "una reunion entre dos empresas";
    public const string Dismissal = "RRHH desvinculando";
    public const string OneToOne = "un 1:1 recurrente";
    public const string Daily = "la daily del equipo";
    public const string Support = "soporte post-venta";

    /// <summary>The day of the interview, the day the candidate moved, and today.</summary>
    public static readonly UtcTimestamp Interviewed = On(2025, 3, 4);

    public static readonly UtcTimestamp Hired = On(2025, 4, 1);

    public static readonly UtcTimestamp Now = On(2026, 8, 6);

    private readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Person> people = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Meeting> meetings = new(StringComparer.Ordinal);

    private Stories()
    {
    }

    public static IReadOnlyList<string> All { get; } =
    [
        Class, Casual, Candidate, Interviewing, TwoProjects, Selling, Team,
        Conference, TwoCompanies, Dismissal, OneToOne, Daily, Support,
    ];

    /// <summary>
    /// Every node the thirteen need, parent first. Nothing here is scaffolding: each one is
    /// something somebody would name out loud.
    /// </summary>
    public static IReadOnlyList<(string Name, string? Parent, NodeKind Kind)> Tree { get; } =
    [
        // Neither a client nor an employer, which is what "organization" and not "company" was for.
        ("Facultad de Ingeniería", null, NodeKind.Organization),
        ("TechSed", null, NodeKind.Organization),
        ("AcmeCo", null, NodeKind.Organization),
        ("Orchard", null, NodeKind.Organization),
        ("Northwind", null, NodeKind.Organization),
        ("Python Software Foundation", null, NodeKind.Organization),

        ("Algoritmos II 2026-1", "Facultad de Ingeniería", NodeKind.Initiative),
        ("Coati", "TechSed", NodeKind.Initiative),
        ("Huemul", "TechSed", NodeKind.Initiative),
        ("Ventas", "TechSed", NodeKind.Initiative),
        ("Soporte", "TechSed", NodeKind.Initiative),
        ("PyCon 2026", "Python Software Foundation", NodeKind.Initiative),

        // The only third level any of the thirteen reaches, and the reason there is one.
        ("ticket #4312", "Soporte", NodeKind.Topic),
    ];

    /// <summary>How each meeting relates to the tree. The comments are the stories themselves.</summary>
    public static IReadOnlyList<(string Meeting, string Node, MeetingNodeRole Role)> Links { get; } =
    [
        // The course carries the classification; the faculty is reached through it, unnamed.
        (Class, "Algoritmos II 2026-1", MeetingNodeRole.WorkOf),

        // Nothing at all, on purpose — which is why there is no line here for the casual one.

        // A company I do not work for and no project inside it. There is nowhere an invented
        // project would go, and nothing asks for one.
        (Candidate, "AcmeCo", MeetingNodeRole.Counterpart),
        (Interviewing, "TechSed", MeetingNodeRole.WorkOf),

        // Two links. One project column made this a choice, and the answer was a project called
        // "varios".
        (TwoProjects, "Coati", MeetingNodeRole.WorkOf),
        (TwoProjects, "Huemul", MeetingNodeRole.WorkOf),

        // Both sides of the table at once, each saying which side it is.
        (Selling, "Ventas", MeetingNodeRole.WorkOf),
        (Selling, "Orchard", MeetingNodeRole.Counterpart),
        (Team, "Coati", MeetingNodeRole.WorkOf),

        // Attending a conference is not work of it, and the organiser is not across the table.
        (Conference, "PyCon 2026", MeetingNodeRole.About),

        // Not "of" either company. Two counterparts, and no third link inventing an owner.
        (TwoCompanies, "Orchard", MeetingNodeRole.Counterpart),
        (TwoCompanies, "Northwind", MeetingNodeRole.Counterpart),
        (Dismissal, "TechSed", MeetingNodeRole.WorkOf),

        // An organization with no project under it, which is the ordinary shape of a one to one.
        (OneToOne, "TechSed", MeetingNodeRole.WorkOf),
        (Daily, "Coati", MeetingNodeRole.WorkOf),

        // The subject is a ticket, so it is a topic, and the client is still the other side.
        (Support, "ticket #4312", MeetingNodeRole.WorkOf),
        (Support, "Orchard", MeetingNodeRole.Counterpart),
    ];

    /// <summary>Who is named on a meeting, and how.</summary>
    public static IReadOnlyList<(string Meeting, string Person, MeetingPersonRole Role)> Named { get; } =
    [
        (Interviewing, "Vikram", MeetingPersonRole.Attended),
        (Team, "Sam", MeetingPersonRole.Attended),

        // Discussed before they are in the room: the subject of a meeting they never attended.
        (Dismissal, "Dana", MeetingPersonRole.Subject),

        // Both, and both true. One row per person made this a choice, and lost the one not picked.
        (OneToOne, "Jo", MeetingPersonRole.Attended),
        (OneToOne, "Jo", MeetingPersonRole.Subject),
    ];

    /// <summary>The thirteen, filed.</summary>
    public static Stories Write(CorpusDbContext context) => Write(context, theFilingToo: true);

    /// <summary>
    /// The thirteen, with every link and every naming left off.
    /// </summary>
    /// <remarks>
    /// The same tree, the same people and the same meetings, and nothing saying what any of them
    /// was about — which is a corpus something else can file into. <c>MeetingClassifyingTests</c>
    /// is what needs it: the screen's own save cannot be proved against a corpus where the answer
    /// is already on disk, because writing what is already there is what that save does when
    /// nothing changed.
    /// </remarks>
    public static Stories WriteWithNothingFiled(CorpusDbContext context) =>
        Write(context, theFilingToo: false);

    public Guid MeetingId(string title) => meetings[title].Id;

    public Guid NodeId(string name) => nodes[name].Id;

    public Guid PersonId(string name) => people[name].Id;

    public string MeetingName(Guid id) => meetings.Single(entry => entry.Value.Id == id).Key;

    public string NodeName(Guid id) => nodes.Single(entry => entry.Value.Id == id).Key;

    public string PersonName(Guid id) => people.Single(entry => entry.Value.Id == id).Key;

    /// <summary>
    /// Every meeting linked to this node or to anything under it. Two joins and no recursion, which
    /// is what capping the tree at three levels bought.
    /// </summary>
    public IReadOnlyList<string> Under(CorpusDbContext context, string name)
    {
        var root = nodes[name].Id;
        var found = context.MeetingNodes
            .Join(context.Nodes, link => link.NodeId, node => node.Id, (link, node) => new { link, node })
            .Where(pair => pair.node.Id == root
                || pair.node.ParentId == root
                || context.Nodes.Any(above => above.Id == pair.node.ParentId && above.ParentId == root))
            .Select(pair => pair.link.MeetingId)
            .Distinct()
            .ToArray();

        return [.. found.Select(MeetingName)];
    }

    private static UtcTimestamp On(int year, int month, int day) =>
        UtcTimestamp.From(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    private static Stories Write(CorpusDbContext context, bool theFilingToo)
    {
        var stories = new Stories();
        stories.Build(context, theFilingToo);
        context.SaveChanges();
        return stories;
    }

    /// <param name="theFilingToo">
    /// Whether the links and the namings go in with the rest. Everything above them is what the
    /// corpus knows before anybody classified anything, and they are what classifying it writes.
    /// </param>
    private void Build(CorpusDbContext context, bool theFilingToo)
    {
        foreach (var (name, parent, kind) in Tree)
        {
            var node = parent is null
                ? Node.Root(Guid.NewGuid(), kind, name, Now)
                : Node.Under(Guid.NewGuid(), nodes[parent], kind, name, Now);
            nodes[name] = node;
            context.Nodes.Add(node);
        }

        foreach (var name in new[] { "Renée", "Vikram", "Sam", "Dana", "Jo" })
        {
            Somebody(context, name, isMe: name == "Renée");
        }

        // The candidate, hired after the interview. Two spells rather than one edited over the
        // other: reading the interview back still says who they worked for that day.
        At(context, "Vikram", "Northwind", until: Hired);
        At(context, "Vikram", "TechSed", from: Hired);
        // The contractor, at both at once, with neither spell closed.
        At(context, "Sam", "Orchard");
        At(context, "Sam", "TechSed");
        At(context, "Dana", "TechSed");
        At(context, "Jo", "TechSed");

        foreach (var title in All)
        {
            var meeting = new Meeting
            {
                Id = Guid.NewGuid(),
                Title = title,
                StartedAt = Now,
                SourceProfile = SourceProfile.Multichannel,
                Language = "es",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            meetings[title] = meeting;
            context.Meetings.Add(meeting);
        }

        if (!theFilingToo)
        {
            return;
        }

        foreach (var (meeting, node, role) in Links)
        {
            context.MeetingNodes.Add(new MeetingNode
            {
                MeetingId = meetings[meeting].Id,
                NodeId = nodes[node].Id,
                Role = role,
                CreatedAt = Now,
            });
        }

        foreach (var (meeting, person, role) in Named)
        {
            context.MeetingPeople.Add(new MeetingPerson
            {
                MeetingId = meetings[meeting].Id,
                PersonId = people[person].Id,
                Role = role,
                CreatedAt = Now,
            });
        }
    }

    private void Somebody(CorpusDbContext context, string name, bool isMe = false)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            IsMe = isMe,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        people[name] = person;
        context.People.Add(person);
    }

    private void At(
        CorpusDbContext context,
        string person,
        string organization,
        UtcTimestamp? from = null,
        UtcTimestamp? until = null) =>
        context.Affiliations.Add(Affiliation.At(
            Guid.NewGuid(), people[person], nodes[organization], Now, from, until));
}
