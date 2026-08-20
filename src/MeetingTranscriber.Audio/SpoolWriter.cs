using System.Buffers.Binary;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One source's blocks, on their way to disk. What the recording thread hands its packets to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A block reaches the file in one write.</b> It is laid out whole in memory first — header,
/// samples and checksum together — so that what a killed process leaves behind is a whole number of
/// blocks rather than half of one, which is the difference between the last packet being lost and
/// the reader having to guess where a record started. A power cut can still tear a write the
/// operating system had not finished, and that is what the checksum is for.
/// </para>
/// <para>
/// Nothing is flushed to the disk itself. Two hours of packets is a couple of hundred thousand
/// blocks, and asking the disk to commit each one would cost the recording far more than it could
/// buy: the bytes are handed to the operating system as each block is written, which is what
/// survives the process dying, and the disk is what a power cut is measured against.
/// </para>
/// </remarks>
public sealed class SpoolWriter : IDisposable
{
    private readonly FileStream file;
    private readonly AudioChannel channel;

    /// <summary>
    /// The frame width of the stretch being written now, which is the file header's until a
    /// device that hands over another format takes this source over.
    /// </summary>
    private int frameBytes;
    private byte[] block;
    private long bytes;

    private SpoolWriter(FileStream file, AudioChannel channel, int frameBytes)
    {
        this.file = file;
        this.channel = channel;
        this.frameBytes = frameBytes;
        block = new byte[BlockSpool.BlockHeaderBytes + 8192 + BlockSpool.ChecksumBytes];
    }

    /// <summary>
    /// Samples written, not counting what the format around them costs. Read while the recording
    /// thread is still writing, which is what it is for.
    /// </summary>
    public long Bytes => Interlocked.Read(ref bytes);

    /// <summary>
    /// Starts a spool for <paramref name="channel"/> at <paramref name="file"/>, refusing to write
    /// over a recording that is already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file system answers rather than a question asked a moment earlier: a check and then a
    /// create is a window in which the recording being truncated is the other capture's.
    /// </para>
    /// <para>
    /// The format is written down as the caller declares it rather than judged here. Whether this
    /// build can read a device's format is settled before the device is opened, which is earlier
    /// than a spool exists; what this file has to do is say what it holds, and what decides whether
    /// a spool can be read is the boundary a spool is read at.
    /// </para>
    /// </remarks>
    public static SpoolWriter Create(FileInfo file, AudioChannel channel, StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(format);
        CapturedAudio.IndexOf(channel);

        FileStream stream;
        try
        {
            // Unbuffered on purpose: this class does its own buffering, one block at a time, and a
            // stream buffer on top of it would hold finished blocks back from the operating system
            // for no reason other than that they were small.
            stream = new FileStream(
                file.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 1);
        }
        catch (IOException taken) when (File.Exists(file.FullName))
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' is already there, and a recording is not written over another one.", taken);
        }

