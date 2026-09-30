using System.Text;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Mcp;

/// <summary>
/// The one place a row of the corpus becomes text an agent reads.
/// </summary>
/// <remarks>
/// <para>
/// Blocks of <c>key: value</c> lines, one blank line between them. Not JSON: an MCP tool result is
/// text a model reads, and a second encoding to unwrap buys nothing here — what it would cost is
/// the shape being decided in six places instead of this one.
/// </para>
/// <para>
/// <b>A value is one line, and that is a rule rather than a layout.</b> A title somebody typed and
/// a turn somebody said can both hold a line break, and a block is only readable while a key is the
/// first thing on every line — the second line of a title would otherwise sit where the next key
/// goes and everything after it would read as part of the title. So every value that came from a
/// person or a model goes through <see cref="OneLine"/> on the way out. Nothing is lost by it: a
/// turn is one stretch of speech and a statement is one sentence.
/// </para>
/// <para>
/// <b>What a source hash means depends on what is being answered, so it is read two ways.</b> A
/// transcript comes from the response the meeting's stored turns were projected from, which is
/// <see cref="MeetingReading.TranscribedFrom"/> — not necessarily the newest response filed, nor
/// the run that finished last. A decision, an action or an open question comes
/// from whatever its citation was quoted out of, which is the row's own column — and the two are
/// different the moment a meeting is transcribed twice, which the corpus is built to allow. A hash
/// a reader checks a quote against and cannot find is worse than no hash.
/// </para>
/// </remarks>
internal static class Answers
{
    /// <summary>
    /// The most rows any one answer carries.
    /// </summary>
    /// <remarks>
    /// Rows are what paging is expressed in and what an agent asks in; <see cref="MostBytesInOneAnswer"/>
    /// is the other bound, and it is what stops one enormous row being the leak that rows were meant
    /// to close. A limit above this is clamped rather than refused — an agent that asked for a
    /// thousand wants as many as it can have, and a refusal would cost it the call. What it is told
    /// instead is that there was more and where to read on from, which is the thing it can act on.
    /// </remarks>
    internal const int MostRowsInOneAnswer = 200;

    /// <summary>
    /// The most UTF-8 bytes of text any one answer carries, header and count line included.
    /// </summary>
    /// <remarks>
    /// Bytes and not tokens: a token count is a property of whoever reads the answer, and this
    /// server would have to carry somebody else's tokenizer to guess at it. A page stops at the last
    /// row that fits, and a row that alone does not fit is refused by position rather than cut, so
    /// an answer never holds half of anything somebody said.
    /// </remarks>
    internal const int MostBytesInOneAnswer = 65_536;

    /// <summary>
    /// The most rows an agent may ask to skip. A page is read by asking the corpus for everything
    /// up to the end of it, so the cost of a page grows with how far in it is, and this is where
    /// that stops being a thing to pay for: past it, the question is to be narrowed.
    /// </summary>
    internal const int MostRowsSkipped = 100_000;

    /// <summary>
    /// What stands where there is nothing: a meeting nobody has named, and a meeting whose turns
    /// came out of nothing anybody paid for. Written out rather than left blank, because a key with
    /// nothing after it reads as a value that failed to arrive.
    /// </summary>
    internal const string Nothing = "none";

    private static string Break => Environment.NewLine + Environment.NewLine;

    /// <summary>The limit an agent asked for, brought inside what this server will answer with.</summary>
    internal static int AtMost(int asked) => Math.Clamp(asked, 1, MostRowsInOneAnswer);

    /// <summary>Which meeting an answer is about, and what its transcript can be checked against.</summary>
    internal static string About(Meeting meeting, string? transcribedFrom) =>
        $"""
        meeting_id: {meeting.Id}
        started_at: {meeting.StartedAt}
        source_sha256: {transcribedFrom ?? Nothing}
        title: {OneLine(meeting.Title)}
        """;

