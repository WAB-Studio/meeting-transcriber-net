namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The mark a read holds over the folder it is reading through: what it says while somebody is
/// reading, what it says once whatever was reading is gone, and how it differs from the two marks
/// beside it.
/// </summary>
/// <remarks>
/// Two bets are probed here rather than one. The first is <see cref="SavingMark"/>'s and is
/// inherited: a mark nothing is holding says nothing, so a read that died strands no recording. The
/// second is this mark's own — it is shared, so a second reader is admitted rather than refused,
/// and one command whose read is two passes may claim it twice without waiting for itself.
/// </remarks>
public sealed class ReadingMarkTests : IDisposable
{
    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public ReadingMarkTests() => root.Create();

    /// <summary>A folder nobody has read has nothing to say about a read.</summary>
    [Fact]
    public void A_folder_nobody_is_reading_is_not_being_read()
    {
        var folder = root.CreateSubdirectory("daily");

        ReadingMark.IsHeldIn(folder).ShouldBeFalse();
        Mark(folder).Exists.ShouldBeFalse();

        // And a folder that is not there at all is not a read either, which is what a listing asks
        // of a corpus that has never recorded anything. Claiming one is refused by naming it.
        var nowhere = new DirectoryInfo(Path.Combine(root.FullName, "nowhere"));

        ReadingMark.IsHeldIn(nowhere).ShouldBeFalse();
        Should.Throw<AudioCaptureException>(() => ReadingMark.Take(nowhere))
            .Message.ShouldContain(nowhere.FullName);
    }

    /// <summary>
    /// The whole of what makes this a second mark rather than a wider first one: it is shared. A
    /// read that is under way says so, and a second reader takes it beside the first rather than
    /// waiting two seconds and being refused.
    /// </summary>
    /// <remarks>
    /// Written as a nest because that is the shape the prompt really has: <c>recover --keep</c>
    /// holds one across both of its passes and <c>Keep</c> claims it again inside that. An
    /// exclusive mark here would have the command refuse itself.
    /// </remarks>
    [Fact]
    public void A_read_that_is_under_way_says_so_and_does_not_refuse_a_second_reader()
    {
        var folder = root.CreateSubdirectory("daily");

        using (ReadingMark.Take(folder))
        {
            ReadingMark.IsHeldIn(folder).ShouldBeTrue();

            using (ReadingMark.Take(folder))
            {
                ReadingMark.IsHeldIn(folder).ShouldBeTrue();
            }

            // The inner reader is gone and the outer one still has it, which is the half a shared
            // claim could get wrong in the other direction.
            ReadingMark.IsHeldIn(folder).ShouldBeTrue();
        }

        ReadingMark.IsHeldIn(folder).ShouldBeFalse();
        ReadingMark.Take(folder).Dispose();
    }

    /// <summary>
    /// The bet <see cref="SavingMark"/> made, arriving through this mark. A mark left lying by a
    /// read that ended is a file nothing is holding, and a file nothing is holding says no read.
    /// </summary>
    [Fact]
    public void A_mark_a_read_left_behind_holds_the_recording_for_nobody()
    {
        var folder = root.CreateSubdirectory("daily");
        ReadingMark.Take(folder).Dispose();

        var mark = Mark(folder);
        mark.Exists.ShouldBeTrue("a read that ended leaves its mark on disk, and this test is about that file");

        // `docs/corpus.md` calls all three marks empty, which is what says a backup carrying one
        // carries nothing and what makes the sweep's delete of a stranded one safe.
        mark.Length.ShouldBe(0);

        ReadingMark.IsHeldIn(folder).ShouldBeFalse();

        // Asked twice with nothing in between, because a reader that cleared the mark on its way
        // past would answer the first one honestly and the second one for the wrong reason.
        ReadingMark.IsHeldIn(folder).ShouldBeFalse();
        mark.Refresh();
        mark.Exists.ShouldBeTrue();
    }

