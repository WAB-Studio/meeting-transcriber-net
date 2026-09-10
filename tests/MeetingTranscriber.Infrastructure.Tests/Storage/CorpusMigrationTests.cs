using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

public class CorpusMigrationTests
{
    /// <summary>The last migration before the six indexes this suite migrates across.</summary>
    private const string BeforeTheIndexes = "20260816220840_OpenQuestionsAndDecisionPositions";

    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string NodeId = "22222222-2222-2222-2222-222222222222";
    private const string PersonId = "33333333-3333-3333-3333-333333333333";
    private const string JobId = "44444444-4444-4444-4444-444444444444";
    private const string RunId = "55555555-5555-5555-5555-555555555555";
    private const string DecisionId = "66666666-6666-6666-6666-666666666666";
    private const string ActionId = "77777777-7777-7777-7777-777777777777";
    private const string QuestionId = "88888888-8888-8888-8888-888888888888";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    private const string When = "2026-03-04T14:00:00.000Z";

    /// <summary>Every table arquitectura.md §5.1 names, so none goes quietly missing.</summary>
    private static readonly string[] ExpectedTables =
    [
        "schema_migrations",
        "meetings",
        "artifacts",
        "capture_runs",
        "capture_source_changes",
        "processing_jobs",
        "transcription_runs",
        "extraction_runs",
        "utterances",
        "summaries",
        "decisions",
        "action_items",
        "action_item_progress",
        "nodes",
        "meeting_nodes",
        "templates",
        "people",
        "affiliations",
        "meeting_people",
        "speaker_assignments",
        "terminology_corrections",
        "settings",
        "audit_events",
    ];

    [Fact]
    public void The_migrations_apply_to_an_empty_corpus()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();

        context.Database.GetAppliedMigrations().ShouldBeEmpty();
        context.Database.Migrate();

