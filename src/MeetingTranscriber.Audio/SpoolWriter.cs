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
    private byte[] block;
    private long bytes;
    private int blocks;

    private SpoolWriter(FileStream file, AudioChannel channel, StreamFormat format)
    {
        this.file = file;
        this.channel = channel;
        Format = format;
        block = new byte[BlockSpool.BlockHeaderBytes + 8192 + BlockSpool.ChecksumBytes];
    }

    /// <summary>The format the device hands over, as this file's header declares it.</summary>
    public StreamFormat Format { get; }

    /// <summary>Samples written, not counting what the format around them costs.</summary>
    public long Bytes => Interlocked.Read(ref bytes);

    /// <summary>Blocks written, which is packets the device handed over.</summary>
    public int Blocks => Volatile.Read(ref blocks);

    /// <summary>
    /// Starts a spool for <paramref name="channel"/> at <paramref name="file"/>, refusing to write
    /// over a recording that is already there.
    /// </summary>
    /// <remarks>
    /// The file system answers rather than a question asked a moment earlier: a check and then a
    /// create is a window in which the recording being truncated is the other capture's.
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
            stream.Write(header);

            return new SpoolWriter(stream, channel, format);
        }
        catch
        {
            // The file was made here, so a header that never landed is this method's mess and not
            // a recording: what it would otherwise leave is an empty file standing exactly where
            // the next attempt wants to write.
            stream.Dispose();
            try
            {
                File.Delete(file.FullName);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
                // Swallowed on purpose: what the caller has to hear is why the spool would not
                // open, not that a handle would not close on the way out.
            }

            throw;
        }
    }

    /// <summary>Writes one packet as one block.</summary>
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
        Volatile.Write(ref blocks, blocks + 1);
    }

    /// <summary>Hands over whatever is still held here, without asking the disk to commit it.</summary>
    public void Flush() => file.Flush();

    public void Dispose() => file.Dispose();
}
