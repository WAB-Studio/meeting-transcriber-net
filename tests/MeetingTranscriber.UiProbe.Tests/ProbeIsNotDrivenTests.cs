using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.UiProbe.Tests;

/// <summary>
/// The line this suite exists on the far side of: it may reference the probe and it may not drive
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/layout.md</c> used to say nothing under <c>tests/</c> could depend on the probe at all,
/// and structure enforced it — with no reference there was nothing to call. This suite is why the
/// sentence narrowed to <em>drive</em>, and a rule that used to be held by the compiler has to be
/// held by something. This is it.
/// </para>
/// <para>
/// What it forbids is the types that start a window, press what is on it, or wait for one:
/// everything a build agent cannot run and a person's desktop can. A fact that named one of them
/// would not fail on a build agent by itself — it would hang, or start somebody's application on
/// their own corpus — which is why the sweep is over source and not over behaviour.
/// </para>
/// </remarks>
public class ProbeIsNotDrivenTests
{
    /// <summary>
    /// Everything in the probe that starts the application, presses what is on it, or holds the
    /// thread a window is driven from.
    /// </summary>
    /// <remarks>
    /// Spelled in halves for the reason <c>LiveDeepgramTests</c> spells its own patterns in halves:
    /// the sweep reads every file of this suite, and a guard that names its subject in one piece is
    /// a guard that finds itself and can never be green.
    /// </remarks>
    private const string Drives =
        @"\b(Sess" + "ion|Launched" + "App|Mcp" + "Host|Command" + "Line|Ui" + "Thread|Bear"
        + "ings|Probe" + @"Corpus)\b";

    /// <summary>
    /// Nothing here names anything that opens or presses a window.
    /// </summary>
    /// <remarks>
    /// The allowlist is the two walks and what they are made of, and widening it is the wrong
    /// answer to a fact that wants a window: that fact belongs in <c>docs/ui-probe.md</c>'s hands,
    /// run by a person, and recorded in <c>ISA.md</c> like every other probe nothing automates.
    /// </remarks>
    [Fact]
    public void Nothing_in_this_suite_starts_presses_or_waits_on_a_window() =>
        Naming(Drives)
            .ShouldBeEmpty(
                "every one of those either starts the application, presses what is on it, or holds "
                + "the thread a window is driven from — and the probe needs an interactive desktop, "
                + "so a build agent running one hangs or opens somebody's own application on "
                + "somebody's own corpus. This suite reaches the halves that open no window: two "
                + "walks over a folder tree. A fact that wants a window is a person's run.");

    /// <summary>Which files of this suite match, by name, in a fixed order.</summary>
    private static IReadOnlyList<string> Naming(string pattern)
    {
        var suite = new DirectoryInfo(Path.GetDirectoryName(Here())!);

        suite.Exists.ShouldBeTrue(
            $"'{suite.FullName}' is the suite this sweep is about, and a sweep over a folder that "
            + "is not there reads nothing and passes.");

        return
        [
            .. suite
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !Inside(file, "obj") && !Inside(file, "bin"))
                .Where(file => Regex.IsMatch(File.ReadAllText(file.FullName), pattern))
                .Select(file => file.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool Inside(FileInfo file, string folder) => file.FullName.Contains(
        $"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}",
        StringComparison.Ordinal);

    private static string Here([CallerFilePath] string file = "") => file;
}