    /// <summary>
    /// Asking whether a read is under way is a question and never a claim, and the obvious wrong
    /// way to detect a shared holder is what this refuses.
    /// </summary>
    /// <remarks>
    /// A shared claim is not detected by asking with <c>FileShare.None</c>: that reads every
    /// scanner and every backup on the machine as a reader, and every one of them would then refuse
    /// somebody's discard. What detects it is that both flavours of claim hold
    /// <c>FileAccess.Write</c>, which the question's own <c>FileShare.Read</c> does not permit.
    /// </remarks>
    [Fact]
    public void Asking_whether_a_read_is_under_way_neither_answers_itself_nor_costs_a_read()
    {
        var folder = root.CreateSubdirectory("daily");
        ReadingMark.Take(folder).Dispose();

        // A second question, in the mode the question itself opens the mark in.
        using (Mark(folder).Open(FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ReadingMark.IsHeldIn(folder).ShouldBeFalse();
        }

        // A scanner or a backup passing over the file shares more than a question does, and is
        // neither a read nor anything a read has to wait for.
        using (Mark(folder).Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            ReadingMark.IsHeldIn(folder).ShouldBeFalse();
            ReadingMark.Take(folder).Dispose();
        }
    }

    /// <summary>
    /// Three marks over one folder, each answering only for itself. A read is not a save and not a
    /// capture, and the file names are what keeps them apart on disk.
    /// </summary>
    [Fact]
    public void A_read_a_save_and_a_capture_are_three_marks_and_none_answers_for_another()
    {
        var folder = root.CreateSubdirectory("daily");

        using (ReadingMark.Take(folder))
        {
            ReadingMark.IsHeldIn(folder).ShouldBeTrue();
            SavingMark.IsHeldIn(folder).ShouldBeFalse();
            CaptureMark.IsHeldIn(folder).ShouldBeFalse();

            using (SavingMark.Take(folder))
            {
                using (CaptureMark.Take(folder))
                {
                    ReadingMark.IsHeldIn(folder).ShouldBeTrue();
                    SavingMark.IsHeldIn(folder).ShouldBeTrue();
                    CaptureMark.IsHeldIn(folder).ShouldBeTrue();
                }

                CaptureMark.IsHeldIn(folder).ShouldBeFalse();
                SavingMark.IsHeldIn(folder).ShouldBeTrue();
                ReadingMark.IsHeldIn(folder).ShouldBeTrue();
            }

            SavingMark.IsHeldIn(folder).ShouldBeFalse();
            ReadingMark.IsHeldIn(folder).ShouldBeTrue();
        }

        ReadingMark.IsHeldIn(folder).ShouldBeFalse();

        folder.GetFiles().Select(file => file.Name).ShouldBe(
            [CaptureMark.FileName, ReadingMark.FileName, SavingMark.FileName], ignoreOrder: true);
    }

    /// <summary>
    /// A folder that will not take the mark is a read that goes on unmarked, and never a read that
    /// failed. The mark is what a read leaves where it can — a recording whose every block is
    /// intact must not become one the application says it cannot read.
    /// </summary>
    /// <remarks>
    /// The folder is made unwritable at exactly the one name that matters, by putting a directory
    /// where the mark's file goes: deterministic, no permissions to set and put back, and what it
    /// stands in for is every real way this happens — no room on the disk, an access this process
    /// does not have. What is asserted afterwards is that the claim is honest about holding
    /// nothing, because a mark that answered "held" over a handle it never got would refuse
    /// somebody's discard forever.
    /// </remarks>
    [Fact]
    public void A_folder_that_will_not_take_the_mark_is_read_all_the_same()
    {
        var folder = root.CreateSubdirectory("daily");
        Directory.CreateDirectory(Path.Combine(folder.FullName, ReadingMark.FileName));

        using (ReadingMark.Take(folder))
        {
            ReadingMark.IsHeldIn(folder).ShouldBeFalse();
        }

        // And it says so again the next time, rather than having remembered anything.
        ReadingMark.Take(folder).Dispose();
        ReadingMark.IsHeldIn(folder).ShouldBeFalse();
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
        new(Path.Combine(folder.FullName, ReadingMark.FileName));
}
