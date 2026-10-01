using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The rows <see cref="WhoTheyMightBe"/> ranks by, read from a real corpus: who has met whom, and
/// which organizations a meeting stands under, as the screen holds it.
/// </summary>
/// <remarks>
/// Rows are written with raw SQL so the CHECKs decide whether each is a legal one, the way
/// <c>CorpusSearchTests</c> does.
/// </remarks>
public class KnownPeopleTests
{
    private const string When = "2026-03-04T14:00:00.000Z";

    // Every id is written in the upper case EF writes a Guid in, because the readers under test
    // compare what they are given against text as stored, and a lower-case row would never match.

    private static readonly Guid MeetingA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    private static readonly Guid MeetingB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static readonly Guid P = Guid.Parse("00000001-0000-0000-0000-000000000000");

    private static readonly Guid Q = Guid.Parse("00000002-0000-0000-0000-000000000000");

    private static readonly Guid Owner = Guid.Parse("00000003-0000-0000-0000-000000000000");

    private static readonly Guid Company = Guid.Parse("c0000001-0000-0000-0000-000000000000");

    private static readonly Guid Project = Guid.Parse("c0000002-0000-0000-0000-000000000000");

    private static OpenedOver Over(
        Guid meeting,
        Guid[]? people = null,
        Guid[]? nodes = null) =>
        new(meeting, people ?? [], nodes ?? [], new HashSet<Guid>());

    private static CorpusDbContext Corpus(TemporaryCorpus corpus)
    {
        var context = corpus.OpenMigrated();
        Meeting(context, MeetingA);
        Meeting(context, MeetingB);
        Person(context, P, "Pablo Paz");
        Person(context, Q, "Quique Quiroga");
        Person(context, Owner, "Yo", isMe: true);
        return context;
    }

    private static bool MetSomebodyHere(PeopleAround around, Guid person) =>
        around.Known.Single(known => known.Person.Id == person).MetSomebodyHere;

