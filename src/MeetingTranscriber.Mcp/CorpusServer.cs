using System.ComponentModel;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MeetingTranscriber.Mcp;

/// <summary>
/// The eight tools arquitectura.md §8.2 names, over one corpus, read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>ISC-98 says this server answers read-only over stdio and never writes, and it is open.</b>
/// What is proved by a build agent is the read-only half and the whole of the tool surface, over
/// <c>StreamServerTransport</c> — a real client, a real session and real answers, over two pipes
/// rather than over stdio. What is still owed is a client that starts
/// <c>meeting-transcriber-mcp.exe</c> as a child process and talks to it over that process's own
/// standard input and output.
/// </para>
/// <para>
/// <b>The barrier is not packaging and not an alias</b>, which is worth one sentence here so
/// whoever takes ISC-98 does not go looking in the wrong place: a child process with redirected
/// streams <em>is</em> stdio, and this executable is already in a referencing suite's own output.
/// What is missing is a way to point a second process at a corpus —
/// <see cref="CorpusLocation.OfThisUser"/> anchors on the Windows profile and nothing under
/// <c>src/</c> overrides it. So what closes ISC-98 is an override this product has its own reason
/// to have, or a person's run.
/// </para>
/// <para>
/// Every tool opens a corpus and lets it go again; nothing here holds one. That is
/// <see cref="TheCorpusHere"/>'s argument, and the ordering it depends on is in
/// <see cref="RunAsync"/>.
/// </para>
/// </remarks>
/// <param name="where">
/// Where to look for the corpus. Handed in rather than asked for, which is the seam a suite
/// reaches: the process this runs in has one answer and a test has its own, and the alternative
/// was a second process writing over somebody's real pointer file.
/// </param>
public sealed class CorpusServer(CorpusLocation where)
{
    private const string Instructions = """
        Answers questions about the meetings this Windows user has recorded: what was said, what
        was decided, what was left to do, and where in a recording each of those was said.

        It reads and never writes. The corpus is opened read-only, there is no way to run SQL
        through it, and nothing here edits a meeting, a transcript or an extraction — an edit by an
        agent is a thing a person has to confirm, and this server is not where that happens.

        Start with `buscar_reuniones`: it searches turns, summaries, titles, the tree a meeting is
        filed under, and the people on it, all at once, and answers with enough to decide which
        meeting to open. Then `leer_resumen` for what one meeting was about, `obtener_cita` for the
        transcript around one cited turn, and `leer_turnos` for a stretch of one. Opening only what
        you need is the point: a transcript is long and the answer to a good query is at the top.

        `listar_decisiones` and `listar_acciones` go the other way — across the corpus rather than
        into one meeting — for what was settled or left to do over a stretch of time.

        `listar_nodos` and `leer_nodo` go the third way: into a project, a client or a subject rather
        than into a meeting. `listar_nodos` is the whole tree — the organizations, the bodies of work
        inside them and the subjects inside those — and it is how a node gets an id. `leer_nodo` reads
        one node's history: everything the meetings filed under it settled, left to do and left open,
        in the order it was said, oldest first, and it includes everything hanging off that node's
        children — so asking an organization reads the whole of it.

        It orders and it shows where each thing came from. It does not say what still stands: every
        statement comes back with its meeting and its date, and judging them against each other is
        the reader's.

        Every answer carries the meeting's id and when it started. Where something was said at a
        moment, the turn's position and its offset in milliseconds come with it: refer to it by
        position and never by an id, because a rebuild mints new ids and positions survive it.

        `source_sha256` is what a quotation can be checked against, and it is two different facts
        under one name. On a transcript it is the paid response those turns were produced from. On
        a decision or an action it is the artifact that sentence was quoted out of, which is not
        the same thing once a meeting has been transcribed twice — the corpus keeps every response
        it ever paid for. A search hit carries none: a hit is a pointer to a meeting, and what is
        worth checking is whatever you then open.

        A corpus that is not there yet, one on a disk that is not plugged in, and one this build's
        schema has moved past are all answered in words. They are answers and not the end of the
        session: ask again once the thing they name has been dealt with.
        """;

