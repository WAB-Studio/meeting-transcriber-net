using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The one ordering <c>MeetingRecording</c> owns and nothing else can: a recording that failed still
/// lets its capture session go, whether it failed starting or stopping.
/// </summary>
/// <remarks>
/// <para>
/// A tripwire and not a proof, and worth being exact about which. Pressing stop on a real session
/// and having it throw needs a device: <c>MeetingRecording</c>'s only door is <c>Start</c>, whose
/// only door is <c>CaptureSession.Start</c>, whose only door is a WASAPI endpoint, and no test in
/// this repository has ever opened one. So this reads the source and checks the shape, which
/// catches the line somebody deletes and not the rewrite somebody argues for.
/// </para>
/// <para>
/// It reads past whole-line comments: every line whose first characters are <c>//</c>, <c>*</c> or
/// <c>/*</c> is dropped before anything is matched, which is the exclusion <c>SavingCardTests</c>
/// writes into its own pattern. Otherwise the comment explaining this rule would satisfy it, and a
/// guard held up by its own explanation is one nobody can rely on. A comment trailing a line of
/// code survives that strip and could still satisfy a match — <c>SourceLines</c> in the application
/// tests is the index-based answer to that, and it is <c>internal</c> to a project this one cannot
/// reach.
/// </para>
/// <para>
/// Both of the shaped patterns bind to the <b>first</b> <c>catch</c> after the token they start
/// from, rather than scanning the rest of the file for any <c>catch</c> that would satisfy them.
/// That is what keeps them from being disarmed by an edit somewhere else: a catch narrowed here
/// cannot be answered by a bare one in the next member down, and a member order this happens to
/// rely on is not a rule anybody has to be told about.
/// </para>
/// </remarks>
public partial class StoppingARecordingTests
{
    [Fact]
    public void A_recording_that_failed_still_lets_the_session_go()
    {
        var code = CodeOf(TheRecording());

        StopsTheSession().Matches(code).Count.ShouldBe(
            1,
            "MeetingRecording.cs stops its capture session in more than one place. What happens "
            + "when that throws is decided at the one place there is, and a second would write its "
            + "own answer. (This reads one file: a caller in another project is invisible to it.)");

        LetsGoHoweverItWent().IsMatch(code).ShouldBeTrue(
            "MeetingRecording.Stop no longer lets the session go when stopping it throws. The "
            + "session holds two devices, its block files and the mark over the folder, and "
            + "CaptureSession.Dispose is the only thing that releases any of them — so one left "
            + "undisposed holds a meeting's blocks until the process ends, and "
            + "MeetingRecordings.Finish cannot read back the recording that just failed.");

        AsksTheHelperOnBothFailingPaths().Matches(code).Count.ShouldBe(
            2,
            "the two ways a recording fails — a start whose row could not be written, and a stop a "
            + "device refused — are what LetGoOf exists for, and both have to ask it. If a third "
            + "path was routed through it on purpose, this number is the one line to change.");

        SwallowsWhateverLettingGoThrows().IsMatch(code).ShouldBeTrue(
            "MeetingRecording.LetGoOf catches by name, or is no longer shaped the way this reads "
            + "for. Letting a session go cannot throw an AudioCaptureException — a source already "
            + "swallows what a device does — so a named catch is dead, and the IOException the "
            + "mark's own handle can throw sails past it and replaces the sentence saying why the "
            + "meeting could not be stopped.");
    }

    [GeneratedRegex(@"session\.Stop\(\)")]
    private static partial Regex StopsTheSession();

    /// <summary>
    /// The first <c>catch</c> after the stop reaches <c>LetGoOf</c> before it reaches any
    /// <c>throw;</c> — so the session is let go of on the way out of that stop, and not by some
    /// later member that happens to carry the same tokens in the same order.
    /// </summary>
    [GeneratedRegex(
        @"session\.Stop\(\)(?:(?!\bcatch\b)[\s\S])*?\bcatch\b(?:(?!\bthrow;)[\s\S])*?"
        + @"LetGoOf\(session\);")]
    private static partial Regex LetsGoHoweverItWent();

    [GeneratedRegex(@"LetGoOf\(session\);")]
    private static partial Regex AsksTheHelperOnBothFailingPaths();

    /// <summary>
    /// The first <c>catch</c> inside the helper takes no exception type. Bound to that first one,
    /// so narrowing it cannot be answered by a bare <c>catch</c> anywhere below it.
    /// </summary>
    [GeneratedRegex(
        @"void LetGoOf\(CaptureSession session\)(?:(?!\bcatch\b)[\s\S])*?\bcatch\b(?!\s*\()")]
    private static partial Regex SwallowsWhateverLettingGoThrows();

    /// <summary>The file with every line that is only a comment taken out.</summary>
    private static string CodeOf(FileInfo file) =>
        string.Join('\n', File.ReadLines(file.FullName).Where(line => !IsProse(line)));

    private static bool IsProse(string line) =>
        line.TrimStart() is var start
        && (start.StartsWith("//", StringComparison.Ordinal)
            || start.StartsWith('*')
            || start.StartsWith("/*", StringComparison.Ordinal));

    private static FileInfo TheRecording() => new(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFile())!, "..", "..",
        "src", "MeetingTranscriber.Recording", "MeetingRecording.cs")));

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
