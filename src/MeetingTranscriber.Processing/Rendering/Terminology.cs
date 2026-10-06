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
        IReadOnlyDictionary<Guid, int>? nodeDepths = null) =>
        ApplyMarked(text, corrections, nodeDepths).Text;

    /// <summary>
    /// <see cref="Apply"/> and where it landed: the corrected text and, for each span a correction
    /// changed, what the stored words were there and what they read now.
    /// </summary>
    /// <remarks>
    /// The one computation behind both the words and their underline, so the two cannot disagree:
    /// <see cref="Apply"/> is this method's text, with the same order and the same whole-word rule.
    /// Each correction is applied to the output of the ones before it, so a mark made by one is
    /// moved by a later replacement that changes the length of something earlier in the line,
    /// updated by one inside it, and merged with one that straddles its edge, with the stored words
    /// under the whole union as its <c>Before</c>. A replacement that changes nothing leaves no mark.
    /// </remarks>
    public static MarkedText ApplyMarked(
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

        IReadOnlyList<CorrectionMark> marks = [];
        foreach (var correction in ordered)
        {
            (text, marks) = Replace(text, marks, correction.WrongText, correction.CorrectText, correction.MatchMode);
        }

        return new MarkedText(text, marks);
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
    /// Every whole-word occurrence, replaced left to right, with the marks carried through it.
    /// Written out rather than as a regular expression because the alias is a person's text: it can
    /// hold a dot, a dash or a bracket, and one that has to be escaped before it is safe is one
    /// that will not be, eventually.
    /// </summary>
    private static (string Text, IReadOnlyList<CorrectionMark> Marks) Replace(
        string text,
        IReadOnlyList<CorrectionMark> marks,
        string wrong,
        string right,
        TerminologyMatchMode mode)
    {
        var comparison = mode is TerminologyMatchMode.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // What this correction changes, where it stands in the text as it is now. A match that
        // already reads as the right text changes nothing and is left out, so it makes no mark.
        var changes = new List<(int Start, int End)>();
        var read = 0;
        while (read < text.Length)
        {
            var found = text.IndexOf(wrong, read, comparison);
            if (found < 0)
            {
                break;
            }

            if (IsWholeWord(text, found, wrong.Length)
                && !string.Equals(text.Substring(found, wrong.Length), right, StringComparison.Ordinal))
            {
                changes.Add((found, found + wrong.Length));
            }

            read = found + wrong.Length;
        }

        if (changes.Count == 0)
        {
            return (text, marks);
        }

        // The replacements and the marks already standing, in the order of the line. A cluster is a
        // run of them that overlap one another: marks alone are only carried to where they now are,
        // and a cluster holding a replacement becomes the one mark spanning all of it.
        var items = changes
            .Select(change => (change.Start, change.End, Mark: (CorrectionMark?)null))
            .Concat(marks.Select(mark => (mark.Start, End: mark.Start + mark.Length, Mark: (CorrectionMark?)mark)))
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Mark is null ? 1 : 0)
            .ToList();

        var built = new StringBuilder(text.Length);
        var made = new List<CorrectionMark>();
        var copied = 0;
        var index = 0;
        while (index < items.Count)
        {
            var first = items[index];
            var from = first.Start;
            var to = first.End;
            var cluster = new List<(int Start, int End, CorrectionMark? Mark)> { first };
            index++;

            while (index < items.Count && items[index].Start < to)
            {
                cluster.Add(items[index]);
                to = Math.Max(to, items[index].End);
                index++;
            }

            built.Append(text, copied, from - copied);
            var start = built.Length;

            if (cluster.All(item => item.Mark is not null))
            {
                built.Append(text, from, to - from);
                made.Add(cluster[0].Mark! with { Start = start });
                copied = to;
                continue;
            }

            // The replacements of this cluster, written over its span.
            var at = from;
            foreach (var change in cluster.Where(item => item.Mark is null))
            {
                built.Append(text, at, change.Start - at);
                built.Append(right);
                at = change.End;
            }

            built.Append(text, at, to - at);

            // The stored words under the span: a mark's own where one stands, and what the text
            // says in between, which no correction has touched.
            var before = new StringBuilder();
            var seen = from;
            foreach (var item in cluster.Where(item => item.Mark is not null))
            {
                before.Append(text, seen, item.Start - seen);
                before.Append(item.Mark!.Before);
                seen = item.End;
            }

            before.Append(text, seen, to - seen);

            var after = built.ToString(start, built.Length - start);
            if (!string.Equals(before.ToString(), after, StringComparison.Ordinal))
            {
                made.Add(new CorrectionMark(start, after.Length, before.ToString(), after));
            }

            copied = to;
        }

        built.Append(text, copied, text.Length - copied);
        return (built.ToString(), made);
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
