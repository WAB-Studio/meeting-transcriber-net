using System.Diagnostics;
using System.Runtime.InteropServices;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MeetingTranscriber.Mcp.Tests;

/// <summary>
/// ISC-98's own run, and the one thing every other fact in this project cannot be: a real child
/// process, started the way an MCP client starts <c>meeting-transcriber-mcp.exe</c>, spoken to
/// over that process's own standard input and output rather than over a pair of pipes this suite
/// built.
/// </summary>
/// <remarks>
/// <para>
/// The process is the suite's own host and not a packaged install, because that is what a build
/// agent has: <c>&lt;dotnet host&gt; exec --runtimeconfig &lt;suite&gt;.runtimeconfig.json
/// --depsfile &lt;suite&gt;.deps.json meeting-transcriber-mcp.dll</c>, run from this project's own
/// output folder, where the server's <c>ProjectReference</c> already puts
/// <c>meeting-transcriber-mcp.dll</c> beside it. The suite's own <c>.runtimeconfig.json</c> and
/// <c>.deps.json</c> are asked for rather than the server's, because both already resolve every
/// assembly the server needs — this suite references it — and asking for the server's own would be
/// a second, narrower manifest naming the same folder for no reason a build agent could tell apart.
/// </para>
/// <para>
/// <c>--corpus &lt;folder&gt;</c> is what makes this possible at all without touching this build
/// agent's own profile: the corpus is one this suite made and nobody's real one.
/// </para>
/// </remarks>
public class ChildProcessTests
{
    /// <summary>
    /// The longest any of these facts waits on a child process before giving up. A hang here is a
    /// real regression — a broken build agent, the DLL not found, a change to stdout redirection
    /// ordering — and CI's own job has no timeout of its own to catch it, so each fact bounds itself
    /// rather than trusting the platform's default of several hours.
    /// </summary>
    private static readonly TimeSpan LongestAChildMayTake = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A search, over a real child process's own stdio, finds the meeting a corpus this test built
    /// holds — the same session <c>CorpusServerTests</c> proves over two pipes, proved here over
    /// what the executable actually is.
    /// </summary>
    [Fact]
    public async Task The_server_answers_a_child_process_over_its_own_standard_streams()
    {
        using var corpus = new CorpusOutsideApplicationData("mcp-child");
        Guid meeting;

        using (var context = corpus.OpenMigrated())
        {
            meeting = AMeeting.RecordedIn(context);
        }

        using var process = Started("--corpus", corpus.Root.FullName);
        var draining = DrainStandardError(process);

        try
        {
            using var bounded = Bounded();

            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream),
                cancellationToken: bounded.Token);

            var tools = await client.ListToolsAsync(cancellationToken: bounded.Token);
            tools.Count.ShouldBe(8);

            var found = await client.CallToolAsync(
                "buscar_reuniones",
                new Dictionary<string, object?> { ["query"] = "presupuesto" },
                cancellationToken: bounded.Token);

            var said = string.Join(
                Environment.NewLine, found.Content.OfType<TextContentBlock>().Select(block => block.Text));
            said.ShouldContain($"meeting_id: {meeting}");
        }
        finally
        {
            Stop(process);
            await draining;
        }
    }

    /// <summary>
    /// A command line this server cannot read — anything but no arguments at all, or exactly
    /// <c>--corpus &lt;folder&gt;</c> — exits <c>2</c> and names its usage on stderr, without ever
    /// touching stdio as the protocol.
    /// </summary>
    [Fact]
    public async Task A_line_the_server_cannot_read_is_refused_with_its_usage()
    {
        using var process = Started("--corpus");

        try
        {
            using var bounded = Bounded();

            process.StandardInput.Close();

            var error = await process.StandardError.ReadToEndAsync(bounded.Token);
            await process.WaitForExitAsync(bounded.Token);

            process.ExitCode.ShouldBe(2);
            error.ShouldContain("Usage");
        }
        finally
        {
            Stop(process);
        }
    }

    /// <summary>
    /// <c>--corpus</c> with an empty folder is the same refusal as any other line this server
    /// cannot read, and not an unhandled exception out of the argument switch — red against
    /// <c>new DirectoryInfo("")</c> reached before the empty string is turned away.
    /// </summary>
    [Fact]
    public async Task An_empty_corpus_value_is_refused_with_its_usage()
    {
        using var process = Started("--corpus", string.Empty);

        try
        {
            using var bounded = Bounded();

            process.StandardInput.Close();

            var error = await process.StandardError.ReadToEndAsync(bounded.Token);
            await process.WaitForExitAsync(bounded.Token);

            process.ExitCode.ShouldBe(2);
            error.ShouldContain("Usage");
        }
        finally
        {
            Stop(process);
        }
    }

    /// <summary>A token that gives up after <see cref="LongestAChildMayTake"/>, linked to the test's own.</summary>
    private static CancellationTokenSource Bounded()
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bounded.CancelAfter(LongestAChildMayTake);
        return bounded;
    }

    /// <summary>
    /// The child process itself, its standard streams redirected and nothing about it started
    /// through a shell.
    /// </summary>
    private static Process Started(params string[] afterTheDll)
    {
        var folder = AppContext.BaseDirectory;
        var suite = Path.GetFileNameWithoutExtension(typeof(ChildProcessTests).Assembly.Location);

        // Encodings are left at their defaults: the client reads and writes through
        // process.StandardInput.BaseStream and process.StandardOutput.BaseStream directly, so
        // nothing here ever goes through the StreamReader/StreamWriter an encoding would apply to.
        var info = new ProcessStartInfo(DotNetHost())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = folder,
        };

        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--runtimeconfig");
        info.ArgumentList.Add(Path.Combine(folder, $"{suite}.runtimeconfig.json"));
        info.ArgumentList.Add("--depsfile");
        info.ArgumentList.Add(Path.Combine(folder, $"{suite}.deps.json"));
        info.ArgumentList.Add(Path.Combine(folder, "meeting-transcriber-mcp.dll"));

        foreach (var argument in afterTheDll)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)
            ?? throw new InvalidOperationException($"'{info.FileName}' did not start.");
    }

    /// <summary>
    /// The dotnet host this suite is running on: what an MCP client config would name, measured off
    /// the runtime this process itself loaded rather than assumed. <c>DOTNET_HOST_PATH</c> first,
    /// because a build agent that already sets it is saying where to find one; otherwise
    /// <c>dotnet.exe</c> three folders above the shared runtime this process loaded from —
    /// <c>…\dotnet\shared\Microsoft.NETCore.App\&lt;version&gt;\</c> is always three folders under
    /// the installation that holds the host.
    /// </summary>
    private static string DotNetHost()
    {
        var named = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(named))
        {
            return named;
        }

        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var installation = runtime.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException(
                $"'{runtime.FullName}' is not three folders under a dotnet installation, so the "
                + "host cannot be found from it. Set DOTNET_HOST_PATH.");

        return Path.Combine(installation.FullName, "dotnet.exe");
    }

    /// <summary>
    /// Reads stderr to the end in the background, so a server that writes more than the pipe's
    /// buffer holds cannot deadlock against a test that is only reading stdout.
    /// </summary>
    private static Task<string> DrainStandardError(Process process) =>
        process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

    private static void Stop(Process process)
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
            // Exited between the check above and the kill — already gone, which is what was asked.
        }

        process.WaitForExit();
    }
}
