using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

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

    public CorpusDbContext OpenMigrated() => CorpusDatabase.OpenMigrated(Root);

    public void Dispose()
    {
        // Without this the pooled connection still holds the file and the delete fails. It empties
        // every pool in the process, so it also reaches the corpora of the tests running alongside
        // this one — which costs them a reconnection and nothing else: a connection somebody is
        // holding is not closed underneath them, only kept out of the pool once they hand it back.
        SqliteConnection.ClearAllPools();

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
