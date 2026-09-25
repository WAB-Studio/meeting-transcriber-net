namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// ISC-89 as a rule rather than as a sentence in the goal: a program nobody starts cannot be
/// missing, so nothing that records, transcribes, renders, searches or recovers may depend on
/// Claude Code being installed — only a future summary, which does not exist yet, may ever start
/// one.
/// </summary>
/// <remarks>
/// The two regexes and the build-output skip are
/// <see cref="NothingUnderTestReachesTheNetworkTests"/>'s own, reused and not copied —
/// <see cref="NothingUnderTestReachesTheNetworkTests.Starts"/> is the one place a process start is
/// spelled in this repository, over <c>tests/</c> today, and this applies it to <c>src/</c>
/// instead, so a start is spelled one way wherever it is read. The walk is written out here rather
/// than taken from that class's own, because that one reads <c>tests/</c> and skips the file
/// holding its rule. What is this file's own is the inventory: a file that starts a process under
/// <c>src/</c> is on the product's own path rather than a suite's, so every entry here says why
/// what it starts costs nobody who has not already reached the corner of the screen that starts
/// it.
/// <para>
/// It reads source text, not compiled IL, so what it cannot see is a process started from inside a
/// referenced package, a <c>P/Invoke</c>, or a call spelled through an alias or a helper method —
/// the same honest limit <see cref="NothingUnderTestReachesTheNetworkTests"/> names for its own
/// scan. Nor is starting a process the only way to depend on Claude Code being installed; reading
/// its config or requiring it on <c>PATH</c> without launching it would not trip this either. What
/// closes ISC-89 is that nothing in this repository does either today, checked by reading every
/// project's dependencies alongside the walk, not by the walk alone.
/// </para>
/// </remarks>
public class ClaudeCodeIsOptionalTests
{
    /// <summary>
    /// Every file under <c>src/</c> that may start a process, and what each one starts. Extend it
    /// by adding the file with its sentence — never by widening the pattern.
    /// </summary>
    private static readonly Allowed[] StartsAProcess =
    [
        new(
            "src/MeetingTranscriber.App/PackagingChecksWindow.xaml.cs",
            "cmd.exe, twice — to see where a child of the package writes and what environment it "
            + "is handed. It is the temporary packaging scaffold reached from a corner of the "
            + "recording screen, and nothing that records, transcribes, renders, searches or "
            + "recovers goes through it."),
    ];

    /// <summary>
    /// Every <c>*.cs</c> under <c>src/</c> that starts a process is on <see cref="StartsAProcess"/>
    /// and nowhere else, so a flow this rule has not looked at cannot yet depend on a program that
    /// might not be there.
    /// </summary>
    /// <remarks>
    /// The whole set and not a contains, both directions, for
    /// <see cref="NothingUnderTestReachesTheNetworkTests.Nothing_under_test_starts_a_process_this_rule_has_not_seen"/>'s
    /// own reason: a file arriving is the thing this is for, and a file that stopped starting a
    /// process goes missing from the list, so an entry cannot outlive what it was written about.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_product_starts_a_program_this_rule_has_not_seen()
    {
        var root = IsaDocument.Root();
        var src = new DirectoryInfo(Path.Combine(root.FullName, "src"));

        var found = src
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !NothingUnderTestReachesTheNetworkTests.Built(file))
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .ToArray();

        found.ShouldNotBeEmpty("no `*.cs` file was found under `src/`, so this check reads nothing.");

        var starting = found
            .Where(file => NothingUnderTestReachesTheNetworkTests.Starts().IsMatch(
                NothingUnderTestReachesTheNetworkTests.Comment()
                    .Replace(File.ReadAllText(file.FullName), " ")))
            .Select(file => Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        starting.ShouldBe(
            StartsAProcess.Select(allowed => allowed.File).Order(StringComparer.Ordinal),
            "a flow that starts a process can be missing whatever that process is, and Claude Code "
            + "is one of those. What may start one is written down with what it starts:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                StartsAProcess.Select(allowed => $"  {allowed.File} — {allowed.Starts}"))
            + Environment.NewLine
            + "If a file is missing from that list, add it there with the one sentence saying what "
            + "it launches and why every other flow still works without it — the summary adapter "
            + "is the one caller expected to join it. If one is on it and no longer starts "
            + "anything, take it off.");
    }

    /// <summary>One file that may start a process, and the one sentence saying what it starts and why.</summary>
    private sealed record Allowed(string File, string Starts);
}
