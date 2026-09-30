using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingTranscriber.Processing.Tests.Summaries.ClaudeCode;

/// <summary>
/// A Claude Code that is not there: a generated <c>claude.cmd</c> standing in for the real CLI, so
/// <see cref="MeetingTranscriber.Processing.Summaries.ClaudeCode.ClaudeCodeSummaries"/> can be
/// proven against a real process without spending Claude Code quota or needing the real CLI
/// installed on the machine that runs the suite.
/// </summary>
/// <remarks>
/// <para>
/// It is a real, runnable <c>.cmd</c> file and not a stand-in swapped in for
/// <see cref="MeetingTranscriber.Processing.Summaries.ISummaryProvider"/>: the whole point of this
/// fake is to prove the adapter's own process handling — the arguments, the environment, the
/// working directory, the streams, the kill on timeout — and none of that is exercised by
/// substituting the interface.
/// </para>
/// <para>
/// The <c>.cmd</c> starts no PowerShell and does no work of its own: it stamps <c>trace.log</c> and
/// hands <c>%*</c> to <c>MeetingTranscriber.FakeClaudeCode.dll</c>, a compiled program this suite
/// references, run by the <c>dotnet.exe</c> this very process runs on, named by absolute path. An
/// earlier version was a PowerShell script and never answered on the CI runner at all, for a reason
/// nobody could read off a green laptop; nothing here depends on what another shell does there.
/// </para>
/// <para>
/// Configuration and results cross the process boundary as JSON files under this fake's own folder:
/// a behaviour is enqueued before a run and consumed — read, then deleted — by the one script
/// invocation that answers it, in the order it was queued; a call is recorded to its own numbered
/// file once the script has read what it was given, so <see cref="Calls"/> can read every one back
/// after the process this fake stood in for has ended.
/// </para>
/// </remarks>
internal sealed class FakeClaudeCode
{
    private readonly DirectoryInfo _root;
    private readonly DirectoryInfo _calls;
    private readonly DirectoryInfo _queue;
    private int _queued;

    private FakeClaudeCode(DirectoryInfo folder)
    {
        _root = folder;
        _root.Create();
        _calls = new DirectoryInfo(Path.Combine(_root.FullName, "calls"));
        _calls.Create();
        _queue = new DirectoryInfo(Path.Combine(_root.FullName, "queue"));
        _queue.Create();

        Executable = new FileInfo(Path.Combine(_root.FullName, "claude.cmd"));
        File.WriteAllText(Executable.FullName, GeneratedCommand());
    }

    /// <summary>The generated <c>claude.cmd</c>, ready to be handed to <c>ClaudeCodeSummaries</c>.</summary>
    public FileInfo Executable { get; }

    /// <summary>Every call the fake answered, in the order it answered them.</summary>
    public IReadOnlyList<Call> Calls => [.. _calls
        .EnumerateFiles("*.json")
        .OrderBy(file => file.Name, StringComparer.Ordinal)
        .Select(file => JsonSerializer.Deserialize<Call>(File.ReadAllText(file.FullName), JsonOptions)!)];

    public static FakeClaudeCode In(DirectoryInfo folder) => new(folder);

    /// <summary>
    /// The environment <c>ClaudeCodeSummaries</c> is handed in a test: the handful of names a
    /// Windows process needs to resolve <c>claude.cmd</c> and run it, kept to one copy so both
    /// suites that build a real fake CLI ask for it the same way rather than hand-rolling it twice.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MinimalEnvironment() => new Dictionary<string, string>
    {
        ["PATH"] = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
        ["PATHEXT"] = System.Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty,
        ["SystemRoot"] = System.Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty,
        ["SystemDrive"] = System.Environment.GetEnvironmentVariable("SystemDrive") ?? string.Empty,
        ["ComSpec"] = System.Environment.GetEnvironmentVariable("ComSpec") ?? string.Empty,
        ["TEMP"] = System.Environment.GetEnvironmentVariable("TEMP") ?? string.Empty,
        ["USERPROFILE"] = System.Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty,
    };

    /// <summary>Every <c>--version</c> asked from here on answers this, until told otherwise.</summary>
    public FakeClaudeCode AnswersVersion(string version)
    {
        File.Delete(RefusedVersionMarker);
        File.WriteAllText(VersionFile, version);
        return this;
    }

    /// <summary>Every <c>--version</c> asked from here on exits non-zero, saying nothing usable.</summary>
    public FakeClaudeCode RefusesVersion()
    {
        File.Delete(VersionFile);
        File.WriteAllText(RefusedVersionMarker, "1");
        return this;
    }

    /// <summary>
    /// Queues one behaviour per <paramref name="outputs"/>, each answering one real run in order:
    /// the exact bytes of standard output, and an exit code of <c>0</c>.
    /// </summary>
    public FakeClaudeCode Answers(params string[] outputs)
    {
        foreach (var output in outputs)
        {
            Enqueue(new QueuedBehaviour("answer", output, null, null));
        }

        return this;
    }

    /// <summary>Queues one run exiting with <paramref name="code"/> and this standard error.</summary>
    public FakeClaudeCode ExitsWith(int code, string standardError)
    {
        Enqueue(new QueuedBehaviour("exit", null, code, standardError));
        return this;
    }

