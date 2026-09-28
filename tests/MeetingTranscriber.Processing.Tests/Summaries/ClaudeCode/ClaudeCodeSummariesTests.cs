using System.Diagnostics;
using System.Text;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Processing.Summaries.ClaudeCode;

namespace MeetingTranscriber.Processing.Tests.Summaries.ClaudeCode;

/// <summary>
/// Runs the real <c>ClaudeCodeSummaries</c> over a real, generated <c>claude.cmd</c> — proving the
/// adapter's own process handling rather than a stand-in for the interface it implements.
/// </summary>
public class ClaudeCodeSummariesTests : IDisposable
{
    private static readonly Guid MeetingId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly DirectoryInfo _root = new(
        Path.Combine(Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    private readonly DirectoryInfo _workspaces;

    public ClaudeCodeSummariesTests()
    {
        _root.Create();
        _workspaces = Folder("workspaces");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root.FullName, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_run_reads_the_meeting_the_instructions_and_the_shape_and_nothing_else()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").Answers(FakeClaudeCode.Envelope("{}"));
        var provider = Provider(fake);
        var request = Request();

        var answer = await provider.ExtractAsync(request, TestContext.Current.CancellationToken);

        answer.ShouldBeOfType<SummaryProviderAnswer.Extracted>();
        var call = fake.Calls.Single(one => one.Arguments.Contains("-p"));
        call.Files.ShouldBe(["instructions.md", "meeting.json", "schema.md"], ignoreOrder: true);
        call.Prompt.ShouldBe(
            Block("instructions.md", request.Instructions)
            + Block("schema.md", request.Schema.Document)
            + Block("meeting.json", Encoding.UTF8.GetString(request.Input.Bytes())));
    }

    [Fact]
    public async Task A_run_is_given_no_tool_none_of_the_person_s_servers_and_none_of_their_settings()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").Answers(FakeClaudeCode.Envelope("{}"));
        var provider = Provider(fake);

        await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var arguments = fake.Calls.Single(one => one.Arguments.Contains("-p")).Arguments;
        After(arguments, "--tools").ShouldBe(string.Empty);
        arguments.ShouldContain("--strict-mcp-config");
        After(arguments, "--setting-sources").ShouldBe("project");
        arguments.ShouldNotContain("--mcp-config");
    }

    [Fact]
    public async Task A_run_carries_no_key_it_found_in_the_environment()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").Answers(FakeClaudeCode.Envelope("{}"));

        const string ApiKey = "ANTHROPIC_API_KEY";
        const string AuthToken = "ANTHROPIC_AUTH_TOKEN";
        const string GitHubToken = "GITHUB_TOKEN";
        const string Canary = "MT_CANARY_SECRET";
        string?[] before =
        [
            Environment.GetEnvironmentVariable(ApiKey),
            Environment.GetEnvironmentVariable(AuthToken),
            Environment.GetEnvironmentVariable(GitHubToken),
            Environment.GetEnvironmentVariable(Canary),
        ];
        Environment.SetEnvironmentVariable(ApiKey, "poison-1");
        Environment.SetEnvironmentVariable(AuthToken, "poison-2");
        Environment.SetEnvironmentVariable(GitHubToken, "poison-3");
        Environment.SetEnvironmentVariable(Canary, "poison-4");
        try
        {
            var provider = ClaudeCodeSummaries.OnThisMachine(() => fake.Executable);
            await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);
        }
        finally
        {
            // Put back whatever was there before, not null: this process's own shell may already
            // carry GITHUB_TOKEN (this repository's own workflow reaches for `gh`), and clearing it
            // for the rest of the test run would be a second, unrelated way for a later test to go
            // red.
            Environment.SetEnvironmentVariable(ApiKey, before[0]);
            Environment.SetEnvironmentVariable(AuthToken, before[1]);
            Environment.SetEnvironmentVariable(GitHubToken, before[2]);
            Environment.SetEnvironmentVariable(Canary, before[3]);
        }

