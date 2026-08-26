namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The one thing <see cref="Folders"/> does that a test relies on and cannot see: that a rename
/// Windows refused was retried and went through, rather than never having been refused at all.
/// A run of the suite going green proves the second reading as readily as the first, so the
/// waiting is asserted here against a handle held on purpose instead.
/// </summary>
public class FoldersTests
{
    /// <summary>
    /// How long the handle is held once the waiting has started. Ten times any refusal ever
    /// measured here and fifty times under the patience below, for one reason: the count this
    /// test asserts is only honest if the first attempt cannot have found the folder already
    /// free. A machine that stalls this thread for a fifth of a second between starting the one
    /// below and the first rename would turn that into a red, and a stall that size is a red
    /// worth having.
    /// </summary>
    private const int HoldForMilliseconds = 200;

    [Fact]
    public void A_folder_somebody_is_still_reading_moves_once_they_let_go()
    {
        using var root = new TemporaryFolderUnderTemp();
        var from = Written(root, "from");

        // Disposed from two places on purpose. The thread further down is the one that matters —
        // letting go while the move is waiting is what this test asserts — and the using is what
        // guarantees the handle is gone when the test leaves, including on the path where the
        // move throws and every line after it is skipped. Without it a red here would leave a
        // handle alive until finalization, and the folder's own cleanup would swallow the delete
        // it then fails, quietly leaving the tree behind. The Join below keeps the two from
        // being the same dispose at the same time.
        using var reader = new FileStream(
            Path.Combine(from.FullName, "inner", "held.txt"), FileMode.Open, FileAccess.Read);

        // That the handle really does refuse the rename, so what follows is the waiting working
        // and not a folder nothing was holding.
        Should.Throw<IOException>(() => Directory.Move(from.FullName, Path.Combine(root.Folder.FullName, "never")));

        // Guarded because this thread is the process's and not xunit's: an exception out of it is
        // unhandled and takes the test host down, and the run then says the host died rather than
        // saying which test did. The using above is what actually guarantees the handle closes,
        // so a failure here is worth an assertion and not worth a process.
        Exception? lettingGoFailed = null;
        var letting = new Thread(() =>
        {
            Thread.Sleep(HoldForMilliseconds);

            try
            {
                reader.Dispose();
            }
            catch (Exception failed)
            {
                lettingGoFailed = failed;
            }
        });
        letting.Start();

        // Generous on purpose: what is under test is that the retry happens at all, and a machine
        // slow enough to stretch the hold past the default would fail this for timing.
        var to = new DirectoryInfo(Path.Combine(root.Folder.FullName, "to"));
        int refusals;
        try
        {
            refusals = Folders.MoveWaitingOutWhoeverHasIt(from, to, patienceMilliseconds: 10_000);
        }
        finally
        {
            letting.Join();
        }

        lettingGoFailed.ShouldBeNull();

        // The assertion this test exists for. Every one below it is just as true of a run where
        // the handle had already gone and the first rename walked straight through — which is
        // exactly what a green suite cannot tell you, and why the helper counts.
        refusals.ShouldBeGreaterThan(0);

        to.Exists.ShouldBeTrue();
        Directory.Exists(from.FullName).ShouldBeFalse();
        File.ReadAllText(Path.Combine(to.FullName, "inner", "held.txt")).ShouldBe("held");
    }

