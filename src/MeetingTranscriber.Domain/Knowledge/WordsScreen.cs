using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// What was corrected to one right word in one place: the forms it replaces, and when the newest of
/// them was made.
/// </summary>
/// <param name="Right">What the forms are replaced by.</param>
/// <param name="Under">The node the correction holds in, or nothing when it holds everywhere.</param>
/// <param name="Meeting">The one meeting it holds in, or nothing.</param>
/// <param name="Wrong">The forms, in ordinal order.</param>
/// <param name="Latest">The newest <see cref="TerminologyCorrection.CreatedAt"/> among them.</param>
public sealed record WordCorrected(
    string Right, Guid? Under, Guid? Meeting, IReadOnlyList<string> Wrong, UtcTimestamp Latest);

/// <summary>
/// The rules the window that corrects words reads its controls off, so that what is ticked, what
/// can be pressed and what is listed are decided once and not in the markup's code.
/// </summary>
public static class WordsScreen
{
    /// <summary>
    /// How alike a form has to be to what was typed to start ticked. It is the line
    /// <c>Correcciones.dc.html</c> draws: for <c>Nubeko</c>, <c>nubeco</c> and <c>nube co</c> are
    /// ticked and <c>nuveco</c> and <c>nubes</c> are not.
    /// </summary>
    public const double TickedAtFirst = 0.8;

    /// <summary>How many words that turned up by themselves the window lists before saying more are left.</summary>
    public const int SuspectsShown = 3;

    /// <summary>How many corrections already made the window lists before saying more are left.</summary>
    public const int CorrectedShown = 3;

    /// <summary>Whether a form starts ticked.</summary>
    public static bool StartsTicked(WrittenForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        return form.Resemblance >= TickedAtFirst;
    }

    /// <summary>Something has to be typed and at least one form ticked for there to be anything to save.</summary>
    public static bool MayBeSaved(string typed, int ticked) =>
        !string.IsNullOrWhiteSpace(typed) && ticked > 0;

    /// <summary>
    /// The corrections already made, one row per right word in one place, newest first and then by
    /// right word. The place is part of the row: the same word corrected everywhere and under one
    /// node are two statements and read as two.
    /// </summary>
    public static IReadOnlyList<WordCorrected> Corrected(IEnumerable<TerminologyCorrection> made)
    {
        ArgumentNullException.ThrowIfNull(made);

        return
        [
            .. made
                .GroupBy(correction => (correction.CorrectText, correction.NodeId, correction.MeetingId))
                .Select(group => new WordCorrected(
                    group.Key.CorrectText,
                    group.Key.NodeId,
                    group.Key.MeetingId,
                    [.. group.Select(correction => correction.WrongText).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)],
                    group.Max(correction => correction.CreatedAt)))
                .OrderByDescending(row => row.Latest)
                .ThenBy(row => row.Right, StringComparer.Ordinal),
        ];
    }
}
