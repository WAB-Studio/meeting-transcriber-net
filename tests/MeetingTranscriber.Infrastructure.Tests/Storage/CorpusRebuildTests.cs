using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// Deleting every derived row and projecting again is what docs/corpus.md promises is safe. These
/// tests are that promise: what a person put in survives it, and what would have been lost in
/// silence now fails out loud instead.
/// </summary>
public class CorpusRebuildTests
{
    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string ExtractionRunId = "22222222-2222-2222-2222-222222222222";
    private const string PersonId = "33333333-3333-3333-3333-333333333333";
    private const string JobId = "44444444-4444-4444-4444-444444444444";
    private const string When = "2026-08-05T14:00:00.000Z";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The four tables a rebuild empties, and the whole of what it is allowed to empty.</summary>
    private static readonly string[] Derived = ["action_items", "decisions", "summaries", "utterances"];

    /// <summary>
    /// The tables <c>docs/corpus.md</c> calls the human layer. Spelled out rather than derived,
    /// because the point of the list is to fail when one of them is missing from what was written.
    /// </summary>
    private static readonly string[] HumanLayerTables =
    [
        "nodes",
        "meeting_nodes",
        "templates",
        "people",
        "affiliations",
        "meeting_people",
        "speaker_assignments",
        "terminology_corrections",
        "action_item_progress",
    ];

    [Fact]
    public void Rebuilding_the_projections_leaves_every_action_where_its_owner_left_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");

        // Two actions out of one extraction, and a person moves both: one done and taken, one
        // dropped and unowned. Nothing below is inferable from the extraction.
        Sql.Execute(context, $"""
            INSERT INTO action_item_progress (extraction_run_id, ordinal, state, owner_person_id, updated_at)
            VALUES ('{ExtractionRunId}', 0, 'done', '{PersonId}', '{When}'),
                   ('{ExtractionRunId}', 1, 'dropped', NULL, '{When}');
            """);

        Rebuild(context);