    /// <summary>
    /// The server as the executable runs it: this user's corpus, over stdio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Console.SetOut(TextWriter)"/> runs first and before anything that could print.
    /// Stdout is the protocol and nothing else may reach it — redirected rather than merely
    /// avoided, because the core prints, the runtime prints, and one stray line makes an agent's
    /// session fail as a parse error a long way from whatever wrote it.
    /// </para>
    /// <para>
    /// Nothing touches the corpus here, and that is the other half of the ordering. An MCP client
    /// starts this server in every session of every checkout, most of which never call a tool, and
    /// a start-up that resolved a corpus would hold somebody's SQLite file open for hours for a
    /// call nobody made.
    /// </para>
    /// </remarks>
    internal static async Task<int> RunAsync()
    {
        Console.SetOut(Console.Error);

        var options = new CorpusServer(CorpusLocation.OfThisUser()).Options();

        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync();

        return 0;
    }

    /// <summary>
    /// What this server is and what it offers, ready for a transport to be wrapped around.
    /// </summary>
    /// <remarks>
    /// The transport is the caller's because it is the one thing that differs between the two ways
    /// this server is run: stdio for the executable, a pair of streams for the suite that proves
    /// the tools. Everything a client sees — the name, the instructions, the eight tools and what
    /// each answers — is the same object either way, which is what makes the second one evidence
    /// about the first.
    /// </remarks>
    public McpServerOptions Options() => new()
    {
        ServerInfo = new Implementation
        {
            Name = "meeting-transcriber",
            Title = "Meeting Transcriber corpus",
            Version = "1.0.0",
        },
        ServerInstructions = Instructions,
        ToolCollection = Tools(),
    };

