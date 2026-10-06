namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// How much reasoning a summary is asked to spend, chosen once in the settings rather than per
/// meeting.
/// </summary>
/// <remarks>
/// <para>
/// Closed, and each member is a level Claude Code's <c>--effort</c> takes once <c>WireNames</c> has
/// lower-cased it: <c>high</c>, <c>medium</c>, <c>low</c>. Measured against <c>claude --help</c> on
/// 2.1.289, which lists <c>low, medium, high, xhigh, max</c>; the two above <see cref="High"/> are
/// left out on purpose, because they spend more of a person's plan on a task whose answer is a
/// fixed shape over one meeting, and nobody has asked for them. Adding one is a member here and a
/// name on the settings screen.
/// </para>
/// <para>
/// Here beside <see cref="SummaryModel"/> for the same reason that one is: a preference stored as a
/// <c>settings</c> row, which no job carries — a summary already running keeps the effort it was
/// sent with. The numbers are given for the reason <see cref="AfterARecording"/> gives.
/// </para>
/// </remarks>
public enum SummaryEffort
{
    /// <summary>Claude Code's <c>high</c> level, what a summary is asked for until somebody chooses another (not measured to be the CLI's own default).</summary>
    High = 1,

    /// <summary>Claude Code's <c>medium</c> level.</summary>
    Medium = 2,

    /// <summary>Claude Code's <c>low</c> level.</summary>
    Low = 3,
}
