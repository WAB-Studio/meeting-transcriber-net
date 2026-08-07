using System.Globalization;
using System.Text;

using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>
/// What a person said the transcription gets wrong, applied to a rendered view and never to the
/// response it was rendered from.
/// </summary>
/// <remarks>
/// <para>
/// The paid response and the stored turns keep the words the provider returned, because they are
/// what a citation is checked against: a quote that has been silently corrected no longer matches
/// the evidence it claims to come from. So this runs on the way out, every time, and a correction
/// added tomorrow changes tomorrow's render of a meeting recorded last year.
/// </para>
/// <para>
/// Two rules carried over from the Python renderer, both learned the hard way. Longest first, so
/// that a term which is a prefix of another does not eat it — correcting "Coati" before "Coati
/// Cloud" leaves the second half stranded. And whole words only: without it, correcting "ml" to
/// "ML" rewrites the middle of "html".
/// </para>
/// </remarks>
public static class Terminology
{
    /// <summary>
    /// Applies every correction that reaches this text. Order is fixed rather than the order the
    /// rows came back in, so the same corpus renders the same way whatever the query planner did.
    /// </summary>
    public static string Apply(string text, IEnumerable<TerminologyCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);

        var ordered = corrections
            .Where(correction => !string.IsNullOrEmpty(correction.WrongText))
            .OrderByDescending(correction => correction.WrongText.Length)
            .ThenBy(correction => correction.WrongText, StringComparer.Ordinal);

        foreach (var correction in ordered)
        {
            text = Replace(text, correction.WrongText, correction.CorrectText, correction.MatchMode);
        }

        return text;
    }

    /// <summary>
    /// Every whole-word occurrence, replaced left to right. Written out rather than as a regular
    /// expression because the alias is a person's text: it can hold a dot, a dash or a bracket, and
    /// one that has to be escaped before it is safe is one that will not be, eventually.
    /// </summary>
    private static string Replace(string text, string wrong, string right, TerminologyMatchMode mode)
    {
        var comparison = mode is TerminologyMatchMode.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var built = new StringBuilder(text.Length);
        var read = 0;
        while (read < text.Length)
        {
            var found = text.IndexOf(wrong, read, comparison);
            if (found < 0)
            {
                break;
            }

            built.Append(text, read, found - read);
            if (IsWholeWord(text, found, wrong.Length))
            {
                built.Append(right);
            }
            else
            {
                built.Append(text, found, wrong.Length);
            }

            read = found + wrong.Length;
        }

        built.Append(text, read, text.Length - read);
        return built.ToString();
    }

    /// <summary>
    /// Whether what sits at that position is the whole word and not the inside of a longer one.
    /// A term that starts or ends with punctuation — <c>gh.</c>, <c>c++</c> — has no word boundary
    /// to check on that side, so that side is not checked: requiring one would make the correction
    /// never apply, which is worse than applying it once too often.
    /// </summary>
    private static bool IsWholeWord(string text, int at, int length) =>
        Boundary(text, at - 1, text[at])
        && Boundary(text, at + length, text[at + length - 1]);

    private static bool Boundary(string text, int index, char inside) =>
        !IsWordCharacter(inside)
        || index < 0
        || index >= text.Length
        || !IsWordCharacter(text[index]);

    /// <summary>
    /// What counts as part of a word. Letters, digits, underscore and the marks that ride on a
    /// letter, so "sesión" is one word in a corpus that is mostly Spanish.
    /// </summary>
    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character)
        || character is '_'
        || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark;
}
