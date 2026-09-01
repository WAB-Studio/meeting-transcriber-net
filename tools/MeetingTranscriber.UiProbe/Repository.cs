using System.IO;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Which checkout this probe is about, where the application it drives is built from, and the name
/// Windows knows that application by.
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
internal sealed record Repository(string Root, string AppFolder, string AppUserModelId)
{
    private const string Solution = "MeetingTranscriber.slnx";

    private const string AppProject = "MeetingTranscriber.App";

    internal static Repository Around()
    {
        var root = RootAbove(Environment.CurrentDirectory)
            ?? RootAbove(AppContext.BaseDirectory)
            ?? throw new ProbeFailed(
                $"Neither {Environment.CurrentDirectory} nor {AppContext.BaseDirectory} is inside "
                + "the repository, so there is no application to start.");

        var appFolder = Path.Combine(root, "src", AppProject);

        return Directory.Exists(appFolder)
            ? new Repository(root, appFolder, WhatTheBuildIsCalled(appFolder))
            : throw new ProbeFailed($"There is no application project at {appFolder}.");
    }

    /// <summary>
    /// Refuses a window belonging to a different checkout of this repository.
    /// </summary>
    /// <remarks>
    /// The one global in this design, and nothing else names it: a package registration belongs to
    /// the machine and not to a folder, and two checkouts whose identity is the same string are one
    /// registration between them. So an activation started from the second opens the window of
    /// whichever one registered last — with this checkout's manifest, this checkout's sources, and
    /// somebody else's screen. Every other check in this tool then answers the wrong question
    /// correctly: <see cref="Freshness"/> compares these sources against that build and demands a
    /// build that can never lift the refusal, or it passes and the tree is a truthful report about
    /// a branch nobody asked about. A suffix on the identity is what keeps two checkouts apart, and
    /// this fires either when there is none or when two checkouts chose the same one — which is why
    /// it says the identity out loud rather than only naming the folders.
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
            $"{AppUserModelId} is registered to {runningFrom}, which is not inside {Root} — the "
            + "checkout this probe is reading. A package registration belongs to the machine and "
            + "not to a folder, so that identity is one registration between the two of them: "
            + "either this checkout has no package of its own, or the other one has taken the same "
            + "name. Give this checkout a package under a name nobody else is using and register "
            + "it — see docs/ui-probe.md.");
    }

    /// <summary>
    /// The application this checkout says to start, read off the manifest the build wrote rather
    /// than the one under source control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated one, because it is the identity Windows was registered with: registering is
    /// <c>Add-AppxPackage -Register</c> over that very file, and the manifest in <c>src/</c> is
    /// only what it was built from. The two say the same thing until somebody asks for a suffix,
    /// and on the day they differ the registered one is the only one that can answer.
    /// </para>
    /// <para>
    /// The name a checkout ends up with is <c>PackageIdentitySuffix</c>, written once into
    /// <c>PackageIdentity.props</c> at the top of that checkout, and
    /// <see cref="MustBeWhatWindowsStarted"/> is the refusal it exists to prevent. One identity is
    /// one registration for the whole machine, so the second checkout to register takes it off the
    /// first, which then meets a <see cref="Freshness"/> refusal naming a file it never edited. An
    /// agent working in a worktree beside others writes that file, builds, registers, and has a
    /// package of its own from then on: two registrations, two sessions, neither in the other's
    /// way. Nobody else writes it — with no file the build leaves the identity in <c>src/</c>
    /// alone. <c>docs/ui-probe.md</c> is the lines to type.
    /// </para>
    /// <para>
    /// The newest of them when a checkout's output holds several, which it does the moment somebody
    /// publishes, builds Release beside Debug, or leaves a layout folder behind. They all say the
    /// same thing while the suffix is a property of the checkout, so the choice only bites across a
    /// change to that file — and then the newest is the build that was registered, because
    /// registering follows building. A wrong pick is never quiet either: Windows refuses to
    /// activate an identity nothing holds, and <see cref="MustBeWhatWindowsStarted"/> catches the
    /// window it opens if something does.
    /// </para>
    /// </remarks>
    private static string WhatTheBuildIsCalled(string appFolder)
    {
        var output = Path.Combine(appFolder, "bin");

        var newest = (Directory.Exists(output)
                ? Directory.GetFiles(output, "AppxManifest.xml", SearchOption.AllDirectories)
                : [])
            .MaxBy(File.GetLastWriteTimeUtc);

        return newest is null
            ? throw new ProbeFailed(
                $"There is no built package manifest under {output}, so nothing has been "
                + "registered from this checkout. Build the application — see docs/ui-probe.md.")
            : Aumid.OfTheApplicationIn(newest);
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
