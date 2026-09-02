using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// ISA.md as another commit had it, and the one rule that needs two versions of the file to state:
/// a claim is not written into it already closed.
/// </summary>
/// <remarks>
/// Every other gate reads one document and is a pure function over it. This one cannot be — a
/// claim marked `[x]` looks identical whether it stood open for a week first or was written that
/// way in the diff that closes it, and which of those happened is the whole question. So it is the
/// only part of the gates that asks git anything, and it asks for objects the clone already has:
/// `GIT_NO_LAZY_FETCH` below is what keeps that true on a partial clone, where reading a blob the
/// clone does not hold would otherwise go and get it over the network.
/// </remarks>
internal static class IsaHistory
{
    /// <summary>
    /// What a branch is judged against. Every pull request in this repo targets `main`, so a base
    /// ref parameter would be a seam for a caller nobody has written.
    /// </summary>
    private const string Trunk = "origin/main";

    /// <summary>
    /// The file as the trunk last had it: the fork point, so a branch is judged on what it did and
    /// not on what landed while it was open.
    /// </summary>
    /// <remarks>
    /// Compared against the working tree rather than against `HEAD`, so the gate answers before the
    /// commit exists — the four commands run over a tree where ISA.md is usually still uncommitted.
    ///
    /// In CI on a pull request the checkout puts HEAD on the merge commit, whose other parent is
    /// the base, so this resolves to `main`'s tip instead of to the fork point. The two can only
    /// differ by claims that landed on `main` while the branch was open, and this gate is what
    /// stops any of those being closed ones — so both readings say the same thing about the branch.
    /// On a push to `main` the fork point is HEAD and the comparison is empty: the gate speaks on
    /// pull requests, which is where a diff is still worth refusing.
    /// </remarks>
    public static IsaDocument Baseline() => At(Run("merge-base", "HEAD", Trunk).Trim());

    /// <summary>ISA.md as one commit had it.</summary>
    /// <remarks>
    /// Split on a bare newline: the blob is LF whatever the working tree is checked out as, and
    /// the byte order mark it carries lands on the line above the frontmatter. Only claim lines
    /// are read off this, and neither of those reaches one.
    /// </remarks>
    public static IsaDocument At(string commit) =>
        IsaDocument.Of(Run("show", $"{commit}:ISA.md").Split('\n'));

    /// <summary>
    /// The claims <paramref name="head"/> marks closed that <paramref name="baseline"/> did not
    /// carry at all — claims born ticked, which never stood as a bet the work had to clear.
    /// </summary>
    /// <remarks>
    /// Ids are all it compares, so reordering the file, rewriting what a claim says and moving one
    /// between feature blocks are invisible to it. Two consequences, both deliberate and both
    /// written down in `references/format.md` rather than only here: renumbering a closed claim
    /// reads as one appearing, which is right because check 10 already refuses renumbering; and
    /// rewriting an open claim to describe what the same diff just built is this defect with one
    /// extra keystroke and is a reviewer's to see, because no comparison of ids can reach it.
    /// </remarks>
    public static IReadOnlyList<string> BornTicked(IsaDocument baseline, IsaDocument head)
    {
        var carried = baseline.Claims.Select(claim => claim.Id).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. head.Claims
                .Where(claim => claim.Closed && !carried.Contains(claim.Id))
                .Select(claim => claim.Id),
        ];
    }

    /// <summary>
    /// The repo root, from this file's compile-time path rather than from a working directory that
    /// depends on where the runner was launched from.
    /// </summary>
    private static string Root([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // A missing object is an error rather than a download: this is a test, and a test that
        // spends somebody's bandwidth on a corpus of blobs is outside what tests may do.
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";

        // These three name a repository, and they beat WorkingDirectory. Inherited from a hook or
        // a `git rebase --exec`, they would silently point this at another repo than the one the
        // line above picked — the launch directory distrusted through the front door and let in
        // through the back.
        foreach (var pointer in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
        {
            start.Environment.Remove(pointer);
        }

        using var git = Start(start);

        // Read on the thread pool rather than after stdout, so a stderr big enough to fill its
        // pipe cannot leave both sides waiting on each other.
        var complaint = git.StandardError.ReadToEndAsync();
        var answer = git.StandardOutput.ReadToEnd();
        git.WaitForExit();

        return git.ExitCode == 0
            ? answer
            : throw new InvalidOperationException(
                $"`git {string.Join(' ', arguments)}` exited {git.ExitCode}: "
                + $"{complaint.GetAwaiter().GetResult().Trim()}{Environment.NewLine}{AboutTheClone}");
    }

    /// <summary>
    /// git's own words come first and this comes after, because the ways this fails are several
    /// and a message that picks one sends the reader to fix something that is not wrong.
    /// </summary>
    private static string AboutTheClone =>
        $"Check 15 reads ISA.md as {Trunk} last had it, so it needs git, a remote called origin, "
        + $"and the history behind {Trunk} back to this branch's fork point. A shallow clone has "
        + $"none of it, and a {Trunk} nobody has fetched in a while resolves to an older fork "
        + "point than the real one — `git fetch` before believing what it named. CI asks its "
        + "checkout for all the history; `.github/workflows/ci.yml` says why.";

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            // Null is for reusing an existing process, which needs UseShellExecute; redirecting
            // rules that out, so the only way this fails is the executable not being there.
            return Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception missing)
        {
            throw new InvalidOperationException($"git is not on PATH. {AboutTheClone}", missing);
        }
    }
}
