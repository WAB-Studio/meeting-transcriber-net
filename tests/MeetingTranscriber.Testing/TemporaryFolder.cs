namespace MeetingTranscriber.Testing;

/// <summary>
/// An empty folder under <c>%TEMP%\meeting-transcriber-tests</c>, gone once the suite lets it go.
/// </summary>
/// <remarks>
/// Not <c>CorpusOutsideApplicationData</c>'s own folder-and-shrug, and not
/// <c>FoldersTests.TemporaryFolderUnderTemp</c>'s either — that one exists apart from this on
/// purpose, over a constraint this one may not stand in for: <c>%TEMP%</c> is itself under this
/// user's application data, so a corpus there is refused, which is the very thing that fact is
/// about. This one carries no such rule and is for a suite that only ever wants an empty folder to
/// read and write files under, and to see gone afterwards.
/// </remarks>
public sealed class TemporaryFolder : IDisposable
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
            // Left behind rather than thrown over: nothing a test can do about a folder Windows
            // will not release, and no fact depends on it being gone.
        }
    }
}
