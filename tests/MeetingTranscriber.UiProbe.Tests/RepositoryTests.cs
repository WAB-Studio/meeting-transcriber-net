using System.IO;

namespace MeetingTranscriber.UiProbe.Tests;

/// <summary>
/// Where the probe decides a checkout is, which is the one answer every other rule in it is
/// measured from.
/// </summary>
public class RepositoryTests
{
    /// <summary>
    /// The root is the folder holding the solution, and a tree with no solution in it has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half is the half that says something. Walking up to the first plausible folder
    /// would find <c>tests/Directory.Build.props</c> and stop, or climb out of the checkout
    /// altogether and answer about whatever is above it — and both of those are answers, not
    /// failures, so nothing downstream would notice. What this holds is that the walk stops at the
    /// solution or stops entirely.
    /// </para>
    /// <para>
    /// Asked from two levels down, because one level down is what a mistake in the loop's exit
    /// condition would still get right.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_root_is_the_folder_holding_the_solution_and_nothing_else()
    {
        using var checkout = new ACheckout();

        var deep = checkout.Folder("tests", "MeetingTranscriber.Something.Tests");

        Repository.RootAbove(deep.FullName).ShouldBe(checkout.Root.FullName);
        Repository.RootAbove(checkout.Root.FullName).ShouldBe(checkout.Root.FullName);

        using var nowhere = new NotACheckout();

        Repository.RootAbove(nowhere.Folder.FullName).ShouldBeNull();
    }

    /// <summary>
    /// A folder with no solution anywhere above it.
    /// </summary>
    /// <remarks>
    /// The temporary folder, and deliberately not one under the build output: that one is inside
    /// this very checkout, so a walk from it finds <c>MeetingTranscriber.slnx</c> and the fact
    /// would quietly assert the opposite of what it says. Everything else in this suite avoids the
    /// temporary folder because a corpus may not live there; a folder holding nothing is not a
    /// corpus and this is the one place it is the right answer.
    /// </remarks>
    private sealed class NotACheckout : IDisposable
    {
        internal NotACheckout()
        {
            Folder = new DirectoryInfo(Path.Combine(
                Path.GetTempPath(), $"meeting-transcriber-not-a-checkout-{Guid.NewGuid():n}"));

            Folder.Create();

            Repository.RootAbove(Path.GetTempPath()).ShouldBeNull(
                $"'{Path.GetTempPath()}' is where this fact puts a folder that is in no checkout, "
                + "and there is a solution somewhere above it. What would be proved instead is "
                + "that the walk finds that one.");
        }

        internal DirectoryInfo Folder { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder.FullName, recursive: true);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
                // A leftover empty folder is not worth reddening a green test over.
            }
        }
    }
}
