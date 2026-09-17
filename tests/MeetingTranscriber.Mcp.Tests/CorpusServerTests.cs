using System.IO.Pipelines;

using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MeetingTranscriber.Mcp.Tests;

/// <summary>
/// The server as a client meets it: a real MCP session, a real handshake, and the six tools
/// answering out of a corpus on disk.
/// </summary>
/// <remarks>
/// <para>
/// No process and no socket. The transport is a pair of pipes, which is the one thing here that is
/// not what the executable runs — <c>CorpusServer</c>'s own remarks say what that leaves owed on
/// ISC-98 and why nothing under <c>tests/</c> closes it today.
/// </para>
/// <para>
/// The corpus is pointed at through <see cref="CorpusLocation"/> and never handed over as a folder,
/// so what these facts exercise is the same resolution the executable does, refusals included.
/// </para>
/// </remarks>
public class CorpusServerTests
{
    /// <summary>
    /// The whole pattern arquitectura.md §8.2 describes, in one session: search, read a summary,
    /// open only the turns needed, and quote one with something to check it against.
    /// </summary>
    /// <remarks>
    /// Red when a transcript stops carrying the hash of the response it was produced from, and red
    /// when a tool's name moves on one side only — which is the failure a server and its client
    /// cannot see between them without a session like this one.
    /// </remarks>
    [Fact]
    public async Task A_real_client_searches_reads_and_opens_only_the_turns_it_needs()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        Guid meeting;

        using (var context = corpus.OpenMigrated())
        {
            meeting = AMeeting.RecordedIn(context);
        }

        await using var talking = await Conversation.Over(corpus);

