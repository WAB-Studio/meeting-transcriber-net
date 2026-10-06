namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// Which model writes a meeting's summary, chosen once in the settings rather than per meeting.
/// </summary>
/// <remarks>
/// <para>
/// Closed, and each member is the alias Claude Code's <c>--model</c> takes once <c>WireNames</c>
/// has lower-cased it: <c>sonnet</c>, <c>opus</c>, <c>haiku</c>. Here beside
/// <see cref="AfterARecording"/> for the same reason that one is: it is a preference stored as a
/// <c>settings</c> row, and nothing in <c>Domain/Jobs/</c> knows about it — a job never carries a
/// model, so a summary already running keeps the one it was sent with.
/// </para>
/// <para>
/// The numbers are given for the reason <see cref="AfterARecording"/> gives.
/// </para>
/// </remarks>
public enum SummaryModel
{
    /// <summary>Claude Code's <c>sonnet</c> alias, the model every summary ran on before this was a choice.</summary>
    Sonnet = 1,

    /// <summary>Claude Code's <c>opus</c> alias.</summary>
    Opus = 2,

    /// <summary>Claude Code's <c>haiku</c> alias.</summary>
    Haiku = 3,
}
