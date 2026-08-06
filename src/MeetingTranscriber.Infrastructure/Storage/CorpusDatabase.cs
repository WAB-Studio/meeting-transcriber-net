using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The one place a corpus is opened. Every context leaves here configured the same way, so no
/// caller has to remember which pragmas the design depends on.
/// </summary>
public static class CorpusDatabase
{
    /// <summary>How long a writer waits for the lock before giving up, in milliseconds.</summary>
    public const int BusyTimeoutMilliseconds = 5_000;

    /// <summary>
    /// EF's bookkeeping table, named as arquitectura.md §5.1 names it rather than
    /// __EFMigrationsHistory, because a person reading the corpus should recognise it.
    /// </summary>
    public const string MigrationsHistoryTable = "schema_migrations";

    public static DbContextOptions<CorpusDbContext> OptionsFor(string path, bool readOnly = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var options = new DbContextOptionsBuilder<CorpusDbContext>()
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsHistoryTable(MigrationsHistoryTable))
            .AddInterceptors(new CorpusPragmaInterceptor(readOnly));

        if (readOnly)
        {
            // Nothing read here can be saved back, so tracking every row would be bookkeeping
            // for a write that the connection itself refuses.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }

        return options.Options;
    }

    /// <summary>Opens the corpus, creating the file if it is not there.</summary>
    public static CorpusDbContext Open(string path) => new(OptionsFor(path));

    /// <summary>Opens the corpus read only, which is how the MCP server reads it.</summary>
    public static CorpusDbContext OpenReadOnly(string path) => new(OptionsFor(path, readOnly: true));

    /// <summary>Opens the corpus and brings its schema up to this build.</summary>
    public static CorpusDbContext OpenMigrated(string path)
    {
        var context = Open(path);
        context.Database.Migrate();
        return context;
    }
}