        var found = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });
        found.ShouldNotBeError();
        found.Said().ShouldContain($"meeting_id: {meeting}");
        found.Said().ShouldContain("found_in: turn");

        // A hit is a pointer and not a quotation, so it carries nothing to check a quote against.
        found.Said().ShouldNotContain("source_sha256");

        var summary = await talking.Call("leer_resumen", new() { ["meeting_id"] = meeting.ToString() });
        summary.ShouldNotBeError();
        summary.Said().ShouldContain($"abstract: {AMeeting.Decided}");
        summary.Said().ShouldContain("kind: decision");
        summary.Said().ShouldContain("kind: action");
        summary.Said().ShouldContain("kind: question");
        summary.Said().ShouldContain($"source_sha256: {AMeeting.ResponseSha256}");

        // Only the stretch the decision was cited in, which is the point of the tool: the meeting
        // has five turns and this asks for the two that begin in two seconds of it.
        var turns = await talking.Call("leer_turnos", new()
        {
            ["meeting_id"] = meeting.ToString(),
            ["desde_ms"] = 3_000,
            ["hasta_ms"] = 5_000,
        });

        turns.ShouldNotBeError();
        turns.Said().ShouldContain("2 turns.");
        turns.Said().ShouldContain(AMeeting.Said);
        turns.Said().ShouldNotContain("turno 4");

        var quoted = await talking.Call("obtener_cita", new()
        {
            ["meeting_id"] = meeting.ToString(),
            ["utterance_ordinal"] = AMeeting.Cited,
        });

        quoted.ShouldNotBeError();
        quoted.Said().ShouldContain($"meeting_id: {meeting}");
        quoted.Said().ShouldContain($"source_sha256: {AMeeting.ResponseSha256}");
        quoted.Said().ShouldContain($"at_ms: {MeetingRows.At(AMeeting.Cited).Milliseconds}");
        quoted.Said().ShouldContain(AMeeting.Said);
    }

    /// <summary>
    /// A transcript says what it was produced from, and a quotation says what it was quoted out of.
    /// </summary>
    /// <remarks>
    /// Two questions with one name, and they come apart the moment a meeting is transcribed twice —
    /// which the corpus is built to allow, because a paid response is never overwritten. The
    /// failure this reddens on is the easy one: answering both from the meeting's newest response
    /// artifact, which hands an agent a hash it cannot find the quotation in and a corpus
    /// inconsistency that does not exist.
    /// </remarks>
    [Fact]
    public async Task A_quotation_carries_what_it_was_quoted_out_of_and_not_the_transcript_it_sits_beside()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        Guid meeting;

        using (var context = corpus.OpenMigrated())
        {
            meeting = AMeeting.RecordedIn(context);
        }

        await using var talking = await Conversation.Over(corpus);

        var settled = await talking.Call("listar_decisiones", new());

        settled.ShouldNotBeError();
        settled.Said().ShouldContain(AMeeting.Decided);
        settled.Said().ShouldContain($"source_sha256: {MeetingRows.QuotedFromSha256}");
        settled.Said().ShouldNotContain(AMeeting.ResponseSha256);

        var turns = await talking.Call("obtener_cita", new()
        {
            ["meeting_id"] = meeting.ToString(),
            ["utterance_ordinal"] = AMeeting.Cited,
        });

        turns.ShouldNotBeError();
        turns.Said().ShouldContain($"source_sha256: {AMeeting.ResponseSha256}");
        turns.Said().ShouldNotContain(MeetingRows.QuotedFromSha256);
    }

    /// <summary>
    /// An answer cut short says so, and one that was not does not.
    /// </summary>
    /// <remarks>
    /// An agent that asked for one row and got one row cannot otherwise tell a corpus of one from a
    /// corpus of a thousand, and it has no cursor to ask with — what it can act on is being told to
    /// narrow. Red against a tool that applies its limit in SQL and then reports the rows it has as
    /// though they were all there were, which every one of these would do without asking for one
    /// row past the bound.
    /// </remarks>
    [Fact]
    public async Task An_answer_the_limit_cut_short_says_so()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");

        using (var context = corpus.OpenMigrated())
        {
            AMeeting.RecordedIn(context);
            AMeeting.RecordedIn(
                context,
                UtcTimestamp.From(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)));
        }

        await using var talking = await Conversation.Over(corpus);

        var cut = await talking.Call("listar_decisiones", new() { ["limite"] = 1 });
        cut.ShouldNotBeError();
        cut.Said().ShouldContain("1 decisions, and there are more");

        var whole = await talking.Call("listar_decisiones", new() { ["limite"] = 20 });
        whole.ShouldNotBeError();
        whole.Said().ShouldContain("2 decisions.");
        whole.Said().ShouldNotContain("there are more");
    }

    /// <summary>
    /// Every tool §8.2 names is on the server, under the parameter names §8.2 spells.
    /// </summary>
    /// <remarks>
    /// A whole set on both halves and never a <em>contains</em>, for the reason
    /// <c>PackageManifestTests</c> gives about capabilities: a parameter arriving is as much a thing
    /// to answer for as one leaving, because an agent types it and a client puts it in a JSON
    /// schema. Red the day <c>meeting_id</c> becomes <c>reunion</c>, and red the day a seventh tool
    /// appears without anybody deciding it belongs on this surface.
    /// </remarks>
    [Fact]
    public async Task Every_tool_arquitectura_names_is_on_the_server_under_the_parameters_it_names()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        await using var talking = await Conversation.Over(corpus);

        var tools = await talking.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ShouldBe([
            "buscar_reuniones",
            "leer_resumen",
            "leer_turnos",
            "listar_acciones",
            "listar_decisiones",
            "obtener_cita",
        ]);

        // The five §8.2 spells, and the three this repository filled `filtros` in with.
        Parameters(tools, "buscar_reuniones").ShouldBe(["limite", "query"]);
        Parameters(tools, "leer_resumen").ShouldBe(["meeting_id"]);
        Parameters(tools, "leer_turnos").ShouldBe(["desde_ms", "hasta_ms", "meeting_id"]);
        Parameters(tools, "obtener_cita").ShouldBe(["meeting_id", "utterance_ordinal"]);
        Parameters(tools, "listar_decisiones").ShouldBe(["desde", "hasta", "limite"]);
        Parameters(tools, "listar_acciones").ShouldBe(["desde", "hasta", "limite"]);
    }

    /// <summary>
    /// The window an agent types reaches the corpus, and a bound it cannot mean is refused in words.
    /// </summary>
    /// <remarks>
    /// What the window <em>is</em> — closed at the bottom, open at the top, on the meeting's own
    /// start — is `CorpusStatementsTests`', asserted directly and in both directions. What only a
    /// session can say is that two strings an agent typed become that window at all, and that a
    /// third string it might type does not become a silently empty answer.
    /// </remarks>
    [Fact]
    public async Task The_window_an_agent_types_reaches_the_corpus()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");

        using (var context = corpus.OpenMigrated())
        {
            AMeeting.RecordedIn(context);
            AMeeting.RecordedIn(
                context,
                UtcTimestamp.From(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)));
        }

        await using var talking = await Conversation.Over(corpus);

        var august = await talking.Call("listar_acciones", new()
        {
            ["desde"] = "2026-08-01T00:00:00.000Z",
            ["hasta"] = "2026-09-01T00:00:00.000Z",
        });

        august.ShouldNotBeError();
        august.Said().ShouldContain("1 actions.");

        var unbounded = await talking.Call("listar_acciones", new());
        unbounded.ShouldNotBeError();
        unbounded.Said().ShouldContain("2 actions.");

        var refused = await talking.Call("listar_acciones", new() { ["desde"] = "2026-08-01" });
        refused.IsError.ShouldBe(true);
        refused.Said().ShouldContain("2026-08-01");
        refused.Said().ShouldContain("not an instant");
    }

    /// <summary>
    /// Input an agent can produce out of a slot it never filled is refused in words, on every tool
    /// that takes one.
    /// </summary>
    /// <remarks>
    /// Each of these otherwise reaches an <see cref="ArgumentException"/> or a silently empty answer
    /// — a blank query through <c>CorpusSearch.Find</c>'s own guard, which is a defect everywhere
    /// else it is called from and ordinary input here; a negative position through a query that
    /// matches nothing and reads as a meeting where nothing was said. Red the day one of them goes
    /// back to <em>an error occurred invoking</em>, which is a refusal that says nothing.
    /// </remarks>
    [Fact]
    public async Task Input_an_agent_can_type_by_accident_is_refused_in_words()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        Guid meeting;

        using (var context = corpus.OpenMigrated())
        {
            meeting = AMeeting.RecordedIn(context);
        }

        await using var talking = await Conversation.Over(corpus);

        var blank = await talking.Call("buscar_reuniones", new() { ["query"] = "   " });
        blank.IsError.ShouldBe(true);
        blank.Said().ShouldContain("query is empty");

        var notAnId = await talking.Call("leer_resumen", new() { ["meeting_id"] = "la reunión" });
        notAnId.IsError.ShouldBe(true);
        notAnId.Said().ShouldContain("is not a meeting id");

        var behindTheStart = await talking.Call("obtener_cita", new()
        {
            ["meeting_id"] = meeting.ToString(),
            ["utterance_ordinal"] = -7,
        });

        behindTheStart.IsError.ShouldBe(true);
        behindTheStart.Said().ShouldContain("never negative");

        var backwards = await talking.Call("leer_turnos", new()
        {
            ["meeting_id"] = meeting.ToString(),
            ["desde_ms"] = -1,
            ["hasta_ms"] = 1_000,
        });

        backwards.IsError.ShouldBe(true);
        backwards.Said().ShouldContain("never negative");
    }

    /// <summary>
    /// A query FTS5 will not parse comes back naming the query, and the session goes on answering.
    /// </summary>
    /// <remarks>
    /// The one refusal a caller can fix by retyping, which is why it is not the same answer as a
    /// corpus that will not open. Red against a catch list that does not name
    /// <see cref="CorpusSearchException"/>, which is the type that carries the query.
    /// </remarks>
    [Fact]
    public async Task A_query_the_index_cannot_parse_comes_back_naming_the_query()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");

        using (var context = corpus.OpenMigrated())
        {
            AMeeting.RecordedIn(context);
        }

        await using var talking = await Conversation.Over(corpus);

        var refused = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto AND" });

        refused.IsError.ShouldBe(true);
        refused.Said().ShouldContain("presupuesto AND");

        var after = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });
        after.ShouldNotBeError();
    }

    /// <summary>
    /// A folder with no corpus in it is refused in words, and not by SQLite.
    /// </summary>
    /// <remarks>
    /// The commonest first call this server will ever get: an MCP client starts it in every session
    /// of every checkout, and most machines it starts on have recorded nothing. Red the day that
    /// refusal goes and <em>unable to open database file</em> comes back instead, which is a
    /// sentence about a file handle where what a person needs to hear is that there is nothing
    /// recorded yet.
    /// </remarks>
    [Fact]
    public async Task A_folder_with_no_corpus_in_it_is_refused_in_words_and_not_by_SQLite()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        await using var talking = await Conversation.Over(corpus);

        var refused = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });

        refused.IsError.ShouldBe(true);
        refused.Said().ShouldContain(corpus.Root.FullName);
        refused.Said().ShouldContain("Record a meeting");
        refused.Said().ShouldNotContain("unable to open database file");
    }

    /// <summary>
    /// A corpus this build's schema has moved past is refused naming the count and the migration.
    /// </summary>
    /// <remarks>
    /// Without it the first tool call fails on a missing column and names neither this build nor
    /// the corpus. The corpus is put one migration behind by taking the last row out of
    /// <c>schema_migrations</c>, which is what a corpus written by an older build looks like from
    /// here.
    /// </remarks>
    [Fact]
    public async Task A_corpus_this_build_has_moved_past_is_refused_naming_the_migration()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        string behind;

        using (var context = corpus.OpenMigrated())
        {
            AMeeting.RecordedIn(context);

            // EF's own bookkeeping, so the columns are EF's names and not this corpus's snake_case
            // ones — the table was given a readable name and nothing else about it was moved.
            behind = Sql.Strings(
                context,
                $"SELECT MigrationId FROM {CorpusDatabase.MigrationsHistoryTable} "
                + "ORDER BY MigrationId DESC LIMIT 1;").Single();

            Sql.Execute(
                context,
                $"DELETE FROM {CorpusDatabase.MigrationsHistoryTable} "
                + $"WHERE MigrationId = '{behind}';");
        }

        await using var talking = await Conversation.Over(corpus);

        var refused = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });

        refused.IsError.ShouldBe(true);
        refused.Said().ShouldContain(behind);
        refused.Said().ShouldContain("1 migration");
    }

    /// <summary>
    /// A read the corpus refuses is an answer, and not the end of the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference this server lives on. A prompt that stops has cost a re-run; a server that
    /// stops has cost the agent every answer after this one, so what a corpus will not do has to
    /// come back as a tool error with the session still up — and the corpus has to be resolved
    /// again on the next call rather than remembered, or a corpus that was repaired would go on
    /// being refused until somebody restarted the client.
    /// </para>
    /// <para>
    /// What stands in for the unplugged drive is a <c>corpus.db</c> that is not a database, which
    /// arrives as a <see cref="Microsoft.Data.Sqlite.SqliteException"/>. The
    /// <see cref="IOException"/> beside it on the list is the same shape and is not reproducible on
    /// a build agent — there is no disk to pull out — so it is on the list on the argument
    /// <c>CorpusServer.Answer</c>'s own remarks make and not on a fact here.
    /// </para>
    /// <para>
    /// Repairing the corpus needs no pool to be emptied here, and that is half of what this holds:
    /// a session that has answered a call lets the file go, so somebody can delete and rebuild
    /// their corpus with the agent still connected. Red the day a tool call leaves a pooled
    /// connection behind — the delete below is what fails.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_read_that_the_corpus_refuses_is_an_answer_and_not_the_end_of_the_session()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp");
        var database = CorpusDatabase.PathIn(corpus.Root);

        await File.WriteAllTextAsync(
            database, "this is not a database", TestContext.Current.CancellationToken);

        await using var talking = await Conversation.Over(corpus);

        var refused = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });
        refused.IsError.ShouldBe(true);

        File.Delete(database);

        using (var context = corpus.OpenMigrated())
        {
            AMeeting.RecordedIn(context);
        }

        // Every pool this suite opened, so the corpus the session reads next is one nothing here is
        // holding — which is the shape the executable meets, where no writer ever ran in-process.
        CorpusDatabase.ClearPoolsFor(corpus.Root);

        var after = await talking.Call("buscar_reuniones", new() { ["query"] = "presupuesto" });

        after.ShouldNotBeError();
        after.Said().ShouldContain("found_in: turn");
    }

    private static IReadOnlyList<string> Parameters(IEnumerable<McpClientTool> tools, string named)
    {
        var schema = tools.Single(tool => tool.Name == named).ProtocolTool.InputSchema;

        return schema.TryGetProperty("properties", out var properties)
            ? [.. properties.EnumerateObject().Select(one => one.Name).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>
    /// One MCP session over two pipes: the server this repository ships, wrapped in the one
    /// transport that needs no process.
    /// </summary>
    private sealed class Conversation : IAsyncDisposable
    {
        private readonly McpServer server;
        private readonly Task serving;

        private Conversation(McpServer server, Task serving, McpClient client)
        {
            this.server = server;
            this.serving = serving;
            Client = client;
        }

        internal McpClient Client { get; }

        internal static async Task<Conversation> Over(CorpusOutsideApplicationData corpus)
        {
            // Nothing of this suite's holding the corpus when the session opens. Every fact here
            // builds its rows through a writable context first, and a pooled writer left behind
            // would have the write-ahead log and its shared-memory segment already attached —
            // which is not the state the executable ever meets, where the only process that has
            // ever touched the file may be this one and it may be opening it read-only, cold.
            CorpusDatabase.ClearPoolsFor(corpus.Root);

            var toServer = new Pipe();
            var toClient = new Pipe();

            var options = new CorpusServer(corpus.Location).Options();

            var server = McpServer.Create(
                new StreamServerTransport(
                    toServer.Reader.AsStream(),
                    toClient.Writer.AsStream(),
                    "meeting-transcriber",
                    null),
                options);

            var serving = server.RunAsync();

            var client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream(), null),
                cancellationToken: TestContext.Current.CancellationToken);

            return new Conversation(server, serving, client);
        }

        internal ValueTask<CallToolResult> Call(string tool, Dictionary<string, object?> with) =>
            Client.CallToolAsync(tool, with, cancellationToken: TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await server.DisposeAsync();

            try
            {
                await serving;
            }
            catch (OperationCanceledException)
            {
                // Ending the session is how this server stops, so the cancellation its own run
                // ends on is the ordinary path and not a failure.
            }
        }
    }
}

/// <summary>What a tool answered, as text.</summary>
internal static class Answered
{
    internal static string Said(this CallToolResult result) => string.Join(
        Environment.NewLine,
        result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    /// <summary>
    /// Asserts the call worked, saying what it answered when it did not — because a bare
    /// <c>IsError.ShouldBeFalse()</c> over a tool that refused says nothing about why.
    /// </summary>
    internal static void ShouldNotBeError(this CallToolResult result) =>
        (result.IsError ?? false).ShouldBeFalse(result.Said());
}
