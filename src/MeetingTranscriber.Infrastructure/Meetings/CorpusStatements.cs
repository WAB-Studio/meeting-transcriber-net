using System.Data.Common;
using System.Text;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>
/// One thing an extraction left, with the meeting it was left in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LeftThing"/> field for field, plus two a corpus-wide reader needs and a screen does
/// not. The meeting, because which meeting it is, is the screen — and here it is the first thing a
/// reader needs. And the artifact the quote came out of: a screen is looking at the meeting's own
/// current transcript, where a listing hands a sentence to somebody who will check it against a
/// source, and the corpus keeps every response it ever paid for.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the three sections it belongs to.</param>
/// <param name="Says">The sentence itself, as the extraction proposed it.</param>
/// <param name="TurnOrdinal">The position of the turn it was said in.</param>
/// <param name="At">Where in the meeting it was said, from the meeting's start.</param>
/// <param name="Quoted">What was actually said there, as the citation recorded it.</param>
/// <param name="SpeakerLabel">The label of whoever said it, never a person's name.</param>
/// <param name="SourceSha256">
/// The artifact the quote was read out of, off the citation's own column.
/// </param>
public sealed record Statement(
    Guid MeetingId,
    UtcTimestamp StartedAt,
    string? Title,
    LeftKind Kind,
    string Says,
    int TurnOrdinal,
    Duration At,
    string Quoted,
    string SpeakerLabel,
    string SourceSha256);

/// <summary>
/// Everything one section left across the whole corpus: every decision, every action, or every
/// open question, out of the one extraction of each meeting that counts.
/// </summary>
/// <remarks>
/// <para>
/// The corpus-wide plural of what <c>MeetingReading.Left</c> reads for one meeting, and it is here
/// rather than beside its caller for one reason: it has to narrow through
/// <see cref="CorpusSearch.TheRunThatCounts"/>, which is <see langword="internal"/> in this
/// assembly and is the only thing that says which extraction answers. Without it a second
/// extraction puts the same decision in front of somebody twice, said slightly differently, with
/// nothing on either to say which is current — which is the failure that method's own remarks
/// describe, arriving through a listing instead of through search.
/// </para>
/// <para>
/// A meeting on its way out says nothing, for the reason every search branch gives: it is being
/// deleted, and offering what it decided is offering something that will not be there when
/// somebody opens it.
/// </para>
/// <para>
/// It reads through <see cref="RawSql"/> and not through EF, for the ordinary reason a raw read
/// does: <see cref="CorpusSearch.TheRunThatCounts"/> is a SQL string and LINQ cannot be handed
/// one. Restating that ordering in LINQ is the one thing this must not do — a second spelling is
/// exactly what that method exists to have removed.
/// </para>
/// </remarks>
public static class CorpusStatements
{
    private static readonly string Active = WireNames<LifecycleState>.Of(LifecycleState.Active);

    /// <summary>
    /// One section of the corpus, newest meeting first, bounded.
    /// </summary>
    /// <param name="context">The corpus to read.</param>
    /// <param name="kind">Which of the three sections to answer about.</param>
    /// <param name="from">The earliest meeting to answer about, or no bound at all.</param>
    /// <param name="to">The first meeting too late to answer about, or no bound at all.</param>
    /// <param name="limit">How many rows at most.</param>
    /// <remarks>
    /// <para>
    /// The window is on the meeting's own start and not on when a model wrote the sentence, because
    /// what somebody asking for <em>the decisions of August</em> means is the meetings of August. A
    /// meeting re-summarised in September is still August's.
    /// </para>
    /// <para>
    /// Closed at the bottom and open at the top, so two calls walking the corpus back to back do
    /// not each answer with the meeting on the boundary. The comparison is textual, and that is
    /// safe rather than lucky: every instant in the corpus is stored as
    /// <see cref="UtcTimestamp.ToStorage"/> writes it — fixed width, UTC, no offset — so byte order
    /// is chronological order. What is bound is that storage spelling and never a
    /// <see cref="DateTime"/>, which the provider would write its own way and which would then
    /// match nothing at all without failing — the one way <see cref="RawSql"/> says a raw read here
    /// can be quietly wrong. It goes through the overload taking an <see cref="UtcTimestamp"/>,
    /// which is where the conversion belongs: with it at the call site the rule would be a thing
    /// every future caller has to remember, and <c>.ToString("o")</c> compiles and matches nothing.
    /// </para>
    /// <para>
    /// A bound nobody gave is left out of the SQL rather than bound as null, so every parameter
    /// here is a value the column really holds.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Statement> Of(
        CorpusDbContext context, LeftKind kind, UtcTimestamp? from, UtcTimestamp? to, int limit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var window = new StringBuilder();

