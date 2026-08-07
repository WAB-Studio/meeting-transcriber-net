using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// A corpus on disk for one test. The third copy of this type in the repo, and the one that says
/// the pattern has stopped paying: two were a deliberate duplicate — the importer's tests get
/// deleted with the importer — and three is a shared test project waiting to be written.
/// </summary>
internal sealed class TemporaryCorpus : IDisposable
{
    private readonly string _directory;

    public TemporaryCorpus()
    {
        _directory = Path.Combine(Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "corpus.db");
    }

    public string DatabasePath { get; }

    /// <summary>The corpus root, which is the folder the database and <c>meetings/</c> share.</summary>
    public DirectoryInfo Root => new(_directory);

    public CorpusDbContext OpenMigrated() => CorpusDatabase.OpenMigrated(DatabasePath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }
}
