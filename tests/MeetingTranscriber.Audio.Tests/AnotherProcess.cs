using System.Diagnostics;
using System.Text;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A second operating system process made to take a mark and sit on it until it is killed, which
/// is the only way a probe reaches a handle a dead process leaves behind.
/// </summary>
/// <remarks>
/// Shared because two marks are probed this way and the ninety lines below are not worth having
/// twice. What differs between them is the share mode a claim takes, so that is the argument.
/// </remarks>
internal static class AnotherProcess
{
    /// <summary>Where the mark's path reaches the other process, so nothing has to quote it.</summary>
    private const string MarkVariable = "MEETING_TRANSCRIBER_MARK";

    /// <summary>What the other process says on stdout the moment it has the mark.</summary>
    /// <remarks>
    /// Seven-bit ASCII on purpose. <c>powershell.exe</c> writes a redirected stdout in its own
    /// output encoding and this process decodes in its own, so anything outside ASCII could arrive
    /// mangled. The diagnostic below may; the sentinel may not.
    /// </remarks>
    private const string TookIt = "held";

    /// <summary>
    /// How long a host that has neither written a line nor ended is given before the probe decides
    /// the host is what failed.
    /// </summary>
    /// <remarks>
    /// A number that asserts nothing, in the sense <see cref="Deadlines.Patience"/> means it — and
    /// not that number, because that one is the application's own deadline, kept so that a test is
    /// never less patient than what it drives, and nothing here drives the application. What is
    /// waited on is another operating system process reaching its first statement, which on a
    /// loaded agent is seconds; so more of this is strictly better and none of it bounds the
    /// handover. It exists at all because an unbounded read answers a host that never runs a line
    /// with silence until the job is cancelled, naming no test — the same dishonesty at the same
    /// gate as the one it replaced, in a rarer case.
    /// </remarks>
    private static readonly TimeSpan WedgedHost = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Another process holding the mark the way a claim holds it, and doing nothing else. It never
    /// closes the handle itself: the only way out of it is being killed, which is the point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handover is a line on stdout, and it may not be a clock, because the clock is what this
    /// used to get wrong. It used to be a poll: this process asking
    /// <see cref="SavingMark.IsHeldIn"/> every 25 ms until the answer came back true. That question
    /// opens the mark <c>FileAccess.Read</c>, <c>FileShare.Read</c>, and <see cref="ScriptFor"/>'s
    /// open asks for <c>FileAccess.Write</c> — which <c>FileShare.Read</c> does not permit — so an
    /// open landing inside one of the poll's own opens is refused, <c>0x80070020</c>. The thing
    /// waiting for the mark to be taken was therefore the one thing that could stop it being taken,
    /// and the open it refused had nothing behind it: no retry, and nothing turning the refusal
    /// into an ending, so that process went on to its sleep holding nothing and the wait spent
    /// thirty seconds on a question whose answer could no longer change. A longer wait was never
    /// what that needed — and neither was asking whether the process was still alive, because it
    /// was. Alive and empty is what losing this race looks like from here.
    /// </para>
    /// <para>
    /// Which is the reading and not a reconstruction of any particular red run: the collision is
    /// real and was reproduced on purpose, but the window is one open — 66 µs on the machine this
    /// was written on, against a 25 ms period — so meeting it takes a process that reaches its own
    /// open late, which is what a loaded two-core agent makes of <c>powershell.exe</c>. A process
    /// merely too slow to arrive inside thirty seconds prints the same sentence and is not excluded. Both end here: the handshake means this
    /// process does not open the mark at all until the other one says it has it, so the collider is
    /// gone rather than rare, and being slow now costs only the time it costs. Widening the
    /// question's own share would also end the collision, by ending the detection with it — see
    /// <see cref="SavingMarkTests.A_save_that_is_running_holds_the_folder_and_refuses_a_second_one"/>,
    /// which is what goes red for that.
    /// </para>
    /// <para>
    /// <paramref name="besideIt"/> is what the held handle shares, so it is the mark's own claim
    /// that is being imitated: <see cref="FileShare.Read"/> for a save or a capture, which admits
    /// nobody else, and <see cref="FileShare.ReadWrite"/> for a read, which admits another reader.
    /// </para>
    /// </remarks>
    internal static Process Holding(FileInfo mark, FileShare besideIt)
    {
        ArgumentNullException.ThrowIfNull(mark);

        var start = new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(Encoding.Unicode.GetBytes(ScriptFor(besideIt))))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        start.Environment[MarkVariable] = mark.FullName;

        return Process.Start(start)
            ?? throw new InvalidOperationException("powershell.exe did not start.");
    }

    /// <summary>
    /// Blocks until the other process says it has the mark, and fails saying what it said instead —
    /// or that it said nothing and ended — rather than asserting into a race.
    /// </summary>
    internal static void HasTakenIt(Process holder)
    {
        ArgumentNullException.ThrowIfNull(holder);

        var reading = Task.Run(holder.StandardOutput.ReadLine);

        reading.Wait(WedgedHost, TestContext.Current.CancellationToken).ShouldBeTrue(
            "the process meant to hold the mark neither took it nor ended, and said nothing at all.");

        var said = reading.Result;

        if (said is null)
        {
            // Its stdout reached the end without a word, which is that process being gone. Waiting
            // is what makes the exit code readable, and it returns at once on one already ended.
            holder.WaitForExit();
            said = $"nothing at all before it ended, exit code {holder.ExitCode}";
        }

        said.ShouldBe(TookIt, "the process meant to hold the mark did not take it.");
    }

    /// <summary>
    /// What the other process runs: take the mark, say so, and then hold it until it is killed.
    /// </summary>
    /// <remarks>
    /// The two seconds are a claim's own, and they are the one bound on this handover: anything
    /// other than this process holding the mark for longer than a claim would wait turns the probe
    /// red saying so. Waiting it out rather than dying on it is what a save does, which is what
    /// this is meant to look like — it opens the mark with the share mode a claim opens it with,
    /// and <c>Open</c> rather than <c>Create</c> so that a mark which is not there is a loud
    /// failure rather than a file this probe made. Nothing but the sentinel and that one sentence
    /// may reach stdout, because the read on the other side takes the first line it is given.
    /// </remarks>
    private static string ScriptFor(FileShare besideIt) => $$"""
        $ErrorActionPreference = 'Stop'
        $mark = $env:{{MarkVariable}}
        $waiting = [Diagnostics.Stopwatch]::StartNew()
        $held = $null
        while ($null -eq $held) {
            try {
                $held = [System.IO.File]::Open($mark, 'Open', 'Write', '{{besideIt}}')
            }
            catch {
                if ($waiting.Elapsed.TotalSeconds -ge 2) {
                    [Console]::Out.WriteLine(($_.Exception.Message -replace '\r?\n', ' '))
                    exit 1
                }
                Start-Sleep -Milliseconds 25
            }
        }
        [Console]::Out.WriteLine('{{TookIt}}')
        Start-Sleep -Seconds 300
        """;
}
