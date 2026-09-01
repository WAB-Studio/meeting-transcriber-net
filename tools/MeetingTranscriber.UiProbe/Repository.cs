using System.IO;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Which checkout this probe is about, and the manifest naming the application to start.
/// </summary>
/// <remarks>
/// <para>
/// Two starting points and not one, because the two hosts are started differently. A command line
/// is typed at the repository, so the working directory is inside it. A server is started by
/// whatever registered it, from a working directory nobody here chose — so the fallback is this
/// assembly, which is inside the repository and will be for as long as the tool lives under
/// <c>tools/</c>. The copy under <c>bin/mcp</c> that the server actually runs — published there
/// so that holding it open cannot fail the build of everything else — does not weaken that: it is
/// in the same checkout, so both starting points still land on it. The working directory is tried
/// first on purpose: it is what somebody meant, and
/// when it disagrees with the assembly <see cref="MustBeWhatWindowsStarted"/> is what turns the
/// disagreement into a sentence instead of a wrong answer.
/// </para>
/// </remarks>
internal sealed record Repository(string Root, string Manifest)
{
    private const string Solution = "MeetingTranscriber.slnx";

    internal static Repository Around()
    {
        var root = RootAbove(Environment.CurrentDirectory)
            ?? RootAbove(AppContext.BaseDirectory)
            ?? throw new ProbeFailed(
                $"Neither {Environment.CurrentDirectory} nor {AppContext.BaseDirectory} is inside "
                + "the repository, so there is no application to start.");

        var manifest = Path.Combine(root, "src", "MeetingTranscriber.App", "Package.appxmanifest");

        return File.Exists(manifest)
            ? new Repository(root, manifest)
            : throw new ProbeFailed($"There is no package manifest at {manifest}.");
    }

    /// <summary>
    /// Refuses a window belonging to a different checkout of this repository.
    /// </summary>
    /// <remarks>
    /// The one global in this design, and nothing else names it: a package registration belongs to
    /// the machine, not to a folder, and the id derived from the manifest is the same string in
    /// every clone and every worktree. So an activation started from a second checkout opens the
    /// window of whichever one was registered — with this checkout's manifest, this checkout's
    /// sources, and somebody else's screen. Every other check in this tool then answers the wrong
    /// question correctly: <see cref="Freshness"/> compares these sources against that build and
    /// demands a build that can never lift the refusal, or it passes and the tree is a truthful
    /// report about a branch nobody asked about.
    /// </remarks>
    internal void MustBeWhatWindowsStarted(string runningFrom)
    {
        if (Path.GetFullPath(runningFrom).StartsWith(
                Path.GetFullPath(Root) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ProbeFailed(
            $"Windows started {runningFrom}, which is not inside {Root} — the checkout this probe "
            + "is reading. A package registration belongs to the machine and not to a folder, so "
            + "there is one registered build for all of them. Register this checkout's build "
            + "output, or drive it from the one that is registered — see docs/ui-probe.md.");
    }

    private static string? RootAbove(string start)
    {
        var here = new DirectoryInfo(start);
        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, Solution)))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        return null;
    }
}
