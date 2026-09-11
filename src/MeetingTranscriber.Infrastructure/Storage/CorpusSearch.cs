using System.Data.Common;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>Which index answered, and therefore what the hit points at.</summary>
/// <remarks>
/// The member names are what the query selects and what the reader parses back, both through
/// <see cref="WireNames{TEnum}"/>. Renaming one changes the two together, which is the only reason
/// the string in the SQL and the enum in the hit cannot come to mean different things.
/// </remarks>
public enum SearchSource
{
    /// <summary>A turn somebody said. It carries where on the timeline it was said.</summary>
    Turn = 1,

    /// <summary>A meeting's summary, which is about the whole of it and has no offset.</summary>
    Summary = 2,

    /// <summary>
    /// The meeting's own words — its title, or the note somebody wrote so that a person who was not
    /// there can read it. Nothing infers either, which is why they are one source and not two: what
    /// a hit points at is the meeting, and the snippet says which of the two matched.
    /// </summary>
    Meeting = 3,

    /// <summary>
    /// Something the meeting is filed under, or something above it in the tree. A meeting filed
    /// under a ticket answers to the ticket, to the initiative over it and to the organization over
    /// that, which is the same reach a listing by node has.
    /// </summary>
    Node = 4,

    /// <summary>
    /// Somebody the meeting names, as having attended it or as what it was about. Not somebody a
    /// voice in it was settled onto — that is <see cref="Voice"/>.
    /// </summary>
    Person = 5,

    /// <summary>Something the meeting settled. It anchors on the turn it was said in.</summary>
    Decision = 6,

    /// <summary>Something the meeting left for somebody to do, anchored the same way.</summary>
    Action = 7,

    /// <summary>Something the meeting raised and did not settle, anchored the same way.</summary>
    Question = 8,

    /// <summary>
    /// Somebody a voice in this meeting was settled onto, whether a person said so or the recording
    /// gave it for free. It is not the meeting naming them: a label is a place in the audio, and
    /// what is on the other end of it is a person the corpus knows spoke here. So a meeting can
    /// answer to a name it never named — which is the ordinary case, because nothing puts somebody
    /// on a meeting for having been recognised in it.
    /// </summary>
    Voice = 9,
}

/// <summary>
/// One thing search found, and deliberately not the thing itself. It carries enough to decide
/// whether to open the meeting and nothing more, because the point of search is to keep the
/// transcript closed until somebody asks for it.
/// </summary>
/// <param name="Snippet">
/// The excerpt the index produced, elided at both ends. Plain text with nothing marking the match:
/// a marker is a convention, and one chosen here is one every consumer has to strip before applying
/// its own.
/// </param>
/// <param name="Ordinal">
/// The turn's position in the meeting, which with the meeting is what a citation anchors on — so a
/// hit is enough to quote from without a second lookup. A decision, an action and an open question
/// carry the turn they cited. Null for everything that is about the whole meeting rather than a
/// moment in it: a summary, the meeting's own words, a node it is filed under, a person on it and a
/// voice heard in it.
/// </param>
public sealed record SearchHit(
    Guid MeetingId,
    UtcTimestamp StartedAt,
    string? Title,
    SearchSource Source,
    string Snippet,
    int? Ordinal,
    Duration? Start,
    Duration? End);

/// <summary>A query the index refused to parse, with what it said.</summary>
public sealed class CorpusSearchException(string query, Exception cause)
    : Exception($"'{query}' is not a search this corpus can run: {cause.Message}", cause)
{
    public string Query { get; } = query;
}

/// <summary>
/// Asking the corpus a question. Every index answers at once, ranked together and bounded, and
/// what comes back is small on purpose.
/// </summary>
/// <remarks>
/// <para>
/// The query is FTS5's own syntax rather than a bag of words, so <c>presupuesto AND cliente</c>,
/// <c>"exactamente esto"</c> and <c>presu*</c> all mean what somebody typing them expects. That is
/// worth the one cost it carries: a query FTS5 cannot parse is an error, and it arrives as
/// <see cref="CorpusSearchException"/> naming the query rather than as a SQLite error naming a
/// column nobody wrote. It is bound as a parameter, so what the syntax cannot do is reach SQL.
/// </para>
/// <para>
/// One row per hit, not per meeting. A meeting where the word is said forty times is forty answers
/// to "where was this said", which is the question being asked; collapsing them would make the
/// limit mean something different for a long meeting than for a short one.
/// </para>
/// </remarks>
public static class CorpusSearch
{
    /// <summary>
    /// How many hits come back when the caller does not say. Small because the caller after this
    /// one is an agent with a context window, and because the answer to a good query is at the top.
    /// </summary>
    public const int DefaultLimit = 20;