    /// <summary>
    /// Which meeting a search hit points at. No source hash, and that is the point: a hit is a
    /// pointer and not a quotation — <see cref="SearchHit"/>'s own remarks say it carries enough to
    /// decide whether to open the meeting and nothing more. What a quote is checked against belongs
    /// on the answer the quote came out of, and putting a meeting-wide hash on a hit would attach
    /// it to a snippet that may have been indexed from another extraction entirely.
    /// </summary>
    internal static string Points(SearchHit hit)
    {
        var anchor = hit is { Ordinal: { } ordinal, Start: { } start }
            ? $"""

                utterance_ordinal: {ordinal}
                at_ms: {start.Milliseconds}
                """
            : string.Empty;

        return $"""
            meeting_id: {hit.MeetingId}
            started_at: {hit.StartedAt}
            found_in: {WireNames<SearchSource>.Of(hit.Source)}
            title: {OneLine(hit.Title)}{anchor}
            snippet: {OneLine(hit.Snippet)}
            """;
    }

    /// <summary>One turn, as it is quoted.</summary>
    internal static string Said(Turn turn, string? speakerName) =>
        $"""
        utterance_ordinal: {turn.Ordinal}
        at_ms: {turn.Start.Milliseconds}
        speaker: {turn.SpeakerLabel}
        speaker_name: {speakerName ?? Nothing}
        says: {OneLine(turn.Text)}
        """;

    /// <summary>
    /// One decision, action or open question of one meeting, as <c>leer_resumen</c> answers it.
    /// </summary>
    /// <remarks>
    /// It carries no quotation and no source hash, and neither is an omission: what one meeting's
    /// screen reads keeps the sentence and where it was said, and what it was quoted out of is not
    /// on that read at all. <c>obtener_cita</c> opens the turn it points at, and
    /// <c>listar_decisiones</c> and <c>listar_acciones</c> answer with the quote and its hash.
    /// </remarks>
    internal static string Left(LeftThing thing, string? speakerName) =>
        Anchored(thing.Kind, thing.TurnOrdinal, thing.At, thing.SpeakerLabel, speakerName, thing.Says);

    /// <summary>
    /// One decision, action or open question of the whole corpus, with the meeting it was left in
    /// and the artifact its quotation was read out of.
    /// </summary>
    internal static string Left(Statement statement) =>
        $"""
        meeting_id: {statement.MeetingId}
        started_at: {statement.StartedAt}
        title: {OneLine(statement.Title)}
        {Anchored(statement.Kind, statement.TurnOrdinal, statement.At, statement.SpeakerLabel, statement.SpeakerName, statement.Says)}
        source_sha256: {statement.SourceSha256}
        quoted: {OneLine(statement.Quoted)}
        """;

    /// <summary>One node of the tree a meeting is filed under, and where it sits in it.</summary>
    /// <remarks>
    /// It carries the parent's id and not the path up to the root: the whole tree comes back in one
    /// answer, so the path is two lookups a reader already has in front of it, and a path repeated on
    /// every node would be the deepest level's spelled three times.
    /// </remarks>
    internal static string Filed(Node node) =>
        $"""
        node_id: {node.Id}
        kind: {WireNames<NodeKind>.Of(node.Kind)}
        name: {OneLine(node.Name)}
        inside: {node.ParentId?.ToString() ?? Nothing}
        """;

    /// <summary>One answer: its text, how many rows it holds, and whether there was more.</summary>
    internal sealed record Page(string Text, int Rows, bool More);

    /// <summary>
    /// The rows an agent asked to skip, or the refusal that says why that cannot be.
    /// </summary>
    internal static int Skipped(int saltar) => saltar switch
    {
        < 0 => throw new McpRefused(
            $"saltar is {saltar}. It counts rows already read, so it is never negative."),
        > MostRowsSkipped => throw new McpRefused(
            $"saltar is {saltar}, past the {MostRowsSkipped} rows any answer here can be paged "
            + "through. Narrow the question instead."),
        _ => saltar,
    };

