using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

public class CorpusSchemaTests
{
    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string When = "2026-08-05T14:00:00.000Z";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void A_connection_arrives_with_foreign_keys_on_and_the_file_in_wal()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Sql.Scalar(context, "PRAGMA foreign_keys;").ShouldBe(1L);
        Sql.Scalar(context, "PRAGMA journal_mode;").ShouldBe("wal");
    }

    [Fact]
    public void An_artifact_cannot_belong_to_a_meeting_that_is_not_there()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO artifacts (id, meeting_id, kind, origin, relative_path, byte_size, sha256, created_at)
            VALUES ('a', 'no-such-meeting', 'audio', 'source', 'audio.wav', 1, '{Sha256}', '{When}');
            """));
    }

    [Fact]
    public void A_meeting_cannot_hold_a_source_profile_the_domain_does_not_know()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => InsertMeeting(context, sourceProfile: "stereo"));
    }

    [Fact]
    public void A_meeting_cannot_hold_a_lifecycle_state_that_is_not_one_of_the_three()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => InsertMeeting(context, lifecycleState: "transcribed"));
    }

    [Fact]
    public void An_active_meeting_cannot_carry_a_deletion_date()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => Sql.Execute(
            context,
            $"UPDATE meetings SET deleted_at = '{When}' WHERE id = '{MeetingId}';"));
    }

    [Fact]
    public void The_same_legacy_meeting_cannot_be_imported_twice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertMeeting(context, legacyId: "2026-03-01-planning");

        Should.Throw<SqliteException>(() => InsertMeeting(
            context,
            id: "22222222-2222-2222-2222-222222222222",
            legacyId: "2026-03-01-planning"));
    }

    [Fact]
    public void A_turn_can_only_sit_on_the_meeting_channel_the_user_channel_or_neither()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertUtterance(context, id: "u0", ordinal: 0, channel: "0"));
        Should.NotThrow(() => InsertUtterance(context, id: "u1", ordinal: 1, channel: "1"));
        Should.NotThrow(() => InsertUtterance(context, id: "u2", ordinal: 2, channel: "NULL"));
        Should.Throw<SqliteException>(() => InsertUtterance(context, id: "u3", ordinal: 3, channel: "2"));
    }

    [Fact]
    public void A_turn_cannot_end_before_it_starts()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('u', '{MeetingId}', 0, 5000, 4000, 0, 'channel_0', 'backwards');
            """));
    }

    [Theory]
    [InlineData("deepgram_response", "derived")]
    [InlineData("audio", "derived")]
    [InlineData("transcript", "source")]
    [InlineData("summary", "source")]
    public void An_artifact_cannot_claim_the_wrong_side_of_the_source_line(string kind, string origin)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => InsertArtifact(context, kind, origin));
    }

    [Theory]
    [InlineData("deepgram_response", "source")]
    [InlineData("audio", "source")]
    [InlineData("manifest", "source")]
    [InlineData("transcript", "derived")]
    [InlineData("summary", "derived")]
    public void An_artifact_on_the_right_side_of_the_line_goes_in(string kind, string origin)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertArtifact(context, kind, origin));
    }

    [Fact]
    public void A_job_cannot_sit_in_a_state_the_design_does_not_have()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertJob(context, id: "j0", state: "awaiting_user"));
        Should.Throw<SqliteException>(() => InsertJob(context, id: "j1", state: "in_progress"));
    }

    [Fact]
    public void Deleting_a_meeting_takes_its_rows_with_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertUtterance(context, id: "u0", ordinal: 0, channel: "0");
        InsertArtifact(context, "audio", "source");

        Sql.Execute(context, $"DELETE FROM meetings WHERE id = '{MeetingId}';");

        Sql.Scalar(context, "SELECT count(*) FROM utterances;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM artifacts;").ShouldBe(0L);
    }

    [Fact]
    public void Search_finds_a_turn_by_its_words_and_forgets_it_once_deleted()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertUtterance(context, id: "u0", ordinal: 0, channel: "0", text: "the drift budget is fifty milliseconds");

        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'drift';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'loopback';").ShouldBe(0L);

        Sql.Execute(context, "DELETE FROM utterances WHERE id = 'u0';");

        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'drift';").ShouldBe(0L);
    }

    [Fact]
    public void Search_finds_a_summary_by_its_words_and_forgets_it_once_deleted()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertSummary(context, "what the meeting settled", "the drift budget is fifty milliseconds");

        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'settled';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'drift';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'loopback';").ShouldBe(0L);

        Sql.Execute(context, "DELETE FROM summaries;");

        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'settled';").ShouldBe(0L);
    }

    /// <summary>How the MCP server reads a corpus somebody else may be writing.</summary>
    [Fact]
    public void A_read_only_corpus_reads_what_the_writer_left_and_refuses_to_add_to_it()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            InsertMeeting(writing);
        }

        SqliteConnection.ClearAllPools();
        using var reading = CorpusDatabase.OpenReadOnly(corpus.DatabasePath);

        Sql.Scalar(reading, "SELECT count(*) FROM meetings;").ShouldBe(1L);
        Sql.Scalar(reading, "PRAGMA foreign_keys;").ShouldBe(1L);
        Should.Throw<SqliteException>(() => InsertMeeting(
            reading,
            id: "22222222-2222-2222-2222-222222222222"));
    }

    private static void InsertMeeting(
        CorpusDbContext context,
        string id = MeetingId,
        string? legacyId = null,
        string sourceProfile = "multichannel",
        string lifecycleState = "active") =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, legacy_id, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{id}', {(legacyId is null ? "NULL" : $"'{legacyId}'")}, '{When}', '{sourceProfile}', 'es', '{lifecycleState}', '{When}', '{When}');
            """);

    private static void InsertUtterance(
        CorpusDbContext context,
        string id,
        int ordinal,
        string channel,
        string text = "anything at all") =>
        Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{id}', '{MeetingId}', {ordinal}, 0, 1000, {channel}, 'channel_0', '{text}');
            """);

    private static void InsertArtifact(CorpusDbContext context, string kind, string origin) =>
        Sql.Execute(context, $"""
            INSERT INTO artifacts (id, meeting_id, kind, origin, relative_path, byte_size, sha256, created_at)
            VALUES ('{kind}-{origin}', '{MeetingId}', '{kind}', '{origin}', '{kind}.{origin}', 1, '{Sha256}', '{When}');
            """);

    /// <summary>A summary needs the extraction run it came out of, so both go in here.</summary>
    private static void InsertSummary(CorpusDbContext context, string @abstract, string body)
    {
        Sql.Execute(context, $"""
            INSERT INTO extraction_runs (id, meeting_id, provider, prompt_version, schema_version, input_hash, state, created_at)
            VALUES ('e0', '{MeetingId}', 'claude_code', '1', '1', '{Sha256}', 'succeeded', '{When}');
            """);

        Sql.Execute(context, $"""
            INSERT INTO summaries (id, meeting_id, extraction_run_id, abstract, body, created_at)
            VALUES ('s0', '{MeetingId}', 'e0', '{@abstract}', '{body}', '{When}');
            """);
    }

    private static void InsertJob(CorpusDbContext context, string id, string state) =>
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{id}', '{MeetingId}', 'transcribe', '{state}', '{id}', '{When}', 0);
            """);
}
