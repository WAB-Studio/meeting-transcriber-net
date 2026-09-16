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
    /// can be quietly wrong. It goes through the text overload because
    /// <see cref="UtcTimestamp.ToStorage"/> answers text; the overload taking an
    /// <see cref="UtcTimestamp"/> that <c>RawSql.Add</c>'s own remarks ask for would move that call
    /// off every call site and into the signature, and is worth having the day there is a second
    /// caller binding an instant. This is the first.
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
                RawSql.Bind(command, "@from", earliest.ToStorage());
            }

            if (to is { } latest)
            {
                RawSql.Bind(command, "@to", latest.ToStorage());
            }
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
    private static string Sql(LeftKind kind, string window)
    {
        var (table, says) = StoredIn(kind);

        return $"""
            SELECT meeting.id,
                   meeting.started_at,
                   meeting.title,
                   said.{says},
                   said.utterance_ordinal,
                   said.start_ms,
                   said.quoted_text,
                   said.speaker_label,
                   said.source_artifact_sha256
            FROM {table} AS said
            JOIN meetings AS meeting ON meeting.id = said.meeting_id
            WHERE meeting.lifecycle_state = @active
              AND said.extraction_run_id = {CorpusSearch.TheRunThatCounts("meeting.id")}{window}
            ORDER BY meeting.started_at DESC, meeting.id, said.utterance_ordinal, said.ordinal
            LIMIT @limit;
            """;
    }

    /// <summary>
    /// One row, in the order the query selects. The section is the one that was asked for rather
    /// than one read back off the row: a table is what makes a decision a decision here, and there
    /// is no column saying so.
    /// </summary>
    private static Func<DbDataReader, Statement> Read(LeftKind kind) => reader => new Statement(
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
}
