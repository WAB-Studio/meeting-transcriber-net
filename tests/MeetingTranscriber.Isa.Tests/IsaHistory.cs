using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// ISA.md as another commit had it, and the rules that need two versions of the file to state: a
/// claim closes only in the words the trunk was already carrying it in, and a claim that moves
/// afterwards takes its evidence with it.
/// </summary>
/// <remarks>
/// Every other gate reads one document and is a pure function over it. These three cannot be — a
/// claim marked `[x]` looks identical whether it stood open for a week first or was written that
/// way in the diff that closes it, and a stub reads the same whether it was written against the
/// sentence above it or against one that has since been rewritten. So it is the only part of the
/// gates that asks git anything, and it asks for objects the clone already has: `GIT_NO_LAZY_FETCH`
/// below is what keeps that true on a partial clone, where reading a blob the clone does not hold
/// would otherwise go and get it over the network.
/// </remarks>
internal static class IsaHistory
{
    /// <summary>
    /// What a branch is judged against. Every pull request in this repo targets `main`, so a base
    /// ref parameter would be a seam for a caller nobody has written.
    /// </summary>
    private const string Trunk = "origin/main";

    /// <summary>
    /// Where a push started, when the thing doing the pushing says so.
    /// `.github/workflows/ci.yml` sets it, on a push and only on a push.
    /// </summary>
    private const string PushedOnto = "ISA_TRUNK_BEFORE";

    /// <summary>
    /// The file as the trunk had it before the change now in hand, resolved once so the three gates
    /// score one document rather than three: `origin/main` is a ref other worktrees over this object
    /// store move, and a fetch between two calls would otherwise give two of them different files.
    /// </summary>
    private static readonly Lazy<IsaDocument> TheBaseline =
        new(() => At(TrunkBefore(Environment.GetEnvironmentVariable(PushedOnto))));

    /// <summary>What the change in hand is judged against.</summary>
    /// <remarks>
    /// Compared against the working tree rather than against `HEAD`, so the gate answers before the
    /// commit exists — the four commands run over a tree where ISA.md is usually still uncommitted.
    ///
    /// In CI on a pull request the checkout puts HEAD on the merge commit, whose other parent is
    /// the base, so the fork point resolves to `main`'s tip instead. The two can only differ by
    /// claims that landed on `main` while the branch was open, and check 15 is what stops any of
    /// those being closed ones — so for check 15 both readings say the same thing about the branch.
    ///
    /// Check 16 does not inherit that: `main` may legitimately gain a *reworded open* claim while a
    /// branch is open, and then the two readings disagree about a branch that has not moved. A
    /// branch closing that claim in the words it was cut with is green here and red in CI; a
    /// narrowing pushed to `main` after the branch was cut is red here and green in CI. Both are one
    /// state — the branch is behind — and neither is fixed by fetching, which does not move a fork
    /// point. Merging `main` in does, and is what a reviewer wants under the tick anyway. Reading
    /// the true fork point in CI needs the checkout to stand on the head commit rather than the
    /// merge commit, which is `.github/workflows/ci.yml`'s to decide and not this file's.
    /// </remarks>
    public static IsaDocument Baseline() => TheBaseline.Value;