    [Fact]
    public void Somebody_met_on_another_meeting_with_a_person_on_this_one_has_met_somebody_here()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);

        // Through meeting_people: P and Q on B, Q on A.
        Named(context, MeetingB, P);
        Named(context, MeetingB, Q);
        Named(context, MeetingA, Q);
        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeTrue();

        // Through speaker_assignments: the same, with Q only a voice on A.
        Sql.Execute(context, $"DELETE FROM meeting_people WHERE meeting_id = '{Upper(MeetingA)}';");
        Voice(context, MeetingA, Q);
        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeTrue();

        // And on B only as a voice: P a voice, Q named on A.
        Sql.Execute(context, $"DELETE FROM meeting_people WHERE meeting_id = '{Upper(MeetingB)}';");
        Voice(context, MeetingB, P);
        Voice(context, MeetingB, Q);
        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeTrue();
    }

    [Fact]
    public void Appearing_beside_somebody_only_on_this_meeting_is_not_having_met_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Named(context, MeetingA, P);
        Named(context, MeetingA, Q);

        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeFalse();
    }

    [Fact]
    public void Two_meetings_sharing_only_the_person_using_this_install_are_not_having_met()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Named(context, MeetingB, P);
        Voice(context, MeetingB, Owner, assignedBy: "channel");
        Voice(context, MeetingA, Owner, assignedBy: "channel");

        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeFalse();
    }

    [Fact]
    public void Somebody_on_the_unsaved_draft_counts_as_here()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Named(context, MeetingB, P);
        Named(context, MeetingB, Q);

        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA)), P).ShouldBeFalse();
        MetSomebodyHere(KnownPeople.Around(context, Over(MeetingA, people: [Q])), P).ShouldBeTrue();
    }

    [Fact]
    public void A_node_on_the_unsaved_draft_brings_its_organization()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Tree(context);

        KnownPeople.Around(context, Over(MeetingA)).Organizations.ShouldBeEmpty();
        KnownPeople.Around(context, Over(MeetingA, nodes: [Project])).Organizations.ShouldBe([Company]);
    }

    [Fact]
    public void Every_organization_somebody_ever_belonged_to_is_theirs()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Tree(context);
        Sql.Execute(context, $"""
            INSERT INTO affiliations (id, person_id, organization_id, organization_kind, started_at, ended_at, created_at)
            VALUES ('{Guid.NewGuid()}', '{Upper(P)}', '{Upper(Company)}', 'organization', '2020-01-01T00:00:00.000Z',
                    '2021-01-01T00:00:00.000Z', '{When}');
            """);

        KnownPeople.Around(context, Over(MeetingA)).Known.Single(known => known.Person.Id == P)
            .Organizations.ShouldBe([Company]);
    }

    [Fact]
    public void A_meeting_brings_the_organization_at_the_top_of_what_it_is_filed_under()
    {
        using var corpus = new TemporaryCorpus();
        using var context = Corpus(corpus);
        Tree(context);
        Sql.Execute(context, $"""
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{Upper(MeetingA)}', '{Upper(Project)}', 'work_of', '{When}');
            """);

        KnownPeople.Around(context, Over(MeetingA)).Organizations.ShouldBe([Company]);
    }

    /// <summary>
    /// A source scan and not an assembly reference: the layering already forbids <c>Infrastructure</c>
    /// and <c>Domain</c> referencing <c>Processing</c>, so a check on references could never go red.
    /// The three files that find a person and offer them are read without their prose, and none may
    /// name what summarises. It goes red with a <c>using MeetingTranscriber.Processing.Summaries;</c>
    /// added to any of them.
    /// </summary>
    [Fact]
    public void Who_somebody_might_be_is_found_by_code_that_reaches_nothing_that_summarises()
    {
        var files = new[]
        {
            RepositoryTree.At("src/MeetingTranscriber.Domain/Meetings/WhoTheyMightBe.cs"),
            RepositoryTree.At("src/MeetingTranscriber.Infrastructure/Meetings/KnownPeople.cs"),
            RepositoryTree.At("src/MeetingTranscriber.App/AddingSomebody.xaml.cs"),
        };

        files.Length.ShouldBe(3);
        foreach (var file in files)
        {
            var code = SourceText.WithoutProse(file);
            code.ShouldNotContain("Summaries", customMessage: file.Name);
            code.ShouldNotContain("ClaudeCode", customMessage: file.Name);
        }
    }

    private static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    private static void Meeting(CorpusDbContext context, Guid id) =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, title, context, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{Upper(id)}', 'reunion', NULL, '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');
            """);

    private static void Person(CorpusDbContext context, Guid id, string name, bool isMe = false) =>
        Sql.Execute(context, $"""
            INSERT INTO people (id, display_name, is_me, created_at, updated_at)
            VALUES ('{Upper(id)}', '{name}', {(isMe ? 1 : 0)}, '{When}', '{When}');
            """);

    private static void Named(CorpusDbContext context, Guid meeting, Guid person) =>
        Sql.Execute(context, $"""
            INSERT INTO meeting_people (meeting_id, person_id, role, created_at)
            VALUES ('{Upper(meeting)}', '{Upper(person)}', 'attended', '{When}');
            """);

    private static void Voice(CorpusDbContext context, Guid meeting, Guid person, string assignedBy = "person") =>
        Sql.Execute(context, $"""
            INSERT INTO speaker_assignments (meeting_id, speaker_label, person_id, assigned_by, assigned_at)
            VALUES ('{Upper(meeting)}', 'ch0:speaker_{person.ToString()[..8]}', '{Upper(person)}', '{assignedBy}', '{When}');
            """);

    private static void Tree(CorpusDbContext context) =>
        Sql.Execute(context, $"""
            INSERT INTO nodes (id, kind, name, depth, parent_id, parent_kind, parent_depth, created_at, updated_at)
            VALUES ('{Upper(Company)}', 'organization', 'Nubeko', 0, NULL, NULL, NULL, '{When}', '{When}');
            INSERT INTO nodes (id, kind, name, depth, parent_id, parent_kind, parent_depth, created_at, updated_at)
            VALUES ('{Upper(Project)}', 'initiative', 'Soporte', 1, '{Upper(Company)}', 'organization', 0, '{When}', '{When}');
            """);
}
