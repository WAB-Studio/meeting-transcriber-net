using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Testing;

/// <summary>
/// A corpus on disk rather than in memory: WAL and busy_timeout only mean anything against a
/// file, and those are exactly the settings worth testing.
/// </summary>
/// <remarks>
/// The only one in the repository. A test project that opens a corpus references this project
/// instead of carrying a copy, so what a temporary corpus is — where it sits, what it holds, how
/// it lets go of the file — is decided once.
/// </remarks>
public sealed class TemporaryCorpus : IDisposable
{
    private static readonly Lazy<string> MigratedOnce =
        new(MakeTheMigratedDatabase, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly string directory;

    public TemporaryCorpus()
    {
        directory = Path.Combine(Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// The corpus root, which is the folder the database sits in and the one <c>meetings/</c> and
    /// <c>spool/</c> hang off. The same arrangement a real corpus has, so a test that walks it is
    /// walking the layout the application writes.
    /// </summary>
    public DirectoryInfo Root => new(directory);

    /// <summary>
    /// The database inside it, for the tests that are about the file itself — that it was made,
    /// that half a copy is refused, what compacting did to its size.
    /// </summary>
    public string DatabasePath => CorpusDatabase.PathIn(Root);

    public CorpusDbContext Open() => CorpusDatabase.Open(Root);

    /// <summary>
    /// The corpus brought up to this build's schema, by copying a database migrated once for the
    /// whole test process rather than running every migration for each test.
    /// </summary>
    /// <remarks>
    /// A test that calls <c>CorpusDatabase.OpenMigrated</c> itself still migrates; only this helper
    /// changed. The copy goes in only where there is no database yet, so a test that opened its
    /// corpus first and then asks for it migrated keeps what it wrote and migrates it as before.
    /// </remarks>
    public CorpusDbContext OpenMigrated()
    {
        if (!File.Exists(DatabasePath))
        {
            File.Copy(MigratedOnce.Value, DatabasePath);
        }

        return CorpusDatabase.OpenMigrated(Root);
    }

    /// <summary>
    /// One migrated database, in a folder of its own, closed and checkpointed before the first copy.
    /// </summary>
    /// <remarks>
    /// A database with its <c>-wal</c> beside it is a different database from the one without, so
    /// the log is folded into the file and the connections are let go of before anything is copied,
    /// and a log that is still there afterwards is a failure rather than a copy of half a database.
    /// </remarks>
    private static string MakeTheMigratedDatabase()
    {
        var template = new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), "meeting-transcriber-tests", "template-" + Guid.NewGuid().ToString("n")));
        template.Create();

        using (var context = CorpusDatabase.OpenMigrated(template))
        {
            context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
        }

        CorpusDatabase.ClearPoolsFor(template);

        var log = new FileInfo(CorpusDatabase.PathIn(template) + "-wal");
        if (log.Exists && log.Length > 0)
        {
            throw new InvalidOperationException(
                $"The migrated template still has a write-ahead log at '{log.FullName}', so copying it would copy half a database.");
        }

        // Nothing owns a process-wide template, so the process takes it away when it ends.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                template.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temp folder is not worth failing a finished run over.
            }
        };

        return CorpusDatabase.PathIn(template);
    }

    public void Dispose()
    {
        // Without this the pooled connection still holds the file and the delete fails. Only this
        // corpus's pools: emptying every pool in the process disposes the idle connections of the
        // corpora belonging to the tests running alongside this one, and the one that then hands
        // a disposed connection to a test that never touched this corpus fails it.
        CorpusDatabase.ClearPoolsFor(Root);

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over. Windows refuses a
            // delete two ways depending on how the other handle was opened — a sharing violation
            // when it forbids deletion, access denied when it allowed it and the delete is still
            // pending — and catching only the first leaves the test that happened to run while a
            // scanner had the file open failing for something it never asserted.
        }
    }
}
