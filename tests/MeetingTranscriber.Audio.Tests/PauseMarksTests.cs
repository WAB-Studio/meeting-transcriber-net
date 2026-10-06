using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The file a pause is written to and the stretches read back off it, which is what the meeting is
/// cut by.
/// </summary>
public sealed class PauseMarksTests : IDisposable
{
    private static readonly UtcTimestamp Noon = UtcTimestamp.Parse("2026-10-05T12:00:00.000Z");

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public PauseMarksTests() => folder.Create();

    [Fact]
    public void Stretches_come_back_in_the_order_they_were_written()
    {
        Write(PauseMark.Paused, 100);
        Write(PauseMark.Resumed, 200);
        Write(PauseMark.Paused, 300);
        Write(PauseMark.Resumed, 450);

        RecordingPauses.Stretches(folder).ShouldBe([(100L, (long?)200), (300L, (long?)450)]);
    }

    [Fact]
    public void A_pause_nobody_resumed_has_no_end()
    {
        Write(PauseMark.Paused, 100);

        RecordingPauses.Stretches(folder).ShouldBe([(100L, (long?)null)]);
    }

    [Fact]
    public void No_file_is_no_stretch() => RecordingPauses.Stretches(folder).ShouldBeEmpty();

    [Fact]
    public void A_resume_with_no_pause_is_ignored()
    {
        Write(PauseMark.Resumed, 50);
        Write(PauseMark.Paused, 100);
        Write(PauseMark.Resumed, 200);

        RecordingPauses.Stretches(folder).ShouldBe([(100L, (long?)200)]);
    }

    /// <summary>What a machine dying mid-write leaves: the last line, unfinished, is dropped.</summary>
    [Fact]
    public void A_torn_last_line_is_dropped_and_the_ones_before_it_stand()
    {
        Write(PauseMark.Paused, 100);
        Write(PauseMark.Resumed, 200);
        File.AppendAllText(RecordingPauses.In(folder).FullName, "{\"kind\":\"paused\",\"at\":3");

        RecordingPauses.Stretches(folder).ShouldBe([(100L, (long?)200)]);
    }

    /// <summary>A whole line that will not read is a file that stopped being what it says, and is not "nothing was paused".</summary>
    [Fact]
    public void A_whole_line_that_does_not_read_is_refused()
    {
        File.WriteAllText(RecordingPauses.In(folder).FullName, "not json" + Environment.NewLine);

        Should.Throw<AudioCaptureException>(() => RecordingPauses.Stretches(folder));
    }

    /// <summary>An append lands on a line of its own even behind the tail a failed one left.</summary>
    [Fact]
    public void An_append_behind_a_torn_tail_begins_a_line_of_its_own()
    {
        Write(PauseMark.Paused, 100);
        File.AppendAllText(RecordingPauses.In(folder).FullName, "{\"kind\":\"res");
        Write(PauseMark.Resumed, 200);

        RecordingPauses.Stretches(folder).ShouldBe([(100L, (long?)200)]);
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private void Write(PauseMark kind, long at) =>
        RecordingPauses.Append(folder, new PauseLine(kind, at, Noon));
}