        if (from is not null)
        {
            window.Append(" AND meeting.started_at >= @from");
        }

        if (to is not null)
        {
            window.Append(" AND meeting.started_at < @to");
        }

        return RawSql.Rows(context, Sql(kind, window.ToString()), Read(kind), command =>
        {
            RawSql.Bind(command, "@active", Active);
            RawSql.Bind(command, "@limit", limit);

            if (from is { } earliest)
            {
                RawSql.Bind(command, "@from", earliest);
            }

            if (to is { } latest)
            {
                RawSql.Bind(command, "@to", latest);
            }
        });
    }

    /// <summary>
    /// Everything the meetings of one node were left with, oldest first — its own meetings and the
    /// meetings of everything hanging off it.
    /// </summary>
    /// <param name="context">The corpus to read.</param>
    /// <param name="node">The node to answer about.</param>
    /// <param name="limit">How many rows at most.</param>
    /// <exception cref="ClassificationException">This corpus holds no such node.</exception>
    /// <remarks>
    /// <para>
    /// Oldest first, and the other listing newest first: a node's story is read forward, the way a
    /// history is, where the corpus-wide listings answer <em>what did the meetings of August
    /// settle</em> and put the newest meeting on top. Two questions, two orders, and the SQL says
    /// so, so nobody decides it again at a call site.
    /// </para>
    /// <para>
    /// The three sections come back interleaved. A node's history is read as one story, and three
    /// lists a reader would have to interleave again is the shape <c>WhatTheAiLeft</c>'s own remarks
    /// refuse one meeting at a time. The tie-break is
    /// <see cref="WhatTheAiLeft.InTheOrderTheyWereSaid"/>'s and is the same four keys in the same
    /// order — where it was said, then the section, then the turn's position, then the sentence —
    /// with <c>said.ordinal</c> last so nothing ties at all.
    /// </para>
    /// <para>
    /// The tie on <c>says</c> is broken here under SQLite's <c>BINARY</c> and on the meeting's own
    /// screen under <see cref="StringComparer.Ordinal"/>, and those are two rules rather than one.
    /// They agree for every string in the Basic Multilingual Plane and can disagree the moment two
    /// sentences first differ at a supplementary-plane character — an emoji, most of the historic
    /// scripts — because <c>BINARY</c> compares UTF-8 bytes and <see cref="StringComparer.Ordinal"/>
    /// compares UTF-16 code units. Two things said in the same recorded millisecond, in the same
    /// section, in the same turn, whose sentences differ only there, would come back in one order in
    /// a node's history and in the other on the meeting. That is accepted and named rather than
    /// closed: closing it means either a collation of this application's own registered on every
    /// connection, or re-sorting in C#, which cannot work here at all — the <c>LIMIT</c> is applied
    /// in SQL, so a re-sort would reorder the page instead of choosing it. No fixture reaches it
    /// today, so what would find it first is somebody reading the same two sentences on two screens.
    /// </para>
    /// <para>
    /// An <c>EXISTS</c> and not a join: a meeting filed under two topics of one initiative joins
    /// twice, and search needs the join because it scores the node row and pays for the duplicate
    /// with <c>DISTINCT</c>. This read wants nothing off the node, so a semi-join says what it means
    /// and cannot double a row whatever the filing is.
    /// </para>
    /// <para>
    /// A node id the corpus does not hold is refused in words, not answered with nothing: an empty
    /// answer reads as a node nothing was ever said about, and an agent or a person who mistyped an
    /// id would believe it.
    /// </para>
    /// <para>
    /// The <c>Guid</c> bind goes through the provider's mapping for a <c>Guid</c>, and the existence
    /// check above goes through EF's — the caveat <c>MeetingReading</c>'s own remarks carry at
    /// <c>MeetingReading.cs:433-440</c>. The two agree only because no <c>Guid</c> in this model
    /// carries a conversion; give one a conversion and the symptom here is a node the check has just
    /// found answering with nothing at all.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Statement> Under(CorpusDbContext context, Guid node, int limit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (!context.Nodes.Any(row => row.Id == node))
        {
            throw new ClassificationException($"This corpus holds no node {node}.");
        }

        return RawSql.Rows(context, UnderSql(), ReadUnder, command =>
        {
            RawSql.Bind(command, "@active", Active);
            RawSql.Bind(command, "@limit", limit);
            RawSql.Bind(command, "@node", node);
        });
    }

    /// <summary>
    /// Which table a section is stored in, and the column its sentence is under.
    /// </summary>
    /// <remarks>
    /// The two travel together because one of the three differs: an open question is stored under
    /// <c>question</c> where a decision and an action are under <c>statement</c>. The arms are the
    /// three <see cref="LeftKind"/> is closed on, so the fourth is a defect and not an answer.
    /// </remarks>
    private static (string Table, string Says) StoredIn(LeftKind kind) => kind switch
    {
        LeftKind.Decision => ("decisions", "statement"),
        LeftKind.Action => ("action_items", "statement"),
        LeftKind.Question => ("open_questions", "question"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no fourth section."),
    };

    /// <summary>
    /// The read, with the table and the window folded in. Neither is a value — the table comes off
    /// a closed enum and the window is two fixed clauses — and the two instants and the limit are
    /// bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordering leaves no tie, and every key in it survives a rebuild. The meeting's own id
    /// follows its instant, because two meetings that started in the same millisecond would
    /// otherwise interleave their decisions; and within one meeting <c>ordinal</c> settles it
    /// outright, since <c>(extraction_run_id, ordinal)</c> is unique and one extraction is all that
    /// answers. A row's own id is deliberately <em>not</em> the last key: a rebuild mints fresh
    /// ones, and a listing that shuffles under somebody between two looks is what
    /// <see cref="WhatTheAiLeft.InTheOrderTheyWereSaid"/> was written against.
    /// </para>
    /// </remarks>
    private static string Sql(LeftKind kind, string window) =>
        $"""
        {OneSection(kind, extraColumns: "", extraWhere: window)}
        ORDER BY meeting.started_at DESC, meeting.id, said.utterance_ordinal, said.ordinal
        LIMIT @limit;
        """;

    /// <summary>
    /// The nine-column projection <see cref="Sql"/> and <see cref="UnderSql"/> both read, written
    /// once: the same <c>SELECT</c>, the same <c>FROM</c>/<c>JOIN</c> and the same two fixed
    /// <c>WHERE</c> clauses. What genuinely differs between the two callers — the order, the
    /// grouping and what else each rejects — is left for each to append after this returns.
    /// </summary>
    /// <param name="kind">Which of the three sections to project — picks the table and the column.</param>
    /// <param name="extraColumns">Columns appended after the nine, comma-led, or empty. Both
    /// callers are this file's own and the fragment is never anything a person or a provider
    /// sent — nothing here binds a parameter this way.</param>
    /// <param name="extraWhere">A clause appended after the two fixed ones, leading with its own
    /// <c>AND</c>, or empty. The same trust <paramref name="extraColumns"/> carries.</param>
    /// <remarks>
    /// Every column here is aliased, which costs <see cref="Of"/> nothing — <see cref="Read"/> takes
    /// its columns by ordinal — and is the only shape in which one string can serve
    /// <see cref="UnderSql"/> too: that caller wraps this in an outer <c>SELECT * FROM (…)</c> whose
    /// own <c>ORDER BY</c> can see nothing but these names. A shared projection written without them
    /// would compile, pass every <see cref="Of"/> fact and fail every <see cref="Under"/> query at
    /// SQLite with <c>no such column</c>.
    /// </remarks>
    private static string OneSection(LeftKind kind, string extraColumns, string extraWhere)
    {
        var (table, says) = StoredIn(kind);

        return $"""
            SELECT meeting.id                   AS meeting_id,
                   meeting.started_at           AS started_at,
                   meeting.title                AS title,
                   said.{says}                  AS says,
                   said.utterance_ordinal       AS utterance_ordinal,
                   said.start_ms                AS at_ms,
                   said.quoted_text             AS quoted,
                   said.speaker_label           AS speaker_label,
                   said.source_artifact_sha256  AS source_sha256{extraColumns}
            FROM {table} AS said
            JOIN meetings AS meeting ON meeting.id = said.meeting_id
            WHERE meeting.lifecycle_state = @active
              AND said.extraction_run_id = {CorpusSearch.TheRunThatCounts("meeting.id")}{extraWhere}
            """;
    }

    /// <summary>
    /// One row, in the order <see cref="OneSection"/> selects. The section is the one that was
    /// asked for rather than one read back off the row: a table is what makes a decision a decision
    /// here, and there is no column saying so.
    /// </summary>
    private static Func<DbDataReader, Statement> Read(LeftKind kind) => reader => Row(reader, kind);

    /// <summary>
    /// The ten-argument construction <see cref="Read"/> and <see cref="ReadUnder"/> both do, written
    /// once. <paramref name="kind"/> is the one field the two readers do not get the same way —
    /// <see cref="Read"/> closes over the kind it was asked for, <see cref="ReadUnder"/> reads it
    /// back off <c>kind_rank</c> — so it stays a parameter here rather than something this method
    /// works out for itself.
    /// </summary>
    private static Statement Row(DbDataReader reader, LeftKind kind) => new(
        Guid.Parse(reader.GetString(0)),
        UtcTimestamp.Parse(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        kind,
        reader.GetString(3),
        reader.GetInt32(4),
        Duration.FromMilliseconds(reader.GetInt64(5)),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8));

    /// <summary>
    /// One arm per <see cref="LeftKind"/>, unioned, ranked and ordered oldest first. One column
    /// carries the section and the order, and that is deliberate: there is no <c>kind</c> string
    /// column beside <c>kind_rank</c>, because the value is written by this method out of a closed
    /// enum in the same statement that reads it back. <c>LeftKind</c>'s numbers are the order a
    /// meeting is read in, which is what
    /// <see cref="WhatTheAiLeft.InTheOrderTheyWereSaid"/>'s <c>.ThenBy(thing => thing.Kind)</c>
    /// already sorts on, so the rank and the section are one fact and not two.
    /// </summary>
    private static string UnderSql()
    {
        // Enum.GetValues is documented to sort by the constants' own binary value, so this walks
        // Decision, Action, Question in that order without spelling it a second time — the order
        // StoredIn's switch already fixes and (int)kind below already writes into kind_rank.
        var exists = $"""

              AND EXISTS (SELECT 1
                            FROM nodes AS under
                            JOIN meeting_nodes AS filed ON filed.node_id = under.id
                           WHERE filed.meeting_id = meeting.id
                             AND ({CorpusSearch.Underneath("@node")}))
            """;

        var arms = Enum.GetValues<LeftKind>().Select(kind =>
            OneSection(
                kind,
                extraColumns: $", {(int)kind} AS kind_rank, said.ordinal AS ordinal",
                extraWhere: exists));

        return $"""
            SELECT * FROM ( {string.Join(" UNION ALL ", arms)} )
            ORDER BY started_at, meeting_id, at_ms, kind_rank, utterance_ordinal, says, ordinal
            LIMIT @limit;
            """;
    }

    /// <summary>
    /// One row of <see cref="Under"/>'s answer. The section is read back off <c>kind_rank</c> — a
    /// value this method itself wrote — and not off a stored name, so
    /// <c>WireNames&lt;LeftKind&gt;</c> is not reached: that type exists for a value the
    /// <em>database</em> holds, and this one never leaves this method.
    /// </summary>
    private static Statement ReadUnder(DbDataReader reader) => Row(reader, (LeftKind)reader.GetInt32(9));
}
