using MeetingTranscriber.Domain.Audio;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a recording is worth after the machine stopped in the middle of it.
/// </summary>
/// <remarks>
/// These are the only tests in this project that touch a file, and they have to: what is being
/// probed is the shape of one on disk after a write was cut, which is not a thing a stream in
/// memory can be. No device is opened — the packets are fabricated, the same way the timeline's
/// are — and every folder is made and removed by the test that used it.
/// </remarks>
public sealed class BlockSpoolTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public BlockSpoolTests() => folder.Create();

    [Fact]
    public void Every_block_comes_back_as_the_device_reported_it()
    {
        var written = Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);

        using var reader = SpoolReader.Open(File(AudioChannel.Loopback));
        var read = reader.Packets().ToList();

        reader.Channel.ShouldBe(AudioChannel.Loopback);
        reader.Format.ShouldBe(StereoFloat);
        reader.Discarded.ShouldBe(0);
        read.Count.ShouldBe(written.Count);
        for (var index = 0; index < written.Count; index++)
        {
            read[index].Channel.ShouldBe(written[index].Channel);
            read[index].DevicePosition.ShouldBe(written[index].DevicePosition);
            read[index].CapturedAt.ShouldBe(written[index].CapturedAt);
            read[index].TimingIsSound.ShouldBe(written[index].TimingIsSound);
            read[index].Samples.ToArray().ShouldBe(written[index].Samples.ToArray());
        }
    }

    /// <summary>
    /// ISC-75. The file is cut inside the last block, which is what killing a process mid write
    /// leaves, and everything before it is still the recording.
    /// </summary>
    [Fact]
    public void A_recording_cut_off_mid_block_comes_back_to_its_last_whole_block()
    {
        var written = Spooled(AudioChannel.Microphone, CheapMicrophone, seconds: 1);
        var whole = File(AudioChannel.Microphone).Length;

        Truncate(AudioChannel.Microphone, whole - BytesOf(written[^1]) + 16);

        using var reader = SpoolReader.Open(File(AudioChannel.Microphone));
        var read = reader.Packets().ToList();

        read.Count.ShouldBe(written.Count - 1);
        read[^1].DevicePosition.ShouldBe(written[^2].DevicePosition);
        read[^1].Samples.ToArray().ShouldBe(written[^2].Samples.ToArray());
        reader.Discarded.ShouldBe(16);
    }

    /// <summary>
    /// The other side of the same cut: a block whose header landed and whose samples did not is
    /// not a shorter block, because reading it as one would put audio nobody recorded on the
    /// timeline.
    /// </summary>
    [Fact]
    public void A_block_whose_samples_never_landed_is_dropped_rather_than_read_short()
    {
        var written = Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        var whole = File(AudioChannel.Loopback).Length;

        Truncate(AudioChannel.Loopback, whole - BytesOf(written[^1]) + BlockSpool.BlockHeaderBytes);

        using var reader = SpoolReader.Open(File(AudioChannel.Loopback));
        reader.Packets().Count().ShouldBe(written.Count - 1);
    }

    /// <summary>
    /// What a power cut leaves once the file system has already recorded the length: a tail that
    /// frames perfectly and holds nothing. A run of empty blocks is the reading this has to refuse.
    /// </summary>
    [Fact]
    public void A_tail_of_zeroes_is_not_a_run_of_blocks()
    {
        var written = Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        Append(AudioChannel.Loopback, new byte[4096]);

        using var reader = SpoolReader.Open(File(AudioChannel.Loopback));

        reader.Packets().Count().ShouldBe(written.Count);
        reader.Discarded.ShouldBe(4096);
    }

    /// <summary>
    /// A block framed the way it was written whose samples are not the ones that were written. Only
    /// the checksum can tell this from a whole block, which is why there is one.
    /// </summary>
    [Fact]
    public void A_block_the_disk_did_not_keep_is_dropped_and_the_ones_before_it_are_not()
    {
        var written = Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        var whole = File(AudioChannel.Loopback).Length;

        Corrupt(AudioChannel.Loopback, whole - BytesOf(written[^1]) + BlockSpool.BlockHeaderBytes + 4);

        using var reader = SpoolReader.Open(File(AudioChannel.Loopback));
        var read = reader.Packets().ToList();

        read.Count.ShouldBe(written.Count - 1);
        read[^1].DevicePosition.ShouldBe(written[^2].DevicePosition);
        reader.Discarded.ShouldBe(BytesOf(written[^1]));
    }

    /// <summary>
    /// The reason a spool is read straight back into the timeline: what the recording turns out to
    /// be cannot depend on whether it went through a file first.
    /// </summary>
    [Fact]
    public void A_recording_read_back_from_its_spools_is_the_one_that_was_written()
    {
        var loopback = Fabricated.Packets(
            AudioChannel.Loopback, StereoFloat, 48_000, 0, 3, Fabricated.Bursts(0.5)).ToList();
        var microphone = Fabricated.Packets(
            AudioChannel.Microphone, CheapMicrophone, 44_100, 0.04, 3, Fabricated.Bursts(0.5)).ToList();

        var live = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, CheapMicrophone, live);
        foreach (var packet in Fabricated.Merged(loopback, microphone))
        {
            timeline.Take(packet);
        }

        var expected = timeline.Close();

        Write(AudioChannel.Loopback, StereoFloat, loopback);
        Write(AudioChannel.Microphone, CheapMicrophone, microphone);

        using var others = SpoolReader.Open(File(AudioChannel.Loopback));
        using var mine = SpoolReader.Open(File(AudioChannel.Microphone));
        var recovered = new Collected();
        var rebuilt = SharedTimeline.Of(others.Format, mine.Format, recovered);
        foreach (var packet in Fabricated.Merged(others.Packets(), mine.Packets()))
        {
            rebuilt.Take(packet);
        }

        rebuilt.Close().Length.ShouldBe(expected.Length);
        recovered.Frames.ShouldBe(live.Frames);
        recovered.FirstDifferenceFrom(live).ShouldBeNull();
    }

    [Fact]
    public void A_spool_pours_back_into_a_file_of_the_format_its_device_handed_over()
    {
        var written = Spooled(AudioChannel.Microphone, CheapMicrophone, seconds: 1);

        var replayed = BlockSpool.ToWav(File(AudioChannel.Microphone));

        replayed.Format.ShouldBe(CheapMicrophone);
        replayed.Blocks.ShouldBe(written.Count);
        replayed.Discarded.ShouldBe(0);

        var wav = BlockSpool.PlaybackFor(File(AudioChannel.Microphone));
        wav.Name.ShouldBe("microphone.wav");
        using var played = new WaveFileReader(wav.FullName);
        StreamFormat.Of(played.WaveFormat).ShouldBe(CheapMicrophone);
        played.Length.ShouldBe(written.Sum(packet => (long)packet.Samples.Length));
    }

    [Fact]
    public void A_file_that_is_not_a_spool_is_refused_rather_than_read_as_audio()
    {
        var stranger = new FileInfo(Path.Combine(folder.FullName, "loopback.blocks"));
        System.IO.File.WriteAllBytes(stranger.FullName, new byte[BlockSpool.HeaderBytes * 2]);

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(stranger))
            .Message.ShouldContain("not a spool");
    }

    [Fact]
    public void A_spool_this_build_did_not_write_is_refused_rather_than_read_field_by_field()
    {
        Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        Corrupt(AudioChannel.Loopback, 8);

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(File(AudioChannel.Loopback)))
            .Message.ShouldContain("version");
    }

    /// <summary>
    /// The header decides how every sample in the file is read, so it is checked the way a block
    /// is. A rate one digit out would otherwise open, and come back as a recording.
    /// </summary>
    [Fact]
    public void A_spool_whose_header_is_not_the_one_that_was_written_is_refused()
    {
        Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        Corrupt(AudioChannel.Loopback, 16);

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(File(AudioChannel.Loopback)))
            .Message.ShouldContain("does not hash to what its header says");
    }

    /// <summary>
    /// The two the header's own checksum cannot catch, because they are what the writer would have
    /// had to be handed: a format no block of which could be read.
    /// </summary>
    [Theory]
    [InlineData(0, 2, 32, "0 Hz")]
    [InlineData(48_000, 0, 32, "0 channels")]
    [InlineData(48_000, 2, 24, "cannot read")]
    public void A_spool_of_a_format_no_block_of_which_could_be_read_is_refused(
        int rate, int channels, int bits, string says)
    {
        var file = File(AudioChannel.Loopback);
        using (SpoolWriter.Create(file, AudioChannel.Loopback, new StreamFormat(
            rate, channels, bits, SampleEncoding.IeeeFloat)))
        {
        }

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(file)).Message.ShouldContain(says);
    }

    /// <summary>
    /// Half a frame of audio would shift every sample after it onto the other channel, so it is
    /// refused where it can still be a defect rather than a recording.
    /// </summary>
    [Fact]
    public void A_packet_that_is_not_whole_frames_of_its_format_is_refused_by_a_spool()
    {
        using var writer = SpoolWriter.Create(File(AudioChannel.Loopback), AudioChannel.Loopback, StereoFloat);
        var half = new CapturePacket(
            AudioChannel.Loopback, 0, MonotonicInstant.FromMilliseconds(0), new byte[30]);

        Should.Throw<AudioContractException>(() => writer.Write(half));
    }

    /// <summary>
    /// A recording still being written is not something to answer about: what came back would be
    /// however far the writer had got, reported as the meeting having ended there.
    /// </summary>
    [Fact]
    public void A_spool_still_being_recorded_into_is_not_read()
    {
        using var writer = SpoolWriter.Create(File(AudioChannel.Loopback), AudioChannel.Loopback, StereoFloat);

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(File(AudioChannel.Loopback)))
            .Message.ShouldContain("still running");
    }

    [Fact]
    public void A_spool_cut_off_before_it_said_what_it_was_holds_no_recording()
    {
        Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);
        Truncate(AudioChannel.Loopback, BlockSpool.HeaderBytes - 1);

        Should.Throw<AudioCaptureException>(() => SpoolReader.Open(File(AudioChannel.Loopback)))
            .Message.ShouldContain("no source");
    }

    [Fact]
    public void A_spool_that_recorded_nothing_is_a_recording_of_nothing_rather_than_a_refusal()
    {
        using (SpoolWriter.Create(File(AudioChannel.Loopback), AudioChannel.Loopback, StereoFloat))
        {
        }

        using var reader = SpoolReader.Open(File(AudioChannel.Loopback));

        reader.Packets().ShouldBeEmpty();
        reader.Discarded.ShouldBe(0);
    }

    [Fact]
    public void A_recording_is_never_written_over_another_one()
    {
        Spooled(AudioChannel.Loopback, StereoFloat, seconds: 1);

        Should.Throw<AudioCaptureException>(
            () => SpoolWriter.Create(File(AudioChannel.Loopback), AudioChannel.Loopback, StereoFloat));
    }

    /// <summary>
    /// A source written to the other one's file would come back as that source, and every word of
    /// the meeting would be attributed to the wrong side of it.
    /// </summary>
    [Fact]
    public void A_packet_of_the_other_source_is_refused_by_a_spool()
    {
        using var writer = SpoolWriter.Create(File(AudioChannel.Loopback), AudioChannel.Loopback, StereoFloat);
        var stranger = Fabricated
            .Packets(AudioChannel.Microphone, StereoFloat, 48_000, 0, 0.1, Fabricated.Quiet)
            .First();

        Should.Throw<AudioContractException>(() => writer.Write(stranger));
    }

    [Fact]
    public void The_two_sources_of_one_recording_are_spooled_under_names_that_say_which_is_which()
    {
        BlockSpool.FileFor(folder, AudioChannel.Loopback).Name.ShouldBe("loopback.blocks");
        BlockSpool.FileFor(folder, AudioChannel.Microphone).Name.ShouldBe("microphone.blocks");
        Should.Throw<AudioContractException>(() => BlockSpool.FileFor(folder, (AudioChannel)7));
    }

    /// <summary>
    /// The playback is replaced every time it is produced, which is right for a file made again
    /// from the spool beside it and wrong for one a previous recording left. What keeps the second
    /// case from arising is that a recording will not start in a folder holding either file.
    /// </summary>
    [Theory]
    [InlineData("loopback.blocks")]
    [InlineData("loopback.wav")]
    [InlineData("microphone.blocks")]
    [InlineData("microphone.wav")]
    [InlineData("audio.wav")]
    public void A_recording_does_not_start_in_a_folder_holding_another_ones_files(string left)
    {
        System.IO.File.WriteAllBytes(Path.Combine(folder.FullName, left), [1, 2, 3]);

        Should.Throw<AudioCaptureException>(() => BlockSpool.EnsureNothingRecordedIn(folder))
            .Message.ShouldContain(left);
    }

    [Fact]
    public void A_folder_nothing_was_recorded_into_takes_a_recording()
    {
        Should.NotThrow(() => BlockSpool.EnsureNothingRecordedIn(folder));
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

    /// <summary>What one block occupies on disk, which is what a cut has to be measured back from.</summary>
    private static long BytesOf(CapturePacket packet) =>
        BlockSpool.BlockHeaderBytes + packet.Samples.Length + BlockSpool.ChecksumBytes;

    private FileInfo File(AudioChannel channel) => BlockSpool.FileFor(folder, channel);

    private List<CapturePacket> Spooled(AudioChannel channel, StreamFormat format, double seconds)
    {
        var packets = Fabricated
            .Packets(channel, format, format.SampleRate, 0, seconds, Fabricated.Bursts(0.25))
            .ToList();

        Write(channel, format, packets);
        return packets;
    }

    private void Write(AudioChannel channel, StreamFormat format, IEnumerable<CapturePacket> packets)
    {
        using var writer = SpoolWriter.Create(File(channel), channel, format);
        foreach (var packet in packets)
        {
            writer.Write(packet);
        }
    }

    private void Truncate(AudioChannel channel, long to)
    {
        using var stream = File(channel).Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(to);
    }

    private void Append(AudioChannel channel, byte[] tail)
    {
        using var stream = File(channel).Open(FileMode.Append, FileAccess.Write);
        stream.Write(tail);
    }

    /// <summary>Changes one byte, the way a disk that did not keep what it was given would.</summary>
    private void Corrupt(AudioChannel channel, long at)
    {
        using var stream = File(channel).Open(FileMode.Open, FileAccess.ReadWrite);
        stream.Position = at;
        var was = stream.ReadByte();
        stream.Position = at;
        stream.WriteByte((byte)(was ^ 0xFF));
    }
}