    /// <summary>
    /// The words either side of the match that come back with it. Enough to tell whether the hit is
    /// the one being looked for, and short enough that twenty of them are still a small answer.
    /// </summary>
    private const int SnippetTokens = 12;

    private static readonly string Active = WireNames<LifecycleState>.Of(LifecycleState.Active);

    private static readonly string TurnSource = WireNames<SearchSource>.Of(SearchSource.Turn);

    private static readonly string SummarySource = WireNames<SearchSource>.Of(SearchSource.Summary);

    private static readonly string MeetingSource = WireNames<SearchSource>.Of(SearchSource.Meeting);

    private static readonly string NodeSource = WireNames<SearchSource>.Of(SearchSource.Node);

    private static readonly string PersonSource = WireNames<SearchSource>.Of(SearchSource.Person);

    private static readonly string DecisionSource = WireNames<SearchSource>.Of(SearchSource.Decision);

    private static readonly string ActionSource = WireNames<SearchSource>.Of(SearchSource.Action);

    private static readonly string QuestionSource = WireNames<SearchSource>.Of(SearchSource.Question);

    private static readonly string VoiceSource = WireNames<SearchSource>.Of(SearchSource.Voice);

    /// <summary>
    /// Every branch, best first. A meeting on its way out does not answer: it is being deleted, and
    /// offering it is offering something that will not be there when somebody opens it.
    /// </summary>
    /// <remarks>
    /// The ranks come from different indexes and are not the same number — BM25 weighs a term
    /// against the index it was found in — so they rank inside a source and not across sources.
    /// What makes them one answer is taking the best of each source in turn, which is why no source
    /// can own the page and why every source with something to say is on it. What it does not
    /// promise is that a turn and a summary of equal quality tie. Nine branches over eight indexes:
    /// the person branch and the voice branch both ask <c>people_fts</c> and answer about different
    /// ways a person reaches a meeting, so partitioning by source ranks each on its own and the page
    /// takes the best of both.
    /// </remarks>
    public static IReadOnlyList<SearchHit> Find(CorpusDbContext context, string query, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        try
        {
            return RawSql.Rows(context, Sql, ReadHit, command =>
            {
                RawSql.Bind(command, "@query", query);
                RawSql.Bind(command, "@limit", limit);
                RawSql.Bind(command, "@active", Active);
            });
        }
        catch (SqliteException refused)
        {
            throw new CorpusSearchException(query, refused);
        }
    }

