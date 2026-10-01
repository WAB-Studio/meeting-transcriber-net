using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Processing.Summaries.ClaudeCode;

namespace MeetingTranscriber.App;

/// <summary>
/// Where this machine's Claude Code is, and the provider the runner's pump sends a summary with —
/// the summarising half of what <see cref="TranscribingOnThisMachinesKey"/> is for a transcription.
/// </summary>
/// <remarks>
/// Here and nowhere a suite can reference, the same reason <see cref="TranscribingOnThisMachinesKey"/>
/// gives for itself: this class adds nothing beyond where Claude Code is and how that is composed
/// into a provider, and both halves are already proven on their own —
/// <c>ClaudeCodeLocation.Chosen()</c> in <c>ClaudeCodeLocationTests</c>, and the search itself in
/// <c>ClaudeCodeExecutableTests</c>. What a suite gets instead is <see cref="ISummaryProvider"/>
/// itself, driven through <c>FakeSummaries</c> in every fact about what a summary comes to, and
/// through <c>ClaudeCodeSummariesTests</c> for the real adapter.
/// </remarks>
internal static class SummarisingOnThisMachine
{
    /// <summary>
    /// This machine's own answer: what was chosen under this user's profile, or a search of this
    /// process's own <c>PATH</c>.
    /// </summary>
    public static FileInfo? WhereClaudeCodeIs() => ClaudeCodeExecutable.Find(
        ClaudeCodeLocation.OfThisUser().Chosen(), Environment.GetEnvironmentVariable("PATH"));

    /// <summary>What the runner's pump sends a summary with, on this machine's own Claude Code.</summary>
    public static ISummaryProvider Provider() => ClaudeCodeSummaries.OnThisMachine(WhereClaudeCodeIs);

    /// <summary>
    /// The memory file a summary would be refused for on this machine right now, or nothing. The
    /// meetings list asks it at draw time, so a refusal names the file as it stands and the path is
    /// never stored.
    /// </summary>
    public static FileInfo? MemoryFileInTheWay() => ClaudeCodeSummaries.OnThisMachine(WhereClaudeCodeIs).MemoryFileInTheWay();
}