        // Same extraction, projected again: new row ids everywhere, same positions.
        Sql.Scalar(context, "SELECT count(*) FROM action_items WHERE id LIKE 'second-%';").ShouldBe(2L);
        Sql.Scalar(context, $"""
            SELECT state FROM action_item_progress AS progress
            JOIN action_items AS action
              ON action.extraction_run_id = progress.extraction_run_id AND action.ordinal = progress.ordinal
            WHERE action.statement = 'send the budget';
            """).ShouldBe("done");
        Sql.Scalar(context, $"""
            SELECT owner_person_id FROM action_item_progress AS progress
            JOIN action_items AS action
              ON action.extraction_run_id = progress.extraction_run_id AND action.ordinal = progress.ordinal
            WHERE action.statement = 'send the budget';
            """).ShouldBe(PersonId);
        Sql.Scalar(context, $"SELECT state FROM action_item_progress WHERE ordinal = 1;").ShouldBe("dropped");
        Sql.Scalar(context, "SELECT count(*) FROM action_item_progress;").ShouldBe(2L);
    }

    /// <summary>
    /// The state of an action is not a column of the table a rebuild throws away. Asserted on the
    /// schema rather than through a round trip: this is about where the column is, and a round
    /// trip would pass just as well with the human layer sitting in the derived table.
    /// </summary>
    [Fact]
    public void What_a_person_moved_is_not_a_column_of_what_the_rebuild_deletes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var derived = Sql.Strings(context, "SELECT name FROM pragma_table_info('action_items');");
        derived.ShouldNotContain("state");
        derived.ShouldNotContain("owner_person_id");
        derived.ShouldContain("ordinal");

        var human = Sql.Strings(context, "SELECT name FROM pragma_table_info('action_item_progress');");
        human.ShouldBe(["extraction_run_id", "ordinal", "state", "owner_person_id", "updated_at"], ignoreOrder: true);
    }

    /// <summary>
    /// A citation anchors on the meeting and the turn's position, so the reference an extraction
    /// wrote down still resolves after the turns have been thrown away and projected again under
    /// ids that no longer exist. This is the whole reason the anchor is not a turn's id.
    /// </summary>
    [Fact]
    public void Rebuilding_the_turns_leaves_every_claim_on_the_turn_it_came_from()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");

        Rebuild(context);

        // Not one id in common with the turns the claims were first projected against.
        Sql.Scalar(context, "SELECT count(*) FROM utterances WHERE id LIKE 'first-%';").ShouldBe(0L);
        Sql.Scalar(context, """
            SELECT turn.text FROM action_items AS action
            JOIN utterances AS turn
              ON turn.meeting_id = action.meeting_id AND turn.ordinal = action.utterance_ordinal
            WHERE action.statement = 'book the room';
            """).ShouldBe("turn 1");
        Sql.Scalar(context, """
            SELECT turn.text FROM decisions AS decision
            JOIN utterances AS turn
              ON turn.meeting_id = decision.meeting_id AND turn.ordinal = decision.utterance_ordinal
            WHERE decision.statement = 'the budget goes up';
            """).ShouldBe("turn 0");
    }

    /// <summary>
    /// The promise the anchor has to keep on the way in as well: a claim citing a position the
    /// meeting does not have is refused, so the corpus holds no claim with nothing behind it.
    /// </summary>
    [Fact]
    public void A_claim_cannot_cite_a_turn_the_meeting_never_had()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");

        Should.Throw<SqliteException>(() => InsertAction(
            context,
            generation: "invented",
            ordinal: 2,
            statement: "read the minutes",
            utteranceOrdinal: 7));
    }

    /// <summary>
    /// The other half of the same failure: a citation used to cascade, so redoing the turns took
    /// the decisions and actions citing them and reported nothing.
    /// </summary>
    [Fact]
    public void Deleting_turns_a_claim_cites_fails_instead_of_taking_the_claim_along()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");

        Should.Throw<SqliteException>(() => Sql.Execute(context, "DELETE FROM utterances;"));

        Sql.Scalar(context, "SELECT count(*) FROM action_items;").ShouldBe(2L);
        Sql.Scalar(context, "SELECT count(*) FROM utterances;").ShouldBe(2L);
    }

    /// <summary>
    /// Refusing that delete must not turn into refusing the one deletion the corpus is built for.
    /// A meeting takes its turns and its claims in the same statement, so the constraint above is
    /// satisfied by the end of it.
    /// </summary>
    [Fact]
    public void Deleting_a_meeting_still_takes_its_turns_its_actions_and_their_state()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");
        Sql.Execute(context, $"""
            INSERT INTO action_item_progress (extraction_run_id, ordinal, state, owner_person_id, updated_at)
            VALUES ('{ExtractionRunId}', 0, 'done', '{PersonId}', '{When}');
            """);

        Sql.Execute(context, $"DELETE FROM meetings WHERE id = '{MeetingId}';");

        Sql.Scalar(context, "SELECT count(*) FROM utterances;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM action_items;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM decisions;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM action_item_progress;").ShouldBe(0L);
        // The person outlives the meeting they were in. Only their state on it goes.
        Sql.Scalar(context, "SELECT count(*) FROM people;").ShouldBe(1L);
    }

    /// <summary>
    /// A rebuild keeps what a person marked. Extracting the meeting again deliberately does not.
    /// </summary>
    /// <remarks>
    /// The two look alike and are opposite. A rebuild reads the same accepted extraction and puts
    /// the same actions back at the same positions, so the state pinned to those positions still
    /// fits. A second extraction is a second run: it can reword an action, add one above another
    /// or drop one, so neither the position nor the text is the same action twice. Following
    /// either would hand somebody's "done" to a line they never read, and be invisible. So the
    /// state stays on the run it was recorded against — still there, still readable, superseded
    /// rather than moved — and the new run's actions arrive with nothing on them.
    /// </remarks>
    [Fact]
    public void A_second_extraction_leaves_the_first_ones_state_alone_and_starts_its_own_blank()
    {
        const string secondJob = "55555555-5555-5555-5555-555555555555";
        const string secondRun = "66666666-6666-6666-6666-666666666666";

        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");
        Sql.Execute(context, $"""
            INSERT INTO action_item_progress (extraction_run_id, ordinal, state, owner_person_id, updated_at)
            VALUES ('{ExtractionRunId}', 0, 'done', '{PersonId}', '{When}');
            """);

        // The meeting is extracted again and the new one is accepted. It reorders what it found:
        // 'send the budget' comes back second, and there is a new action above it.
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{secondJob}', '{MeetingId}', 'extract', 'succeeded', 'extract/{MeetingId}/2', '{When}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{secondRun}', '{MeetingId}', '{secondJob}', 'claude_code', '2', '1', '{Sha256}', '{When}', '{When}');
            """);
        InsertAction(context, "second", ordinal: 0, statement: "chase the invoice", utteranceOrdinal: 0, extractionRunId: secondRun);
        InsertAction(context, "second", ordinal: 1, statement: "send the budget", utteranceOrdinal: 0, extractionRunId: secondRun);

        // Nothing followed it across, by position or by wording.
        Sql.Scalar(context, $"SELECT count(*) FROM action_item_progress WHERE extraction_run_id = '{secondRun}';")
            .ShouldBe(0L);

        // And nothing was taken from where it was: the older run keeps its actions and its state.
        Sql.Scalar(context, $"SELECT state FROM action_item_progress WHERE extraction_run_id = '{ExtractionRunId}' AND ordinal = 0;")
            .ShouldBe("done");
        Sql.Scalar(context, $"SELECT count(*) FROM action_items WHERE extraction_run_id = '{ExtractionRunId}';")
            .ShouldBe(2L);
    }

    [Fact]
    public void One_extraction_cannot_put_two_actions_in_the_same_position()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");

        Should.Throw<SqliteException>(() => InsertAction(
            context,
            generation: "collision",
            ordinal: 0,
            statement: "send the budget again",
            utteranceOrdinal: 0));
    }

    /// <summary>
    /// The promise <c>docs/corpus.md</c> makes about the whole corpus and not only about the tables
    /// somebody thought to check: the four derived tables are thrown away and projected again, and
    /// every other table comes back byte for byte what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tables are read out of the database rather than listed here, so one added later is
    /// compared without anybody remembering to add it — and so are the columns, which is the failure
    /// worth catching: a human layer column quietly deleted looks exactly like a rebuild working.
    /// </para>
    /// <para>
    /// The human layer goes in through <see cref="HumanLayer"/> rather than through SQL. What is
    /// under test is what a rebuild leaves alone, so the rows have to be the ones the application
    /// would actually have written; hand-written SQL could satisfy this while the real write path
    /// put a column somewhere a rebuild deletes.
    /// </para>
    /// </remarks>
    [Fact]
    public void Deleting_every_derived_row_and_projecting_again_leaves_every_other_table_as_it_was()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, generation: "first");
        WriteHumanLayer(context, corpus.Root);

        var before = Snapshot(context);

        // The snapshot is only worth comparing if it holds the rows this is about. Without this the
        // test passes just as well over a corpus where nothing was ever written.
        foreach (var table in HumanLayerTables)
        {
            before.ShouldContain(row => row.StartsWith($"{table}: ", StringComparison.Ordinal), customMessage: table);
        }

        Rebuild(context);

        Snapshot(context).ShouldBe(before);
    }

    /// <summary>
    /// Every table the corpus holds except the four a rebuild is allowed to empty, each row as text
    /// with its columns in a fixed order.
    /// </summary>
    private static List<string> Snapshot(CorpusDbContext context) =>
    [
        .. Sql.Tables(context)
            .Where(table => !Derived.Contains(table, StringComparer.Ordinal))
            .SelectMany(table => Sql.Rows(context, table).Select(row => $"{table}: {row}")),
    ];

    /// <summary>
    /// A human layer with something in every one of its tables, written the way the application
    /// writes one.
    /// </summary>
    private static void WriteHumanLayer(CorpusDbContext context, DirectoryInfo root)
    {
        var clock = new SteppedClock(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));
        var human = new HumanLayer(context, clock);
        var meeting = Guid.Parse(MeetingId);

        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var coati = human.Under(techsed, NodeKind.Initiative, "Coati");
        var template = human.Template("trabajo");
        var ada = human.Add("Ada");
        var jo = human.Add("Jo");

        human.ThisIsMe(ada);
        human.Join(ada, techsed);
        human.Link(meeting, coati, MeetingNodeRole.WorkOf);
        human.Link(meeting, techsed, MeetingNodeRole.Counterpart);
        // Both roles at once, which is the pair a single row per person could not hold.
        human.Name(meeting, jo, MeetingPersonRole.Attended);
        human.Name(meeting, jo, MeetingPersonRole.Subject);
        human.Assign(meeting, "ch1:speaker_0", ada);
        human.Correct("quati", "Coati", under: coati);
        human.Mark(Guid.Parse(ExtractionRunId), ordinal: 0, ActionItemState.Done, jo);

        var stored = context.Meetings.Find(meeting)!;
        human.Describe(stored, "la daily del equipo", "arranca el sprint");
        human.Shape(stored, template);
    }

    /// <summary>What a rebuild does: every derived row goes, then the same sources go back in.</summary>
    private static void Rebuild(CorpusDbContext context)
    {
        Sql.Execute(context, "DELETE FROM action_items;");
        Sql.Execute(context, "DELETE FROM decisions;");
        Sql.Execute(context, "DELETE FROM summaries;");
        Sql.Execute(context, "DELETE FROM utterances;");

        Project(context, generation: "second", sources: false);
    }

    /// <summary>
    /// The derived rows of one meeting. The generation prefixes the ids, so a rebuild's rows are
    /// visibly not the ones before it — which is the whole reason neither the human layer nor a
    /// citation can key on them. Nothing the claims carry mentions the generation: what they hold
    /// is what the accepted extraction holds, and that file reads the same every time.
    /// </summary>
    private static void Project(CorpusDbContext context, string generation, bool sources = true)
    {
        if (sources)
        {
            Sql.Execute(context, $"""
                INSERT INTO meetings (id, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
                VALUES ('{MeetingId}', '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');
                """);

            Sql.Execute(context, $"""
                INSERT INTO people (id, display_name, is_me, created_at, updated_at)
                VALUES ('{PersonId}', 'Ada', 0, '{When}', '{When}');
                """);

            // A run belongs to the job that ran it, which is the only row holding where it stands.
            Sql.Execute(context, $"""
                INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
                VALUES ('{JobId}', '{MeetingId}', 'extract', 'succeeded', 'extract/{MeetingId}', '{When}', 1);
                """);

            Sql.Execute(context, $"""
                INSERT INTO extraction_runs (id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, created_at)
                VALUES ('{ExtractionRunId}', '{MeetingId}', '{JobId}', 'claude_code', '1', '1', '{Sha256}', '{When}');
                """);
        }

        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            Sql.Execute(context, $"""
                INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
                VALUES ('{generation}-u{ordinal}', '{MeetingId}', {ordinal}, {ordinal * 1000}, {(ordinal + 1) * 1000}, 0, 'channel_0', 'turn {ordinal}');
                """);
        }

        InsertDecision(context, generation, statement: "the budget goes up", utteranceOrdinal: 0);
        InsertAction(context, generation, ordinal: 0, statement: "send the budget", utteranceOrdinal: 0);
        InsertAction(context, generation, ordinal: 1, statement: "book the room", utteranceOrdinal: 1);
    }

    private static void InsertAction(
        CorpusDbContext context,
        string generation,
        int ordinal,
        string statement,
        int utteranceOrdinal,
        string extractionRunId = ExtractionRunId) =>
        Sql.Execute(context, $"""
            INSERT INTO action_items (
                id, meeting_id, extraction_run_id, ordinal, statement, due_date, created_at,
                utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text, source_artifact_sha256)
            VALUES (
                '{generation}-a{ordinal}', '{MeetingId}', '{extractionRunId}', {ordinal}, '{statement}', NULL, '{When}',
                {utteranceOrdinal}, 0, 1000, 'channel_0', '{statement}', '{Sha256}');
            """);

    private static void InsertDecision(
        CorpusDbContext context,
        string generation,
        string statement,
        int utteranceOrdinal) =>
        Sql.Execute(context, $"""
            INSERT INTO decisions (
                id, meeting_id, extraction_run_id, statement, decided_by_person_id, created_at,
                utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text, source_artifact_sha256)
            VALUES (
                '{generation}-d{utteranceOrdinal}', '{MeetingId}', '{ExtractionRunId}', '{statement}', NULL, '{When}',
                {utteranceOrdinal}, 0, 1000, 'channel_0', '{statement}', '{Sha256}');
            """);
}
