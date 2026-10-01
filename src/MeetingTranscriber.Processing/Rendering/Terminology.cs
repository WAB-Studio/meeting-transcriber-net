using System.Text;

using MeetingTranscriber.Domain.Knowledge;
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
/// "ML" rewrites the middle of "html". What counts as a word is <see cref="Spellings.IsWordCharacter"/>'s,
/// so a form found in the corpus is split by the rule a correction made from it is applied by.
/// </para>
/// </remarks>
public static class Terminology
{
    /// <summary>
    /// Applies every correction that reaches this text. Order is fixed rather than the order the
    /// rows came back in, so the same corpus renders the same way whatever the query planner did.
    /// </summary>
    /// <remarks>
    /// Longest wrong text first, then the narrower place: the first correction to replace a word
    /// leaves nothing for a later one of the same form, so a correction made under one organization
    /// would lose to an everywhere-correction if both were left to the order of the rows. Place comes
    /// before the text so that two forms differing only in case cannot put the everywhere-correction
    /// first. Between two node corrections the deeper node goes first, because a correction made under a
    /// project is the more specific word inside it; the depth comes from <paramref name="nodeDepths"/>,
    /// and a node missing from it is ranked as a root. Only a caller holding no tree passes nothing,
    /// and then two nodes tie and fall to the id.
    /// </remarks>
    /// <param name="nodeDepths">The depth of each node a correction may name, as the tree has it.</param>
    public static string Apply(
        string text,
        IEnumerable<TerminologyCorrection> corrections,
        IReadOnlyDictionary<Guid, int>? nodeDepths = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);

        int Place(TerminologyCorrection correction) => PlaceOf(correction, nodeDepths);

        var ordered = corrections
            .Where(correction => !string.IsNullOrEmpty(correction.WrongText))
            .OrderByDescending(correction => correction.WrongText.Length)
            .ThenBy(Place)
            .ThenBy(correction => correction.WrongText, StringComparer.Ordinal)
            .ThenBy(correction => correction.Id);

        foreach (var correction in ordered)
        {
            text = Replace(text, correction.WrongText, correction.CorrectText, correction.MatchMode);
        }

        return text;
    }

    /// <summary>
    /// A meeting's own correction is narrowest, a node's is next with the deeper node before the
    /// shallower, and everywhere is widest.
    /// </summary>
    private static int PlaceOf(TerminologyCorrection correction, IReadOnlyDictionary<Guid, int>? nodeDepths)
    {
        if (correction.MeetingId is not null)
        {
            return 0;
        }

        if (correction.NodeId is not { } node)
        {
            return int.MaxValue;
        }

        var depth = nodeDepths is not null && nodeDepths.TryGetValue(node, out var known) ? known : 0;
        return 1 + Math.Max(0, Node.MaxDepth - depth);
    }

    /// <summary>
    /// Whether <see cref="Apply"/> would replace anything in <paramref name="text"/> with this
    /// correction: its wrong text as a whole word, in the correction's mode. It asks the same
    /// <c>IsWholeWord</c> the replacement does. It judges the text as given: a correction whose wrong
    /// text only appears once an earlier correction has replaced something is not seen.
    /// </summary>
    public static bool Reaches(string text, TerminologyCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(correction);

        if (string.IsNullOrEmpty(correction.WrongText))
        {
            return false;
        }

        var comparison = correction.MatchMode is TerminologyMatchMode.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var read = 0;
        while (read < text.Length)
        {
            var found = text.IndexOf(correction.WrongText, read, comparison);
            if (found < 0)
            {
                return false;
            }

            if (IsWholeWord(text, found, correction.WrongText.Length))
            {
                return true;
            }

            read = found + correction.WrongText.Length;
        }

        return false;
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
        !Spellings.IsWordCharacter(inside)
        || index < 0
        || index >= text.Length
        || !Spellings.IsWordCharacter(text[index]);
}
