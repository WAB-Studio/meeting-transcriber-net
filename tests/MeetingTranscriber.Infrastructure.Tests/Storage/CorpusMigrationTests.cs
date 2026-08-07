using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

public class CorpusMigrationTests
{
    /// <summary>Every table arquitectura.md §5.1 names, so none goes quietly missing.</summary>
    private static readonly string[] ExpectedTables =
    [
        "schema_migrations",
        "meetings",
        "artifacts",
        "capture_runs",
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

        objects.ShouldContain("utterances_fts");
        objects.ShouldContain("summaries_fts");

        foreach (var table in new[] { "utterances", "summaries" })
        {
            foreach (var moment in new[] { "insert", "delete", "update" })
            {
                objects.ShouldContain($"{table}_fts_after_{moment}");
            }
        }
    }
}
