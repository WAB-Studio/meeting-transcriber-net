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
/// Generated using only <c>cmd.exe</c>'s own script format and
/// <c>%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe</c>, named by that literal path
/// rather than found on <c>PATH</c>, because a test's own environment may not carry one. The batch
/// file is one line forwarding <c>%*</c> into a PowerShell script that does the actual work: every
/// piece of real behaviour here is PowerShell, never batch, because batch's delayed-expansion traps
/// inside a conditional are not worth the one line saved.
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
    private static readonly string PowerShell =
        Path.Combine(
            Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

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
        File.WriteAllText(
            Executable.FullName,
            $"@echo off\r\n\"{PowerShell}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass "
            + $"-File \"{Path.Combine(_root.FullName, "run.ps1")}\" %*\r\nexit /b %ERRORLEVEL%\r\n");
        File.WriteAllText(Path.Combine(_root.FullName, "run.ps1"), Script);
    }

    /// <summary>The generated <c>claude.cmd</c>, ready to be handed to <c>ClaudeCodeSummaries</c>.</summary>
    public FileInfo Executable { get; }

    /// <summary>Every call the fake answered, in the order it answered them.</summary>
    public IReadOnlyList<Call> Calls => [.. _calls
        .EnumerateFiles("*.json")
        .OrderBy(file => file.Name, StringComparer.Ordinal)
        .Select(file => JsonSerializer.Deserialize<Call>(File.ReadAllText(file.FullName), JsonOptions)!)];

    public static FakeClaudeCode In(DirectoryInfo folder) => new(folder);

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

    /// <summary>
    /// What every invocation runs. Standard input is copied through
    /// <c>[Console]::OpenStandardInput().CopyTo(...)</c>, and only for a real run — a
    /// <c>--version</c> ask is never given a redirected stream to read, so asking would block on
    /// whatever this process itself inherited.
    /// </summary>
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $root = Split-Path -Parent $MyInvocation.MyCommand.Path
        $callsDir = Join-Path $root 'calls'
        $isVersion = $args.Count -gt 0 -and $args[0] -eq '--version'

        $prompt = ''
        if (-not $isVersion) {
            $stdin = New-Object System.IO.MemoryStream
            [Console]::OpenStandardInput().CopyTo($stdin)
            $prompt = [System.Text.Encoding]::UTF8.GetString($stdin.ToArray())
        }

        $envDump = @{}
        foreach ($entry in [System.Environment]::GetEnvironmentVariables().GetEnumerator()) {
            $envDump[[string]$entry.Key] = [string]$entry.Value
        }

        $files = @(Get-ChildItem -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)

        $existing = @(Get-ChildItem $callsDir -Filter '*.json' -ErrorAction SilentlyContinue)
        $callNumber = $existing.Count
        $call = @{
            arguments = @($args)
            environment = $envDump
            workspace = (Get-Location).Path
            files = $files
            prompt = $prompt
        }
        $call | ConvertTo-Json -Depth 6 -Compress |
            Set-Content -Path (Join-Path $callsDir ('{0:D6}.json' -f $callNumber)) -Encoding UTF8 -NoNewline

        if ($isVersion) {
            $refused = Join-Path $root 'version-refused'
            if (Test-Path $refused) {
                [Console]::Error.Write('fake claude: refuses to say its version')
                exit 1
            }

            [Console]::Out.Write((Get-Content (Join-Path $root 'version.txt') -Raw))
            exit 0
        }

        $queueDir = Join-Path $root 'queue'
        $next = Get-ChildItem $queueDir -Filter '*.json' -ErrorAction SilentlyContinue |
            Sort-Object Name | Select-Object -First 1
        if ($null -eq $next) {
            [Console]::Error.Write('fake claude: no behaviour was queued for this call')
            exit 1
        }

        $behaviour = Get-Content $next.FullName -Raw | ConvertFrom-Json
        Remove-Item $next.FullName -Force

        switch ($behaviour.kind) {
            'answer' {
                [Console]::Out.Write($behaviour.stdout)
                exit 0
            }
            'exit' {
                [Console]::Error.Write($behaviour.standardError)
                exit $behaviour.code
            }
            'forever' {
                Start-Sleep -Seconds 300
                exit 1
            }
        }
        """;
}
