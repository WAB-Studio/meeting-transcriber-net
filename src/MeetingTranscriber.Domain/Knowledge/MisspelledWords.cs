namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>One stored turn's words, and the meeting they were said in.</summary>
public sealed record TurnText(Guid Meeting, string Text);

/// <summary>One way the corpus wrote something, and how often.</summary>
public sealed record WrittenForm(string Text, int Times, int Meetings, double Resemblance);

/// <summary>One word of a response as the provider wrote it, with how sure the provider was.</summary>
public sealed record WordAsHeard(string Written, double Confidence);

/// <summary>A word as the corpus heard it: how often, and how sure the provider was on average.</summary>
public sealed record HeardWord(string Word, int Times, double MeanConfidence);

/// <summary>A word the provider seems to keep getting wrong, and the commoner word it looks like.</summary>
public sealed record SuspectWord(
    string Word, int Times, double MeanConfidence, string LooksLike, int LooksLikeTimes);

/// <summary>
/// Finding the words a transcript gets wrong without asking a model: counting how the corpus
/// wrote them, and weighing how sure the provider was.
/// </summary>
/// <remarks>
/// The thresholds are tuned, not argued, and they change only what is offered — never what is
/// charged or stored. <see cref="LikeTyped"/> has no cap on how many forms come back because the
/// claim is every way a term was written; a screen that cannot show them all pages them.
/// </remarks>
public static class MisspelledWords
{
    /// <summary>How alike a form must be to what was typed to be offered.</summary>
    public const double CloseEnough = 0.6;

    /// <summary>The fewest characters, folded, a word has to have to be a suspect.</summary>
    public const int ShortestWord = 4;

    /// <summary>A word the provider was on average less sure of than this is doubtful.</summary>
    public const double DoubtfulBelow = 0.70;

    /// <summary>How often a word has to have been heard to be a suspect.</summary>
    public const int AtLeast = 3;

    /// <summary>How many times as often a lookalike has to have been heard.</summary>
    public const int FarMoreOften = 5;

    /// <summary>How alike a suspect and its lookalike have to be.</summary>
    public const double Alike = 0.75;

    /// <summary>
    /// Every way the turns wrote something like <paramref name="typed"/>, closest first. A term of
    /// <c>k</c> words is compared against every run of one fewer to one more words inside one
    /// turn, which is what lets <c>Nubeko</c> find <c>nube co</c>. A run of other than <c>k</c>
    /// words counts only where it beats every <c>k</c>-word run it overlaps, so a function word
    /// beside a variant never becomes a form of its own.
    /// </summary>
    public static IReadOnlyList<WrittenForm> LikeTyped(string typed, IEnumerable<TurnText> turns)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(turns);

        var typedWords = Spellings.WordsOf(typed);
        var k = typedWords.Count;
        if (k is 0)
        {
            return [];
        }

        var asTyped = string.Join(' ', typedWords.Select(word => word.ToLowerInvariant()));
        var counted = new Dictionary<string, (int Times, HashSet<Guid> Meetings)>(StringComparer.Ordinal);

        foreach (var turn in turns)
        {
            var words = Spellings.WordsOf(turn.Text)
                .Select(word => word.ToLowerInvariant())
                .ToArray();

            var exact = new Dictionary<int, double>();
            for (var start = 0; start + k <= words.Length; start++)
            {
                exact[start] = Spellings.Resemblance(typed, string.Join(' ', words[start..(start + k)]));
            }

            for (var length = Math.Max(1, k - 1); length <= k + 1; length++)
            {
                for (var start = 0; start + length <= words.Length; start++)
                {
                    var form = string.Join(' ', words[start..(start + length)]);
                    if (form == asTyped)
                    {
                        continue;
                    }

                    var resemblance = Spellings.Resemblance(typed, form);
                    if (resemblance < CloseEnough)
                    {
                        continue;
                    }

                    // The k-word runs sharing a word with this one start after start - k and
                    // before start + length.
                    if (length != k
                        && exact.Any(run => run.Key > start - k && run.Key < start + length
                            && run.Value >= resemblance))
                    {
                        continue;
                    }

                    var seen = counted.TryGetValue(form, out var existing)
                        ? existing
                        : (Times: 0, Meetings: new HashSet<Guid>());
                    seen.Meetings.Add(turn.Meeting);
                    counted[form] = (seen.Times + 1, seen.Meetings);
                }
            }
        }

