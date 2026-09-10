using System.IO;
using System.Reflection;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Refuses a verb once the copy of this tool that is running predates what the tool is built out of.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists for has no symptom at all. The server runs from a published copy under
/// <c>bin/mcp</c> — it has to, because a connected server holding <c>bin/Debug</c> open failed the
/// build of everything else in the solution — and <c>dotnet build</c> never writes <c>bin/mcp</c>.
/// So somebody edits the probe, forgets the publish, and every answer for the rest of the session
/// comes out of yesterday's tool: the same trees, the same pictures, the same confident sentences,
/// and none of the change they just made.
/// </para>
/// <para>
/// <b>The sources are this project's and every project behind it, never the published copy's own
/// folder.</b> The published copy carries a <c>.cs</c>-free layout of assemblies; a guard that read
/// <c>bin/mcp</c> as its own source would be comparing a copy with itself and answering yes
/// forever. And it follows <c>ProjectReference</c> for the same reason the extension list below
/// includes <c>.csproj</c>: this tool references <c>MeetingTranscriber.Infrastructure</c>, so
/// <c>bin/mcp</c> carries that assembly and <c>MeetingTranscriber.Domain</c>'s beside it, and an
/// edit to <c>CorpusLocation</c> is an edit to what this tool does. A guard that watched one folder
/// would go quiet on exactly the file the probe's corpus is built on.
/// </para>
/// <para>
/// <c>.csproj</c> as well as <c>.cs</c>, and that is not tidiness: a reference added to a project
/// changes what the published copy is made of and changes nothing under it that ends in <c>.cs</c>.
/// There is no <c>.xaml</c> in any of these projects — this tool has no markup and neither has
/// Infrastructure — and adding the extension anyway would be watching for a file kind they cannot
/// contain.
/// </para>
/// <para>
/// Asked live rather than fixed at the launch, which is the opposite of <see cref="Freshness"/> and
/// for the opposite reason. What an application's window can contain is fixed when its process
/// starts, so asking again would refuse at the moment the refusal stopped being true. What this
/// process is running is fixed too — but the answer that matters is whether a publish is owed, and
/// a publish becomes owed the moment somebody saves a file. Re-asking is what catches that, and the
/// refusal lifts on the next session, which is the same session boundary <c>docs/ui-probe.md</c>
/// already asks for.
/// </para>
/// <para>
/// A run started with <c>dotnet run</c> can never be refused by this, because that command builds
/// before it starts. That is right and not a hole: nothing is owed a publish there.
/// </para>
/// </remarks>
internal sealed class ProbeBuild
{
    private static readonly string[] Compiled = ["*.cs", "*.csproj"];

    private const string ProbeProject = "MeetingTranscriber.UiProbe";

    private const string Assemblies = "*.dll";

    private readonly IReadOnlyList<string> _projects;

    private readonly string _runningFrom;

    private readonly DateTime _built;

    private ProbeBuild(IReadOnlyList<string> projects, string runningFrom, DateTime built)
    {
        _projects = projects;
        _runningFrom = runningFrom;
        _built = built;
    }

    /// <summary>
    /// The copy of this tool that is running, and every project in this checkout it was built out
    /// of.
    /// </summary>
    /// <remarks>
    /// The entry assembly's own file and not <see cref="AppContext.BaseDirectory"/>'s apphost: an
    /// apphost is copied rather than compiled, so its stamp can be older than the code it starts —
    /// the same trap <see cref="Freshness.Of"/> names.
    /// </remarks>
    internal static ProbeBuild Running(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var running = Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assembly
            ? assembly
            : Environment.ProcessPath ?? AppContext.BaseDirectory;

        var sources = Path.Combine(repository.Root, "tools", ProbeProject);
        if (!Directory.Exists(sources))
        {
            // Refused rather than passed. Root is a folder holding the solution file, so this is
            // not a checkout of this repository at all — and a staleness rule that answers "fine"
            // because it could not find anything to compare is the silence the whole file exists
            // to end.
            throw new ProbeFailed(
                $"There is no probe project at {sources}, so there is no way to tell whether the "
                + "copy of this tool that is running is the one this checkout describes.");
        }

        return new ProbeBuild(
            Sources.ProjectsBehind(sources),
            running,
            WhenTheCopyWasLaidDown(Path.GetDirectoryName(running) ?? AppContext.BaseDirectory, running));
    }

    /// <summary>
    /// When the copy that is running was last laid down: the newest assembly beside it, not its
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the difference between a guard and a brick, and the first shape of it was a brick.
    /// <c>dotnet publish</c> preserves the timestamps of what it copies, so this assembly's stamp
    /// is <em>when the compiler last ran for this project</em> and never when the publish ran. And
    /// the compiler does not run for this project when only a method body changes in a project it
    /// references: reference assemblies are on, the reference this tool sees is byte for byte what
    /// it was, and <c>CoreCompile</c> is skipped.
    /// </para>
    /// <para>
    /// Put together, an edit to the body of <c>CorpusLocation.Choose</c> — the exact file the
    /// remarks above name as the reason this follows <c>ProjectReference</c> — made this refuse,
    /// and then the publish it asked for moved nothing, and it refused again, forever. A refusal
    /// its own instructions cannot lift is worse than no refusal at all: it costs the tool, and it
    /// teaches whoever meets it to stop believing the message.
    /// </para>
    /// <para>
    /// The newest assembly in the folder does move, because the referenced project <em>is</em>
    /// recompiled and its new file is copied in beside this one. Reading the folder rather than the
    /// file is not reading the published copy as a source — that is still forbidden and still not
    /// done; it is asking when the copy was laid down, which is a question only the copy can
    /// answer. A package assembly carrying a stamp from the day it was packed can only make this
    /// answer older, never newer, and older is the direction that refuses.
    /// </para>
    /// </remarks>
    private static DateTime WhenTheCopyWasLaidDown(string layout, string running)
    {
        var newest = File.GetLastWriteTimeUtc(running);

        foreach (var assembly in Directory.EnumerateFiles(layout, Assemblies))
        {
            var written = File.GetLastWriteTimeUtc(assembly);
            if (written > newest)
            {
                newest = written;
            }
        }

        return newest;
    }

    internal void MustNotPredateItsSources()
    {
        if (Sources.NewestUnder(_projects, Compiled) is not { } newest || newest.Written <= _built)
        {
            return;
        }

        throw new ProbeFailed(
            $"This probe is running a build from before {Path.GetFileName(newest.Path)} was last "
            + "edited, so nothing it says is about the tool as it is now. What is running is "
            + $"{_runningFrom}, built {_built.ToLocalTime():yyyy-MM-dd HH:mm:ss}, and that file was "
            + $"written {newest.Written.ToLocalTime():yyyy-MM-dd HH:mm:ss}. End this session, "
            + "publish the tool, and open a new one — `dotnet publish "
            + "tools/MeetingTranscriber.UiProbe -c Debug -o tools/MeetingTranscriber.UiProbe/bin/mcp` "
            + "— in that order, because a connected server holds that copy open and the publish "
            + "fails on it. See docs/ui-probe.md.");
    }
}