    /// <summary>Queues one run that answers nothing and sleeps until it is killed.</summary>
    public FakeClaudeCode TakesForever()
    {
        Enqueue(new QueuedBehaviour("forever", null, null, null));
        return this;
    }

    /// <summary>
    /// One envelope <c>--output-format json</c> would print: <paramref name="result"/> as the
    /// answer, with an optional <c>session_id</c> and an optional single-entry <c>modelUsage"</c>
    /// naming <paramref name="model"/>.
    /// </summary>
    public static string Envelope(string result, string? sessionId = null, string? model = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "result");
            writer.WriteString("subtype", "success");
            writer.WriteBoolean("is_error", false);
            writer.WriteString("result", result);
            if (sessionId is not null)
            {
                writer.WriteString("session_id", sessionId);
            }

            if (model is not null)
            {
                writer.WriteStartObject("modelUsage");
                writer.WriteStartObject(model);
                writer.WriteNumber("outputTokens", 1);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// <c>claude.cmd</c> itself. <c>trace.log</c> gets a line before and after the program runs, so
    /// a run that never reached the program (the host missing, the environment too narrow) reads
    /// differently from one the program answered and <see cref="Diagnosis"/> can say which.
    /// </summary>
    private string GeneratedCommand()
    {
        var folder = _root.FullName;
        var output = AppContext.BaseDirectory;
        var runtimeConfig = Path.Combine(output, "MeetingTranscriber.FakeClaudeCode.runtimeconfig.json");
        var depsFile = Path.Combine(output, "MeetingTranscriber.FakeClaudeCode.deps.json");
        var program = Path.Combine(output, "MeetingTranscriber.FakeClaudeCode.dll");
        foreach (var needed in new[] { runtimeConfig, depsFile, program })
        {
            if (!File.Exists(needed))
            {
                throw new FileNotFoundException($"The fake Claude Code program is missing beside the suite: '{needed}'.");
            }
        }

        var trace = Path.Combine(folder, "trace.log");

        return "@echo off\r\n"
            + $"echo %TIME% claude.cmd started >> \"{trace}\"\r\n"
            + $"\"{DotNetHost()}\" exec --runtimeconfig \"{runtimeConfig}\" --depsfile \"{depsFile}\" \"{program}\" \"{folder}\" %*\r\n"
            + "set \"FAKE_EXIT=%ERRORLEVEL%\"\r\n"
            + $"echo %TIME% claude.cmd finished, dotnet exited with %FAKE_EXIT% >> \"{trace}\"\r\n"
            + "exit /b %FAKE_EXIT%\r\n";
    }

    /// <summary>
    /// What happened in this fake, for a failure message: how many calls it recorded, its trace, and
    /// the behaviours it was never asked for. Whoever reads a red run on a machine they cannot sit at
    /// needs this to tell "the program never started" from "it started and answered the wrong thing".
    /// </summary>
    public string Diagnosis()
    {
        var trace = Path.Combine(_root.FullName, "trace.log");
        return $"The fake at {_root.FullName}: {_calls.GetFiles("*.json").Length} call(s) recorded, "
            + $"{_queue.GetFiles("*.json").Length} behaviour(s) never consumed.\n"
            + "trace.log:\n" + (File.Exists(trace) ? File.ReadAllText(trace) : "(none: claude.cmd was never started)");
    }

    /// <summary>
    /// The <c>dotnet.exe</c> this process runs on, measured off the runtime it loaded rather than
    /// found on <c>PATH</c>, which a test may narrow: <c>DOTNET_HOST_PATH</c> first, otherwise three
    /// folders above the shared runtime (<c>…\dotnet\shared\Microsoft.NETCore.App\&lt;version&gt;\</c>).
    /// </summary>
    private static string DotNetHost()
    {
        var named = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(named) && File.Exists(named))
        {
            return named;
        }

        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var installation = runtime.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException(
                $"'{runtime.FullName}' is not three folders under a dotnet installation. Set DOTNET_HOST_PATH.");
        var host = Path.Combine(installation.FullName, "dotnet.exe");
        return File.Exists(host)
            ? host
            : throw new FileNotFoundException($"There is no dotnet host at '{host}'. Set DOTNET_HOST_PATH.");
    }

    private void Enqueue(QueuedBehaviour behaviour)
    {
        var name = Path.Combine(_queue.FullName, $"{++_queued:D6}.json");
        File.WriteAllText(name, JsonSerializer.Serialize(behaviour, JsonOptions));
    }

    private string VersionFile => Path.Combine(_root.FullName, "version.txt");

    private string RefusedVersionMarker => Path.Combine(_root.FullName, "version-refused");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>One invocation of the fake, whichever it was asked for.</summary>
    /// <param name="Files">
    /// The names of every file in <see cref="Workspace"/> at the moment this call was recorded —
    /// caught here because <c>ClaudeCodeSummaries</c> deletes the folder once the process has
    /// exited, which for a real run is before <c>ExtractAsync</c> ever returns to a test.
    /// </param>
    public sealed record Call(
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment,
        string Workspace,
        IReadOnlyList<string> Files,
        string Prompt);

    private sealed record QueuedBehaviour(string Kind, string? Stdout, int? Code, string? StandardError);
}
