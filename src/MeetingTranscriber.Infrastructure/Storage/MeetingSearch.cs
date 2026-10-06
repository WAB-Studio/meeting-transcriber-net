using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// Where in a meeting a typed word was found, best first. A meeting is listed under the best band
/// it reaches and under no other.
/// </summary>
public enum SearchBand
{
    /// <summary>What somebody filed or said the meeting is: a node, a person, its title or its note.</summary>
    Filing = 1,

    /// <summary>What the summary says: its text, a decision, an action or an open question.</summary>
    Summary = 2,

    /// <summary>What was said in it: a turn, or a voice that was recognised.</summary>
    Transcript = 3,

    /// <summary>
    /// A word in a transcript that is like the one typed and not the one typed, so a word the
    /// provider heard wrongly still leads to its meeting.
    /// </summary>
    Similar = 4,
}

/// <summary>A meeting search found, and the best band it was found in.</summary>
public sealed record MeetingFound(Meeting Meeting, SearchBand Band);

/// <summary>
/// What somebody typing into the meetings list is asking: which meetings hold these words, and
/// where. It answers meetings and never places in them, which is what keeps each keystroke cheap.
/// </summary>
/// <remarks>
/// <para>
/// What is typed is words and never FTS5 syntax. <see cref="CorpusSearch"/> takes the syntax,
/// because its caller is an agent that knows it; a person typing <c>C++</c> or a stray quote would
/// be refused by it. Here every word becomes a quoted term, all of them required, and only the last
/// is a prefix, and only once it has <see cref="ShortestPrefix"/> letters, so a half-typed word
/// finds what it is becoming without every two-letter start matching half the corpus.
/// </para>
/// <para>
/// The bands are by which of the nine sources answered. Inside a band the newest meeting comes
/// first, because BM25 does not compare across indexes and a band mixes them.
/// </para>
/// <para>
/// The fourth band reads the index's own list of words, <c>utterances_fts_terms</c>, and never the
/// turns: what a near spelling costs is a pass over the vocabulary, which grows with the words
/// the corpus holds and not with the hours it holds them for.
/// </para>
/// </remarks>
public static class MeetingSearch
{
    /// <summary>How many meetings come back, at most.</summary>
    public const int Limit = 50;

    /// <summary>How many near words the fourth band looks for, closest first.</summary>
    public const int SimilarWordsAsked = 5;

    /// <summary>How many letters the last word needs before it matches as a start of one.</summary>
    public const int ShortestPrefix = 3;

    private static readonly string Active = WireNames<LifecycleState>.Of(LifecycleState.Active);

    /// <summary>The meetings whose words match what was typed, in band order and then newest first.</summary>
    public static IReadOnlyList<MeetingFound> Find(CorpusDbContext context, string typed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(typed);

        var asked = AsTyped(typed);
        if (asked is null)
        {
            return [];
        }

        var running = asked;
        var wrongs = CorpusSearch.WrongSpellingsOf(context, typed);
        if (wrongs.Count > 0)
        {
            running = $"({asked}) OR {string.Join(" OR ", wrongs.Select(CorpusSearch.AsAPhrase))}";
        }

        var best = new Dictionary<Guid, SearchBand>();

        var answers = RawSql.Rows(
            context,
            $"SELECT DISTINCT meeting_id, source FROM ({CorpusSearch.Branches(false)});",
            reader => (Meeting: Guid.Parse(reader.GetString(0)), Source: WireNames<SearchSource>.Parse(reader.GetString(1))),
            command =>
            {
                RawSql.Bind(command, "@query", running);
                RawSql.Bind(command, "@active", Active);
            });

        foreach (var (meeting, source) in answers)
        {
            var band = BandOf(source);
            if (!best.TryGetValue(meeting, out var held) || band < held)
            {
                best[meeting] = band;
            }
        }

        foreach (var meeting in SimilarOnly(context, typed))
        {
            best.TryAdd(meeting, SearchBand.Similar);
        }

        if (best.Count == 0)
        {
            return [];
        }

        var ids = best.Keys.ToList();
        var meetings = context.Meetings.AsNoTracking().Where(meeting => ids.Contains(meeting.Id)).ToList();

        return
        [
            .. meetings
                .Select(meeting => new MeetingFound(meeting, best[meeting.Id]))
                .OrderBy(found => found.Band)
                .ThenByDescending(found => found.Meeting.StartedAt)
                .ThenBy(found => found.Meeting.Id)
                .Take(Limit),
        ];
    }