    /// <summary>
    /// The eight of arquitectura.md §8.2, the last two of them the node face §8.2 gained with
    /// ISC-144 and ISC-145, under the parameter names §8.2 spells.
    /// </summary>
    /// <remarks>
    /// A tool's parameter is not a private name: an MCP client puts it in a JSON schema and an
    /// agent types it, so it is part of the specified surface and it is Spanish where §8.2 is.
    /// What §8.2 leaves as <c>filtros</c> is filled in with three names and no more —
    /// <c>limite</c>, <c>desde</c> and <c>hasta</c> — and a filter surface nobody has asked a
    /// question through is not built. The descriptions are English, like every other word this
    /// work leaves behind.
    /// </remarks>
    private McpServerPrimitiveCollection<McpServerTool> Tools() =>
    [
        Tool(
            "buscar_reuniones",
            "Searches every index at once — turns, summaries, titles and notes, the tree a meeting "
            + "is filed under, the people named on it and the voices recognised in it — and "
            + "answers with the best of each, ranked. The query is FTS5 syntax, so "
            + "`presupuesto AND cliente`, `\"exactamente esto\"` and `presu*` all mean what they "
            + "look like. Start here.",
            (
                [Description("What to look for, in FTS5 syntax.")] string query,
                [Description("How many hits at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit) =>
                Answer(corpus =>
                {
                    var wanted = Answers.AtMost(limite);
                    var hits = CorpusSearch.Find(corpus, Asked(query), wanted + 1);

                    return Text(Answers.All(
                        "hits",
                        [.. hits.Take(wanted).Select(Answers.Points)],
                        hits.Count > wanted));
                })),

        Tool(
            "leer_resumen",
            "What one meeting was about, who transcribed and summarised it, and everything the "
            + "accepted extraction left: its decisions, what it left to do, and what it left open. "
            + "Only the extraction a person accepted last answers. To quote one of them, open the "
            + "turn it points at with `obtener_cita`.",
            ([Description("The meeting's id, as another answer gave it.")] string meeting_id) =>
                Answer(corpus =>
                {
                    var meeting = Named(meeting_id);
                    var reading = new MeetingReading(corpus, TimeProvider.System);
                    var read = reading.Of(meeting);
                    var left = read.Screen.Left;

                    return Text(string.Join(
                        Environment.NewLine,
                        Answers.About(read.Meeting, reading.TranscribedFrom(meeting)),
                        $"transcribed_by: {left.Wrote.Transcriber ?? Answers.Nothing}",
                        $"summarised_by: {left.Wrote.Summariser ?? Answers.Nothing}",
                        $"abstract: {left.Abstract ?? Answers.Nothing}",
                        string.Empty,
                        Answers.All(
                            "things the extraction left",
                            [.. left.Things.Take(Answers.MostRowsInOneAnswer).Select(Answers.Left)],
                            left.Things.Count > Answers.MostRowsInOneAnswer)));
                })),

        Tool(
            "leer_turnos",
            "The turns said in one stretch of a meeting, which is how a transcript is opened a "
            + "piece at a time. The stretch takes the turns that began inside it, so two adjoining "
            + "calls answer exactly what one call over both would have and no turn is quoted twice.",
            (
                [Description("The meeting's id, as another answer gave it.")] string meeting_id,
                [Description("Where the stretch opens, in milliseconds from the meeting's start.")] long desde_ms,
                [Description("Where it closes. The first offset outside the stretch.")] long hasta_ms) =>
                Answer(corpus =>
                {
                    var meeting = Named(meeting_id);
                    var from = Offset(desde_ms, nameof(desde_ms));
                    var to = Offset(hasta_ms, nameof(hasta_ms));

                    if (to < from)
                    {
                        throw new McpRefused(
                            $"hasta_ms ({hasta_ms}) is before desde_ms ({desde_ms}), so the stretch "
                            + "asked for has nothing in it.");
                    }

                    // The meeting's own row is read first, so a meeting this corpus does not hold is
                    // refused by name rather than answered with an empty stretch — which would read
                    // as a meeting that was silent for those seconds. The row and not the screen:
                    // this tool needs two of its fields and none of the stage, the extraction or the
                    // audio file that reading the screen would cost.
                    var reading = new MeetingReading(corpus, TimeProvider.System);
                    var about = reading.Row(meeting);

                    // One past the bound, in the query: the stretch asked for can be the whole of a
                    // three-hour meeting, and the extra row is what says there was more without
                    // reading the rest to count it.
                    var turns = reading.Between(meeting, from, to, Answers.MostRowsInOneAnswer + 1);

                    return Text(
                        Answers.About(about, reading.TranscribedFrom(meeting))
                        + Environment.NewLine + Environment.NewLine
                        + Answers.All(
                            "turns",
                            [.. turns.Take(Answers.MostRowsInOneAnswer).Select(Answers.Said)],
                            turns.Count > Answers.MostRowsInOneAnswer));
                })),

        Tool(
            "obtener_cita",
            "The transcript around one cited turn: the turn itself and the two either side, which "
            + "is what says whether a decision really follows from what was said. Anchored by the "
            + "turn's position and never by an id — a rebuild mints new ids and positions survive "
            + "it, so a citation written in March still finds its turn in April.",
            (
                [Description("The meeting's id, as another answer gave it.")] string meeting_id,
                [Description("The cited turn's position, as another answer gave it.")] int utterance_ordinal) =>
                Answer(corpus =>
                {
                    var meeting = Named(meeting_id);
                    var at = Position(utterance_ordinal, nameof(utterance_ordinal));

                    var reading = new MeetingReading(corpus, TimeProvider.System);
                    var about = reading.Row(meeting);
                    var turns = reading.Around(meeting, at);

                    return Text(
                        Answers.About(about, reading.TranscribedFrom(meeting))
                        + Environment.NewLine + Environment.NewLine
                        + Answers.All("turns", [.. turns.Select(Answers.Said)], more: false));
                })),

        Tool(
            "listar_decisiones",
            "Everything the meetings of a stretch of time settled, newest meeting first, out of "
            + "the one extraction of each that a person accepted. Each carries the meeting it was "
            + "settled in and the turn it was settled at.",
            Listing(LeftKind.Decision, "decisions")),

        Tool(
            "listar_acciones",
            "Everything the meetings of a stretch of time left for somebody to do, newest meeting "
            + "first, out of the one extraction of each that a person accepted. Each carries the "
            + "meeting it was left in and the turn it was left at.",
            Listing(LeftKind.Action, "actions")),

        Tool(
            "listar_nodos",
            "Every node of the tree a meeting can be filed under: the organizations, the bodies of "
            + "work inside them and the subjects inside those, root first. This is how a node gets "
            + "an id — nothing else answers with one. Small: the tree stops at three levels and "
            + "belongs to one person.",
            (
                [Description("How many at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit) =>
                Answer(corpus =>
                {
                    var wanted = Answers.AtMost(limite);
                    var tree = CorpusNodes.All(corpus);

                    return Text(Answers.All(
                        "nodes",
                        [.. tree.Take(wanted).Select(Answers.Filed)],
                        tree.Count > wanted));
                })),

        Tool(
            "leer_nodo",
            "One node's history: everything the meetings filed under it settled, left to do and "
            + "left open, oldest first, each carrying the meeting it was said in and when that "
            + "meeting was. It includes everything hanging off the node's children, so an "
            + "organization reads the whole of it. It orders and shows where each thing came from, "
            + "and says nothing about what still stands.",
            (
                [Description("The node's id, as listar_nodos gave it.")] string nodo_id,
                [Description("How many at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit) =>
                Answer(corpus =>
                {
                    var wanted = Answers.AtMost(limite);
                    var statements = CorpusStatements.Under(corpus, Node(nodo_id), wanted + 1);

                    return Text(Answers.All(
                        "statements",
                        [.. statements.Take(wanted).Select(Answers.Left)],
                        statements.Count > wanted));
                })),
    ];

    /// <summary>
    /// The two corpus-wide listings, which differ by one word. Built from one delegate rather than
    /// written twice, because what a caller sees of them is identical and a second copy is a second
    /// place for the window and the limit to come apart.
    /// </summary>
    private Delegate Listing(LeftKind kind, string what) => (
        [Description("The earliest meeting to answer about, as 2026-08-19T09:00:00.000Z. Optional.")]
        string? desde = null,
        [Description("The first meeting too late to answer about. Optional.")]
        string? hasta = null,
        [Description("How many at most. Above 200 is answered with 200.")]
        int limite = CorpusSearch.DefaultLimit) =>
        Answer(corpus =>
        {
            var wanted = Answers.AtMost(limite);

            // One past the bound, for the reason `leer_turnos` asks for one past its own: the limit
            // is in the SQL, so an answer of exactly `wanted` rows cannot say whether it was cut,
            // and counting the rest would be a second query over the whole corpus.
            var statements = CorpusStatements.Of(
                corpus,
                kind,
                Instant(desde, nameof(desde)),
                Instant(hasta, nameof(hasta)),
                wanted + 1);

            return Text(Answers.All(
                what,
                [.. statements.Take(wanted).Select(Answers.Left)],
                statements.Count > wanted));
        });

    private static McpServerTool Tool(string name, string does, Delegate what) =>
        McpServerTool.Create(what, new McpServerToolCreateOptions { Name = name, Description = does });

    /// <summary>
    /// A meeting id as an agent typed it. Refused in words rather than left to
    /// <see cref="Guid.Parse(string)"/>, whose <see cref="FormatException"/> is not a thing to say.
    /// </summary>
    private static Guid Named(string meeting_id) => Guid.TryParse(meeting_id, out var meeting)
        ? meeting
        : throw new McpRefused(
            $"'{meeting_id}' is not a meeting id. They look like "
            + "8d0c1d6a-6f0e-4a3d-9d1a-2c9f4b5e6a70 and come back on every answer.");

    /// <summary>A node id as an agent typed it, refused in words for the reason a meeting id is.</summary>
    private static Guid Node(string nodo_id) => Guid.TryParse(nodo_id, out var node)
        ? node
        : throw new McpRefused(
            $"'{nodo_id}' is not a node id. They look like "
            + "8d0c1d6a-6f0e-4a3d-9d1a-2c9f4b5e6a70 and come back from listar_nodos.");

    /// <summary>
    /// A query with something in it.
    /// </summary>
    /// <remarks>
    /// An agent composing a search from a slot it never filled produces this, and
    /// <c>CorpusSearch.Find</c> answers it with an <see cref="ArgumentException"/> — which is a
    /// defect where every other caller of that method is, and ordinary input here. Refused at the
    /// seam, in words, rather than by widening what this server says out loud to include a type
    /// that everywhere else means somebody made a mistake in code.
    /// </remarks>
    private static string Asked(string query) => string.IsNullOrWhiteSpace(query)
        ? throw new McpRefused(
            "query is empty, so there is nothing to look for. It is FTS5 syntax: a word, "
            + "`presupuesto AND cliente`, \"exactamente esto\" or `presu*`.")
        : query;

    /// <summary>An offset into a meeting, refusing the one value a timeline has no room for.</summary>
    private static Duration Offset(long milliseconds, string named) => milliseconds >= 0
        ? Duration.FromMilliseconds(milliseconds)
        : throw new McpRefused(
            $"{named} is {milliseconds}. An offset into a meeting is counted from its start, so it "
            + "is never negative.");

    /// <summary>
    /// A turn's position on a meeting's timeline. Refused below zero for the same reason an offset
    /// is: a position that cannot exist is a question with no answer, and answering it with an
    /// empty stretch reads as a meeting where nothing was said.
    /// </summary>
    private static int Position(int ordinal, string named) => ordinal >= 0
        ? ordinal
        : throw new McpRefused(
            $"{named} is {ordinal}. A turn's position is counted from the start of the meeting, so "
            + "it is never negative.");

    /// <summary>
    /// A bound on a stretch of time, or none. Refused in words for the same reason a meeting id is:
    /// what <see cref="UtcTimestamp.Parse"/> throws names a format string and not what to type.
    /// </summary>
    private static UtcTimestamp? Instant(string? written, string named)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        try
        {
            return UtcTimestamp.Parse(written.Trim());
        }
        catch (ArgumentException)
        {
            throw new McpRefused(
                $"{named} is '{written}', which is not an instant. They are UTC to the "
                + "millisecond, written 2026-08-19T09:00:00.000Z.");
        }
    }

    /// <summary>
    /// A corpus, the tool's work over it, and the corpus let go again — or what stopped it, as an
    /// answer rather than as the end of the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The list is what these eight tools can reach, and it was derived by walking them.</b> The
    /// obvious thing to do was take <c>ScreenFailures.Reportable</c> — the closed list of what a
    /// read of the corpus says rather than stops over — and adapt it. That list is about a screen
    /// over the whole product, and taken by symmetry it comes out wrong in both directions here:
    /// it names <c>DbUpdateException</c>, which is the write side and unreachable through a
    /// read-only connection, and it names <c>ClassificationException</c> for a reason that is not
    /// this one — a screen reports it when a person files a meeting, and here it is a node id an
    /// agent typed. And it does not name <see cref="MeetingStageException"/> or
    /// <see cref="CorpusSearchException"/>, which two of these tools throw on ordinary input. So:
    /// what a tool here can reach, and nothing by analogy.
    /// </para>
    /// <para>
    /// <see cref="McpRefused"/> is everything this server decided to say itself — a corpus that is
    /// not there, an id that is not one, a bound that is not an instant.
    /// <see cref="MeetingStageException"/> is a meeting this corpus does not hold and
    /// <see cref="CorpusSearchException"/> is a query FTS5 will not parse; both are an agent's
    /// input and both name it.
    /// <see cref="ClassificationException"/> is a node this corpus does not hold, which
    /// <c>leer_nodo</c> throws on an id an agent typed wrong; it is here on the same rule as the
    /// rest and not by analogy with <c>ScreenFailures.Reportable</c>, which names it for a
    /// different reason entirely. The alternative was an empty answer, which reads as a node
    /// nothing was ever said about. <see cref="SqliteException"/> is the corpus itself refusing —
    /// locked, unreadable, not a database — which is the one an agent can do nothing about and a
    /// person can. <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> are the
    /// filesystem's two. <b>They are here on the argument and not on a fact:</b> every path this
    /// server takes to a file goes through <c>CorpusLocation</c> or <c>CorpusDatabase</c>, and both
    /// of those already turn that pair into a refusal, so nothing here has been seen to throw one —
    /// they are the residue, and a sentence costs less than finding out.
    /// </para>
    /// <para>
    /// Anything else is a defect and is left to arrive as one. A server that throws out of a tool
    /// still answers the next call — the SDK turns it into an error result — so what this list buys
    /// is the difference between a sentence somebody can act on and <em>an error occurred</em>, and
    /// never the difference between a live session and a dead one. Which is exactly why it must not
    /// grow to cover a defect: a defect said as though it were a thing a person can deal with is a
    /// defect nobody ever finds.
    /// </para>
    /// </remarks>
    private CallToolResult Answer(Func<CorpusDbContext, CallToolResult> work)
    {
        try
        {
            var corpus = TheCorpusHere.OpenReadOnly(where);

            try
            {
                return work(corpus);
            }
            finally
            {
                TheCorpusHere.LetGo(corpus);
            }
        }
        catch (Exception refused) when (
            refused is McpRefused
                or MeetingStageException
                or CorpusSearchException
                or ClassificationException
                or SqliteException
                or IOException
                or UnauthorizedAccessException)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = refused.Message }],
                IsError = true,
            };
        }
    }

    private static CallToolResult Text(string said) =>
        new() { Content = [new TextContentBlock { Text = said }] };
}
