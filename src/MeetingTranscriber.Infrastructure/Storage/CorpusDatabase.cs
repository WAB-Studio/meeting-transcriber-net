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
    /// Whether this folder holds a corpus, which is the question anything pointed at a folder asks
    /// before opening one.
    /// </summary>
    /// <remarks>
    /// A <c>corpus.db</c> of zero bytes is not a corpus and is not nothing: it is what a create
    /// that was cut off leaves, SQLite refuses to put it into WAL, and every write against it —
    /// the migration that would have made it a corpus included — comes back as a read-only
    /// database. So the size is part of the answer rather than a second check somebody has to
    /// remember. The command line asks the same three-state question one step further out, because
    /// it tells the two ways of having no corpus apart in what it says about them.
    /// </remarks>
    public static bool HoldsACorpus(DirectoryInfo root)
    {
        var database = new FileInfo(PathIn(root));
        return database.Exists && database.Length > 0;
    }

    /// <summary>
    /// How this corpus is named to SQLite, in each of the two ways it can be opened. Both, because
    /// a connection pool is keyed by the exact string that opened it, so read-only is a second pool
    /// holding the same file — and a corpus reached both ways is holding it twice.
    /// </summary>
    private static IEnumerable<string> ConnectionStringsFor(DirectoryInfo root)
    {
        yield return ConnectionStringFor(root, readOnly: false);
        yield return ConnectionStringFor(root, readOnly: true);
    }

    private static string ConnectionStringFor(DirectoryInfo root, bool readOnly) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = PathIn(root),
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        }.ToString();

    /// <summary>
    /// Internal because the way in is a folder. A caller composing its own options could open a
    /// database anywhere, which is a corpus whose folder is not one this product laid out.
    /// </summary>
    internal static DbContextOptions<CorpusDbContext> OptionsFor(DirectoryInfo root, bool readOnly = false)
    {
        var connectionString = ConnectionStringFor(root, readOnly);

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

    /// <summary>
    /// Drops the idle connections this corpus left pooled, so its file is no longer held open and
    /// the folder can be deleted or moved.
    /// </summary>
    /// <remarks>
    /// Here rather than at the caller because a pool is found by connection string and this class
    /// is the only thing that builds them: a caller spelling the string out itself would go on
    /// emptying a pool nothing is in the day a setting is added here, and the file would quietly
    /// stay held. It reaches this corpus and no other, which is the whole point —
    /// <c>SqliteConnection.ClearAllPools</c> disposes the idle connections of every corpus in the
    /// process, including the one another thread is about to take out of the pool and open.
    /// The other way to stop a test corpus holding its file is to take its connections out of the
    /// pool altogether, and it costs more than it looks: pooling off is a flag on the way a corpus
    /// is opened that exists for tests alone, and the suites stop exercising the connection the
    /// product actually uses.
    /// </remarks>
    public static void ClearPoolsFor(DirectoryInfo root)
    {
        foreach (var connectionString in ConnectionStringsFor(root))
        {
            using var connection = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(connection);
        }
    }
}
