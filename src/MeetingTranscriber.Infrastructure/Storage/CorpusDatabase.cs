using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The one place a corpus is opened. Every context leaves here configured the same way, so no
/// caller has to remember which pragmas the design depends on.
/// </summary>
/// <remarks>
/// A corpus is opened by naming its folder and never its database file. The folder is the corpus
/// — the rows and the artifacts are two halves of one thing — and taking the file would let the
/// two be named apart: rows written into one corpus and files into the folder of another, both
/// operations succeeding. Which folder that is is still always given and never inferred; what is
/// gone is the second place to say it.
/// </remarks>
public static class CorpusDatabase
{
    /// <summary>How long a writer waits for the lock before giving up, in milliseconds.</summary>
    public const int BusyTimeoutMilliseconds = 5_000;

    /// <summary>Where a corpus keeps its database, as arquitectura.md §4.1 lays a corpus out.</summary>
    public const string DatabaseName = "corpus.db";

    /// <summary>
    /// EF's bookkeeping table, named as arquitectura.md §5.1 names it rather than
    /// __EFMigrationsHistory, because a person reading the corpus should recognise it.
    /// </summary>
    public const string MigrationsHistoryTable = "schema_migrations";

    /// <summary>The database of the corpus in this folder, whether or not it is there yet.</summary>
    public static string PathIn(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Absolute, and that is load-bearing: it is what the context reads the root back off, and
        // a relative data source would make the root depend on the working directory.
        return Path.Combine(root.FullName, DatabaseName);
    }

    /// <summary>
    /// Internal because the way in is a folder. A caller composing its own options could open a
    /// database anywhere, which is a corpus whose folder is not one this product laid out.
    /// </summary>
    internal static DbContextOptions<CorpusDbContext> OptionsFor(DirectoryInfo root, bool readOnly = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = PathIn(root),
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

    /// <summary>Opens the corpus in this folder, creating the file if it is not there.</summary>
    public static CorpusDbContext Open(DirectoryInfo root) => new(OptionsFor(root));

    /// <summary>Opens the corpus read only, which is how the MCP server reads it.</summary>
    public static CorpusDbContext OpenReadOnly(DirectoryInfo root) => new(OptionsFor(root, readOnly: true));

    /// <summary>Opens the corpus and brings its schema up to this build.</summary>
    public static CorpusDbContext OpenMigrated(DirectoryInfo root)
    {
        var context = Open(root);
        context.Database.Migrate();
        return context;
    }
}