    /// <summary>
    /// The half a green suite never reaches. A folder held for good is this suite's own handle
    /// left open, and saying so loudly is the whole reason the waiting is bounded.
    /// </summary>
    [Fact]
    public void A_folder_nobody_lets_go_of_is_a_red_and_not_a_wait()
    {
        using var root = new TemporaryFolderUnderTemp();
        var from = Written(root, "from");
        using var reader = new FileStream(
            Path.Combine(from.FullName, "inner", "held.txt"), FileMode.Open, FileAccess.Read);

        var gaveUp = Should.Throw<IOException>(() => Folders.MoveWaitingOutWhoeverHasIt(
            from,
            new DirectoryInfo(Path.Combine(root.Folder.FullName, "to")),
            patienceMilliseconds: 30));

        gaveUp.Message.ShouldContain(from.FullName);
        gaveUp.Message.ShouldContain("a handle this suite still holds");
        gaveUp.InnerException.ShouldBeOfType<IOException>();
        Directory.Exists(from.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// The refusal that is an answer. Waiting on it would turn a mistake in a test into a pause
    /// and then the same red a quarter of a second later, which is the way to teach somebody that
    /// this helper is where their time goes.
    /// </summary>
    /// <remarks>
    /// The thirty seconds below is not a copied constant and is not generosity — do not shrink it.
    /// It is a trap, and the only one this test has. Every assertion here passes instantly when
    /// the helper is right, because a destination that already exists is handed straight back;
    /// they would also pass under the default patience if the helper were wrong, a quarter of a
    /// second later and looking exactly the same. Set against thirty seconds, a misclassification
    /// stops being invisible: it costs half a minute of every CI run until somebody fixes it,
    /// which is precisely the cost <see cref="Folders"/> exists to keep off the suite. What proves
    /// nothing waited is the null inner exception; what makes anybody notice is the clock.
    /// </remarks>
    [Fact]
    public void A_refusal_that_is_not_somebody_reading_comes_straight_back()
    {
        using var root = new TemporaryFolderUnderTemp();
        var from = Written(root, "from");
        var occupied = new DirectoryInfo(Path.Combine(root.Folder.FullName, "occupied"));
        occupied.Create();

        var refused = Should.Throw<IOException>(
            () => Folders.MoveWaitingOutWhoeverHasIt(from, occupied, patienceMilliseconds: 30_000));

        // Windows' own, not one this helper built after waiting: nothing was ever going to change,
        // so none of the advice it gives about who might be holding the folder is offered either.
        refused.InnerException.ShouldBeNull();
        refused.Message.ShouldNotContain("a handle this suite still holds");
        Directory.Exists(from.FullName).ShouldBeTrue();
    }

    private static DirectoryInfo Written(TemporaryFolderUnderTemp root, string name)
    {
        var folder = new DirectoryInfo(Path.Combine(root.Folder.FullName, name));
        folder.CreateSubdirectory("inner");
        File.WriteAllText(Path.Combine(folder.FullName, "inner", "held.txt"), "held");
        return folder;
    }

    /// <summary>
    /// A folder of this test's own, and nothing about where a corpus may live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CorpusLocationTests</c> keeps a <c>TemporaryFolderOutsideApplicationData</c>, which is
    /// this same folder-and-shrug with a different root and a pool clear, and until this pass
    /// both were called <c>TemporaryFolder</c>. The constraint runs one way: that one cannot be
    /// this one, because <c>%TEMP%</c> is under this user's application data and a corpus there
    /// is refused, which is the thing it exists to assert. This one could have been that one —
    /// build output is writable, and clearing a pool over a folder holding no database does
    /// nothing.
    /// </para>
    /// <para>
    /// It is not, because taking it would put an assertion about where a corpus may live in the
    /// path of a test that has nothing to say about that, and a red here would then be about the
    /// wrong thing. That is a reason to keep two, and not a reason to keep two <em>copies</em>:
    /// the folder-and-shrug is written out again in every test project in this repo and wants one
    /// owner next to <c>TemporaryCorpus</c>, which the PR this comment arrived in priced and left
    /// to a card of its own rather than doing on the way past.
    /// </para>
    /// </remarks>
    private sealed class TemporaryFolderUnderTemp : IDisposable
    {
        public TemporaryFolderUnderTemp()
        {
            Folder = new DirectoryInfo(Path.Combine(
                Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));
            Folder.Create();
        }

        public DirectoryInfo Folder { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder.FullName, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The same shrug TemporaryCorpus makes, for the same two Windows refusals.
            }
        }
    }
}