        try
        {
            var header = new byte[BlockSpool.HeaderBytes];
            BlockSpool.FileMagic.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), BlockSpool.Version);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), CapturedAudio.IndexOf(channel));
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), format.SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), format.Channels);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), format.BitsPerSample);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), (int)format.Encoding);

            // The same treatment every block gets, and for the same reason: these thirty two bytes
            // decide how the file's samples are read until a stretch record says otherwise, so a
            // header that says 44 101 Hz where 44 100 was written would come back as a recording
            // rather than as a refusal.
            BinaryPrimitives.WriteUInt64LittleEndian(
                header.AsSpan(BlockSpool.HeaderBytes - BlockSpool.ChecksumBytes),
                BlockSpool.Checksum(header.AsSpan(8, BlockSpool.HeaderBytes - BlockSpool.ChecksumBytes - 8), []));
            stream.Write(header);

            return new SpoolWriter(stream, channel, format.Channels * format.BytesPerSample);
        }
        catch
        {
            // The file was made here, so a header that never landed is this method's mess and not
            // a recording: what it would otherwise leave is an empty file standing exactly where
            // the next attempt wants to write. The delete happens whatever closing the handle did,
            // because a handle that would not close is the case that leaves the file behind.
            try
            {
                stream.Dispose();
            }
            finally
            {
                BlockSpool.Erase(file);
            }

            throw;
        }
    }

    /// <summary>
    /// Writes one packet as one block, behind the record saying this source changed device when it
    /// is the packet that says so.
    /// </summary>
    public void Write(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Channel != channel)
        {
            throw new AudioContractException(
                $"A {packet.Channel} packet reached the {channel} spool. A block file holds one "
                + "source, and the two would be read back as each other's.");
        }

        var samples = packet.Samples.Span;
        if (samples.IsEmpty)
        {
            throw new AudioContractException(
                $"A {channel} packet carried no samples. A block with nothing in it says a device "
                + "produced a stretch of no audio, which is not something that happens.");
        }

        // Every check first and then every write, which matters more here than it looks. A packet
        // that opens a stretch is two records, and writing the first of them before the second is
        // known to be sound would leave a file saying every block behind it is another device's
        // — including the ones the device that is leaving is still handing over. So the width this
        // packet claims to be frames of is the one it declares, and nothing lands until it is.
        var frame = packet.Opening is { } declared
            ? declared.Channels * declared.BytesPerSample
            : frameBytes;

        // A device hands over frames, so half of one is not something a device produced. Refused
        // here, where it is still a defect: written down, it would shift every sample after it onto
        // the other channel of a recording nothing else could tell was wrong.
        if (frame <= 0 || (samples.Length % frame) != 0)
        {
            throw new AudioContractException(
                $"A {channel} packet of {samples.Length} bytes is not whole frames of {frame} "
                + "bytes, so it is not what this source's device hands over.");
        }

        if (packet.Opening is { } opening)
        {
            Stretch(opening);
        }

        var length = BlockSpool.BlockHeaderBytes + samples.Length + BlockSpool.ChecksumBytes;
        if (block.Length < length)
        {
            block = new byte[length];
        }

        var whole = block.AsSpan(0, length);
        BlockSpool.BlockMagic.CopyTo(whole);
        BinaryPrimitives.WriteInt64LittleEndian(whole[4..], packet.DevicePosition);
        BinaryPrimitives.WriteInt64LittleEndian(whole[12..], packet.CapturedAt.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(whole[20..], samples.Length);
        BinaryPrimitives.WriteInt32LittleEndian(whole[24..], packet.TimingIsSound ? 1 : 0);
        samples.CopyTo(whole[BlockSpool.BlockHeaderBytes..]);

        // Everything but the magic, which is what says a block starts here rather than what says
        // the block is whole.
        BinaryPrimitives.WriteUInt64LittleEndian(
            whole[(length - BlockSpool.ChecksumBytes)..],
            BlockSpool.Checksum(whole[4..BlockSpool.BlockHeaderBytes], samples));

        file.Write(whole);
        Interlocked.Add(ref bytes, samples.Length);
    }

    /// <summary>
    /// Writes down that this source is a different device from here on, and what that device hands
    /// over.
    /// </summary>
    /// <remarks>
    /// The format is written as the caller declares it, the way the file's own header is: what a
    /// stretch may be is settled where the device is opened, which is before a packet of it exists.
    /// What is refused here is a format nothing could be whole frames of, because that one would
    /// otherwise be caught by every block after it rather than by the record that said it.
    /// </remarks>
    private void Stretch(StreamFormat opening)
    {
        var frame = opening.Channels * opening.BytesPerSample;
        if (frame <= 0)
        {
            throw new AudioContractException(
                $"The {channel} source was handed over to a device at {opening}, whose frames are "
                + $"{frame} bytes. Nothing a device produces is whole frames of that.");
        }

        var record = new byte[BlockSpool.StretchBytes];
        BlockSpool.StretchMagic.CopyTo(record);

        var said = record.AsSpan(BlockSpool.MagicBytes, 16);
        BinaryPrimitives.WriteInt32LittleEndian(said, opening.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(said[4..], opening.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(said[8..], opening.BitsPerSample);
        BinaryPrimitives.WriteInt32LittleEndian(said[12..], (int)opening.Encoding);
        BinaryPrimitives.WriteUInt64LittleEndian(
            record.AsSpan(BlockSpool.StretchBytes - BlockSpool.ChecksumBytes),
            BlockSpool.Checksum(said, []));

        file.Write(record);
        frameBytes = frame;
    }

    /// <summary>Hands over whatever is still held here, without asking the disk to commit it.</summary>
    public void Flush() => file.Flush();

    public void Dispose() => file.Dispose();
}
