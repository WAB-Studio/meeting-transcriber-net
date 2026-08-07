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
/// hit is enough to quote from without a second lookup. Null for a summary.
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
/// Asking the corpus a question. Both indexes answer at once, ranked together and bounded, and
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

    /// <summary>
    /// Both indexes, best first. A meeting on its way out does not answer: it is being deleted, and
    /// offering it is offering something that will not be there when somebody opens it.
    /// </summary>
    /// <remarks>
    /// The two ranks come from different indexes and are not the same number, but both are BM25 and
    /// both are smaller when the match is better, so ordering by them together puts good hits above
    /// weak ones. What it does not promise is that a turn and a summary of equal quality tie.
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
                Bind(command, "@query", query);
                Bind(command, "@limit", limit);
                Bind(command, "@active", Active);
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

    private static void Bind(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Both halves and the ordering. Each half joins its index back to the table it indexes and
    /// then to the meeting, so what comes back is the row as it is now rather than whatever the
    /// index happened to keep — the index holds no copy of the text, and this is where that shows.
    /// </summary>
    private static string Sql { get; } = $"""
        SELECT * FROM (
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
            WHERE summaries_fts MATCH @query AND meeting.lifecycle_state = @active
        )
        ORDER BY score, started_at DESC, source, ordinal
        LIMIT @limit;
        """;
}