        var environment = fake.Calls.Single(one => one.Arguments.Contains("-p")).Environment;
        environment.ShouldNotContainKey(ApiKey);
        environment.ShouldNotContainKey(AuthToken);
        environment.ShouldNotContainKey(GitHubToken);
        environment.ShouldNotContainKey(Canary);
        environment.ShouldContainKey("PATH");
        environment.ShouldContainKey("USERPROFILE");
    }

    [Fact]
    public async Task No_run_continues_a_conversation_and_no_two_runs_share_a_folder()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").Answers(FakeClaudeCode.Envelope("{}"), FakeClaudeCode.Envelope("{}"));
        var provider = Provider(fake);

        await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);
        await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var runs = fake.Calls.Where(one => one.Arguments.Contains("-p")).ToArray();
        runs.Length.ShouldBe(2);
        foreach (var run in runs)
        {
            run.Arguments.ShouldNotContain("--resume");
            run.Arguments.ShouldNotContain("-r");
            run.Arguments.ShouldNotContain("--continue");
            run.Arguments.ShouldNotContain("-c");
            run.Arguments.ShouldNotContain("--session-id");
            run.Arguments.ShouldNotContain("--fork-session");
            run.Arguments.ShouldContain("--no-session-persistence");
        }

        runs[0].Workspace.ShouldNotBe(runs[1].Workspace);
    }

    [Fact]
    public async Task A_run_starts_in_a_folder_of_its_own_outside_the_corpus_that_is_gone_when_it_ends()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").Answers(FakeClaudeCode.Envelope("{}"));
        var provider = Provider(fake);

        await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var call = fake.Calls.Single(one => one.Arguments.Contains("-p"));
        call.Workspace.ShouldStartWith(_workspaces.FullName);
        Directory.Exists(call.Workspace).ShouldBeFalse();
    }

    [Fact]
    public async Task What_the_run_answered_is_handed_over_unwrapped_with_its_version_model_and_session()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 3.2.1")
            .Answers(FakeClaudeCode.Envelope(
                """{"schema_version":"1"}""", sessionId: "session-77", model: ClaudeCodeSummaries.Model));
        var provider = Provider(fake);

        var answer = await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var extracted = answer.ShouldBeOfType<SummaryProviderAnswer.Extracted>();
        Encoding.UTF8.GetString(extracted.Output).ShouldBe("""{"schema_version":"1"}""");
        extracted.ProviderVersion.ShouldBe("fake 3.2.1");
        extracted.Model.ShouldBe(ClaudeCodeSummaries.Model);
        extracted.SessionId.ShouldBe("session-77");
    }

    [Fact]
    public async Task A_run_that_exits_with_an_error_did_not_answer_and_says_how()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").ExitsWith(7, "boom, it broke\nsecond line never quoted");
        var provider = Provider(fake);

        var answer = await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var didNotAnswer = answer.ShouldBeOfType<SummaryProviderAnswer.DidNotAnswer>();
        didNotAnswer.Said.ShouldContain("7");
        didNotAnswer.Said.ShouldContain("boom, it broke");
        didNotAnswer.Said.ShouldNotContain("second line never quoted");
    }

    [Fact]
    public async Task A_run_that_takes_too_long_is_stopped_and_did_not_answer()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").TakesForever();

        // Five seconds and not two: cmd.exe spawning a cold powershell.exe is itself sometimes
        // most of a second on a machine that has not run one yet this session, and the fact this
        // proves is the kill, not how tight a margin it survives on.
        var provider = Provider(fake, longestRun: TimeSpan.FromSeconds(5));

        var clock = Stopwatch.StartNew();
        var answer = await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);
        clock.Stop();

        answer.ShouldBeOfType<SummaryProviderAnswer.DidNotAnswer>();
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));

        var call = fake.Calls.Single(one => one.Arguments.Contains("-p"));
        await WaitUntilGoneAsync(call.Workspace);
    }

    [Fact]
    public async Task A_run_cancelled_from_outside_is_stopped_where_it_stands()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 1").TakesForever();
        var provider = Provider(fake, longestRun: TimeSpan.FromMinutes(10));
        using var cancel = new CancellationTokenSource();

        var running = provider.ExtractAsync(Request(), cancel.Token);
        await WaitUntilAsync(() => fake.Calls.Any(one => one.Arguments.Contains("-p")));
        cancel.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await running);

        var call = fake.Calls.Single(one => one.Arguments.Contains("-p"));
        await WaitUntilGoneAsync(call.Workspace);
    }

    [Fact]
    public async Task A_CLI_that_refuses_to_say_its_version_does_not_answer()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.RefusesVersion();
        var provider = Provider(fake);

        var availability = await provider.IsAvailableAsync(TestContext.Current.CancellationToken);

        availability.Is.ShouldBe(Availability.DoesNotAnswer);
        availability.Said.ShouldNotBeNullOrWhiteSpace();

        var call = fake.Calls.Single();
        call.Arguments.ShouldBe(["--version"]);
    }

    [Fact]
    public async Task With_Claude_Code_nowhere_nothing_is_started_and_it_says_it_is_not_there()
    {
        var provider = new ClaudeCodeSummaries(() => null, Env(), _workspaces, TimeSpan.FromSeconds(30));

        var answer = await provider.ExtractAsync(Request(), TestContext.Current.CancellationToken);

        var notAvailable = answer.ShouldBeOfType<SummaryProviderAnswer.NotAvailable>();
        notAvailable.Said.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Asking_whether_it_is_there_answers_with_its_version()
    {
        var fake = FakeClaudeCode.In(Folder("fake"));
        fake.AnswersVersion("fake 9.9.9 (Claude Code)");
        var provider = Provider(fake);

        var availability = await provider.IsAvailableAsync(TestContext.Current.CancellationToken);

        availability.Is.ShouldBe(Availability.Answers);
        availability.Version.ShouldBe("fake 9.9.9 (Claude Code)");

        var call = fake.Calls.Single();
        call.Arguments.ShouldBe(["--version"]);
    }

    private static ExtractionRequest Request(SummaryCorrection? correction = null) => new(
        new MeetingInput(
            MeetingId,
            new string('a', 64),
            [new Turn(0, Duration.FromMilliseconds(0), Duration.FromMilliseconds(1000), AudioChannel.Microphone, "ch1:speaker_0", "Hola")]),
        "extract, please",
        new ExtractionSchema("1", "the shape a run has to answer in"),
        correction);

    private ClaudeCodeSummaries Provider(FakeClaudeCode fake, TimeSpan? longestRun = null) => new(
        () => fake.Executable, Env(), _workspaces, longestRun ?? TimeSpan.FromSeconds(30));

    private static IReadOnlyDictionary<string, string> Env() => new Dictionary<string, string>
    {
        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
        ["PATHEXT"] = Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty,
        ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty,
        ["SystemDrive"] = Environment.GetEnvironmentVariable("SystemDrive") ?? string.Empty,
        ["ComSpec"] = Environment.GetEnvironmentVariable("ComSpec") ?? string.Empty,
        ["TEMP"] = Environment.GetEnvironmentVariable("TEMP") ?? string.Empty,
        ["USERPROFILE"] = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty,
    };

    private DirectoryInfo Folder(string name)
    {
        var folder = new DirectoryInfo(Path.Combine(_root.FullName, name));
        folder.Create();
        return folder;
    }

    private static string Block(string name, string content) => $"--- {name} ---\n{content}\n";

    private static string After(IReadOnlyList<string> arguments, string flag) =>
        arguments[arguments.ToList().IndexOf(flag) + 1];

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The fake never recorded the call this test is waiting for.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// <c>TryDelete</c> swallows the exact Windows refusal a handle release right after a kill can
    /// produce, so asserting the folder is gone the instant a call returns is asserting something
    /// this adapter's own defensive code says is not always immediate. Retrying for a few seconds is
    /// what proves the deletion rather than a race against Windows letting go of the handle.
    /// </summary>
    private static async Task WaitUntilGoneAsync(string path)
    {
        var clock = Stopwatch.StartNew();
        while (Directory.Exists(path))
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException($"'{path}' was still there {clock.Elapsed.TotalSeconds:F1}s later.");
            }

            await Task.Delay(25);
        }
    }
}
