using System.Globalization;

using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The shape every command answers in: a label, the values under each other, and one way of
/// writing an offset into a meeting.
/// </summary>
/// <remarks>
/// One place rather than a format string per command, because the output of a diagnostic is read
/// by a person comparing two runs of it and by whatever somebody pipes it into. Neither is served
/// by <c>turns</c> lining up under <c>corpus</c> in one command and not in the next.
/// </remarks>
public static class Report
{
    /// <summary>How wide the labels are, so the values line up under each other.</summary>
    private const int LabelWidth = 12;

    public static void Line(TextWriter output, string label, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(label);

        // One space always, whatever the label's length. Padding alone puts the value hard against
        // a label that is already as wide as the column, and a report whose separator disappears on
        // one line is one nothing can read a value back out of.
        output.WriteLine($"{label.PadRight(LabelWidth - 1)} {value}");
    }

    /// <summary>
    /// Where on the timeline something was said, as a person reads a clock rather than as the
    /// milliseconds the corpus stores.
    /// </summary>
    public static string Offset(Duration offset) =>
        offset.ToTimeSpan().ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
}
