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
    /// What counts as a source file of the application, for the staleness question.
    /// </summary>
    /// <remarks>
    /// The application has XAML and this tool has none, so the two readers want two lists — and
    /// until 2026-09-10 each held its own literal with the reason for the difference written in
    /// neither, which is how they drifted and were then put back by editing one to match the
    /// other. One place, two named answers, and the difference argued here.
    /// </remarks>
    internal static readonly string[] OfTheApplication = ["*.cs", "*.xaml", "*.csproj"];

    /// <summary>The same question about this tool, which compiles no XAML.</summary>
    internal static readonly string[] OfThisTool = ["*.cs", "*.csproj"];

    /// <summary>
    /// The two files above every project folder that are sources of the running application all the
    /// same.
    /// </summary>
    /// <remarks>
    /// A bump in <c>Directory.Packages.props</c> changes what the application contains — a different
    /// Windows App SDK, a different SQLite — and edits no file under any project folder, so the walk
    /// below cannot see it at any depth: both of these sit <em>above</em> the folders it descends
    /// from. <c>Directory.Build.props</c> is the same fact about the settings every project is
    /// compiled under. Neither carries a kind either reader names, which is why they are listed by
    /// name rather than folded into <see cref="OfTheApplication"/>.
    /// </remarks>
    private static readonly string[] AboveEveryProject =
        ["Directory.Packages.props", "Directory.Build.props"];

    /// <summary>
    /// The newest file of these kinds under any of these folders — or of the two props files above
    /// them all — and nothing when there is none.
    /// </summary>
    /// <param name="folders">
    /// The project folders to walk. A list rather than a sequence because this reads it twice: once
    /// to walk it, and once to find the checkout it is in. Both callers hand over
    /// <see cref="ProjectsBehind"/>'s answer, which is already one.
    /// </param>
    internal static (string Path, DateTime Written)? NewestUnder(
        IReadOnlyList<string> folders, IReadOnlyList<string> kinds)
    {
        var newest = folders
            .SelectMany(folder => Under(folder, kinds))
            .Concat(AtTheRootAbove(folders))
            .Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(file => file.Written)
            .FirstOrDefault();

        return newest.Path is null ? null : newest;
    }

    /// <summary>
    /// <see cref="AboveEveryProject"/> at the root of the checkout these folders are in, and
    /// nothing at all when there are no folders, no checkout above any of them, or no such file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The root is <see cref="Repository.RootAbove"/>'s answer and never a count of <c>..</c> from a
    /// project folder, because where the root is, is written down once: it is the folder holding
    /// <c>MeetingTranscriber.slnx</c>. That matters here more than usual —
    /// <c>tests/Directory.Build.props</c> is a different file with one of these names, and walking
    /// up to the first hit rather than to the solution would find it and stop.
    /// </para>
    /// <para>
    /// Every folder is tried and not the first, because <see cref="ProjectsBehind"/> answers out of
    /// a <see cref="HashSet{T}"/> and nothing defines which project comes back first. They are all
    /// inside the same checkout today, so it makes no difference today; an ordering dependency on a
    /// collection with no ordering is not worth carrying to find that out later.
    /// </para>
    /// <para>
    /// <b>The refusal this feeds can be lifted by the build it asks for, which is the question
    /// <see cref="Freshness"/> would raise about any file listed here.</b> That one excludes
    /// <c>Package.appxmanifest</c> because it is a change that ships without recompiling anything,
    /// so a refusal over it would stand for ever. These two are not that, and it was measured rather
    /// than reasoned: touching <c>Directory.Packages.props</c> and rebuilding the application
    /// restamps <c>MeetingTranscriber.App.dll</c>, because MSBuild counts every imported file among
    /// the compile's inputs. So close, build, start clears it exactly as the refusal says.
    /// </para>
    /// <para>
    /// <b>Additive, and that is what stands in for a probe.</b> <c>tools/</c> is run by hand and
    /// nothing under <c>tests/</c> may depend on it, so what there is instead is that this can only
    /// make the answer <em>newer</em> — and newer is the direction that refuses. What it cannot do
    /// is refuse wrongly. What it can do, and what nothing here would say, is go quiet: a checkout
    /// this finds no root above, or a rename of either file, puts the two staleness rules back to
    /// watching only the project folders, which is where they were before this existed.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> AtTheRootAbove(IReadOnlyList<string> folders) =>
        folders.Select(Repository.RootAbove).FirstOrDefault(root => root is not null) is not { } root
            ? []
            : AboveEveryProject.Select(name => Path.Combine(root, name)).Where(File.Exists);

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
