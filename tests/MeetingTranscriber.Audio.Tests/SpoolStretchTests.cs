using MeetingTranscriber.Domain.Audio;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A spool of a source that changed device: the record saying where one device's audio ends and the
/// next one's begins, and what a file holding two of them can and cannot be poured into.
/// </summary>
/// <remarks>
/// Beside <see cref="BlockSpoolTests"/> rather than in it, and for the reason that one is a file at
/// all: what is probed here is bytes on a disk after a device was taken away mid meeting, which is
/// the one thing a stream in memory cannot be. No device is opened.
/// </remarks>
public sealed class SpoolStretchTests : IDisposable
{
    private static readonly StreamFormat MonoFloat = new(48_000, 1, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public SpoolStretchTests() => folder.Create();

    /// <summary>
    /// ISC-78. The seam is written down where it happened, and it comes back on the first block the
    /// device that took over really handed over — so a recording rebuilt from the folder is the
    /// same two stretches as the one that was being written.
    /// </summary>
    [Fact]
    public void A_spool_says_where_one_device_ended_and_the_next_one_began()
    {
        var first = Packets(MonoFloat, 0, 1);
        var second = Fabricated.TakingOver(CheapMicrophone, Packets(CheapMicrophone, 2, 3)).ToList();

        Write(first.Concat(second));

        using var reader = SpoolReader.Open(File);
        var read = reader.Packets().ToList();

        reader.Format.ShouldBe(MonoFloat, "the header is still what the first block is read at");
        reader.Discarded.ShouldBe(0);
        read.Count.ShouldBe(first.Count + second.Count);

        read.Take(first.Count).ShouldAllBe(packet => packet.Opening == null);
        read[first.Count].Opening.ShouldBe(CheapMicrophone);
        read.Skip(first.Count + 1).ShouldAllBe(packet => packet.Opening == null);

        // Every block behind the seam is read at the format the seam declared, which is the whole
        // of what the record is for: read at the header's it would have been refused as not being
        // whole frames, and read at the header's width it would have been noise.
        for (var index = 0; index < second.Count; index++)
        {
            read[first.Count + index].Samples.ToArray().ShouldBe(second[index].Samples.ToArray());
            read[first.Count + index].DevicePosition.ShouldBe(second[index].DevicePosition);
        }
    }

    /// <summary>
    /// ISC-75, one record wider. The changeover is where the machine died, so the record saying it
    /// happened never finished landing — and everything the device before it caught is still the
    /// recording.
    /// </summary>
    [Fact]
    public void A_spool_cut_off_inside_the_record_that_says_a_device_took_over_keeps_what_came_first()
    {
        var first = Packets(MonoFloat, 0, 1);
        var second = Fabricated.TakingOver(CheapMicrophone, Packets(CheapMicrophone, 2, 3)).ToList();

        Write(first.Concat(second));

        // Back to the last whole block of the first device, and then half of the record that says
        // the second one took over.
        Truncate(BlockSpool.HeaderBytes
            + first.Sum(packet => (long)(BlockSpool.BlockHeaderBytes + packet.Samples.Length + BlockSpool.ChecksumBytes))
            + (BlockSpool.StretchBytes / 2));

        using var reader = SpoolReader.Open(File);
        var read = reader.Packets().ToList();

        read.Count.ShouldBe(first.Count);
        reader.Discarded.ShouldBe(BlockSpool.StretchBytes / 2);
    }

    /// <summary>
    /// Every sample behind it is read through those sixteen bytes, so a record that does not hash
    /// to what was written ends the file the way a torn block does rather than being read anyway.
    /// </summary>
    [Fact]
    public void A_record_that_does_not_hash_to_what_was_written_ends_the_file()
    {
        var first = Packets(MonoFloat, 0, 1);

        Write(first.Concat(Fabricated.TakingOver(CheapMicrophone, Packets(CheapMicrophone, 2, 3))));

        Corrupt(BlockSpool.HeaderBytes
            + first.Sum(packet => (long)(BlockSpool.BlockHeaderBytes + packet.Samples.Length + BlockSpool.ChecksumBytes))
            + BlockSpool.MagicBytes);

        using var reader = SpoolReader.Open(File);

        reader.Packets().Count().ShouldBe(first.Count);
    }

    /// <summary>
    /// A record that is whole and names a format nothing here can decode is not a torn write at
    /// all: it is a file describing a device this build has no way to read, and reading the blocks
    /// behind it as audio would invent a recording.
    /// </summary>
    [Fact]
    public void A_record_naming_a_format_this_build_cannot_read_is_refused_rather_than_dropped()
    {
        var first = Packets(MonoFloat, 0, 1);
        var unreadable = new StreamFormat(48_000, 1, 24, SampleEncoding.Pcm);

        Write(first.Concat(Fabricated.TakingOver(
            unreadable,
            [new CapturePacket(
                AudioChannel.Microphone,
                0,
                MonotonicInstant.FromMilliseconds(2_000),
                new byte[unreadable.BytesPerSample * 480])])));

        using var reader = SpoolReader.Open(File);

        var refused = Should.Throw<AudioCaptureException>(() => reader.Packets().ToList());

        refused.Message.ShouldContain("cannot read");

        // And it is not the one refusal a caller is allowed to report and carry on past. A source
        // this build cannot decode and a source that simply holds two formats look alike from the
        // outside and are opposites underneath: one is an artifact nobody here can get a recording
        // out of, the other is a recording that is entirely fine and a convenience file that
        // cannot exist. Only the second is a NoSinglePlaybackException.
        refused.ShouldNotBeOfType<NoSinglePlaybackException>();
    }

    /// <summary>
    /// ISC-124. One device's audio is one format all the way down, so the file somebody plays to
    /// hear what a device caught cannot hold two of them — and it says so rather than handing back
    /// the half it could pour. What it says has to be true of a recording being recovered as much
    /// as of a finished one: those blocks are where both stretches are, and the meeting's own audio
    /// is made from them rather than already sitting there.
    /// </summary>
    [Fact]
    public void A_source_that_changed_format_is_not_poured_into_one_playable_file()
    {
        Write(Packets(MonoFloat, 0, 1)
            .Concat(Fabricated.TakingOver(CheapMicrophone, Packets(CheapMicrophone, 2, 3))));

        // The narrow type and not the family: it is what says this refusal is a fine recording
        // without a convenience file rather than an artifact that cannot be read, and a caller
        // with other work to do reads exactly that to decide whether it may carry on.
        var refusal = Should.Throw<NoSinglePlaybackException>(() => BlockSpool.ToWav(File)).Message;

        // Both files, and the blocks are the load-bearing half. Naming only the meeting's own audio
        // sends whoever is recovering a spool at a file that recording never had — it is made from
        // these blocks, not sitting beside them — so what the sentence has to carry is where the
        // two stretches really are. The clause saying what makes the other file is prose and is not
        // pinned here; the rule it exists for is in this test's summary.
        refusal.ShouldContain(File.Name);
        refusal.ShouldContain(MeetingAudio.FileName);

        // And nothing is left standing under that name. Half a source is what the next attempt
        // would find and what somebody would play as though it were the whole of it.
        BlockSpool.PlaybackFor(File).Refresh();
        BlockSpool.PlaybackFor(File).Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The other half of the same rule: a device replaced by one handing over the same format is
    /// still one file all the way down, so it pours like any other source.
    /// </summary>
    [Fact]
    public void A_source_whose_replacement_hands_over_the_same_format_still_pours()
    {
        var written = Packets(MonoFloat, 0, 1)
            .Concat(Fabricated.TakingOver(MonoFloat, Packets(MonoFloat, 2, 3)))
            .ToList();

        Write(written);

        var replayed = BlockSpool.ToWav(File);

        replayed.Format.ShouldBe(MonoFloat);
        replayed.Blocks.ShouldBe(written.Count);

        using var played = new WaveFileReader(BlockSpool.PlaybackFor(File).FullName);
        played.Length.ShouldBe(written.Sum(packet => (long)packet.Samples.Length));
    }

    public void Dispose()
    {
        if (folder.Exists)
        {
            folder.Delete(recursive: true);
        }
    }

    private FileInfo File => BlockSpool.FileFor(folder, AudioChannel.Microphone);

    private static List<CapturePacket> Packets(StreamFormat format, double from, double until) =>
        [.. Fabricated.Packets(
            AudioChannel.Microphone,
            format,
            format.SampleRate,
            from,
            until,
            Fabricated.Bursts(0.25),
            packetFrames: format.SampleRate / 100)];

    private void Write(IEnumerable<CapturePacket> packets)
    {
        using var writer = SpoolWriter.Create(File, AudioChannel.Microphone, MonoFloat);
        foreach (var packet in packets)
        {
            writer.Write(packet);
        }
    }

    private void Truncate(long to)
    {
        using var stream = File.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(to);
    }

    /// <summary>Changes one byte, the way a disk that did not keep what it was given would.</summary>
    private void Corrupt(long at)
    {
        using var stream = File.Open(FileMode.Open, FileAccess.ReadWrite);
        stream.Position = at;
        var was = stream.ReadByte();
        stream.Position = at;
        stream.WriteByte((byte)(was ^ 0xFF));
    }
}
