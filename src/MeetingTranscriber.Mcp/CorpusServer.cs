using System.ComponentModel;
using System.Text;

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
/// <b>ISC-98 says this server answers read-only over stdio and never writes.</b> That it answers
/// over stdio is <c>ChildProcessTests.The_server_answers_a_child_process_over_its_own_standard_streams</c>,
/// a real child process started and spoken to over its own standard input and output; that it never
/// writes is the source sweeps in <c>ReadOnlyTests</c>, which are sweeps of the source and not
/// a run. The whole tool surface is held over <c>StreamServerTransport</c> — a real client, a real
/// session and real answers, over two pipes.
/// </para>
/// <para>
/// <b>The one file this server does write is the record of what it was asked</b> (ISC-100), and it
/// is written outside the corpus on purpose: a line kept in the corpus folder would make "never
/// writes the corpus" false, and the connection is read-only by design. See
/// <see cref="RequestRecord"/>.
/// </para>
/// <para>
/// <b>The barrier was not packaging and not an alias.</b> A child process with redirected streams
/// <em>is</em> stdio, and this executable was already in a referencing suite's own output. What
/// was missing was a way to point a second process at a corpus that is not this user's —
/// <see cref="CorpusLocation.OfThisUser"/> anchors on the Windows profile and nothing under
/// <c>src/</c> overrode it. <c>--corpus &lt;folder&gt;</c> is that override now, resolved through
/// <see cref="CorpusLocation.At"/> rather than through a setting file.
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
/// <param name="record">
/// Where each request is written down. Handed in for the same reason: a suite must never write into
/// the real one, and the executable takes <c>--record &lt;file&gt;</c> for the same seam.
/// </param>
public sealed class CorpusServer(CorpusLocation where, FileInfo record)
{
    private const string Buscar = "buscar_reuniones";
    private const string LeerResumen = "leer_resumen";
    private const string LeerTurnos = "leer_turnos";
    private const string ObtenerCita = "obtener_cita";
    private const string ListarDecisiones = "listar_decisiones";
    private const string ListarAcciones = "listar_acciones";
    private const string ListarNodos = "listar_nodos";
    private const string LeerNodo = "leer_nodo";

    private const string Saltar =
        "How many rows of this answer to skip, as the answer before it said. Starts at 0.";

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

        Every list comes back a page at a time. An answer that was cut says so on its first line and
        says what to pass as `saltar` to read on; nothing is skipped or repeated between two pages of
        one question over a corpus that did not change between them. No answer is larger than 64 KiB,
        whatever its row count: a page stops at the last row that fits, and a single row larger than
        that is refused naming the `saltar` that reads past it.

        Every call that reaches a tool is recorded on this machine, with what was asked and how much
        came back, or why nothing did.