        context.Database.GetPendingMigrations().ShouldBeEmpty();
        context.Database.GetAppliedMigrations().Count().ShouldBe(context.Database.GetMigrations().Count());
    }

    [Fact]
    public void Migrating_an_up_to_date_corpus_does_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.NotThrow(() => context.Database.Migrate());
        context.Database.GetPendingMigrations().ShouldBeEmpty();
    }

    /// <summary>
    /// The guard against the model and the migrations drifting apart. Without it, a property
    /// added to an entity reaches the code long before it reaches anybody's corpus.
    /// </summary>
    [Fact]
    public void The_model_has_nothing_the_migrations_do_not()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void Every_table_the_design_names_exists()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var tables = Sql.Strings(context, "SELECT name FROM sqlite_master WHERE type = 'table';");

        foreach (var table in ExpectedTables)
        {
            tables.ShouldContain(table);
        }
    }

    /// <summary>
    /// EF calls its bookkeeping table __EFMigrationsHistory. The corpus is meant to be readable
    /// by a person with a SQLite browser, so it carries the name the design uses.
    /// </summary>
    [Fact]
    public void The_history_table_is_the_one_the_design_names()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var tables = Sql.Strings(context, "SELECT name FROM sqlite_master WHERE type = 'table';");

        tables.ShouldContain(CorpusDatabase.MigrationsHistoryTable);
        tables.ShouldNotContain("__EFMigrationsHistory");
    }

    [Fact]
    public void Search_and_its_triggers_are_there_even_though_the_model_cannot_see_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var objects = Sql.Strings(context, "SELECT name FROM sqlite_master WHERE type IN ('table', 'trigger');");

        foreach (var index in new[]
            {
                "utterances_fts", "summaries_fts", "meetings_fts", "nodes_fts",
                "people_fts", "decisions_fts", "action_items_fts", "open_questions_fts",
            })
        {
            objects.ShouldContain(index);
        }

        foreach (var table in new[]
            {
                "utterances", "summaries", "meetings", "nodes",
                "people", "decisions", "action_items", "open_questions",
            })
        {
            foreach (var moment in new[] { "insert", "delete", "update" })
            {
                objects.ShouldContain($"{table}_fts_after_{moment}");
            }
        }
    }

    /// <summary>
    /// A corpus that already had rows when an index arrived. An FTS5 external content index starts
    /// empty and its triggers only speak for what happens next, so a migration that creates one and
    /// stops leaves everything already in the corpus unfindable — with no error, and with
    /// <see cref="CorpusIntegrity.Check"/> reporting the corpus unsound, because an empty index over
    /// a table with rows in it is exactly the disagreement it compares for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The corpus is migrated in two steps because that is the only way to be on the wrong side of
    /// this: every other test here migrates an empty corpus in one go, and every fixture writes its
    /// rows afterwards, so the triggers always fire. The one this protects is somebody's own corpus
    /// on the day they pull.
    /// </para>
    /// <para>
    /// A row in every one of the six tables, and a word of its own in each, because six seeds are
    /// six statements and a test that wrote only a meeting would pass with five of them deleted.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_index_that_arrives_after_the_rows_holds_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();

        context.GetService<IMigrator>().Migrate(BeforeTheIndexes);
        SomethingInEveryTableTheNewIndexesCover(context);

        context.Database.Migrate();

        foreach (var (word, source) in new[]
            {
                ("ventanal", SearchSource.Meeting),
                ("higuera", SearchSource.Node),
                ("Ximena", SearchSource.Person),
                ("veredas", SearchSource.Decision),
                ("farolas", SearchSource.Action),
                ("cañerias", SearchSource.Question),
            })
        {
            CorpusSearch.Find(context, word).ShouldHaveSingleItem(word).Source.ShouldBe(source, word);
        }

        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// One row in each of the six tables the new indexes cover, written before they exist.
    /// </summary>
    /// <remarks>
    /// Raw SQL and not the model, because the point is to be standing at a migration that is not
    /// the last one — the model describes the schema as it will be, and half these columns are not
    /// there yet when this runs.
    /// </remarks>
    private static void SomethingInEveryTableTheNewIndexesCover(CorpusDbContext context)
    {
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, title, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{MeetingId}', 'la del ventanal', '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');

            INSERT INTO nodes (id, kind, name, depth, parent_id, parent_kind, parent_depth, created_at, updated_at)
            VALUES ('{NodeId}', 'organization', 'higuera', 0, NULL, NULL, NULL, '{When}', '{When}');

            INSERT INTO people (id, display_name, is_me, created_at, updated_at)
            VALUES ('{PersonId}', 'Ximena', 0, '{When}', '{When}');

            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{MeetingId}-0', '{MeetingId}', 0, 0, 1000, 0, 'ch0:speaker_0', 'algo dicho en voz alta');

            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{JobId}', '{MeetingId}', 'extract', 'succeeded', 'extract/{MeetingId}', '{When}', 1);

            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{RunId}', '{MeetingId}', '{JobId}', 'claude_code', '1', '1', '{Sha256}', '{When}', '{When}');

            INSERT INTO decisions (id, meeting_id, extraction_run_id, statement, ordinal,
                                   utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                   source_artifact_sha256, created_at)
            VALUES ('{DecisionId}', '{MeetingId}', '{RunId}', 'arreglamos las veredas', 0,
                    0, 0, 1000, 'ch0:speaker_0', 'algo dicho en voz alta', '{Sha256}', '{When}');

            INSERT INTO action_items (id, meeting_id, extraction_run_id, statement, ordinal,
                                      utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                      source_artifact_sha256, created_at)
            VALUES ('{ActionId}', '{MeetingId}', '{RunId}', 'contar las farolas', 0,
                    0, 0, 1000, 'ch0:speaker_0', 'algo dicho en voz alta', '{Sha256}', '{When}');

            INSERT INTO open_questions (id, meeting_id, extraction_run_id, question, ordinal,
                                        utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                        source_artifact_sha256, created_at)
            VALUES ('{QuestionId}', '{MeetingId}', '{RunId}', 'quien paga las cañerias', 0,
                    0, 0, 1000, 'ch0:speaker_0', 'algo dicho en voz alta', '{Sha256}', '{When}');
            """);

        // The node and the person only reach search through a meeting that names them.
        Sql.Execute(context, $"""
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{MeetingId}', '{NodeId}', 'work_of', '{When}');

            INSERT INTO meeting_people (meeting_id, person_id, role, created_at)
            VALUES ('{MeetingId}', '{PersonId}', 'attended', '{When}');
            """);
    }
}
