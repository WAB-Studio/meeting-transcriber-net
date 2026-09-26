using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// What a whole corpus was left with, as opposed to one meeting: every decision, every action,
/// every open question, out of the one extraction of each meeting that counts.
/// </summary>
/// <remarks>
/// The corpus-wide plural of what <c>MeetingReadingTests</c> holds for one meeting, and the facts
/// here are the ones only a second meeting can show: which run answers when a corpus holds several,
/// what a window over time does and does not take in, and that a meeting on its way out says
/// nothing.
/// </remarks>
public class CorpusStatementsTests
{
    private static readonly UtcTimestamp August =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp July =
        UtcTimestamp.From(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Only the extraction accepted last answers.
    /// </summary>
    /// <remarks>
    /// Red the moment <c>CorpusSearch.TheRunThatCounts</c> is dropped for a plain join, which does
    /// not fail — it answers, twice, with the same decision worded two ways and nothing on either
    /// to say which is current. That is the failure this narrowing exists against, and it is the
    /// only reason this type is in <c>Infrastructure</c> rather than beside its caller.
    /// </remarks>
    [Fact]
    public void Only_the_extraction_a_person_accepted_last_answers()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "the first go");
        MeetingRows.Extracted(
            context,
            meeting,
            August,
            accepted: UtcTimestamp.From(August.Value.AddHours(1)),
            "the second go");

        var settled = CorpusStatements.Of(context, LeftKind.Decision, null, null, 20);

