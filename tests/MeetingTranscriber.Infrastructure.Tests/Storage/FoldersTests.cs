namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The one thing <see cref="Folders"/> does that a test relies on and cannot see: that a rename
/// Windows refused was retried and went through, rather than never having been refused at all.
/// A run of the suite going green proves the second reading as readily as the first, so the
/// waiting is asserted here against a handle held on purpose instead.
/// </summary>
public class FoldersTests
{
    [Fact]
    public void A_folder_somebody_is_still_reading_moves_once_they_let_go()
    {
        using var root = new TemporaryFolder();
        var from = Written(root, "from");
        var reader = new FileStream(
            Path.Combine(from.FullName, "inner", "held.txt"), FileMode.Open, FileAccess.Read);

        // That the handle really does refuse the rename, so what follows is the waiting working
        // and not a folder nothing was holding.
        Should.Throw<IOException>(() => Directory.Move(from.FullName, Path.Combine(root.Folder.FullName, "never")));

        var letting = new Thread(() =>
        {
            Thread.Sleep(20);
            reader.Dispose();
        });
        letting.Start();

        // Generous on purpose: what is under test is that the retry happens at all, and a machine
        // slow enough to stretch twenty milliseconds past the default would fail this for timing.
        var to = new DirectoryInfo(Path.Combine(root.Folder.FullName, "to"));
        Folders.MoveWaitingOutWhoeverHasIt(from, to, patienceMilliseconds: 10_000);

        letting.Join();
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
        using var root = new TemporaryFolder();
        var from = Written(root, "from");
        using var reader = new FileStream(
            Path.Combine(from.FullName, "inner", "held.txt"), FileMode.Open, FileAccess.Read);

        var gaveUp = Should.Throw<IOException>(() => Folders.MoveWaitingOutWhoeverHasIt(
            from,
            new DirectoryInfo(Path.Combine(root.Folder.FullName, "to")),
            patienceMilliseconds: 30));

        gaveUp.Message.ShouldContain(from.FullName);
        gaveUp.Message.ShouldContain("ClearPoolsFor");
        gaveUp.InnerException.ShouldBeOfType<IOException>();
        Directory.Exists(from.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// The refusal that is an answer. Waiting on it would turn a mistake in a test into a pause
    /// and then the same red a quarter of a second later, which is the way to teach somebody that
    /// this helper is where their time goes.
    /// </summary>
    [Fact]
    public void A_refusal_that_is_not_somebody_reading_comes_straight_back()
    {
        using var root = new TemporaryFolder();
        var from = Written(root, "from");
        var occupied = new DirectoryInfo(Path.Combine(root.Folder.FullName, "occupied"));
        occupied.Create();

        var refused = Should.Throw<IOException>(
            () => Folders.MoveWaitingOutWhoeverHasIt(from, occupied, patienceMilliseconds: 30_000));

        // Windows' own, not one this helper built after waiting: nothing was ever going to change.
        refused.InnerException.ShouldBeNull();
        Directory.Exists(from.FullName).ShouldBeTrue();
    }

    private static DirectoryInfo Written(TemporaryFolder root, string name)
    {
        var folder = new DirectoryInfo(Path.Combine(root.Folder.FullName, name));
        folder.CreateSubdirectory("inner");
        File.WriteAllText(Path.Combine(folder.FullName, "inner", "held.txt"), "held");
        return folder;
    }

    /// <summary>A folder of this test's own, and nothing about where a corpus may live.</summary>
    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
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
