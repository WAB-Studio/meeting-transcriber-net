using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// What an extraction produced is named by the run and the position inside it, and never by an id:
/// projecting the same accepted extraction again mints new ids, so anything somebody pinned to one
/// would point at a row that is gone.
/// </summary>
/// <remarks>
/// The half a writer cannot see is that a position belongs to one row. Two rows sharing one makes
/// what somebody pinned there ambiguous rather than wrong — nothing fails, and the note is read
/// against whichever row the join happened to reach.
/// </remarks>
public class ExtractionPositionTests
{
    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string ExtractionRunId = "22222222-2222-2222-2222-222222222222";
    private const string JobId = "44444444-4444-4444-4444-444444444444";
    private const string When = "2026-08-05T14:00:00.000Z";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Everything an extraction projects that a person can pin something to, and what each one calls
    /// the line it holds. The note a person writes is anchored too and is not here: it carries a
    /// state rather than a line, so it is inserted by hand below.
    /// </summary>
    public static TheoryData<string, string> Positioned =>
        new() { { "decisions", "statement" }, { "action_items", "statement" }, { "open_questions", "question" } };

    /// <summary>Every table the probes in here hold to the rule, and the whole of it.</summary>
    private static readonly string[] Probed =
        ["action_item_progress", "action_items", "decisions", "open_questions"];

    /// <summary>
    /// What the anchor promises, over every table the model says carries it: the two columns are
    /// mapped, a run has one row at each position, and the position belongs to a run that exists.
    /// </summary>
    /// <remarks>
    /// Discovered off the model rather than off the schema, because the schema can only be asked
    /// which tables have both columns — and a projection that took a position and forgot to map one
    /// of them would answer that question by disappearing from it. Implementing
    /// <see cref="IExtractionPosition"/> is what makes a row one of these, and that is a decision
    /// somebody makes in the domain and cannot make halfway. What is read off SQLite is the
    /// enforcement, because what has to refuse the second row is the database and not the writer.
    /// <c>action_item_progress</c> passes the uniqueness on its primary key, which is that pair and
    /// says it more strongly than an index.
    /// </remarks>
    [Fact]
    public void A_position_in_an_extraction_belongs_to_one_row_wherever_it_is_stored()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var anchored = Anchored(context);

