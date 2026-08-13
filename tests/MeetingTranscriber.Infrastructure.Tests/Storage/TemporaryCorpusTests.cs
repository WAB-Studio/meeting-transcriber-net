using System.Runtime.CompilerServices;

using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The harness every suite opens a corpus through, tested for the one thing it does that reaches
/// past its own test: letting go of the file. The suites run in parallel, so a corpus closing has
/// to be invisible to the corpora of the tests running beside it — a shared pool emptied here
/// fails a test that never touched this corpus, and it fails it intermittently.
/// </summary>
public class TemporaryCorpusTests
{
    [Fact]
    public void Closing_a_corpus_leaves_another_corpus_the_connection_it_had_pooled()
    {
        using var other = new TemporaryCorpus();
        object pooled;

        using (var context = other.OpenMigrated())
        {
            pooled = HandleOf(context);
        }

        using (var closing = new TemporaryCorpus())
        {
            using var context = closing.OpenMigrated();
        }

        using var reopened = other.Open();
        HandleOf(reopened).ShouldBeSameAs(pooled);
    }

    [Fact]
    public void A_closed_corpus_leaves_nothing_of_itself_on_disk()
    {
        var corpus = new TemporaryCorpus();
        var root = corpus.Root;

        using (var context = corpus.OpenMigrated())
        {
            context.Database.OpenConnection();
        }

        using (var reading = CorpusDatabase.OpenReadOnly(root))
        {
            reading.Database.OpenConnection();
        }

        corpus.Dispose();

        // A pooled connection still holding the file makes the delete fail, and the delete swallows
        // that failure rather than reddening a test over a leftover temp folder. So the folder being
        // gone is what says every way of opening this corpus was let go of — the read-only mode
        // included, which is its own pool because it is its own connection string.
        Directory.Exists(root.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// The helper is not the only place that can empty the process's pools, and the second place
    /// was found by a reviewer rather than by anything failing — which is the whole difficulty:
    /// the call reads as harmless, and what it breaks is somebody else's test, sometimes.
    /// </summary>
    [Fact]
    public void No_test_empties_the_pools_of_every_corpus_in_the_process()
    {
        var thisFile = ThisFile();
        var tree = new DirectoryInfo(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")));
        var everyPool = nameof(SqliteConnection.ClearAllPools);

        var offenders = tree
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            // This one, which has to name the call in order to look for it.
            .Where(file => !string.Equals(file.FullName, thisFile, StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file.FullName).Contains(everyPool, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(tree.FullName, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            $"These call {everyPool}, which reaches every corpus in the process. A test lets go of "
            + $"its own with {nameof(CorpusDatabase)}.{nameof(CorpusDatabase.ClearPoolsFor)}.");
    }

    private static bool IsBuildOutput(FileInfo file)
    {
        var separator = Path.DirectorySeparatorChar;
        return file.FullName.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
    }

    /// <summary>
    /// This source file, from where it was compiled rather than from the working directory, the
    /// way <c>IsaDocument</c> finds the repo root. The test tree is two folders up from it.
    /// </summary>
    private static string ThisFile([CallerFilePath] string path = "") => Path.GetFullPath(path);

    /// <summary>
    /// Which connection this context is actually on. Two contexts reporting the same handle were
    /// served by the same pooled connection, which is what makes "the pool was left alone" a thing
    /// a test can see rather than a race it has to wait for.
    /// </summary>
    private static object HandleOf(CorpusDbContext context)
    {
        context.Database.OpenConnection();

        var connection = (SqliteConnection)context.Database.GetDbConnection();
        return connection.Handle.ShouldNotBeNull();
    }
}