        A corpus that is not there yet, one on a disk that is not plugged in, and one this build's
        schema has moved past are all answered in words. They are answers and not the end of the
        session: ask again once the thing they name has been dealt with.
        """;

    /// <summary>Refused rather than parsed further: what a line this server cannot read is told.</summary>
    private const string Usage = "Usage: meeting-transcriber-mcp [--corpus <folder>] [--record <file>]";

    /// <summary>
    /// The server as the executable runs it: this user's corpus over stdio, or — named through
    /// <c>--corpus &lt;folder&gt;</c> — somebody else's, the way a second process or a test points
    /// this server at a corpus that is not this user's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="arguments"/> is read before anything that could print, and the usage line
    /// goes to stderr rather than stdout for the same reason nothing else may reach it: a session
    /// that never starts still must not cost an agent a parse error over the wrong stream.
    /// <see cref="Console.SetOut(TextWriter)"/> then runs before anything else that could print.
    /// Stdout is the protocol and nothing else may reach it — redirected rather than merely
    /// avoided, because the core prints, the runtime prints, and one stray line makes an agent's
    /// session fail as a parse error a long way from whatever wrote it.
    /// </para>
    /// <para>
    /// Nothing touches the corpus here, and that is the other half of the ordering. An MCP client
    /// starts this server in every session of every checkout, most of which never call a tool, and
    /// a start-up that resolved a corpus would hold somebody's SQLite file open for hours for a
    /// call nobody made. That holds for <c>--corpus</c> exactly as it does for the fallback —
    /// <see cref="CorpusLocation.At"/> only ever names the folder, and the corpus in it is not
    /// opened until the first tool call asks for it.
    /// </para>
    /// </remarks>
    internal static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        string? corpus = null;
        string? recorded = null;

        // Each flag once, in either order, with a value that says something. Anything else is the
        // usage line, including a flag with nothing after it.
        for (var at = 0; at < arguments.Count; at += 2)
        {
            var taken = at + 1 < arguments.Count && !string.IsNullOrWhiteSpace(arguments[at + 1]);

            if (taken && corpus is null && arguments[at] == "--corpus")
            {
                corpus = arguments[at + 1];
            }
            else if (taken && recorded is null && arguments[at] == "--record")
            {
                recorded = arguments[at + 1];
            }
            else
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        var where = corpus is null
            ? CorpusLocation.OfThisUser()
            : CorpusLocation.At(new DirectoryInfo(corpus));

        Console.SetOut(Console.Error);

        var options = new CorpusServer(
            where, recorded is null ? RequestRecord.OfThisUser() : new FileInfo(recorded)).Options();

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
            Buscar,
            "Searches every index at once — turns, summaries, titles and notes, the tree a meeting "
            + "is filed under, the people named on it and the voices recognised in it — and "
            + "answers with the best of each, ranked. The query is FTS5 syntax, so "
            + "`presupuesto AND cliente`, `\"exactamente esto\"` and `presu*` all mean what they "
            + "look like. Start here.",
            (
                [Description("What to look for, in FTS5 syntax.")] string query,
                [Description("How many hits at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit,
                [Description(Saltar)] int saltar = 0) =>
                Answer(
                    Buscar,
                    Args(("query", query), ("limite", limite), ("saltar", saltar)),
                    corpus =>
                    {
                        var wanted = Answers.AtMost(limite);
                        var skipped = Answers.Skipped(saltar);
                        var hits = CorpusSearch.Find(corpus, Asked(query), skipped + wanted + 1);

                        return Answers.Paged(
                            null, "hits", [.. hits.Skip(skipped).Select(Answers.Points)], skipped, wanted);
                    })),

        Tool(
            LeerResumen,
            "What one meeting was about, who transcribed and summarised it, and everything the "
            + "accepted extraction left: its decisions, what it left to do, and what it left open. "
            + "Only the extraction the meeting shows answers: the one accepted last, or the one "
            + "somebody put back since. To quote one of them, open the "
            + "turn it points at with `obtener_cita`.",
            (
                [Description("The meeting's id, as another answer gave it.")] string meeting_id,
                [Description(Saltar)] int saltar = 0) =>
                Answer(
                    LeerResumen,
                    Args(("meeting_id", meeting_id), ("saltar", saltar)),
                    corpus =>
                    {
                        var meeting = Named(meeting_id);
                        var skipped = Answers.Skipped(saltar);
                        var reading = new MeetingReading(corpus, TimeProvider.System);
                        var read = reading.Of(meeting);
                        var left = read.Screen.Left;
                        var voices = new MeetingVoices(corpus, TimeProvider.System).Heard(meeting);

                        // The whole of what the extraction left is already in hand, so a page is a
                        // slice of it and the bound is the only thing that cuts.
                        return Answers.Paged(
                            string.Join(
                                Environment.NewLine,
                                Answers.About(read.Meeting, reading.TranscribedFrom(meeting)),
                                $"transcribed_by: {left.Wrote.Transcriber ?? Answers.Nothing}",
                                $"summarised_by: {left.Wrote.Summariser ?? Answers.Nothing}",
                                $"abstract: {left.Abstract ?? Answers.Nothing}"),
                            "things the extraction left",
                            [.. left.Things.Skip(skipped)
                                .Take(Answers.MostRowsInOneAnswer + 1)
                                .Select(thing => Answers.Left(thing, voices.NameOf(thing.SpeakerLabel)))],
                            skipped,
                            Answers.MostRowsInOneAnswer);
                    })),

        Tool(
            LeerTurnos,
            "The turns said in one stretch of a meeting, which is how a transcript is opened a "
            + "piece at a time. The stretch takes the turns that began inside it, so two adjoining "
            + "calls answer exactly what one call over both would have and no turn is quoted twice.",
            (
                [Description("The meeting's id, as another answer gave it.")] string meeting_id,
                [Description("Where the stretch opens, in milliseconds from the meeting's start.")] long desde_ms,
                [Description("Where it closes. The first offset outside the stretch.")] long hasta_ms,
                [Description(Saltar)] int saltar = 0) =>
                Answer(
                    LeerTurnos,
                    Args(("meeting_id", meeting_id), ("desde_ms", desde_ms), ("hasta_ms", hasta_ms), ("saltar", saltar)),
                    corpus =>
                    {
                        var meeting = Named(meeting_id);
                        var from = Offset(desde_ms, nameof(desde_ms));
                        var to = Offset(hasta_ms, nameof(hasta_ms));
                        var skipped = Answers.Skipped(saltar);

                        if (to < from)
                        {
                            throw new McpRefused(
                                $"hasta_ms ({hasta_ms}) is before desde_ms ({desde_ms}), so the stretch "
                                + "asked for has nothing in it.");
                        }

                        // The meeting's own row is read first, so a meeting this corpus does not hold
                        // is refused by name rather than answered with an empty stretch — which would
                        // read as a meeting that was silent for those seconds. The row and not the
                        // screen: this tool needs two of its fields and none of the stage, the
                        // extraction or the audio file that reading the screen would cost.
                        var reading = new MeetingReading(corpus, TimeProvider.System);
                        var about = reading.Row(meeting);
                        var voices = new MeetingVoices(corpus, TimeProvider.System).Heard(meeting);

                        // Everything up to one past the page, in the query: the stretch asked for can
                        // be the whole of a three-hour meeting, and the extra row is what says there
                        // was more without reading the rest to count it. Turns are ordered by their
                        // position, which is unique in a meeting, so two pages never trade a turn.
                        var turns = reading.Between(
                            meeting, from, to, skipped + Answers.MostRowsInOneAnswer + 1);

                        return Answers.Paged(
                            Answers.About(about, reading.TranscribedFrom(meeting)),
                            "turns",
                            [.. turns.Skip(skipped)
                                .Select(turn => Answers.Said(turn, voices.NameOf(turn.SpeakerLabel)))],
                            skipped,
                            Answers.MostRowsInOneAnswer);
                    })),

        Tool(
            ObtenerCita,
            "The transcript around one cited turn: the turn itself and the two either side, which "
            + "is what says whether a decision really follows from what was said. Anchored by the "
            + "turn's position and never by an id — a rebuild mints new ids and positions survive "
            + "it, so a citation written in March still finds its turn in April.",
            (
                [Description("The meeting's id, as another answer gave it.")] string meeting_id,
                [Description("The cited turn's position, as another answer gave it.")] int utterance_ordinal) =>
                Answer(
                    ObtenerCita,
                    Args(("meeting_id", meeting_id), ("utterance_ordinal", utterance_ordinal)),
                    corpus =>
                    {
                        var meeting = Named(meeting_id);
                        var at = Position(utterance_ordinal, nameof(utterance_ordinal));

                        var reading = new MeetingReading(corpus, TimeProvider.System);
                        var about = reading.Row(meeting);
                        var turns = reading.Around(meeting, at);
                        var voices = new MeetingVoices(corpus, TimeProvider.System).Heard(meeting);

                        // No `saltar`: there are five turns at most, and when they do not fit in one
                        // answer what reads them is `leer_turnos`, which does page.
                        return Answers.Paged(
                            Answers.About(about, reading.TranscribedFrom(meeting)),
                            "turns",
                            [.. turns.Select(turn => Answers.Said(turn, voices.NameOf(turn.SpeakerLabel)))],
                            skipped: null,
                            turns.Count);
                    })),

        Tool(
            ListarDecisiones,
            "Everything the meetings of a stretch of time settled, newest meeting first, out of "
            + "the one extraction of each that was accepted. Each carries the meeting it was "
            + "settled in and the turn it was settled at.",
            Listing(ListarDecisiones, LeftKind.Decision, "decisions")),

        Tool(
            ListarAcciones,
            "Everything the meetings of a stretch of time left for somebody to do, newest meeting "
            + "first, out of the one extraction of each that was accepted. Each carries the "
            + "meeting it was left in and the turn it was left at.",
            Listing(ListarAcciones, LeftKind.Action, "actions")),

        Tool(
            ListarNodos,
            "Every node of the tree a meeting can be filed under: the organizations, the bodies of "
            + "work inside them and the subjects inside those, root first. This is how a node gets "
            + "an id — nothing else answers with one. Small: the tree stops at three levels and "
            + "belongs to one person.",
            (
                [Description("How many at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit,
                [Description(Saltar)] int saltar = 0) =>
                Answer(
                    ListarNodos,
                    Args(("limite", limite), ("saltar", saltar)),
                    corpus =>
                    {
                        var wanted = Answers.AtMost(limite);
                        var skipped = Answers.Skipped(saltar);
                        var tree = CorpusNodes.All(corpus);

                        return Answers.Paged(
                            null,
                            "nodes",
                            [.. tree.Skip(skipped).Take(wanted + 1).Select(Answers.Filed)],
                            skipped,
                            wanted);
                    })),

        Tool(
            LeerNodo,
            "One node's history: everything the meetings filed under it settled, left to do and "
            + "left open, oldest first, each carrying the meeting it was said in and when that "
            + "meeting was. It includes everything hanging off the node's children, so an "
            + "organization reads the whole of it. It orders and shows where each thing came from, "
            + "and says nothing about what still stands.",
            (
                [Description("The node's id, as listar_nodos gave it.")] string nodo_id,
                [Description("How many at most. Above 200 is answered with 200.")]
                int limite = CorpusSearch.DefaultLimit,
                [Description(Saltar)] int saltar = 0) =>
                Answer(
                    LeerNodo,
                    Args(("nodo_id", nodo_id), ("limite", limite), ("saltar", saltar)),
                    corpus =>
                    {
                        var wanted = Answers.AtMost(limite);
                        var skipped = Answers.Skipped(saltar);
                        var statements = CorpusStatements.Under(corpus, Node(nodo_id), skipped + wanted + 1);

                        return Answers.Paged(
                            null,
                            "statements",
                            [.. statements.Skip(skipped).Select(Answers.Left)],
                            skipped,
                            wanted);
                    })),
    ];

    /// <summary>
    /// The two corpus-wide listings, which differ by one word. Built from one delegate rather than
    /// written twice, because what a caller sees of them is identical and a second copy is a second
    /// place for the window and the limit to come apart.
    /// </summary>
    private Delegate Listing(string tool, LeftKind kind, string what) => (
        [Description("The earliest meeting to answer about, as 2026-08-19T09:00:00.000Z. Optional.")]
        string? desde = null,
        [Description("The first meeting too late to answer about. Optional.")]
        string? hasta = null,
        [Description("How many at most. Above 200 is answered with 200.")]
        int limite = CorpusSearch.DefaultLimit,
        [Description(Saltar)] int saltar = 0) =>
        Answer(
            tool,
            Args(("desde", desde), ("hasta", hasta), ("limite", limite), ("saltar", saltar)),
            corpus =>
            {
                var wanted = Answers.AtMost(limite);
                var skipped = Answers.Skipped(saltar);

                // Everything up to one past the page, for the reason `leer_turnos` asks for one past
                // its own: the limit is in the SQL, so an answer of exactly `wanted` rows cannot say
                // whether it was cut, and counting the rest would be a second query over the whole
                // corpus.
                var statements = CorpusStatements.Of(
                    corpus,
                    kind,
                    Instant(desde, nameof(desde)),
                    Instant(hasta, nameof(hasta)),
                    skipped + wanted + 1);

                return Answers.Paged(
                    null, what, [.. statements.Skip(skipped).Select(Answers.Left)], skipped, wanted);
            });

    /// <summary>What a request asked, by the names the tool gave its parameters.</summary>
    private static IReadOnlyDictionary<string, object?> Args(params (string Name, object? Value)[] asked) =>
        asked.ToDictionary(one => one.Name, one => one.Value);

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
    /// <para>
    /// <b>Every call that gets here is recorded, in the same call and before the answer leaves.</b>
    /// An answer, a refusal on the list and a defect off it all leave one line, because what an
    /// agent read is reconstructed from what it was asked and how much came back, and a call that
    /// was refused is part of that. A request that cannot be recorded is not answered: a record
    /// with holes is worse than none, since it would be read as complete. A defect is recorded with
    /// its type and then thrown again, and if its own record fails too it is the defect that
    /// arrives. What is not reached is a call the SDK refuses before any tool runs — an unknown
    /// tool, or arguments that do not bind.
    /// </para>
    /// </remarks>
    private CallToolResult Answer(
        string tool, IReadOnlyDictionary<string, object?> asked, Func<CorpusDbContext, Answers.Page> work)
    {
        string? folder = null;
        Answers.Page? page = null;
        string? refusal = null;

        try
        {
            var corpus = TheCorpusHere.OpenReadOnly(where);

            try
            {
                // Read before the corpus is let go of, which is when the root stops being readable.
                folder = corpus.Root.FullName;
                page = work(corpus);
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
            refusal = refused.Message;
        }
        catch (Exception defect)
        {
            try
            {
                Record(tool, asked, folder, answered: false, rows: 0, said: string.Empty, more: false, defect.GetType().Name);
            }
            catch (Exception failedToRecord) when (failedToRecord is not OutOfMemoryException)
            {
                // The defect is what has to arrive, and it is about to.
            }

            throw;
        }

        var said = page?.Text ?? refusal!;

        try
        {
            Record(tool, asked, folder, page is not null, page?.Rows ?? 0, said, page?.More ?? false, refusal);
        }
        catch (Exception failedToRecord) when (failedToRecord is IOException or UnauthorizedAccessException)
        {
            return Failed(
                $"This request could not be recorded in '{record.FullName}', so it was not answered: "
                + failedToRecord.Message);
        }

        return page is null ? Failed(said) : Text(said);
    }

    /// <summary>
    /// One line of the record. Where no corpus opened, the corpus is the folder that was looked
    /// for, or nothing when even that could not be read.
    /// </summary>
    private void Record(
        string tool,
        IReadOnlyDictionary<string, object?> asked,
        string? folder,
        bool answered,
        int rows,
        string said,
        bool more,
        string? refusal)
    {
        if (folder is null)
        {
            try
            {
                folder = where.Resolve().Path;
            }
            catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
            {
                folder = null;
            }
        }

        RequestRecord.Append(
            record,
            new RecordedRequest(
                UtcTimestamp.From(DateTimeOffset.UtcNow),
                folder,
                tool,
                asked,
                answered,
                rows,
                Encoding.UTF8.GetByteCount(said),
                more,
                refusal));
    }

    private static CallToolResult Failed(string said) => new()
    {
        Content = [new TextContentBlock { Text = said }],
        IsError = true,
    };

    private static CallToolResult Text(string said) =>
        new() { Content = [new TextContentBlock { Text = said }] };
}
