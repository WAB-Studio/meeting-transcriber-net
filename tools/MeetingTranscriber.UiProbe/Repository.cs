using System.IO;
using System.Runtime.InteropServices;

using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace MeetingTranscriber.UiProbe;

/// <summary>One package Windows has registered, and the folder it is registered against.</summary>
internal sealed record Registration(string AppUserModelId, string InstalledPath);

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
            ? new Repository(root, appFolder, TheOneRegisteredFrom(root))
            : throw new ProbeFailed($"There is no application project at {appFolder}.");
    }

    /// <summary>
    /// Refuses a window belonging to a different checkout of this repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second look and not the same one. What is registered is now asked of Windows before the
    /// activation, so this can no longer fire for a manifest nothing registered — what it still
    /// catches is the gap between asking and activating, which is another checkout registering the
    /// same name in that moment, and a registration pointing at a layout since deleted.
    /// </para>
    /// <para>
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
    /// </para>
    /// </remarks>
    internal void MustBeWhatWindowsStarted(string runningFrom)
    {
        if (IsInside(runningFrom, Root))
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
    /// The application Windows has registered from somewhere inside this checkout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of Windows and not worked out from the build output, which is the whole of what
    /// changed here. What runs is whatever layout the registration points at, and a checkout's
    /// <c>bin</c> holds several manifests the moment somebody builds Release beside Debug or
    /// publishes — so picking the newest was picking whichever build finished last, which is not
    /// the same question. When the two disagreed, every check downstream answered correctly about
    /// a package nothing had registered: the commonest shape of it was a suffix collision reported
    /// against two folders that had never collided.
    /// </para>
    /// <para>
    /// The identity is built from the family name Windows gives and the application id in the
    /// registered layout's own manifest, rather than from <c>Package.GetAppListEntries</c>, which
    /// is the WinRT call that hands back an application user model id whole. That one is
    /// asynchronous, and every verb of the server host runs on a WPF dispatcher thread — blocking
    /// on a WinRT completion there is a deadlock waiting for the one thread that would schedule it
    /// back. The family name is the half that used to be computed here, wrongly-shaped SHA-256
    /// base 32 and all, and it is the half Windows will simply say.
    /// </para>
    /// <para>
    /// Exactly one, and both other answers are refusals with what to do in them. None means this
    /// checkout has registered nothing; more than one means somebody registered two packages out of
    /// one checkout, which nothing here can choose between.
    /// </para>
    /// </remarks>
    private static string TheOneRegisteredFrom(string root) => RegisteredFrom(root) switch
    {
        [] => throw new ProbeFailed(
            $"Windows has no package registered from {root}, so there is nothing of this "
            + "checkout's to start. Build the application and register its output — see "
            + "docs/ui-probe.md."),

        [var only] => only.AppUserModelId,

        var several => throw new ProbeFailed(
            $"Windows has {several.Count} packages registered from inside {root} — "
            + string.Join(", ", several.Select(one => $"{one.AppUserModelId} at {one.InstalledPath}"))
            + " — so which one this probe is about is not something it can decide. Remove the ones "
            + "that are not wanted: see docs/ui-probe.md."),
    };

    /// <summary>
    /// Every package of this user's registered against a folder at or inside this checkout, with
    /// the identity that activates it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep, and it is asked once — at <see cref="Around"/>, where the question is
    /// <em>which</em> package — and never again. Reading <c>InstalledPath</c> is a property get
    /// across the package repository per package, and an ordinary machine has hundreds.
    /// </para>
    /// <para>
    /// <b>Never again is a decision.</b> Asking a narrower version of it before every verb would
    /// catch a registration that moved out from under an open session, and this card was asked for
    /// exactly that. It is not here because that state cannot be arrived at: a session always has
    /// the application running, and Windows will not let a registration move while it is.
    /// Measured on 2026-09-10 — <c>Add-AppxPackage -Register</c> over a running package is refused
    /// with <c>0x80073D02</c> naming the application to close, and <c>Remove-AppxPackage</c> ends
    /// the application, which <c>LaunchedApp.HasGone</c> then reports first and in better words.
    /// What was asked for is a refusal that cannot fire, paid for on every press.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Registration> RegisteredFrom(string root)
    {
        var found = new List<Registration>();
        foreach (var package in ThisUsersPackages())
        {
            if (LaidOutAt(package) is not { } installed || !IsInside(installed, root))
            {
                continue;
            }

            var manifest = Path.Combine(installed, "AppxManifest.xml");
            if (!File.Exists(manifest))
            {
                // Registered against a folder that has since been emptied. It is not this
                // checkout's answer and it is not this method's job to say so: the activation
                // itself and MustBeWhatWindowsStarted both report it in the terms of what failed.
                continue;
            }

            string identity;
            try
            {
                identity = $"{package.Id.FamilyName}!{ApplicationId.DeclaredIn(manifest)}";
            }
            catch (ProbeFailed)
            {
                // A manifest that will not say which application to activate — half written, or
                // declaring none. Skipped exactly the way a missing one is, and for the same
                // reason: a sentence about somebody else's manifest, out of a method whose job is
                // to name this checkout's package, sends whoever reads it to the wrong file.
                continue;
            }

            found.Add(new Registration(identity, installed));
        }

        return found;
    }

    /// <summary>
    /// This user's packages. Materialised inside the guard, because the enumeration is what talks
    /// to the package repository and a caller iterating it later would meet the COM failure outside
    /// anything that turns it into a sentence.
    /// </summary>
    /// <remarks>
    /// The empty security id is this user, which is the only one a probe run by hand is about —
    /// and the only one <c>FindPackagesForUser</c> answers without being an administrator, where
    /// <c>FindPackages()</c> wants elevation. Run unelevated on 2026-09-10 and it answered.
    /// </remarks>
    private static IReadOnlyList<Package> ThisUsersPackages()
    {
        try
        {
            return new PackageManager().FindPackagesForUser(string.Empty).ToList();
        }
        catch (Exception refused)
            when (refused is UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            throw new ProbeFailed(
                "Windows would not say what this user has registered, so there is no way to tell "
                + $"which package belongs to this checkout: {refused.Message}");
        }
    }

    /// <summary>
    /// Where a package is laid out, or nothing when it will not say. A package whose location has
    /// been deleted, and a few of Windows' own, throw rather than answering.
    /// </summary>
    /// <remarks>
    /// Blank counts as not saying, and it is not a hypothetical: this machine carries a
    /// registration left behind by a deleted worktree whose <c>InstalledPath</c> comes back empty
    /// rather than throwing. Left to reach <see cref="IsInside"/> that is an
    /// <see cref="ArgumentException"/> out of <c>Path.GetFullPath</c> — "the probe broke", on
    /// every verb, over somebody else's dead package.
    /// </remarks>
    private static string? LaidOutAt(Package package)
    {
        try
        {
            return package.InstalledPath is { Length: > 0 } laidOut ? laidOut : null;
        }
        catch (Exception unreadable)
            when (unreadable is InvalidOperationException or COMException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this path is that folder or inside it. The folder itself counts: a package
    /// registered at a checkout's root is still this checkout's.
    /// </summary>
    private static bool IsInside(string path, string root)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        return full.Equals(top, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
