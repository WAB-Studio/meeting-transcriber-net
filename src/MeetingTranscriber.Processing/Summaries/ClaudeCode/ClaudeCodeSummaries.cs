using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MeetingTranscriber.Processing.Summaries.ClaudeCode;

/// <summary>
/// Runs Claude Code headless, over a workspace holding nothing but this call's own files, and reads
/// back what it printed. The one file under <c>src/</c> that starts the Claude Code CLI.
/// </summary>
/// <remarks>
/// <para>
/// A run is always on the person's own Claude plan: the environment it is given is built from an
/// allowlist and never from the parent minus a blocklist, so a name this machine's own environment
/// carries and this class does not know about is simply absent rather than passed through
/// unexamined. No API-key mode is built — that is #125's question, and every answer it could give
/// fits on top of an account-only run.
/// </para>
/// <para>
/// Every call asks <see cref="_whereItIs"/> fresh rather than keeping what it answered last: where
/// Claude Code is can change between one call and the next — a person can pick a different file from
/// the settings screen while a run is in flight — and this class has no business remembering an
/// answer <c>ClaudeCodeLocation</c> already owns keeping current.
/// </para>
/// <para>
/// A run refuses to start under a Claude Code memory file, and does not count the person's own
/// user memory as one: <see cref="MemoryAbove"/> says why.
/// </para>
/// <para>
/// One call at a time. The pump that drives a summary (Decides 8) stays serial the way it stays
/// serial for a transcription, so nothing here is proven, or needs to be, against two
/// <see cref="ExtractAsync"/> calls in flight on the same instance at once.
/// </para>
/// </remarks>
public sealed class ClaudeCodeSummaries : ISummaryProvider
{
    /// <summary>Stored as <c>extraction_runs.provider</c>.</summary>
    public const string ProviderName = "claude-code";

    /// <summary>The model a run is pinned to, and the name <see cref="ClaudeCodeEnvelope"/> looks
    /// for among <c>"modelUsage"</c>'s entries.</summary>
    public const string Model = "sonnet";

    /// <summary>After this, the whole process tree is killed and the run is <c>DidNotAnswer</c>.</summary>
    public static readonly TimeSpan LongestARunMayTake = TimeSpan.FromMinutes(10);

    /// <summary>After this, asking <c>--version</c> is given up on.</summary>
    public static readonly TimeSpan LongestAVersionMayTake = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The arguments a run is started with, always in this order. Nothing from the meeting and no
    /// path from the corpus is ever among them — the prompt goes in on standard input — and an
    /// argument the installed CLI does not know fails the run loudly rather than reaching further
    /// than was asked. The model is the one the request asks for, or <see cref="Model"/> when it
    /// asks for none.
    /// </summary>
    private static IReadOnlyList<string> ArgumentsForARun(string model, string? effort) =>
    [
        "-p",
        "--output-format", "json",
        "--model", model,
        .. effort is null ? Array.Empty<string>() : ["--effort", effort],
        "--tools", string.Empty,
        "--strict-mcp-config",
        "--setting-sources", "project",
        "--no-session-persistence",
    ];

    /// <summary>
    /// The only names a run's environment may carry, compared ignoring case. <c>CLAUDE_CONFIG_DIR</c>
    /// is a path and not a key: without it, somebody who moved their Claude configuration would read
    /// as signed out rather than signed in. Nothing that would let a run reach a proxy or a second
    /// Git installation is here on purpose — a machine that needs one of those reads as <em>did not
    /// answer</em> on every run, loud rather than silent, and widening this is #125's to weigh.
    /// </summary>
    private static readonly string[] AllowedEnvironmentNames =
    [
        "SystemRoot", "windir", "SystemDrive", "ComSpec", "PATH", "PATHEXT", "TEMP", "TMP",
        "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "ProgramData",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles",
        "CommonProgramFiles(x86)", "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS",
        "USERNAME", "USERDOMAIN", "COMPUTERNAME", "CLAUDE_CONFIG_DIR",
    ];

    private static readonly UTF8Encoding NoBom = new(false);

    private readonly Func<FileInfo?> _whereItIs;
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly DirectoryInfo _workspaces;
    private readonly TimeSpan _longestRun;

    public ClaudeCodeSummaries(
        Func<FileInfo?> whereItIs,
        IReadOnlyDictionary<string, string> environment,
        DirectoryInfo workspaces,
        TimeSpan longestRun)
    {
        _whereItIs = whereItIs ?? throw new ArgumentNullException(nameof(whereItIs));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _longestRun = longestRun;
    }

    /// <summary>
    /// One built for this machine: the real environment, filtered through
    /// <see cref="AllowedEnvironmentNames"/>, and <c>%TEMP%\meeting-transcriber-summaries</c> as the
    /// folder every run's own workspace is made under, or <paramref name="workspaces"/> when one is
    /// given — what <c>claude-live</c> uses, so the folders above a run are ones it planted.
    /// </summary>
    public static ClaudeCodeSummaries OnThisMachine(Func<FileInfo?> whereItIs, DirectoryInfo? workspaces = null) => new(
        whereItIs,
        OnlyAllowed(Environment.GetEnvironmentVariables()),
        workspaces ?? new DirectoryInfo(Path.Combine(Path.GetTempPath(), "meeting-transcriber-summaries")),
        LongestARunMayTake);

