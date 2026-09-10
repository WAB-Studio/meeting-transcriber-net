using System.IO;
using System.Xml.Linq;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// What a project is built out of: the projects behind it, and the newest source under them.
/// </summary>
/// <remarks>
/// <para>
/// Two rules ask it and they ask about different things: <see cref="Freshness"/> about the
/// application's sources against the window Windows started, <see cref="ProbeBuild"/> about this
/// tool's sources against the copy of itself that is running. Both are "is what is running older
/// than what is written", and neither is the other — the application's answer is fixed when its
/// process starts and this tool's is not, they name different file kinds, and their refusals send
/// somebody to do different things. What they share is the two walks, so the walks are here.
/// </para>
/// <para>
/// <c>bin</c> and <c>obj</c> are skipped on the way down rather than filtered afterwards. Over MCP
/// this runs before every instruction, and a build output is an order of magnitude larger than the
/// sources beside it. It is also what keeps <c>bin/mcp</c> out of the probe's own answer, which
/// would be a copy compared with itself.
/// </para>
/// </remarks>
internal static class Sources
{
    private static readonly string[] Output = ["bin", "obj"];

    /// <summary>
    /// The newest file of these kinds under any of these folders, or nothing when there is none.
    /// </summary>
    internal static (string Path, DateTime Written)? NewestUnder(
        IEnumerable<string> folders, IReadOnlyList<string> kinds)
    {
        var newest = folders
            .SelectMany(folder => Under(folder, kinds))
            .Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(file => file.Written)
            .FirstOrDefault();

        return newest.Path is null ? null : newest;
    }

    /// <summary>
    /// This project and everything it is built out of, following <c>ProjectReference</c> as far as
    /// it goes. Read off the project files rather than listed anywhere, so a project added
    /// tomorrow is covered without anybody remembering to say so.
    /// </summary>
    internal static IReadOnlyList<string> ProjectsBehind(string projectFolder)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(Directory.EnumerateFiles(projectFolder, "*.csproj"));

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

        return found.Select(project => Path.GetDirectoryName(project)!).Distinct().ToList();
    }

    private static IEnumerable<string> Under(string folder, IReadOnlyList<string> kinds)
    {
        foreach (var kind in kinds)
        {
            foreach (var file in Directory.EnumerateFiles(folder, kind))
            {
                yield return file;
            }
        }

        foreach (var below in Directory.EnumerateDirectories(folder))
        {
            if (Output.Contains(Path.GetFileName(below), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Under(below, kinds))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> References(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar));
}