    /// <summary>
    /// The commit to read the baseline out of: where the push began if something told us, and the
    /// fork point otherwise.
    /// </summary>
    /// <remarks>
    /// Which route a change is taking is knowable, so it is stated rather than worked out. The
    /// tempting inference — that HEAD being an ancestor of <see cref="Trunk"/> means a push to the
    /// trunk — is true of a branch cut from `main` that has not committed yet, which is the state
    /// the four commands usually run in, and it would judge that branch against `main` minus its own
    /// tip. A worker following `.claude/skills/isa/SKILL.md` — push the claim open, cut a branch,
    /// close it — would be told the claim was born ticked. So the environment is the only signal,
    /// and its absence means the fork point, which is what every route but the push wants.
    ///
    /// On the trunk the fork point is HEAD, so the file is compared with itself. Before the commit
    /// exists that is still a real comparison and the gates speak; after it, they cannot, which is
    /// the whole of what CI on a push is here to cover.
    ///
    /// A value that is not a commit behind HEAD throws rather than falling back, because on the one
    /// route this is read the fallback would be that silence again. Three ways it can happen and one
    /// answer to all of them: a force push names a tip nothing points at any more, a rollback names
    /// one HEAD does not descend from, and a hand-set `ISA_TRUNK_BEFORE=HEAD` names the tree itself
    /// — which is why the shape is checked before the ancestry, since `--is-ancestor HEAD HEAD` is
    /// perfectly true and would turn every gate off from a shell.
    /// </remarks>
    internal static string TrunkBefore(string? pushedOnto)
    {
        var told = pushedOnto?.Trim();

        if (string.IsNullOrEmpty(told))
        {
            return Run("merge-base", "HEAD", Trunk).Trim();
        }

        return Behind(told)
            ? told
            : throw new InvalidOperationException(
                $"{PushedOnto} is '{told}', and a baseline has to be a commit this clone holds "
                + $"with HEAD descended from it. {AboutTheClone}");
    }

    /// <summary>
    /// Whether <paramref name="commit"/> is a full commit id this clone holds and HEAD stands after.
    /// </summary>
    /// <remarks>
    /// The shape first, so nothing that resolves to a ref is ever asked about: a name, a tag or
    /// `HEAD` would all answer the ancestry question honestly and mean something else. A push event
    /// carries forty hex digits or the all-zero id that says there was nothing before it, and the
    /// second is not a commit git can answer about, so it fails the ancestry test with the rest.
    /// </remarks>
    private static bool Behind(string commit) =>
        commit.Length == 40
        && commit.All(char.IsAsciiHexDigit)
        && Execute("merge-base", "--is-ancestor", commit, "HEAD").Code == 0;

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
    /// Ids are all it compares, so reordering the file and moving a claim between feature blocks
    /// are invisible to it, and renumbering a closed claim reads as one appearing — which is right,
    /// because check 10 already refuses renumbering. What a claim says is
    /// <see cref="RewordedIntoClosure"/>'s half: between them they name every closure the trunk was
    /// not already standing behind, under that id and in those words.
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
    /// The claims <paramref name="head"/> closes that <paramref name="baseline"/> had open in other
    /// words — a claim rewritten to describe what the change just built and then ticked, which is
    /// <see cref="BornTicked"/>'s defect with the id left alone.
    /// </summary>
    /// <remarks>
    /// It reaches a claim the baseline had open and nothing else, which is the whole of what ISC-176
    /// says. A claim already closed on the trunk is not being closed here whatever happens to its
    /// words, and refusing that would refuse what the repo has twice done right: `ISC-121` in PR #58
    /// and `ISC-120` in PR #74 each followed a product that had moved under a standing closure, and
    /// each rewrote its `## Verification` stub in the same commit to say so. The gate would have had
    /// to send both down a route with no reviewer on it.
    ///
    /// First wins where the baseline carries an id twice. Check 4 refuses that in the file as it
    /// stands and cannot promise it of a commit far enough back, so the alternative is a throw
    /// reporting a duplicate ID as this gate being broken.
    /// </remarks>
    public static IReadOnlyList<string> RewordedIntoClosure(IsaDocument baseline, IsaDocument head)
    {
        var open = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var claim in baseline.Claims.Where(claim => !claim.Closed))
        {
            open.TryAdd(claim.Id, claim.Text);
        }

