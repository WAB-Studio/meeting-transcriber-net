using System.Reflection;

using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// A corpus is a database and a folder, and these are what keep it from being two things a caller
/// has to keep pointing at the same place.
/// </summary>
/// <remarks>
/// The suite that walks the whole product is where this lives, because the rule is about every
/// layer at once: the CLI reaches Processing, Infrastructure and Domain, so a signature added in
/// any of them is in reach here. The importer is the one assembly it cannot see —
/// <c>src/</c> may not reference <c>tools/</c> — and its own suite holds the same rule over it.
/// </remarks>
public class CorpusIsOneThingTests
{
    /// <summary>
    /// Every corpus is opened by naming its folder, so the folder is knowable from the corpus and
    /// there is never a second one to disagree with it.
    /// </summary>
    [Fact]
    public void A_corpus_says_which_folder_it_is()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        context.Root.FullName.ShouldBe(corpus.Root.FullName);
        Path.GetDirectoryName(corpus.DatabasePath).ShouldBe(context.Root.FullName);
    }

    /// <summary>
    /// The whole of the claim. Rows written into one corpus while files land in another is not a
    /// bug to be caught at run time here: it is a sentence there is no way to write, because
    /// nothing takes the two halves apart. This is what fails the day something does again.
    /// </summary>
    [Fact]
    public void Nothing_takes_a_corpus_and_a_folder_as_two_things()
    {
        CorpusPairing.WhereACorpusMeetsAFolder([.. Product()]).ShouldBeEmpty();
    }

    /// <summary>
    /// A context built onto something that is not a file in a folder has no corpus to write into,
    /// and says so rather than answering with a folder that is true only from wherever the process
    /// was started.
    /// </summary>
    [Fact]
    public void A_corpus_that_is_not_a_file_in_a_folder_has_no_root()
    {
        using var context = new CorpusDbContext(
            new DbContextOptionsBuilder<CorpusDbContext>().UseSqlite("Data Source=:memory:").Options);

        var refused = Should.Throw<InvalidOperationException>(() => context.Root);
        refused.Message.ShouldContain(nameof(CorpusDatabase));
    }

    /// <summary>
    /// The one way left to make the two halves disagree, and it stops here. EF lets a caller move
    /// a context to another database while its connection is closed; a corpus that has been moved
    /// refuses to say where it is rather than answering with the folder of the one it was.
    /// </summary>
    [Fact]
    public void A_corpus_moved_to_another_database_says_so_instead_of_answering()
    {
        using var first = new TemporaryCorpus();
        using var second = new TemporaryCorpus();
        using var context = first.OpenMigrated();

        context.Root.FullName.ShouldBe(first.Root.FullName);
        context.Database.SetConnectionString($"Data Source={second.DatabasePath}");

        var refused = Should.Throw<InvalidOperationException>(() => context.Root);
        refused.Message.ShouldContain(second.DatabasePath);
    }

    /// <summary>Every assembly of the product this suite can reach, and no test assembly.</summary>
    private static IEnumerable<Assembly> Product()
    {
        var reached = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(Corpus).Assembly]);

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            foreach (var referenced in assembly.GetReferencedAssemblies()
                .Where(name => name.Name!.StartsWith("MeetingTranscriber.", StringComparison.Ordinal))
                .Where(name => !reached.ContainsKey(name.Name!)))
            {
                var loaded = Assembly.Load(referenced);
                reached[referenced.Name!] = loaded;
                pending.Enqueue(loaded);
            }
        }

        return reached.Values.Prepend(typeof(Corpus).Assembly);
    }
}
