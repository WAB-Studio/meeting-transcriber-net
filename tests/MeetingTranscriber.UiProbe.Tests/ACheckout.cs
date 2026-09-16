using System.IO;

namespace MeetingTranscriber.UiProbe.Tests;

/// <summary>
/// A tree the probe's two walks are pointed at: a solution at the root, so it is a checkout, and
/// whatever folders and files a fact puts under it.
/// </summary>
/// <remarks>
/// Under the folder the test binary runs from and not under the temporary folder, so a tree a run
/// left behind goes with a clean rather than sitting in somebody's <c>%TEMP%</c>.
/// </remarks>
internal sealed class ACheckout : IDisposable
{
    internal ACheckout()
    {
        Root = new DirectoryInfo(Path.Combine(
            AppContext.BaseDirectory, "checkouts", Guid.NewGuid().ToString("n")[..8]));

        Root.Create();
        Write(Path.Combine(Root.FullName, "MeetingTranscriber.slnx"), "<Solution />");
    }

    internal DirectoryInfo Root { get; }

    internal DirectoryInfo Folder(params string[] under)
    {
        var folder = new DirectoryInfo(Path.Combine([Root.FullName, .. under]));
        folder.Create();

        return folder;
    }

    internal string Write(string path, string text)
    {
        File.WriteAllText(path, text);

        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root.FullName, recursive: true);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A leftover folder under the build output is not worth reddening a green test over.
        }
    }
}