        return
        [
            .. head.Claims
                .Where(claim => claim.Closed
                    && open.TryGetValue(claim.Id, out var was)
                    && !string.Equals(was, claim.Text, StringComparison.Ordinal))
                .Select(claim => claim.Id),
        ];
    }

    /// <summary>
    /// The claims <paramref name="head"/> rewords while they are already closed and leaves their
    /// `## Verification` stub exactly as it was — a tick standing over a sentence nobody probed.
    /// </summary>
    /// <remarks>
    /// The half <see cref="RewordedIntoClosure"/> deliberately does not reach: rewording a standing
    /// closure is allowed, and the two times this repo did it right are named there. What is not
    /// allowed is doing it and leaving the evidence pointing at the old sentence, which is how
    /// `ISC-157` came to read closed over a run against a narrower claim for eight days.
    ///
    /// One direction only. A stub is rewritten whenever the probe is re-run, and a claim that has
    /// not moved has nothing to say about that; the rule is that a moved claim drags its evidence
    /// with it, not that the two change together.
    ///
    /// What it sees is bytes: that the stub moved, never that a probe ran. A date bumped over a run
    /// nobody made passes, and no gate could tell — the residue is the reviewer's, and
    /// `references/format.md` says so where it says what else is.
    ///
    /// A claim with no stub on either side is out of reach here and is check 5's, which fails on a
    /// closed claim carrying no evidence at all. Nothing is left standing when nothing was there.
    /// </remarks>
    public static IReadOnlyList<string> StubLeftBehind(IsaDocument baseline, IsaDocument head)
    {
        var was = Closures(baseline);

        return
        [
            .. Closures(head)
                .Where(now => was.TryGetValue(now.Key, out var before)
                    && !string.Equals(before.Text, now.Value.Text, StringComparison.Ordinal)
                    && string.Equals(before.Evidence, now.Value.Evidence, StringComparison.Ordinal))
                .Select(now => now.Key),
        ];
    }

    /// <summary>
    /// Every closed claim in <paramref name="document"/> that carries a stub, as the sentence it
    /// makes and the evidence under it. First wins on both, the way
    /// <see cref="RewordedIntoClosure"/> takes duplicates: checks 4 and 14 refuse a second of
    /// either in the file as it stands and can promise nothing of a commit far enough back.
    /// </summary>
    private static Dictionary<string, (string Text, string Evidence)> Closures(IsaDocument document)
    {
        var stubs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var stub in document.Stubs)
        {
            stubs.TryAdd(stub.Id, stub.Evidence);
        }

        var closures = new Dictionary<string, (string Text, string Evidence)>(StringComparer.Ordinal);

        foreach (var claim in document.Claims.Where(claim => claim.Closed))
        {
            if (stubs.TryGetValue(claim.Id, out var evidence))
            {
                closures.TryAdd(claim.Id, (claim.Text, evidence));
            }
        }

        return closures;
    }

    /// <summary>
    /// The repo root, from this file's compile-time path rather than from a working directory that
    /// depends on where the runner was launched from.
    /// </summary>
    private static string Root([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Run(params string[] arguments)
    {
        var (code, answer, complaint) = Execute(arguments);

        return code == 0
            ? answer
            : throw new InvalidOperationException(
                $"`git {string.Join(' ', arguments)}` exited {code}: "
                + $"{complaint.Trim()}{Environment.NewLine}{AboutTheClone}");
    }

    /// <summary>
    /// git run for its exit code as well as its output, for the one question asked of a commit that
    /// may honestly not be here — <see cref="Reaches"/>, over a tip a force push orphaned.
    /// </summary>
    private static (int Code, string Answer, string Complaint) Execute(params string[] arguments)
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

        return (git.ExitCode, answer, complaint.GetAwaiter().GetResult());
    }

    /// <summary>
    /// git's own words come first and this comes after, because the ways this fails are several
    /// and a message that picks one sends the reader to fix something that is not wrong.
    /// </summary>
    private static string AboutTheClone =>
        $"Checks 15, 16 and 17 read ISA.md as {Trunk} had it before this change, so they need git, "
        + $"a remote called origin, and the history behind {Trunk} back to this branch's fork point. "
        + $"A shallow clone has none of it, and a {Trunk} nobody has fetched in a while resolves to "
        + "an older fork point than the real one — `git fetch` before believing what it named, and "
        + "merge `main` in if the fork point is what is behind. In CI on a push the baseline is "
        + $"`{PushedOnto}` instead, which is where the push began: a force push names a commit "
        + "nothing points at any more, and nothing can judge a push against history it destroyed. "
        + "CI asks its checkout for all the history; `.github/workflows/ci.yml` says why.";

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
