using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.CorpusImport.Tests;

/// <summary>
/// A corpus on disk for one test. A copy of the one in the infrastructure tests, deliberately:
/// this whole project is deleted the day the last old corpus is imported, and sharing a helper
/// with the tests that stay would make that a change to them rather than a deletion.
/// </summary>
internal sealed class TemporaryCorpus : IDisposable
{
    private readonly string directory;

    public TemporaryCorpus()
    {
        directory = Path.Combine(Path.GetTempPath(), "corpus-import-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, "corpus.db");
    }

    public string DatabasePath { get; }

    public CorpusDbContext OpenMigrated() => CorpusDatabase.OpenMigrated(DatabasePath);

    public void Dispose()
    {
        // Without this the pooled connection still holds the file and the delete fails.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over, and Windows
            // refuses a contended delete as access denied as readily as a sharing violation.
        }
    }
}