    /// <summary>
    /// What is typed as the FTS5 query that runs, or nothing when it holds no word at all.
    /// Public only so a test can name it: this repository carries no <c>InternalsVisibleTo</c>.
    /// </summary>
    public static string? AsTyped(string typed)
    {
        var words = Spellings.WordsOf(typed);
        if (words.Count == 0)
        {
            return null;
        }

        var terms = words.Select(CorpusSearch.AsAPhrase).ToList();
        if (words[^1].Length >= ShortestPrefix)
        {
            terms[^1] += "*";
        }

        return string.Join(' ', terms);
    }

    /// <summary>
    /// Which band an answering source puts a meeting in. Public only so a test can name it.
    /// </summary>
    public static SearchBand BandOf(SearchSource source) => source switch
    {
        SearchSource.Node or SearchSource.Person or SearchSource.Meeting => SearchBand.Filing,
        SearchSource.Summary or SearchSource.Decision or SearchSource.Action or SearchSource.Question => SearchBand.Summary,
        SearchSource.Turn or SearchSource.Voice => SearchBand.Transcript,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "No band holds this source."),
    };

    /// <summary>
    /// The meetings whose transcripts hold a word like the one typed, when exactly one long enough
    /// word was typed. The words close to it come off the index's own vocabulary, and only then is
    /// a turn looked up, by the index, for those few.
    /// </summary>
    private static IReadOnlyList<Guid> SimilarOnly(CorpusDbContext context, string typed)
    {
        var words = Spellings.WordsOf(typed);
        if (words.Count != 1)
        {
            return [];
        }

        var folded = Spellings.Folded(words[0]);
        if (folded.Length < MisspelledWords.ShortestWord)
        {
            return [];
        }

        // Resemblance is one minus the distance over the longer length, so a term can only reach
        // the bar when its length is within the bar's reach of this one.
        var shortest = (int)Math.Floor(folded.Length * MisspelledWords.CloseEnough);
        var longest = (int)Math.Ceiling(folded.Length / MisspelledWords.CloseEnough);

        var near = RawSql.Rows(
                context,
                "SELECT term FROM utterances_fts_terms WHERE length(term) BETWEEN @shortest AND @longest;",
                reader => reader.GetString(0),
                command =>
                {
                    RawSql.Bind(command, "@shortest", shortest);
                    RawSql.Bind(command, "@longest", longest);
                })
            .Where(term => term != folded)
            .Select(term => (Term: term, Resemblance: Spellings.Resemblance(folded, term)))
            .Where(candidate => candidate.Resemblance >= MisspelledWords.CloseEnough)
            .OrderByDescending(candidate => candidate.Resemblance)
            .ThenBy(candidate => candidate.Term, StringComparer.Ordinal)
            .Take(SimilarWordsAsked)
            .Select(candidate => CorpusSearch.AsAPhrase(candidate.Term))
            .ToList();

        if (near.Count == 0)
        {
            return [];
        }

        return RawSql.Rows(
            context,
            """
            SELECT DISTINCT turn.meeting_id
            FROM utterances_fts
            JOIN utterances AS turn ON turn.rowid = utterances_fts.rowid
            JOIN meetings AS meeting ON meeting.id = turn.meeting_id
            WHERE utterances_fts MATCH @query AND meeting.lifecycle_state = @active;
            """,
            reader => Guid.Parse(reader.GetString(0)),
            command =>
            {
                RawSql.Bind(command, "@query", string.Join(" OR ", near));
                RawSql.Bind(command, "@active", Active);
            });
    }
}