        anchored.ShouldNotBeEmpty();
        foreach (var table in anchored)
        {
            var columns = Columns(context, table);

            columns.ShouldContain("extraction_run_id", $"'{table}' is named by its extraction");
            columns.ShouldContain("ordinal", $"'{table}' is named by its position in that extraction");

            var unique = Sql.Strings(context, $"""
                SELECT name FROM pragma_index_list('{table}') WHERE "unique" = 1;
                """);

            var anchor = unique.Any(index => Sql
                .Strings(context, $"SELECT name FROM pragma_index_info('{index}') ORDER BY name;")
                .SequenceEqual(["extraction_run_id", "ordinal"]));

            anchor.ShouldBeTrue(
                $"'{table}' is named by its extraction and its position, and nothing stops two rows "
                + "from sharing one — so what somebody pinned there could come to mean either");

            Sql.Strings(context, $"""
                SELECT "table" FROM pragma_foreign_key_list('{table}') WHERE "from" = 'extraction_run_id';
                """).ShouldContain(
                    "extraction_runs",
                    $"'{table}' names a run, so it cannot be left holding one the corpus does not have");
        }
    }

    /// <summary>
    /// The half discovery cannot do on its own: that every table the model anchors is one the probes
    /// below actually insert into. They read a position out of a database and cannot invent the rest
    /// of a row, so a table added later is held to the rule by going red here until somebody probes
    /// it — which is a smaller promise than covering it unattended, and one that is kept.
    /// </summary>
    [Fact]
    public void Nothing_the_model_anchors_goes_unprobed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Anchored(context).ShouldBe(
            Probed,
            "a row named by its extraction and its position is refused a second one at that position "
            + "by the database, and nothing here has watched this table do it");
    }

    [Theory]
    [MemberData(nameof(Positioned))]
    public void One_extraction_cannot_put_two_things_in_the_same_position(string table, string text)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Extracted(context);

        Insert(context, table, text, id: "first", ordinal: 0, line: "lo que salió de la reunión");

        Should.Throw<SqliteException>(() => Insert(
            context, table, text, id: "second", ordinal: 0, line: "lo mismo, dicho de otra forma"));
    }

    /// <summary>
    /// A second extraction numbers its own rows from zero, so the same position in two runs is two
    /// positions and nothing the second one produced collides with the first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Positioned))]
    public void The_same_position_in_another_extraction_is_another_position(string table, string text)
    {
        const string secondJob = "55555555-5555-5555-5555-555555555555";
        const string secondRun = "66666666-6666-6666-6666-666666666666";

        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Extracted(context);
        Extracted(context, job: secondJob, run: secondRun, key: "extract/2");

        Insert(context, table, text, id: "first", ordinal: 0, line: "lo de la primera corrida");

        Should.NotThrow(() => Insert(
            context, table, text, id: "second", ordinal: 0, line: "lo de la segunda", run: secondRun));
    }

    /// <summary>A position counts from zero, so a row before the first one has nowhere to be.</summary>
    [Theory]
    [MemberData(nameof(Positioned))]
    public void A_position_that_is_not_a_position_is_refused(string table, string text)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Extracted(context);

        Should.Throw<SqliteException>(() => Insert(
            context, table, text, id: "before", ordinal: -1, line: "antes de la primera"));
    }

    /// <summary>
    /// And of the note itself, which is the fourth thing the model anchors. It holds a state rather
    /// than a line the meeting produced, so it is written here by hand — but the position it pins to
    /// is the same position, and one that counts from somewhere else would point at nothing.
    /// </summary>
    [Fact]
    public void A_position_that_is_not_a_position_is_refused_of_a_note_as_well()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Extracted(context);

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO action_item_progress (extraction_run_id, ordinal, state, owner_person_id, updated_at)
            VALUES ('{ExtractionRunId}', -1, 'open', NULL, '{When}');
            """));
    }

    /// <summary>
    /// An open question is a claim about the meeting the way a decision is, so it carries the turn
    /// it was raised at and cannot be stored pointing at one the meeting never had. The other two
    /// are held to this already; the table that is new is the one worth proving it of.
    /// </summary>
    [Fact]
    public void An_open_question_cannot_cite_a_turn_the_meeting_never_had()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Extracted(context);

        Should.Throw<SqliteException>(() => Insert(
            context,
            "open_questions",
            "question",
            id: "invented",
            ordinal: 0,
            line: "¿esto lo dijo alguien?",
            utteranceOrdinal: 7));
    }

    /// <summary>
    /// The tables the model anchors, in name order — every row type that took
    /// <see cref="IExtractionPosition"/> on, and nothing else.
    /// </summary>
    private static string[] Anchored(CorpusDbContext context) =>
        context.Model.GetEntityTypes()
            .Where(entity => typeof(IExtractionPosition).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.GetTableName()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static List<string> Columns(CorpusDbContext context, string table) =>
        Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');");

    /// <summary>
    /// The columns every projected row shares, so the rule is written once for the three tables
    /// that carry it rather than once per table. What each of them adds — a due date, who decided —
    /// is nullable and has nothing to do with the position.
    /// </summary>
    private static void Insert(
        CorpusDbContext context,
        string table,
        string text,
        string id,
        int ordinal,
        string line,
        string run = ExtractionRunId,
        int utteranceOrdinal = 0) =>
        Sql.Execute(context, $"""
            INSERT INTO {table} (
                id, meeting_id, extraction_run_id, ordinal, {text}, created_at,
                utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text, source_artifact_sha256)
            VALUES (
                '{id}', '{MeetingId}', '{run}', {ordinal}, '{line}', '{When}',
                {utteranceOrdinal}, 0, 1000, 'ch0:speaker_0', '{line}', '{Sha256}');
            """);

    /// <summary>
    /// A meeting with one turn and an accepted extraction over it, which is the least a projected
    /// row needs to exist at all. The meeting and its turn go in once; a second call adds another
    /// run over the same meeting.
    /// </summary>
    private static void Extracted(
        CorpusDbContext context,
        string job = JobId,
        string run = ExtractionRunId,
        string key = "extract/1")
    {
        if (Sql.Scalar(context, $"SELECT count(*) FROM meetings WHERE id = '{MeetingId}';") is 0L)
        {
            Sql.Execute(context, $"""
                INSERT INTO meetings (id, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
                VALUES ('{MeetingId}', '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');
                INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
                VALUES ('u0', '{MeetingId}', 0, 0, 1000, 0, 'ch0:speaker_0', 'lo que se dijo');
                """);
        }

        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{job}', '{MeetingId}', 'extract', 'succeeded', '{key}', '{When}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{run}', '{MeetingId}', '{job}', 'claude_code', '1', '1', '{Sha256}', '{When}', '{When}');
            """);
    }
}
