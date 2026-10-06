using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>
/// A span of corrected text: where it stands in it, what the stored turn said there and what it
/// reads now.
/// </summary>
public sealed record CorrectionMark(int Start, int Length, string Before, string After);

/// <summary>A corrected text and every span a correction changed in it.</summary>
public sealed record MarkedText(string Text, IReadOnlyList<CorrectionMark> Marks);

/// <summary>A turn as it reads, with the spans of its words that a correction changed.</summary>
/// <remarks>
/// It wraps <see cref="Turn"/> and does not change it: the stored turn is what a citation is checked
/// against.
/// </remarks>
public sealed record MarkedTurn(Turn Turn, IReadOnlyList<CorrectionMark> Marks);

/// <summary>One correction as it landed in a transcript, and how many times it did.</summary>
public sealed record CorrectionSeen(string Before, string After, int Times);

/// <summary>What the marks of a transcript add up to.</summary>
public static class CorrectionMarks
{
    /// <summary>
    /// The corrections that changed a word of these turns, one row per distinct pair of words before
    /// (case-blind) and after, with how many times it landed. It is made from the marks and never
    /// from the corrections that reach the meeting, so one that reaches it and changed nothing is
    /// not listed.
    /// </summary>
    public static IReadOnlyList<CorrectionSeen> Seen(IEnumerable<MarkedTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        return
        [
            .. turns
                .SelectMany(turn => turn.Marks)
                .GroupBy(mark => (Before: mark.Before.ToUpperInvariant(), mark.After))
                .Select(group => new CorrectionSeen(group.First().Before, group.Key.After, group.Count()))
                .OrderByDescending(seen => seen.Times)
                .ThenBy(seen => seen.After, StringComparer.Ordinal),
        ];
    }
}
