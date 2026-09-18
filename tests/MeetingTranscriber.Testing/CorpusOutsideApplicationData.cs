using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Testing;

/// <summary>
/// A corpus folder somewhere this product allows one.
/// </summary>
/// <remarks>
/// <para>
/// The other <see cref="TemporaryCorpus"/>, for the facts that go through
/// <see cref="CorpusLocation"/>. That one puts its folder under <see cref="Path.GetTempPath"/>,
/// which on Windows is <c>%LOCALAPPDATA%\Temp</c> — inside the one tree a corpus is refused in,
/// because a packaged build's writes under it are redirected into a folder an uninstall deletes.
/// A fact that resolves a location against a folder there proves that refusal and nothing else.
/// </para>
/// <para>
/// What is left that is short, writable and outside it is the folder the test binary runs from, so
/// that is where this puts one. It is build output, so it goes with a clean.
/// </para>
/// </remarks>
public sealed class CorpusOutsideApplicationData : IDisposable
{
    public CorpusOutsideApplicationData(string named)
    {
        Root = new DirectoryInfo(Path.Combine(
            AppContext.BaseDirectory, named, Guid.NewGuid().ToString("n")[..8]));

        if (CorpusLocation.GoesWhenThePackageDoes(Root.FullName))
        {
            throw new InvalidOperationException(
                $"'{Root.FullName}' is where this suite puts a corpus, and a corpus there is "
                + "refused for being under this user's application data. Whatever is being "
                + "asserted, that refusal is what would be proved instead.");
        }

        Root.Create();
    }

    /// <summary>The corpus root: the folder the database sits in, as a real corpus is laid out.</summary>
    public DirectoryInfo Root { get; }

    /// <summary>
    /// Where a caller is told to look for it: a pointer file under a folder that is not there, so
    /// nobody has chosen and the fallback answers. That is the shape a machine is in when nobody
    /// has moved their corpus, which is the ordinary one.
    /// </summary>
    public CorpusLocation Location => new(
        new FileInfo(Path.Combine(Root.FullName, "nothing-here", CorpusLocation.SettingName)),
        Root);

    public CorpusDbContext OpenMigrated() => CorpusDatabase.OpenMigrated(Root);

    public void Dispose()
    {
        // Without this a pooled connection still holds the file and the delete fails. Only this
        // corpus's pools, for the reason TemporaryCorpus gives.
        CorpusDatabase.ClearPoolsFor(Root);

        try
        {
            Directory.Delete(Root.FullName, recursive: true);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A leftover folder under the build output is not worth reddening a green test over,
            // and for the two Windows refusals TemporaryCorpus names.
        }
    }
}