    /// <summary>
    /// One row, in the order the query selects. The source is parsed rather than compared against
    /// one name, so a row this cannot place stops the search instead of arriving as the other kind
    /// of hit — which, with a summary's nulls where a turn's anchor goes, is a citation quietly
    /// losing the place it points at.
    /// </summary>
    private static SearchHit ReadHit(DbDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        UtcTimestamp.Parse(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        WireNames<SearchSource>.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : Duration.FromMilliseconds(reader.GetInt64(6)),
        reader.IsDBNull(7) ? null : Duration.FromMilliseconds(reader.GetInt64(7)));

    /// <summary>
    /// Which extraction a meeting's summary, decisions, actions and open questions come from: the
    /// last one a person accepted, ties broken out to the run's own id, and a run nobody accepted
    /// not read at all.
    /// </summary>
    /// <param name="meeting">
    /// The SQL naming the meeting: <c>meeting.id</c> where this correlates with a query,
    /// <c>@meeting</c> where one meeting is being asked about. It is SQL and never a value — the
    /// only two callers are in this assembly and both hand it a column or a parameter name.
    /// </param>
    /// <remarks>
    /// <para>
    /// Without it a second extraction puts the same decision in front of somebody twice, said
    /// slightly differently, with nothing on either to say which is current — which is the exact
    /// failure <see cref="Domain.Knowledge.WhatTheAiLeft"/> is shaped to avoid on a screen, arriving
    /// through search instead. And an unaccepted run answering at all would put sentences nobody has
    /// vouched for under the meeting's own name.
    /// </para>
    /// <para>
    /// One spelling, asked by both readers. <c>MeetingReading.TheRunThatCounts</c> asks this same
    /// method about one meeting rather than restating the ordering in LINQ, so the tie a search
    /// breaks and the tie the meeting's screen breaks cannot come apart — there is no other way to
    /// break it. The two places both could have asked instead are still refused: a view has to be
    /// mapped keyless to be readable, and a column on <c>meetings</c> naming the run is a second
    /// copy of a fact the runs already hold, wrong from the moment somebody accepts a run and
    /// something forgets to update it. What
    /// <c>CorpusSearchTests.Search_and_the_meeting_screen_break_a_tie_between_two_accepted_runs_the_same_way</c>
    /// holds is no longer that two spellings agree but that the one spelling reaches both readers,
    /// which is still worth running: they reach it by two different paths to the database.
    /// </para>
    /// <para>
    /// Costed rather than indexed. This is a correlated subquery on four of the nine branches and
    /// <c>extraction_runs</c> carries an index on <c>(meeting_id, created_at)</c>, which the
    /// <c>WHERE</c> already seeks — what an index on <c>(meeting_id, accepted_at)</c> would add is
    /// the ordering and the <c>accepted_at IS NOT NULL</c> filter over the handful of rows one
    /// meeting has. Measured over 300 meetings with two runs each, one of them accepted, each run
    /// carrying a summary, a decision, an action and an open question, over 20 searches hitting all
    /// four branches after an <c>ANALYZE</c>: 6.4, 7.4 and 6.4 ms a search as it stands, against
    /// 6.4, 6.3 and 7.1 ms with that index created by hand on the same corpus. The two are one
    /// noise band, so it was refused on the measurement rather than never taken — and the answer
    /// does not move with the corpus, because what the subquery orders is one meeting's runs and ten
    /// times the meetings gives it no more of them. What would move it is a meeting with hundreds,
    /// which is a person accepting hundreds of extractions of one conversation.
    /// </para>
    /// <para>
    /// A method and not a constant with a hole in it. The hole would have to be filled by
    /// <c>string.Format</c>, which reads every brace in the SQL as its own — so a future
    /// <c>json_extract</c> path or <c>printf</c> would throw while a static field was initialising,
    /// and the first search would fail with something that names neither the query nor this file.
    /// A method is also what keeps the caller below out of the one trap a field here has: a static
    /// field built from this and read by <see cref="Sql"/>'s initialiser is null unless it is
    /// declared textually above it, which compiles clean and turns every search in the application
    /// into a syntax error.
    /// </para>
    /// </remarks>
    internal static string TheRunThatCounts(string meeting) => $"""
        (SELECT id FROM extraction_runs
          WHERE meeting_id = {meeting} AND accepted_at IS NOT NULL
          ORDER BY accepted_at DESC, created_at DESC, id DESC
          LIMIT 1)
        """;

    /// <summary>
    /// Every branch and the ordering. Each joins its index back to the table it indexes and then to
    /// the meeting, so what comes back is the row as it is now rather than whatever the index
    /// happened to keep — the index holds no copy of the text, and this is where that shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node branch reaches up the tree as well as down: a meeting filed under a ticket answers
    /// to the ticket, the initiative over it and the organization over that. Two joins and no
    /// recursion, which is what capping the tree at three levels bought, and it is the same reach
    /// <c>ClassificationStoriesTests.Searching_an_organization_finds_the_meetings_under_it</c>
    /// already promises for a listing. <c>DISTINCT</c> is what stops a meeting filed under two
    /// initiatives of one organization coming back twice for that organization: every column of the
    /// duplicated rows is identical, the score included, so there is nothing to choose between them.
    /// Two different nodes that both match are two index rows with two scores, and it leaves both.
    /// </para>
    /// <para>
    /// The four branches an extraction produced ask <see cref="TheRunThatCounts"/>, correlated on
    /// <c>meeting.id</c>. The other five do not and must not: a turn, a title, a node, a person and
    /// a voice are the meeting's, not a model's.
    /// </para>
    /// <para>
    /// The voice branch and the person branch are two ways to one meeting and are deliberately two
    /// sources rather than one. <c>meeting_people</c> is somebody a person put on this meeting;
    /// <c>speaker_assignments</c> is somebody the corpus knows spoke in it, and nothing puts one on
    /// the other. A hit says which it was, so an answer arriving because a voice was recognised is
    /// never read as a meeting that named them — and somebody both named on a meeting and heard in
    /// it answers twice for that meeting, once as each, which is one row per way of reaching it and
    /// the same rule the whole of this query is built on. Neither branch is filtered by what settled
    /// the label: <c>SpeakerAssignmentSource.Channel</c> is the recording giving the user's own
    /// microphone away for free, and leaving it out would make the one person every meeting has the
    /// one person nobody can search for.
    /// </para>
    /// <para>
    /// The <c>EXISTS</c> on the voice branch is what makes its sentence true. An assignment hangs
    /// off a label and nothing deletes one whose label stopped naming any turn, so a meeting
    /// transcribed again into a different set of labels keeps the old rows — and this is the first
    /// reader that does not reach the name through the label. <c>MeetingRenderer.Header</c> builds a
    /// label-to-name map, so a stale row falls out of the transcript silently; without the
    /// <c>EXISTS</c> it would fall <em>into</em> search instead, and somebody would be told the
    /// corpus knows this person spoke in a meeting whose transcript never names them, with no
    /// anchor to check it against. It costs a seek into one meeting's turns per meeting the branch
    /// was already going to answer with, which is the price of the branch meaning what it says.
    /// </para>
    /// <para>
    /// A hit on a decision, an action or an open question carries the turn it cited — the position
    /// and both offsets — so it is quotable without a second lookup, exactly as a turn hit is. A
    /// summary carries none, because it is about the whole meeting; a node, a person, a voice and
    /// the meeting's own words carry none for the same reason. Which is also why <c>ordinal</c> is last
    /// in the ordering and does nothing on half the branches: it breaks ties inside one meeting's
    /// turns, and where there is no position there is nothing left to break them with.
    /// </para>
    /// <para>
    /// <c>place</c> is what makes nine branches over eight indexes one answer, and with two it would
    /// not have been worth writing. <c>bm25</c> weighs a term against the index it was found in, so
    /// the number is meaningful inside a source and arbitrary across them — and the node, person and
    /// voice branches make that worse, because one index row fans out to one row per meeting and
    /// every one of those carries the same score. The voice branch is the sharpest of the three: one
    /// <c>people_fts</c> row for a name fans out to every meeting that person was ever recognised
    /// in, at one identical score, and somebody recognised in a hundred meetings is the ordinary
    /// case rather than a corner of one. Ordered flat, an initiative with forty meetings under it answers a
    /// search for its own name with forty identical rows and evicts every turn where somebody
    /// actually said the word: measured, twenty node rows and no turns at all. Ranking within each
    /// source and then taking the best of each in turn is what stops one source owning the page,
    /// and it costs nothing when only one source answers — there the places run 1, 2, 3… and the
    /// order is exactly the ranking. What it still does not promise is that a turn and a summary of
    /// equal quality tie; that would need a score the indexes do not produce.
    /// </para>
    /// </remarks>
    private static string Sql { get; } = $"""
        SELECT * FROM (
        SELECT *, ROW_NUMBER() OVER (
                      PARTITION BY source ORDER BY score, started_at DESC, ordinal) AS place
        FROM (
            SELECT meeting.id AS meeting_id,
                   meeting.started_at AS started_at,
                   meeting.title AS title,
                   '{TurnSource}' AS source,
                   snippet(utterances_fts, 0, '', '', '…', {SnippetTokens}) AS snippet,
                   turn.ordinal AS ordinal,
                   turn.start_ms AS start_ms,
                   turn.end_ms AS end_ms,
                   bm25(utterances_fts) AS score
            FROM utterances_fts
            JOIN utterances AS turn ON turn.rowid = utterances_fts.rowid
            JOIN meetings AS meeting ON meeting.id = turn.meeting_id
            WHERE utterances_fts MATCH @query AND meeting.lifecycle_state = @active

            UNION ALL

            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{SummarySource}',
                   snippet(summaries_fts, -1, '', '', '…', {SnippetTokens}),
                   NULL,
                   NULL,
                   NULL,
                   bm25(summaries_fts)
            FROM summaries_fts
            JOIN summaries AS summary ON summary.rowid = summaries_fts.rowid
            JOIN meetings AS meeting ON meeting.id = summary.meeting_id
            WHERE summaries_fts MATCH @query
              AND meeting.lifecycle_state = @active
              AND summary.extraction_run_id = {TheRunThatCounts("meeting.id")}

            UNION ALL

            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{MeetingSource}',
                   snippet(meetings_fts, -1, '', '', '…', {SnippetTokens}),
                   NULL,
                   NULL,
                   NULL,
                   bm25(meetings_fts)
            FROM meetings_fts
            JOIN meetings AS meeting ON meeting.rowid = meetings_fts.rowid
            WHERE meetings_fts MATCH @query AND meeting.lifecycle_state = @active

            UNION ALL

            SELECT DISTINCT
                   meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{NodeSource}',
                   snippet(nodes_fts, 0, '', '', '…', {SnippetTokens}),
                   NULL,
                   NULL,
                   NULL,
                   bm25(nodes_fts)
            FROM nodes_fts
            JOIN nodes AS found ON found.rowid = nodes_fts.rowid
            JOIN nodes AS under ON under.id = found.id
                                OR under.parent_id = found.id
                                OR under.parent_id IN (SELECT id FROM nodes WHERE parent_id = found.id)
            JOIN meeting_nodes AS filed ON filed.node_id = under.id
            JOIN meetings AS meeting ON meeting.id = filed.meeting_id
            WHERE nodes_fts MATCH @query AND meeting.lifecycle_state = @active

            UNION ALL

            SELECT DISTINCT
                   meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{PersonSource}',
                   snippet(people_fts, 0, '', '', '…', {SnippetTokens}),
                   NULL,
                   NULL,
                   NULL,
                   bm25(people_fts)
            FROM people_fts
            JOIN people AS somebody ON somebody.rowid = people_fts.rowid
            JOIN meeting_people AS named ON named.person_id = somebody.id
            JOIN meetings AS meeting ON meeting.id = named.meeting_id
            WHERE people_fts MATCH @query AND meeting.lifecycle_state = @active

            UNION ALL

            SELECT DISTINCT
                   meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{VoiceSource}',
                   snippet(people_fts, 0, '', '', '…', {SnippetTokens}),
                   NULL,
                   NULL,
                   NULL,
                   bm25(people_fts)
            FROM people_fts
            JOIN people AS somebody ON somebody.rowid = people_fts.rowid
            JOIN speaker_assignments AS voice ON voice.person_id = somebody.id
            JOIN meetings AS meeting ON meeting.id = voice.meeting_id
            WHERE people_fts MATCH @query
              AND meeting.lifecycle_state = @active
              AND EXISTS (SELECT 1 FROM utterances AS said
                           WHERE said.meeting_id = voice.meeting_id
                             AND said.speaker_label = voice.speaker_label)

            UNION ALL

            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{DecisionSource}',
                   snippet(decisions_fts, 0, '', '', '…', {SnippetTokens}),
                   settled.utterance_ordinal,
                   settled.start_ms,
                   settled.end_ms,
                   bm25(decisions_fts)
            FROM decisions_fts
            JOIN decisions AS settled ON settled.rowid = decisions_fts.rowid
            JOIN meetings AS meeting ON meeting.id = settled.meeting_id
            WHERE decisions_fts MATCH @query
              AND meeting.lifecycle_state = @active
              AND settled.extraction_run_id = {TheRunThatCounts("meeting.id")}

            UNION ALL

            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{ActionSource}',
                   snippet(action_items_fts, 0, '', '', '…', {SnippetTokens}),
                   todo.utterance_ordinal,
                   todo.start_ms,
                   todo.end_ms,
                   bm25(action_items_fts)
            FROM action_items_fts
            JOIN action_items AS todo ON todo.rowid = action_items_fts.rowid
            JOIN meetings AS meeting ON meeting.id = todo.meeting_id
            WHERE action_items_fts MATCH @query
              AND meeting.lifecycle_state = @active
              AND todo.extraction_run_id = {TheRunThatCounts("meeting.id")}

            UNION ALL

            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   '{QuestionSource}',
                   snippet(open_questions_fts, 0, '', '', '…', {SnippetTokens}),
                   asked.utterance_ordinal,
                   asked.start_ms,
                   asked.end_ms,
                   bm25(open_questions_fts)
            FROM open_questions_fts
            JOIN open_questions AS asked ON asked.rowid = open_questions_fts.rowid
            JOIN meetings AS meeting ON meeting.id = asked.meeting_id
            WHERE open_questions_fts MATCH @query
              AND meeting.lifecycle_state = @active
              AND asked.extraction_run_id = {TheRunThatCounts("meeting.id")}
        )
        )
        ORDER BY place, score, started_at DESC, source, ordinal
        LIMIT @limit;
        """;
}