        settled.Count.ShouldBe(1);
        settled[0].Says.ShouldBe("the second go");
        settled[0].MeetingId.ShouldBe(meeting);
        settled[0].StartedAt.ShouldBe(August);
        settled[0].TurnOrdinal.ShouldBe(0);
        settled[0].At.ShouldBe(MeetingRows.At(0));
        settled[0].SpeakerLabel.ShouldBe(MeetingRows.SpeakerLabel);
        settled[0].Quoted.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Every statement carries the artifact its quotation was read out of, off the citation's own
    /// column and never off the meeting.
    /// </summary>
    /// <remarks>
    /// A meeting can hold more than one paid response — re-transcribing files a new one and never
    /// replaces the old — so <em>the meeting's response</em> is not an answer to <em>what was this
    /// sentence quoted out of</em>. The column exists for that, and this is the reader that hands
    /// it to somebody who will check the quotation against it.
    /// </remarks>
    [Fact]
    public void A_statement_says_what_its_quotation_was_read_out_of()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Single()
            .SourceSha256
            .ShouldBe(MeetingRows.QuotedFromSha256);
    }

    /// <summary>
    /// A statement names whoever the voice it quotes belongs to, once somebody has named it, and
    /// null while nobody has — through both <see cref="CorpusStatements.Of"/> and
    /// <see cref="CorpusStatements.Under"/>.
    /// </summary>
    [Fact]
    public void A_statement_names_whoever_said_it_once_somebody_named_the_voice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var node = human.Root(NodeKind.Organization, "acme");
        var meeting = Extracted(context, August, "lo que se dijo");
        human.Link(meeting, node, MeetingNodeRole.WorkOf);

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).Single().SpeakerName.ShouldBeNull();
        CorpusStatements.Under(context, node.Id, 20)
            .Single(statement => statement.Kind == LeftKind.Decision)
            .SpeakerName.ShouldBeNull();

        var somebody = human.Add("Renata");
        human.Assign(meeting, MeetingRows.SpeakerLabel, somebody);

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Single()
            .SpeakerName
            .ShouldBe("Renata");
        CorpusStatements.Under(context, node.Id, 20)
            .Single(statement => statement.Kind == LeftKind.Decision)
            .SpeakerName
            .ShouldBe("Renata");
    }

    /// <summary>
    /// A run nobody accepted is not read at all, because a run that was refused or never filed is
    /// one whose sentences never held up against the meeting.
    /// </summary>
    [Fact]
    public void An_extraction_nobody_accepted_says_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: null, "never accepted");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting on its way out says nothing, for the reason every search branch gives: it is being
    /// deleted, and what it decided will not be there when somebody opens it.
    /// </summary>
    [Fact]
    public void A_meeting_on_its_way_out_says_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).Count.ShouldBe(1);

        var onItsWayOut = context.Meetings.Single(row => row.Id == meeting);
        onItsWayOut.LifecycleState = LifecycleState.Deleting;

        // Both, because the corpus refuses one without the other: a CHECK holds the state and the
        // instant together, so a meeting on its way out always says when it started going.
        onItsWayOut.DeletedAt = August;
        context.SaveChanges();

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).ShouldBeEmpty();
    }

    /// <summary>
    /// The window is on the meeting's own start, closed at the bottom and open at the top.
    /// </summary>
    /// <remarks>
    /// Two calls over adjoining stretches answer exactly what one call over both would have, which
    /// is what makes a listing pageable by time. It is on the meeting and not on when a model wrote
    /// the sentence, because what somebody asking for <em>the decisions of August</em> means is the
    /// meetings of August — a meeting re-summarised in September is still August's.
    /// </remarks>
    [Fact]
    public void The_window_is_the_meetings_own_start_and_takes_neither_end_twice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, July, "july");
        Extracted(context, August, "august");

        var wholeOfAugust = CorpusStatements.Of(
            context,
            LeftKind.Decision,
            UtcTimestamp.From(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            UtcTimestamp.From(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
            20);

        wholeOfAugust.Select(statement => statement.Says).ShouldBe(["august"]);

        // The meeting's own instant as the top of the window leaves it out, and as the bottom takes
        // it in — which is the pair that makes two adjoining calls one answer.
        CorpusStatements.Of(context, LeftKind.Decision, null, August, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["july"]);

        CorpusStatements.Of(context, LeftKind.Decision, August, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["august"]);
    }

    /// <summary>
    /// Newest meeting first, and bounded by what the caller asked for.
    /// </summary>
    [Fact]
    public void The_newest_meeting_answers_first_and_the_limit_is_kept()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, July, "july");
        Extracted(context, August, "august");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["august", "july"]);

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 1)
            .Select(statement => statement.Says)
            .ShouldBe(["august"]);
    }

    /// <summary>
    /// Each of the three sections answers about itself and never about another, which is the whole
    /// of what the table name decides.
    /// </summary>
    /// <remarks>
    /// An open question is stored under a column of a different name, so a listing that reached for
    /// <c>statement</c> on every table would fail on exactly one of the three — and it is the one
    /// nothing else in this repository lists.
    /// </remarks>
    [Fact]
    public void Every_section_answers_about_itself()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled"]);

        CorpusStatements.Of(context, LeftKind.Action, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled, to do"]);

        CorpusStatements.Of(context, LeftKind.Question, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled, unresolved"]);
    }

    /// <summary>
    /// Every field a statement carries, read through <see cref="CorpusStatements.Of"/> and through
    /// <see cref="CorpusStatements.Under"/>, answers the same — but for <see cref="Statement.Kind"/>,
    /// which each read is asked for differently.
    /// </summary>
    /// <remarks>
    /// Red with a column added to one of <c>OneSection</c>'s two callers and forgotten in the
    /// other: the shared projection still compiles, and this is the only fact comparing the two
    /// reads field for field.
    /// </remarks>
    [Fact]
    public void Both_reads_of_a_statement_answer_with_the_same_ten_fields()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var node = human.Root(NodeKind.Organization, "acme");
        var meeting = Extracted(context, August, "lo que se dijo");
        human.Link(meeting, node, MeetingNodeRole.WorkOf);

        var of = CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).Single();
        var under = CorpusStatements.Under(context, node.Id, 20)
            .Single(statement => statement.Kind == LeftKind.Decision);

        of.MeetingId.ShouldBe(under.MeetingId);
        of.StartedAt.ShouldBe(under.StartedAt);
        of.Title.ShouldBe(under.Title);
        of.Says.ShouldBe(under.Says);
        of.TurnOrdinal.ShouldBe(under.TurnOrdinal);
        of.At.ShouldBe(under.At);
        of.Quoted.ShouldBe(under.Quoted);
        of.SpeakerLabel.ShouldBe(under.SpeakerLabel);
        of.SourceSha256.ShouldBe(under.SourceSha256);
        of.SpeakerName.ShouldBe(under.SpeakerName);
    }

    /// <summary>
    /// A node's own meetings come back oldest first, the other order from the corpus-wide listings —
    /// a node's story is read forward, like a history.
    /// </summary>
    [Fact]
    public void A_nodes_own_meetings_come_back_oldest_first()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var root = human.Root(NodeKind.Organization, "acme");

        var julyMeeting = Extracted(context, July, "july");
        var augustMeeting = Extracted(context, August, "august");
        human.Link(julyMeeting, root, MeetingNodeRole.WorkOf);
        human.Link(augustMeeting, root, MeetingNodeRole.WorkOf);

        // Extracted writes all three sections, so a node's read carries all three per meeting,
        // meeting order first.
        CorpusStatements.Under(context, root.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe([
                "july", "july, to do", "july, unresolved",
                "august", "august, to do", "august, unresolved",
            ]);
    }

    /// <summary>
    /// A node answers with everything hanging off its children, up to two levels down, and a node
    /// with no children of its own answers only for itself.
    /// </summary>
    /// <remarks>Red with any one arm of <c>CorpusSearch.Underneath</c> dropped.</remarks>
    [Fact]
    public void A_node_answers_with_everything_hanging_off_its_children()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var organization = human.Root(NodeKind.Organization, "acme");
        var initiative = human.Under(organization, NodeKind.Initiative, "migración");
        var topic = human.Under(initiative, NodeKind.Topic, "corte de agosto");

        var onOrganization = Extracted(context, August, "sobre la organización");
        human.Link(onOrganization, organization, MeetingNodeRole.WorkOf);

        var onInitiative = Extracted(context, UtcTimestamp.From(August.Value.AddHours(1)), "sobre la iniciativa");
        human.Link(onInitiative, initiative, MeetingNodeRole.WorkOf);

        var onTopic = Extracted(context, UtcTimestamp.From(August.Value.AddHours(2)), "sobre el tema");
        human.Link(onTopic, topic, MeetingNodeRole.WorkOf);

        CorpusStatements.Under(context, organization.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe([
                "sobre la organización", "sobre la organización, to do", "sobre la organización, unresolved",
                "sobre la iniciativa", "sobre la iniciativa, to do", "sobre la iniciativa, unresolved",
                "sobre el tema", "sobre el tema, to do", "sobre el tema, unresolved",
            ]);

        CorpusStatements.Under(context, initiative.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe([
                "sobre la iniciativa", "sobre la iniciativa, to do", "sobre la iniciativa, unresolved",
                "sobre el tema", "sobre el tema, to do", "sobre el tema, unresolved",
            ]);

        CorpusStatements.Under(context, topic.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["sobre el tema", "sobre el tema, to do", "sobre el tema, unresolved"]);
    }

    /// <summary>
    /// A meeting filed under two nodes below one parent answers that parent once, not twice.
    /// </summary>
    /// <remarks>Red with the <c>EXISTS</c> written as a join.</remarks>
    [Fact]
    public void A_meeting_filed_under_two_nodes_below_one_parent_is_answered_once()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var initiative = human.Root(NodeKind.Initiative, "migración");
        var first = human.Under(initiative, NodeKind.Topic, "primer tema");
        var second = human.Under(initiative, NodeKind.Topic, "segundo tema");

        var meeting = Extracted(context, August, "lo que se dijo");
        human.Link(meeting, first, MeetingNodeRole.WorkOf);
        human.Link(meeting, second, MeetingNodeRole.WorkOf);

        CorpusStatements.Under(context, initiative.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["lo que se dijo", "lo que se dijo, to do", "lo que se dijo, unresolved"]);
    }

    /// <summary>
    /// The three sections of one meeting come back interleaved in the order they were said, and the
    /// sentences here are chosen so the section order and the alphabetical order of the sentences
    /// disagree — that is the whole point of the fact.
    /// </summary>
    /// <remarks>
    /// Two mutations, and both move a row: <c>kind_rank</c> dropped from the <c>ORDER BY</c> puts
    /// <c>"algo"</c> before <c>"lo primero"</c> at 1000 ms, because <c>says</c> then decides outright;
    /// <c>at_ms</c> dropped groups the two decisions, moving <c>"lo segundo"</c> ahead of the two
    /// inline entries at 1000 ms.
    /// </remarks>
    [Fact]
    public void The_three_sections_come_back_interleaved_in_the_order_they_were_said()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var node = human.Root(NodeKind.Organization, "lo que importa");

        var meeting = MeetingRows.Recorded(context, August, ["uno", "dos", "tres", "cuatro"]);
        var run = MeetingRows.Extracted(context, meeting, August, accepted: August, "lo primero");
        human.Link(meeting, node, MeetingNodeRole.WorkOf);

        // An action whose sentence sorts before the decision's, at the same offset, so that the
        // section key is the only thing putting the decision first. Ordinal 1 because
        // (extraction_run_id, ordinal) is unique per table and Extracted writes 0 in each of the
        // three.
        MeetingRows.Add(context, new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run,
            Ordinal = 1,
            Statement = "algo",
            Evidence = MeetingRows.Citing(context, meeting, 0),
            CreatedAt = August,
        });

        // A second decision at a later offset, so that the offset key is the only thing keeping it
        // out of the run above — where the section key alone would put it second.
        MeetingRows.Add(context, new Decision
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run,
            Ordinal = 1,
            Statement = "lo segundo",
            Evidence = MeetingRows.Citing(context, meeting, 2),
            CreatedAt = August,
        });

        CorpusStatements.Under(context, node.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["lo primero", "algo", "lo primero, to do", "lo primero, unresolved", "lo segundo"]);
    }

    /// <summary>The sibling of <see cref="Only_the_extraction_a_person_accepted_last_answers"/>, over
    /// <see cref="CorpusStatements.Under"/>.</summary>
    /// <remarks>Red with <c>CorpusSearch.TheRunThatCounts</c> replaced by a plain join.</remarks>
    [Fact]
    public void Only_the_extraction_a_person_accepted_last_answers_about_a_node()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var node = human.Root(NodeKind.Organization, "lo que importa");
        var meeting = Recorded(context, August);
        human.Link(meeting, node, MeetingNodeRole.WorkOf);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "the first go");
        MeetingRows.Extracted(
            context,
            meeting,
            August,
            accepted: UtcTimestamp.From(August.Value.AddHours(1)),
            "the second go");

        CorpusStatements.Under(context, node.Id, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["the second go", "the second go, to do", "the second go, unresolved"]);
    }

    /// <summary>The sibling of <see cref="A_meeting_on_its_way_out_says_nothing"/>, over
    /// <see cref="CorpusStatements.Under"/>.</summary>
    /// <remarks>Red with the <c>lifecycle_state</c> filter dropped.</remarks>
    [Fact]
    public void A_meeting_on_its_way_out_says_nothing_about_its_node()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);
        var node = human.Root(NodeKind.Organization, "lo que importa");
        var meeting = Recorded(context, August);
        human.Link(meeting, node, MeetingNodeRole.WorkOf);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "what was settled");

        CorpusStatements.Under(context, node.Id, 20).Count.ShouldBe(3);

        var onItsWayOut = context.Meetings.Single(row => row.Id == meeting);
        onItsWayOut.LifecycleState = LifecycleState.Deleting;
        onItsWayOut.DeletedAt = August;
        context.SaveChanges();

        CorpusStatements.Under(context, node.Id, 20).ShouldBeEmpty();
    }

    /// <summary>A node id this corpus does not hold is refused by name, and not answered with an
    /// empty list — which would read as a node nothing was ever said about.</summary>
    /// <remarks>Red with the existence check dropped, where the call answers with an empty list.</remarks>
    [Fact]
    public void A_node_this_corpus_does_not_hold_is_refused_by_name()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var missing = Guid.NewGuid();

        Should.Throw<ClassificationException>(() => CorpusStatements.Under(context, missing, 20))
            .Message.ShouldContain(missing.ToString());
    }

    /// <summary>One meeting with one turn to cite, which is all any fact here reads.</summary>
    private static Guid Recorded(CorpusDbContext context, UtcTimestamp when) =>
        MeetingRows.Recorded(context, when, ["lo que se dijo"]);

    /// <summary>A meeting recorded and summarised in one go, for the facts about two of them.</summary>
    private static Guid Extracted(CorpusDbContext context, UtcTimestamp when, string saying)
    {
        var meeting = Recorded(context, when);
        MeetingRows.Extracted(context, meeting, when, accepted: when, saying);
        return meeting;
    }
}
