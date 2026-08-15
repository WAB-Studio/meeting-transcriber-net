using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// What <c>spool</c> makes of a folder of blocks, which is the only thing a recording nobody got to
/// stop leaves behind.
/// </summary>
/// <remarks>
/// This one does record, in the sense that matters here: no device is opened, but the blocks are
/// written the way a capture writes them and one of them is cut off the way a killed process cuts
/// one off. That is the whole path a person's recovery goes through, and it is deterministic —
/// unlike the run on real hardware, which is a probe somebody watches, recorded in the ISA.
/// </remarks>
public sealed class SpoolCommandTests : IDisposable
{
    private static readonly StreamFormat Format = new(48_000, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public SpoolCommandTests() => folder.Create();

    [Fact]
    public void A_recording_that_was_cut_off_reads_back_to_its_last_whole_block()
    {
        Spool(AudioChannel.Loopback, blocks: 10);
        Spool(AudioChannel.Microphone, blocks: 10);
        CutOffMidBlock(AudioChannel.Microphone);

        var run = CommandLine.Of("spool", "--in", folder.FullName);

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch0 recovered").ShouldStartWith("loopback.wav, 10 blocks");
        run.Value("ch0 recovered").ShouldNotContain("discarded");
        run.Value("ch1 recovered").ShouldStartWith("microphone.wav, 9 blocks");
        run.Value("ch1 recovered").ShouldContain("discarded");
        run.Value("ch1 format").ShouldBe(Format.ToString());

        new FileInfo(Path.Combine(folder.FullName, "loopback.wav")).Exists.ShouldBeTrue();
        new FileInfo(Path.Combine(folder.FullName, "microphone.wav")).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A recording with one source is still a recording somebody wants. What the command must not
    /// do is decide anything about it — the blocks are still there afterwards.
    /// </summary>
    [Fact]
    public void One_source_on_its_own_is_read_back_and_left_where_it_is()
    {
        Spool(AudioChannel.Loopback, blocks: 3);

        var run = CommandLine.Of("spool", "--in", folder.FullName);

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch0 recovered").ShouldStartWith("loopback.wav, 3 blocks");
        run.Value("ch1").ShouldBe("no microphone.blocks");
        BlockSpool.FileFor(folder, AudioChannel.Loopback).Exists.ShouldBeTrue();
    }

    [Fact]
    public void A_folder_holding_no_spool_is_refused()
    {
        var run = CommandLine.Of("spool", "--in", folder.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("no spool");
    }

    [Fact]
    public void A_folder_that_is_not_there_is_refused()
    {
        var run = CommandLine.Of("spool", "--in", Path.Combine(folder.FullName, "nowhere"));

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("nowhere");
    }

    [Fact]
    public void Reading_a_spool_without_saying_which_is_a_misuse()
    {
        var run = CommandLine.Of("spool");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--in");
    }

    [Fact]
    public void A_flag_this_command_does_not_read_is_a_misuse()
    {
        var run = CommandLine.Of("spool", "--in", folder.FullName, "--corpus", ".");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--corpus");
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private void Spool(AudioChannel channel, int blocks)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, Format);
        for (var block = 0; block < blocks; block++)
        {
            writer.Write(new CapturePacket(
                channel,
                block * 480L,
                MonotonicInstant.FromMilliseconds(block * 10d),
                new byte[480 * Format.BytesPerSample]));
        }
    }

    /// <summary>Takes the tail off the way a process being killed mid write takes it off.</summary>
    private void CutOffMidBlock(AudioChannel channel)
    {
        var file = BlockSpool.FileFor(folder, channel);
        using var stream = file.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(file.Length - 32);
    }
}
