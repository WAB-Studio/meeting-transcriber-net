using System.Diagnostics;

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
    /// a process asleep on somebody's machine would be worse than the defect.
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
            Until(() => SavingMark.IsHeldIn(folder), "the other process to take the mark");
            SavingMark.IsHeldIn(folder).ShouldBeTrue();

            // And nothing in this process may start a save over it while that one has it.
            Should.Throw<AudioCaptureException>(() => SavingMark.Take(folder));
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            holder.WaitForExit();
        }

        Until(() => !SavingMark.IsHeldIn(folder), "the dead process's handle to be closed");

        // The mark is still there — nobody cleared it and nothing will — and it holds nothing.
        mark.Refresh();
        mark.Exists.ShouldBeTrue();
        SavingMark.IsHeldIn(folder).ShouldBeFalse();

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

    /// <summary>
    /// Another process holding the mark the way a save holds it, and doing nothing else. It never
    /// closes the handle itself: the only way out of it is being killed, which is the point.
    /// </summary>
    /// <remarks>
    /// The path is quoted for PowerShell by doubling the one character that would end the literal,
    /// because a temp folder sits under a user name somebody may have an apostrophe in.
    /// </remarks>
    private static Process Holding(FileInfo mark) => Process.Start(new ProcessStartInfo(
        "powershell.exe",
        "-NoProfile -NonInteractive -Command "
        + $"\"$held = [System.IO.File]::Open('{mark.FullName.Replace("'", "''", StringComparison.Ordinal)}', "
        + "'Open', 'Write', 'Read'); Start-Sleep -Seconds 300\"")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    })!;

    /// <summary>
    /// Waits for something another process is doing, and fails saying what never happened rather
    /// than asserting into a race.
    /// </summary>
    private static void Until(Func<bool> settled, string what)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(30);

        while (!settled() && DateTime.UtcNow < giveUpAt)
        {
            Thread.Sleep(25);
        }

        settled().ShouldBeTrue($"waited 30 s for {what} and it never did.");
    }
}
