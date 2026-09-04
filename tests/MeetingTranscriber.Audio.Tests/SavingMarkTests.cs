using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The mark a finish holds over the folder it is reading: what it says while a save is running,
/// and what it says once whatever was running is gone.
/// </summary>
/// <remarks>
/// The second half is the whole bet. A mark that means something by being there is one a process
/// that died leaves behind for good, and the recording it names is then out of reach of every
/// answer — so what is probed here is not that a save can be marked, which is easy, but that a
/// mark nothing is holding says nothing. Every test that would pass over an implementation reading
/// <c>File.Exists</c> is worth nothing to this claim, and the ones below that would fail over one
/// say so in their own words.
/// </remarks>
public sealed class SavingMarkTests : IDisposable
{
    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public SavingMarkTests() => root.Create();

    /// <summary>A folder no save has ever run over has nothing to say about one.</summary>
    [Fact]
    public void A_folder_no_save_has_touched_is_not_being_saved()
    {
        var folder = root.CreateSubdirectory("daily");

        SavingMark.IsHeldIn(folder).ShouldBeFalse();
        Mark(folder).Exists.ShouldBeFalse();

        // And a folder that is not there at all is not a save either, which is what a listing asks
        // of a corpus that has never recorded anything. Claiming one is refused by naming it.
        var nowhere = new DirectoryInfo(Path.Combine(root.FullName, "nowhere"));

        SavingMark.IsHeldIn(nowhere).ShouldBeFalse();
        Should.Throw<AudioCaptureException>(() => SavingMark.Take(nowhere))
            .Message.ShouldContain(nowhere.FullName);
    }

    /// <summary>
    /// ISC-126.1's mechanism. While a save holds the folder, anything looking at that folder is
    /// told so, and a second save of the same recording is refused before it has read a block.
    /// </summary>
    [Fact]
    public void A_save_that_is_running_holds_the_folder_and_refuses_a_second_one()
    {
        var folder = root.CreateSubdirectory("daily");

        using (SavingMark.Take(folder))
        {
            SavingMark.IsHeldIn(folder).ShouldBeTrue();

            var refused = Should.Throw<AudioCaptureException>(() => SavingMark.Take(folder));
            refused.Message.ShouldContain("already running");
            refused.Message.ShouldContain(folder.FullName);
        }

        // Let go of, and the folder is anybody's again — including the next save's.
        SavingMark.IsHeldIn(folder).ShouldBeFalse();
        SavingMark.Take(folder).Dispose();
    }

