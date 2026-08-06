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
    private const string When = "2026-08-05T14:00:00.000Z";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

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

            Sql.Execute(context, $"""
                INSERT INTO extraction_runs (id, meeting_id, provider, prompt_version, schema_version, input_hash, state, created_at)
                VALUES ('{ExtractionRunId}', '{MeetingId}', 'claude_code', '1', '1', '{Sha256}', 'succeeded', '{When}');
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
        int utteranceOrdinal) =>
        Sql.Execute(context, $"""
            INSERT INTO action_items (
                id, meeting_id, extraction_run_id, ordinal, statement, due_date, created_at,
                utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text, source_artifact_sha256)
            VALUES (
                '{generation}-a{ordinal}', '{MeetingId}', '{ExtractionRunId}', {ordinal}, '{statement}', NULL, '{When}',
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
