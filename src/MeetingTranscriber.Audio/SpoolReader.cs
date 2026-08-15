using System.Buffers.Binary;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One source's blocks, read back as the packets its device handed over.
/// </summary>
/// <remarks>
/// <para>
/// It stops at the first thing that is not a whole block and keeps everything before it. That is
/// the whole of what a spool is for: a recording the machine was cut off in the middle of is worth
/// every block that landed, and the one it was writing at the time is worth nothing — so the tail
/// is dropped rather than read short, guessed at, or allowed to end the recording.
/// </para>
/// <para>
/// Three things are asked of a block, and each of them is a way a cut-off write ends: the file has
/// to hold the whole of it, it has to start where a block starts, and it has to hash to what was
/// written. The last one is what catches the tail a power cut leaves — a length the file system
/// already recorded with bytes the disk never received, which frames perfectly and is not audio.
/// </para>
/// <para>
/// What comes out is <see cref="CapturePacket"/>s in the order they were written, which is the
/// order the devices read them, which is the order <see cref="SharedTimeline"/> takes them in.
/// Nothing is inferred on the way through: a block says where the device was and when, and a spool
/// read back is the same recording as the one that was being written.
/// </para>
/// </remarks>
public sealed class SpoolReader : IDisposable
{
    private readonly FileStream file;
    private long discarded;

    private SpoolReader(FileStream file, AudioChannel channel, StreamFormat format)
    {
        this.file = file;
        Channel = channel;
        Format = format;
    }

    /// <summary>Which source this file holds, as its own header says.</summary>
    public AudioChannel Channel { get; }

    /// <summary>The format its device handed over, as its own header says.</summary>
    public StreamFormat Format { get; }

    /// <summary>
    /// Bytes at the end of the file that were not a whole block, which is what the recording being
    /// cut off cost. Answered once <see cref="Packets"/> has been read to the end; before that it
    /// is nothing, because nothing has looked yet.
    /// </summary>
    public long Discarded => Interlocked.Read(ref discarded);

    /// <summary>
    /// Opens a spool, refusing a file that is not one rather than reading whatever it holds as
    /// audio.
    /// </summary>
    public static SpoolReader Open(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var stream = new FileStream(
            file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        try
        {
            var header = new byte[BlockSpool.HeaderBytes];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                throw new AudioCaptureException(
                    $"'{file.FullName}' is shorter than a spool's own header, so it names no source "
                    + "and no format. There is no recording in it to recover.");
            }

            if (!header.AsSpan(0, BlockSpool.FileMagic.Length).SequenceEqual(BlockSpool.FileMagic))
            {
                throw new AudioCaptureException($"'{file.FullName}' is not a spool of recorded blocks.");
            }

            var version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (version != BlockSpool.Version)
            {
                throw new AudioCaptureException(
                    $"'{file.FullName}' is a version {version} spool and this build writes version "
                    + $"{BlockSpool.Version}. Reading the fields it recognises out of a layout that "
                    + "moved would be a recording made up rather than recovered.");
            }

            var channel = CapturedAudio.ChannelAt(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12)));
            var format = new StreamFormat(
                BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16)),
                BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20)),
                BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24)),
                Encoding(file, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28))));

            return new SpoolReader(stream, channel, format);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every whole block, in the order it was written. Reading to the end is what settles
    /// <see cref="Discarded"/>.
    /// </summary>
    public IEnumerable<CapturePacket> Packets()
    {
        file.Position = BlockSpool.HeaderBytes;
        Interlocked.Exchange(ref discarded, 0);

        var header = new byte[BlockSpool.BlockHeaderBytes];
        var trailer = new byte[BlockSpool.ChecksumBytes];
        var whole = 0L;

        while (true)
        {
            if (Read(header) < header.Length
                || !header.AsSpan(0, BlockSpool.BlockMagic.Length).SequenceEqual(BlockSpool.BlockMagic))
            {
                break;
            }

            var samples = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
            if (samples is <= 0 or > BlockSpool.MostBytesInABlock)
            {
                break;
            }

            var body = new byte[samples];
            if (Read(body) < samples || Read(trailer) < trailer.Length)
            {
                break;
            }

            if (BinaryPrimitives.ReadUInt64LittleEndian(trailer)
                != BlockSpool.Checksum(header.AsSpan(4, BlockSpool.BlockHeaderBytes - 4), body))
            {
                break;
            }

            whole += header.Length + samples + trailer.Length;
            yield return new CapturePacket(
                Channel,
                BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(4)),
                MonotonicInstant.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12))),
                body,
                (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24)) & 1) == 1);
        }

        Interlocked.Exchange(ref discarded, file.Length - BlockSpool.HeaderBytes - whole);
    }

    public void Dispose() => file.Dispose();

    private static SampleEncoding Encoding(FileInfo file, int stored) =>
        stored == (int)SampleEncoding.Pcm || stored == (int)SampleEncoding.IeeeFloat
            ? (SampleEncoding)stored
            : throw new AudioCaptureException(
                $"'{file.FullName}' says its samples are encoded as {stored}, which this build "
                + "cannot read.");

    private int Read(Span<byte> into) => file.ReadAtLeast(into, into.Length, throwOnEndOfStream: false);
}
