using System.Runtime.CompilerServices;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// Every hand-written source file of the application, which is all this project has to hold it to:
/// there is no <c>ProjectReference</c> to it, for the reason the project file gives.
/// </summary>
/// <remarks>
/// One place and not one per test class. Which folders are skipped and how the application is found
/// from here is the same answer for every check that reads a screen, and a second copy of it is a
/// second thing to forget when the layout moves.
/// </remarks>
internal static class AppSources
{
    /// <summary>
    /// Where the application is, found from this file and not from whoever asked. A
    /// <c>[CallerFilePath]</c> parameter would bind at the call site, so a test class that moved
    /// into a subfolder would quietly resolve a different root — which is the failure this type
    /// exists to have exactly one of.
    /// </summary>
    private static readonly DirectoryInfo App = new(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(Here())!, "..", "..", "src", "MeetingTranscriber.App")));

    /// <summary>
    /// The application's files with <paramref name="extension"/>, in a fixed order. <c>obj</c> and
    /// <c>bin</c> are skipped: what the XAML compiler generates in there is a copy of what was
    /// already checked.
    /// </summary>
    public static IReadOnlyList<FileInfo> With(string extension)
    {
        return App
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// One source file of any project, named from <c>src/</c> — the application's own included.
    /// </summary>
    /// <remarks>
    /// Here and not in whichever test wanted it, for the reason the root above is found this way:
    /// one probe holds the application and the prompt's own commands to stopping a meeting through
    /// the same call, so it has to reach outside the application, and a second copy of how this
    /// repo is laid out is a second thing to forget when the layout moves.
    /// </remarks>
    public static FileInfo At(string relative) =>
        new(Path.GetFullPath(Path.Combine(App.FullName, "..", relative)));

    private static string Here([CallerFilePath] string file = "") => file;
}