    /// <summary>
    /// Asking whether a save is running is a question and never a claim: two of them arriving
    /// together answer the same, and one of them does not cost a save that starts beside it.
    /// </summary>
    /// <remarks>
    /// A listing asks this once per waiting folder, so a question that held the folder to itself
    /// would answer "its save is running" over a recording nobody is saving whenever two lists were
    /// built at once — the stranding this whole type exists to make impossible, arriving through
    /// the reader. The same handle would refuse the claim, which is a person's stop failing because
    /// somebody was looking at a list. Both are held here against handles taken the way each is
    /// really taken.
    /// </remarks>
    [Fact]
    public void Asking_whether_a_save_is_running_neither_answers_itself_nor_costs_a_save()
    {
        var folder = root.CreateSubdirectory("daily");
        SavingMark.Take(folder).Dispose();

        // A second question, in the mode the question itself opens the mark in. It must not read
        // as a save: a listing asks this once per folder, so two lists built at once would
        // otherwise each report the other as a save over a recording nobody is saving.
        using (Mark(folder).Open(FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            SavingMark.IsHeldIn(folder).ShouldBeFalse();
        }

        // A scanner or a backup passing over the file shares more than a question does, and is
        // neither a save nor anything a save has to wait for.
        using (Mark(folder).Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            SavingMark.IsHeldIn(folder).ShouldBeFalse();
            SavingMark.Take(folder).Dispose();
        }

        // And a question that is open when a save starts is waited out rather than refused. It is
        // the one handle a claim really does collide with — asking is one open of the file — and
        // the person on the other end pressed stop, so the answer cannot be that their save is
        // already running.
        using var asked = new ManualResetEventSlim(initialState: false);
        var asking = new Thread(() =>
        {
            using var question = Mark(folder).Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            asked.Set();
            Thread.Sleep(250);
        });

        asking.Start();
        asked.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the question was never asked.");

        SavingMark.Take(folder).Dispose();
        asking.Join();
    }

    /// <summary>
    /// ISC-126.2, without a process to kill. A mark left lying by a save that ended badly is a file
    /// nothing is holding, and a file nothing is holding says no save is running.
    /// </summary>
    /// <remarks>
    /// This is the test that goes red over a mark whose meaning is its existence: the file is
    /// really there, asserted before the question is asked, and the answer is still no. Nothing
    /// clears it — no start, no listing, no read — because there is nothing to clear.
    /// </remarks>
    [Fact]
    public void A_mark_a_save_left_behind_holds_the_recording_for_nobody()
    {
        var folder = root.CreateSubdirectory("daily");
        SavingMark.Take(folder).Dispose();

        var mark = Mark(folder);
        mark.Exists.ShouldBeTrue("a save that ended leaves its mark on disk, and this test is about that file");

        SavingMark.IsHeldIn(folder).ShouldBeFalse();

        // Asked twice with nothing in between, because a reader that cleared the mark on its way
        // past would answer the first one honestly and the second one for the wrong reason.
        SavingMark.IsHeldIn(folder).ShouldBeFalse();
        mark.Refresh();
        mark.Exists.ShouldBeTrue();
    }

    /// <summary>
    /// ISC-126.2. A save the process died in the middle of leaves its mark stranded on disk, and
    /// the recording is one anybody may decide about again the moment that process is gone.
    /// </summary>
    /// <remarks>
    /// A second process and a real kill, because that is the failure the claim names and nothing
    /// inside one process reaches it: a handle let go of on the way out is a save ending, not a
    /// save dying. What the other process does is hold the mark the way a save holds it — writing,
    /// letting nothing else write — and what is being read is Windows closing a dead process's
    /// handles, which is the only thing that lifts this mark and the reason the mark is a handle
    /// rather than a record. The kill runs in a <c>finally</c>: a probe that failed early and left
    /// a process asleep on somebody's machine would be worse than the defect. How that process is
    /// made to take the mark, and why asking it to every 25 ms was what kept it from taking it at
    /// all, is on <see cref="Holding"/>.
    /// </remarks>
    [Fact]
    public void A_save_the_process_died_in_the_middle_of_leaves_the_recording_decidable_again()
    {
        var folder = root.CreateSubdirectory("daily");
        SavingMark.Take(folder).Dispose();
        var mark = Mark(folder);

        using var holder = Holding(mark);
        try
        {
            HasTakenTheMark(holder);
            SavingMark.IsHeldIn(folder).ShouldBeTrue();

            // And nothing in this process may start a save over it while that one has it.
            Should.Throw<AudioCaptureException>(() => SavingMark.Take(folder));
        }
        finally
        {
            try
            {
                if (!holder.HasExited)
                {
                    holder.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ending) when (
                ending is InvalidOperationException or Win32Exception or AggregateException)
            {
                // Every way a kill can refuse: it ended between the question and the kill, which is
                // what the kill was for; Windows would not have it; a child of it would not go. A
                // throw from a finally would bury the sentence saying what really failed, and what
                // is left behind either way ends itself when its own sleep runs out.
            }

            holder.WaitForExit();
        }

        // The mark is still there — nobody cleared it and nothing will — and it holds nothing.
        // Asked straight after WaitForExit rather than waited for: Windows tears a process's handle
        // table down during rundown, before the process object is signalled, so there is nothing
        // left to wait on and a clock here would re-import the defect this probe just had removed.
        mark.Refresh();
        mark.Exists.ShouldBeTrue();

        // Said in full because the answer is also the answer a broken SavingMark gives, and this is
        // the probe whose whole complaint was a red run nobody could read.
        SavingMark.IsHeldIn(folder).ShouldBeFalse(
            "the mark still reads as held after the only process holding it is gone. Either the "
            + "mark means its existence rather than a handle, which is what this test is here to "
            + "refuse, or something else on this machine had the file open for the one instant "
            + "this was asked — IsHeldIn answers 'held' to any IOException, deliberately.");

        // Which is what makes the recording somebody's again: the next save takes the folder.
        SavingMark.Take(folder).Dispose();
    }

    public void Dispose()
    {
        try
        {
            root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    /// <summary>The mark's file, which this type deliberately does not hand anybody.</summary>
    private static FileInfo Mark(DirectoryInfo folder) =>
        new(Path.Combine(folder.FullName, SavingMark.FileName));

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
    private static readonly string Script = $$"""
        $ErrorActionPreference = 'Stop'
        $mark = $env:{{MarkVariable}}
        $waiting = [Diagnostics.Stopwatch]::StartNew()
        $held = $null
        while ($null -eq $held) {
            try {
                $held = [System.IO.File]::Open($mark, 'Open', 'Write', 'Read')
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

    /// <summary>
    /// Another process holding the mark the way a save holds it, and doing nothing else. It never
    /// closes the handle itself: the only way out of it is being killed, which is the point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handover is a line on stdout, and it may not be a clock, because the clock is what this
    /// used to get wrong. It used to be a poll: this process asking
    /// <see cref="SavingMark.IsHeldIn"/> every 25 ms until the answer came back true. That question
    /// opens the mark <c>FileAccess.Read</c>, <c>FileShare.Read</c>, and <see cref="Script"/>'s
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
    /// <see cref="A_save_that_is_running_holds_the_folder_and_refuses_a_second_one"/>, which is
    /// what goes red for that.
    /// </para>
    /// </remarks>
    private static Process Holding(FileInfo mark)
    {
        var start = new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)))
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
    private static void HasTakenTheMark(Process holder)
    {
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
}