    /// <summary>
    /// A whole answer: what it is about, what it holds, then the rows — as many as were asked for
    /// and as fit in <see cref="MostBytesInOneAnswer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count comes first because an agent deciding whether to narrow its question should not
    /// have to read to the bottom to find out it was cut. What it says about the cut is that there
    /// was more and where to read on from, never how much more: the limit is applied in SQL and the
    /// caller asks for one row past it, so what is known is the overflow and never the total.
    /// </para>
    /// <para>
    /// <paramref name="rows"/> is what the reader returned past the ones skipped. A page stops at
    /// the last row that fits, and <c>saltar</c> in the count line is the row it stopped at. A first
    /// row that alone would not fit is refused naming its position, because an agent told only that
    /// there was more would ask again at the same place for ever. <paramref name="skipped"/> is
    /// <c>null</c> for an answer that has no <c>saltar</c> to read on with, which is whole or refused.
    /// </para>
    /// </remarks>
    internal static Page Paged(
        string? header, string what, IReadOnlyList<string> rows, int? skipped, int wanted)
    {
        var headerBytes = header is null ? 0 : Encoding.UTF8.GetByteCount(header) + Break.Length;
        var offered = Math.Min(rows.Count, wanted);

        // The count line is budgeted at its longest, which is the cut one at the last row offered:
        // what is written below can only be shorter, so a page never outgrows the bound by its own
        // count line.
        var budget = MostBytesInOneAnswer - headerBytes
            - Encoding.UTF8.GetByteCount(Counted(offered, what, more: true, skipped) + Break);

        if (budget <= 0)
        {
            throw new McpRefused(
                $"What this meeting is about takes {headerBytes} of the {MostBytesInOneAnswer} bytes "
                + "one answer may be, leaving no room for a row, so it is not sent.");
        }

        var taken = 0;
        var used = 0;
        while (taken < offered)
        {
            var next = Encoding.UTF8.GetByteCount(rows[taken]) + (taken == 0 ? 0 : Break.Length);

            if (used + next > budget)
            {
                break;
            }

            used += next;
            taken++;
        }

        if (skipped is null && taken < offered)
        {
            // An answer with no `saltar` is whole or it is refused: a citation that dropped the turn
            // it was asked about, because the turns before it were large, would answer nothing.
            throw new McpRefused(
                $"The {what} around this one do not fit in one answer ({MostBytesInOneAnswer} bytes). "
                + "Open them with leer_turnos, which pages.");
        }

        if (taken == 0 && offered > 0)
        {
            throw new McpRefused(
                $"The {what} row at position {skipped} is {Encoding.UTF8.GetByteCount(rows[0])} bytes, "
                + $"more than the {budget} one answer has room for after what it says first, so it is "
                + $"not sent. Ask again with saltar: {skipped + 1} to read past it.");
        }

        var more = rows.Count > taken;
        var said = Counted(taken, what, more, skipped);
        var body = taken == 0 ? said : said + Break + string.Join(Break, rows.Take(taken));

        return new Page(header is null ? body : header + Break + body, taken, more);
    }

    private static string Counted(int taken, string what, bool more, int? skipped) => more
        ? $"{taken} {what}, and there are more — ask again with saltar: {skipped + taken}, or narrow the question."
        : $"{taken} {what}.";

    /// <summary>
    /// The fields every anchored thing carries, in one shape, so that a decision read out of one
    /// meeting and the same decision read out of the corpus are not two formats an agent has to
    /// learn.
    /// </summary>
    private static string Anchored(
        LeftKind kind, int ordinal, Duration at, string speaker, string? speakerName, string says) =>
        $"""
        kind: {WireNames<LeftKind>.Of(kind)}
        utterance_ordinal: {ordinal}
        at_ms: {at.Milliseconds}
        speaker: {speaker}
        speaker_name: {speakerName ?? Nothing}
        says: {OneLine(says)}
        """;

    /// <summary>
    /// A value as one line, or the word for having none. Line breaks and carriage returns become
    /// spaces rather than being stripped, so two sentences somebody typed on two lines do not run
    /// into one word.
    /// </summary>
    private static string OneLine(string? value) => string.IsNullOrWhiteSpace(value)
        ? Nothing
        : value.ReplaceLineEndings(" ").Trim();
}