    public string Name => ProviderName;

    public async Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping) =>
        (await CheckAsync(stopping).ConfigureAwait(false)).Availability;

    public async Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checkedOnce = await CheckAsync(stopping).ConfigureAwait(false);
        if (checkedOnce.Availability.Is != Availability.Answers)
        {
            return new SummaryProviderAnswer.NotAvailable(
                checkedOnce.Availability.Said ?? "Claude Code is not available.");
        }

        // The very executable Check just proved answers, not asked for again: a second ask could
        // legitimately come back different or null — a person can change the choice on the
        // settings screen between one line and the next — and running a file nobody just checked,
        // or throwing over one that changed, are both worse than trusting the one already proven.
        var executable = checkedOnce.Executable!;

        if (MemoryAbove(_workspaces, _environment) is { } memory)
        {
            return new SummaryProviderAnswer.MemoryInTheWay(
                $"A Claude Code memory file sits above where this run would work, at '{memory.FullName}', "
                + "and Claude Code would have read it into the summary. Nothing was sent. "
                + "Move or delete that file and summarise again.",
                memory.FullName);
        }

        var model = request.Model ?? Model;
        var workspace = ClaudeCodeWorkspace.Build(_workspaces, request);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(_longestRun);

            ProcessResult result;
            try
            {
                result = await RunAsync(
                    executable, ArgumentsForARun(model, request.Effort), workspace.Folder, workspace.Prompt, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                return new SummaryProviderAnswer.DidNotAnswer(
                    $"Claude Code did not answer within {_longestRun}.");
            }
            catch (Win32Exception exception)
            {
                return new SummaryProviderAnswer.DidNotAnswer($"Claude Code did not start: {exception.Message}");
            }

            if (result.ExitCode != 0)
            {
                return new SummaryProviderAnswer.DidNotAnswer(
                    $"Claude Code exited with code {result.ExitCode}: {FirstLine(result.StandardError, 200)}");
            }

            return ClaudeCodeEnvelope.Read(result.StandardOutput, checkedOnce.Availability.Version!, model);
        }
        finally
        {
            TryDelete(workspace.Folder);
        }
    }

    /// <summary>The memory file a run would be refused for right now, or nothing. Costs nothing and starts no run.</summary>
    public FileInfo? MemoryFileInTheWay() => MemoryAbove(_workspaces, _environment);

    /// <summary>
    /// The nearest Claude Code memory file in <paramref name="workspaces"/> or any folder above it,
    /// or <c>null</c> when none is there.
    /// </summary>
    /// <remarks>
    /// <c>--setting-sources project</c> does not keep Claude Code from reading a <c>CLAUDE.md</c>
    /// out of an ancestor of its working directory: <c>claude-live</c> on 2.1.285 showed one planted
    /// above the workspace reaching the answer. A run under a folder that has one would put
    /// somebody's stray file into what a meeting's summary says, so it is refused before anything
    /// is started, naming the file. The places looked at are the three Claude Code reads a project
    /// memory from: <c>CLAUDE.md</c>, <c>CLAUDE.local.md</c> and <c>.claude\CLAUDE.md</c>.
    /// <para>
    /// The one file not counted is <c>CLAUDE.md</c> in the person's own Claude Code configuration
    /// folder — <c>CLAUDE_CONFIG_DIR</c> when the run's environment carries it,
    /// <c>%USERPROFILE%\.claude</c> otherwise. That is user-level memory, a setting source the
    /// argument already leaves out, and refusing on it would stop every summary for anybody who
    /// keeps a personal memory. <b>Whether the CLI really leaves it out is not measured:</b>
    /// <c>claude-live</c> does not write under the person's profile.
    /// </para>
    /// </remarks>
    private static FileInfo? MemoryAbove(
        DirectoryInfo workspaces, IReadOnlyDictionary<string, string> environment)
    {
        string? Named(string name) => environment
            .FirstOrDefault(one => string.Equals(one.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        var configuration = Named("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Named("USERPROFILE") is { Length: > 0 } profile
                ? Path.Combine(profile, ".claude")
                : null;

        // GetFullPath spells out any 8.3 short names, so the profile under one spelling and the
        // workspaces under another still compare as the one file; a fact holds that.
        var own = configuration is null
            ? null
            : Path.GetFullPath(Path.Combine(configuration, "CLAUDE.md"));

        string[] places = ["CLAUDE.md", "CLAUDE.local.md", Path.Combine(".claude", "CLAUDE.md")];

        for (var folder = workspaces; folder is not null; folder = folder.Parent)
        {
            foreach (var place in places)
            {
                var file = new FileInfo(Path.Combine(folder.FullName, place));

                if (file.Exists
                    && !string.Equals(file.FullName, own, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What <see cref="IsAvailableAsync"/> answers, together with the exact <see cref="FileInfo"/>
    /// that answer was proven against — <c>null</c> unless it answered <see cref="Availability.Answers"/> —
    /// so <see cref="ExtractAsync"/> never has to ask <see cref="_whereItIs"/> a second time and risk
    /// running a file its own version check never saw.
    /// </summary>
    private readonly record struct Checked(SummaryAvailability Availability, FileInfo? Executable);

    private async Task<Checked> CheckAsync(CancellationToken stopping)
    {
        var executable = _whereItIs();
        if (executable is null || !executable.Exists)
        {
            return new Checked(
                new SummaryAvailability(
                    Availability.NotOnThisMachine, null, "Claude Code was not found on this machine."),
                null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(LongestAVersionMayTake);
        try
        {
            var result = await RunAsync(
                executable, ["--version"], workingDirectory: null, standardInput: null, timeout.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new Checked(
                    new SummaryAvailability(
                        Availability.DoesNotAnswer, null,
                        $"Claude Code exited with code {result.ExitCode} asking its version."),
                    null);
            }

            return new Checked(
                new SummaryAvailability(Availability.Answers, FirstLine(result.StandardOutput, 200), null),
                executable);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            return new Checked(
                new SummaryAvailability(
                    Availability.DoesNotAnswer, null, "Claude Code did not answer --version in time."),
                null);
        }
        catch (Win32Exception exception)
        {
            return new Checked(
                new SummaryAvailability(
                    Availability.DoesNotAnswer, null, $"Claude Code did not start: {exception.Message}"),
                null);
        }
    }

    /// <summary>
    /// The parent's own environment, kept to the names <see cref="AllowedEnvironmentNames"/> lists,
    /// compared ignoring case. <c>internal</c> and not exercised by name from outside this assembly —
    /// what it does is proven through <see cref="OnThisMachine"/> and a real run, not by calling it
    /// directly, since this repository carries no <c>InternalsVisibleTo</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> OnlyAllowed(IDictionary parentEnvironment)
    {
        ArgumentNullException.ThrowIfNull(parentEnvironment);

        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in parentEnvironment)
        {
            if (entry.Key is string name
                && entry.Value is string value
                && AllowedEnvironmentNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                kept[name] = value;
            }
        }

        return kept;
    }

    private async Task<ProcessResult> RunAsync(
        FileInfo executable,
        IReadOnlyList<string> arguments,
        DirectoryInfo? workingDirectory,
        string? standardInput,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(executable.FullName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardInputEncoding = standardInput is not null ? NoBom : null,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables.Clear();
        foreach (var (name, value) in _environment)
        {
            startInfo.EnvironmentVariables[name] = value;
        }

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory.FullName;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var copyStdout = CopyAsync(process.StandardOutput.BaseStream, stdout);
        var copyStderr = CopyAsync(process.StandardError.BaseStream, stderr);
        var writeStdin = standardInput is null
            ? Task.CompletedTask
            : WriteStandardInputAsync(process, standardInput);

        using var registration = token.Register(() => TryKill(process));
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await Task.WhenAll(writeStdin, copyStdout, copyStderr).ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();

        return new ProcessResult(process.ExitCode, stdout.ToArray(), stderr.ToArray());
    }

    /// <summary>
    /// Writes the prompt through <see cref="ProcessStartInfo.StandardInputEncoding"/> — UTF-8 with
    /// no byte-order mark — then closes standard input.
    /// </summary>
    private static async Task WriteStandardInputAsync(Process process, string content)
    {
        try
        {
            await process.StandardInput.WriteAsync(content).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process ended, killed or otherwise, before the whole prompt was written. What it
            // read of a prompt it never finished receiving is its own answer to give, not this
            // method's to raise.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task CopyAsync(Stream from, Stream to)
    {
        try
        {
            await from.CopyToAsync(to).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check above and the call — the ordinary shape of this race.
        }
        catch (Win32Exception)
        {
            // The same race, the other exception .NET can throw for it: a process that finished
            // terminating on its own right as this reached for it. This runs off a cancellation
            // callback with no synchronous caller to observe a throw, so both are swallowed the
            // same way TryDelete swallows the losing side of its own race with Windows.
        }
    }

    private static void TryDelete(DirectoryInfo folder)
    {
        try
        {
            if (Directory.Exists(folder.FullName))
            {
                Directory.Delete(folder.FullName, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The first line of <paramref name="bytes"/>, trimmed and cut to at most
    /// <paramref name="maxLength"/> characters.</summary>
    private static string FirstLine(byte[] bytes, int maxLength)
    {
        var text = NoBom.GetString(bytes);
        var newline = text.IndexOfAny(['\r', '\n']);
        var line = (newline >= 0 ? text[..newline] : text).Trim();
        return line.Length > maxLength ? line[..maxLength] : line;
    }

    private readonly record struct ProcessResult(int ExitCode, byte[] StandardOutput, byte[] StandardError);
}