        return
        [
            .. counted
                .Select(form => new WrittenForm(
                    form.Key, form.Value.Times, form.Value.Meetings.Count, Spellings.Resemblance(typed, form.Key)))
                .OrderByDescending(form => form.Resemblance)
                .ThenByDescending(form => form.Times)
                .ThenBy(form => form.Text, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Each written word cut where a correction cuts it, lower-cased, every piece carrying that
    /// word's confidence, then grouped.
    /// </summary>
    public static IReadOnlyList<HeardWord> Tally(IEnumerable<WordAsHeard> heard)
    {
        ArgumentNullException.ThrowIfNull(heard);

        return
        [
            .. heard
                .SelectMany(word => Spellings.WordsOf(word.Written)
                    .Select(piece => (Word: piece.ToLowerInvariant(), word.Confidence)))
                .GroupBy(piece => piece.Word, StringComparer.Ordinal)
                .Select(group => new HeardWord(group.Key, group.Count(), group.Average(piece => piece.Confidence)))
                .OrderBy(word => word.Word, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The words the provider was unsure of, heard often, that look like a word heard far more
    /// often — and that nobody has corrected or said is right.
    /// </summary>
    public static IReadOnlyList<SuspectWord> Unprompted(
        IEnumerable<HeardWord> heard, IEnumerable<string> alreadyAnswered)
    {
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentNullException.ThrowIfNull(alreadyAnswered);

        var words = heard.ToList();
        var corrected = alreadyAnswered
            .Select(wrong => wrong.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var suspects = new List<SuspectWord>();
        foreach (var word in words)
        {
            if (Spellings.Folded(word.Word).Length < ShortestWord
                || word.MeanConfidence >= DoubtfulBelow
                || word.Times < AtLeast
                || corrected.Contains(word.Word))
            {
                continue;
            }

            var lookalike = words
                .Where(other => other.Word != word.Word && other.Times >= FarMoreOften * word.Times)
                .Select(other => (Other: other, Resemblance: Spellings.Resemblance(word.Word, other.Word)))
                .Where(candidate => candidate.Resemblance >= Alike)
                .OrderByDescending(candidate => candidate.Other.Times)
                .ThenByDescending(candidate => candidate.Resemblance)
                .ThenBy(candidate => candidate.Other.Word, StringComparer.Ordinal)
                .Select(candidate => candidate.Other)
                .FirstOrDefault();

            if (lookalike is not null)
            {
                suspects.Add(new SuspectWord(
                    word.Word, word.Times, word.MeanConfidence, lookalike.Word, lookalike.Times));
            }
        }

        return
        [
            .. suspects
                .OrderByDescending(suspect => suspect.Times)
                .ThenBy(suspect => suspect.Word, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The suspects one meeting heard, kept as the whole corpus judged them and ordered by how
    /// often that meeting heard each.
    /// </summary>
    public static IReadOnlyList<SuspectWord> HeardIn(
        IEnumerable<SuspectWord> suspects, IEnumerable<HeardWord> thisMeeting)
    {
        ArgumentNullException.ThrowIfNull(suspects);
        ArgumentNullException.ThrowIfNull(thisMeeting);

        var here = thisMeeting.ToDictionary(word => word.Word, word => word.Times, StringComparer.Ordinal);

        return
        [
            .. suspects
                .Where(suspect => here.ContainsKey(suspect.Word))
                .OrderByDescending(suspect => here[suspect.Word])
                .ThenBy(suspect => suspect.Word, StringComparer.Ordinal),
        ];
    }
}
