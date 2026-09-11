using System.IO;

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
/// How often <see cref="MustNotPredateTheCode"/> is then asked is the host's and not this class's,
/// and the two answer differently: <see cref="McpHost"/> asks every turn, <see cref="CommandLine"/>
/// once at <see cref="Session.Open"/> and never again. The source side is the half that can move
/// under a session, so re-asking catches an edit and nothing else; whether that is worth ending a
/// walk over is argued where each host decides it.
/// </para>
/// <para>
/// What it compares is deliberately narrow: the <c>.cs</c>, <c>.xaml</c> and <c>.csproj</c> of the
/// projects the application is actually built from, found by following <c>ProjectReference</c> out
/// of its own project file. Both halves of that matter. Sweeping all of <c>src/</c> instead was the
/// first try and it has a dead end — the command line and the transcript renderer are under
/// <c>src/</c> and are not in this application, so editing one of them made the probe demand a
/// build that could not restamp anything, forever. And the three extensions are the three that are
/// compile inputs, so the build the refusal asks for is a build that lifts it.
/// The walk itself is <see cref="Sources"/>'s, shared with <see cref="ProbeBuild"/>, which asks
/// the same shape of question about this tool rather than about the application.
/// </para>
/// <para>
/// <c>.csproj</c> is in that list for the reason <see cref="ProbeBuild"/> gives beside it: a
/// reference added to a project changes what the published copy is made of and changes nothing
/// under it that ends in <c>.cs</c>. The argument holds identically for the application — a
/// <c>ProjectReference</c> added to it or to anything behind it changes what its window can
/// contain and moves no <c>.cs</c> or <c>.xaml</c> file. Without it, an agent driving a window that
/// gained a whole assembly's worth of behaviour since the registered build reads a truthful-looking
/// tree of a window missing all of it, and this class calls that window current.
/// </para>
/// <para>
/// What it therefore does not see: a change that ships without recompiling anything — an asset, a
/// package version bumped in <c>Directory.Packages.props</c>, an edit to
/// <c>Package.appxmanifest</c>. The manifest is the one that bites, and it is not an omission to
/// fix by adding the extension: what a manifest edit needs is a <em>re-registration</em> and not a
/// build, so a build that lifted the refusal would restamp nothing and the refusal could never
/// lift — the forever-stale dead end the paragraph above is about, arrived at from the other side.
/// A window driven against a manifest it does not carry is therefore this class's blind spot and
/// stays one; whoever changes a capability re-registers, which <c>docs/ui-probe.md</c> says how to
/// do.
/// </para>
/// </remarks>
internal sealed class Freshness
{
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
            Sources.ProjectsBehind(repository.AppFolder),
            runningFrom,
            File.GetLastWriteTimeUtc(File.Exists(compiled) ? compiled : runningFrom));
    }

    internal void MustNotPredateTheCode()
    {
        if (Sources.NewestUnder(_projects, Sources.OfTheApplication) is not { } newest
            || newest.Written <= _built)
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
}
