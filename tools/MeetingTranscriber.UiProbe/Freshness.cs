using System.IO;
using System.Xml.Linq;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Refuses to probe a window built before the code that is on disk.
/// </summary>
/// <remarks>
/// <para>
/// This is the trap the whole tool would otherwise walk into every time. What Windows starts is
/// the package it has registered, and <c>dotnet build</c> does not touch that registration: it
/// writes new assemblies and the shell goes on launching whatever the layout held when somebody
/// last registered it. The first run of this tool photographed an application six days old and
/// said nothing, because there is nothing about a stale window that looks stale.
/// </para>
/// <para>
/// What it compares is deliberately narrow: the <c>.cs</c> and <c>.xaml</c> of the projects the
/// application is actually built from, found by following <c>ProjectReference</c> out of its own
/// project file. Both halves of that matter. Sweeping all of <c>src/</c> instead was the first
/// try and it has a dead end — the command line and the transcript renderer are under
/// <c>src/</c> and are not in this application, so editing one of them made the probe demand a
/// build that could not restamp anything, forever. And the two extensions are the two that are
/// compile inputs, so the build the refusal asks for is a build that lifts it.
/// </para>
/// <para>
/// What it therefore does not see: a change that ships without recompiling anything — a resource
/// file, an asset, a manifest edit. There is none of that in this application today, and a check
/// that named files the build does not compile would be back to demanding a build that changes
/// nothing.
/// </para>
/// </remarks>
internal static class Freshness
{
    private static readonly string[] Compiled = ["*.cs", "*.xaml"];

    private static readonly string[] Output =
    [
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
    ];

    internal static void MustNotPredate(string manifestPath, string runningFrom)
    {
        // The assembly beside the executable rather than the executable, when there is one: an
        // apphost is copied and not compiled, so its stamp can be older than the code it starts.
        var compiled = Path.ChangeExtension(runningFrom, ".dll");
        var built = File.GetLastWriteTimeUtc(File.Exists(compiled) ? compiled : runningFrom);

        var newest = ProjectsBehind(Path.GetDirectoryName(manifestPath)!)
            .SelectMany(project => Compiled.SelectMany(kind =>
                Directory.EnumerateFiles(project, kind, SearchOption.AllDirectories)))
            .Where(path => !Output.Any(path.Contains))
            .Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(file => file.Written)
            .FirstOrDefault();

        if (newest.Path is null || newest.Written <= built)
        {
            return;
        }

        throw new ProbeFailed(
            $"The window would be showing code from before {Path.GetFileName(newest.Path)} was "
            + $"last edited. What Windows starts is {runningFrom}, built "
            + $"{built.ToLocalTime():yyyy-MM-dd HH:mm:ss}, and that file was written "
            + $"{newest.Written.ToLocalTime():yyyy-MM-dd HH:mm:ss}. Build the application, and "
            + "register the build output if it has never been — see docs/ui-probe.md.");
    }

    /// <summary>
    /// The application's project and everything it is built out of, following
    /// <c>ProjectReference</c> as far as it goes. Read off the project files rather than listed
    /// here, so a project added to the application tomorrow is covered without anybody
    /// remembering this.
    /// </summary>
    private static IEnumerable<string> ProjectsBehind(string appFolder)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(Directory.EnumerateFiles(appFolder, "*.csproj"));

        while (pending.Count > 0)
        {
            var project = Path.GetFullPath(pending.Dequeue());
            if (!found.Add(project))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(project)!;
            foreach (var referenced in References(project))
            {
                pending.Enqueue(Path.GetFullPath(Path.Combine(folder, referenced)));
            }
        }

        return found.Select(project => Path.GetDirectoryName(project)!).Distinct();
    }

    private static IEnumerable<string> References(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar));
}
