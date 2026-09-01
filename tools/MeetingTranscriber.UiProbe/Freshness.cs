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
/// The build stamp is read once, at the launch, and never again — which is the difference between
/// a check that survives a session and one that dissolves during it. Read live, it says "the file
/// on disk is newer than the sources", and a rebuild makes that true while the window goes on
/// showing the image it was started from: the refusal would evaporate at the exact moment it
/// became right. What a window can contain is fixed when the process starts, so that is when the
/// question is asked. Today a running application also holds a lock on its own assemblies, so the
/// rebuild that would do this cannot finish — but that is a fact about file locking and not
/// something this knows, and it is not what the guarantee rests on.
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
internal sealed class Freshness
{
    private static readonly string[] Compiled = ["*.cs", "*.xaml"];

    private static readonly string[] Output = ["bin", "obj"];

    private readonly IReadOnlyList<string> _projects;

    private readonly string _runningFrom;

    private readonly DateTime _built;

    private Freshness(IReadOnlyList<string> projects, string runningFrom, DateTime built)
    {
        _projects = projects;
        _runningFrom = runningFrom;
        _built = built;
    }

    /// <summary>
    /// Everything the answer depends on, taken at the moment the application was started: which
    /// projects it is built from, and when the image Windows launched was compiled.
    /// </summary>
    internal static Freshness Of(Repository repository, string runningFrom)
    {
        // The assembly beside the executable rather than the executable, when there is one: an
        // apphost is copied and not compiled, so its stamp can be older than the code it starts.
        var compiled = Path.ChangeExtension(runningFrom, ".dll");

        return new Freshness(
            ProjectsBehind(repository.AppFolder),
            runningFrom,
            File.GetLastWriteTimeUtc(File.Exists(compiled) ? compiled : runningFrom));
    }

    internal void MustNotPredateTheCode()
    {
        var newest = _projects
            .SelectMany(SourcesUnder)
            .Select(path => (Path: path, Written: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(file => file.Written)
            .FirstOrDefault();

        if (newest.Path is null || newest.Written <= _built)
        {
            return;
        }

        throw new ProbeFailed(
            $"The window is showing code from before {Path.GetFileName(newest.Path)} was last "
            + $"edited. What Windows started is {_runningFrom}, built "
            + $"{_built.ToLocalTime():yyyy-MM-dd HH:mm:ss}, and that file was written "
            + $"{newest.Written.ToLocalTime():yyyy-MM-dd HH:mm:ss}. Close the application, build "
            + "it, and start it again — in that order, because a running application holds its own "
            + "assemblies open and the build fails on them. If the build output has never been "
            + "registered, see docs/ui-probe.md.");
    }

    /// <summary>
    /// <c>bin</c> and <c>obj</c> are skipped on the way down rather than filtered afterwards. This
    /// runs before every instruction now, so walking a build output that is an order of magnitude
    /// larger than the sources and then throwing it away is work paid for on every press.
    /// </summary>
    private static IEnumerable<string> SourcesUnder(string folder)
    {
        foreach (var kind in Compiled)
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

            foreach (var file in SourcesUnder(below))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The application's project and everything it is built out of, following
    /// <c>ProjectReference</c> as far as it goes. Read off the project files rather than listed
    /// here, so a project added to the application tomorrow is covered without anybody
    /// remembering this.
    /// </summary>
    private static IReadOnlyList<string> ProjectsBehind(string appFolder)
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

        return found.Select(project => Path.GetDirectoryName(project)!).Distinct().ToList();
    }

    private static IEnumerable<string> References(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar));
}
