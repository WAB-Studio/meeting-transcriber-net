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
    /// <remarks>
    /// It asks for the file to itself, so a recording still being written cannot be read. Reading
    /// one would answer about a file that is changing underneath the answer — a block half handed
    /// over reported as the cut-off tail, a length measured after the blocks were counted — and the
    /// person would be told a meeting still going on had ended where the reader happened to look.
    /// </remarks>
    public static SpoolReader Open(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        FileStream stream;
        try
        {
            stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException held) when (File.Exists(file.FullName))
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' is open elsewhere, which on this machine means a recording that "
                + "is still running. What it holds can be read once it stops.", held);
        }

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

            // The version is read before anything is checked against it, because it is what says
            // where the rest of the header is — a later layout could keep its checksum somewhere
            // else entirely, and refusing it for not hashing would be answering the wrong question.
            var version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (version != BlockSpool.Version)
            {
                throw new AudioCaptureException(
                    $"'{file.FullName}' is a version {version} spool and this build writes version "
                    + $"{BlockSpool.Version}. Reading the fields it recognises out of a layout that "
                    + "moved would be a recording made up rather than recovered.");
            }

            var stated = BlockSpool.HeaderBytes - BlockSpool.ChecksumBytes;
            if (BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(stated))
                != BlockSpool.Checksum(header.AsSpan(8, stated - 8), []))
            {
                throw new AudioCaptureException(
                    $"'{file.FullName}' does not hash to what its header says. Every sample in it is "
                    + "read through those bytes, so a recording read out of them would be invented.");
            }

            var channel = CapturedAudio.ChannelAt(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12)));
            var format = Readable(
                file.FullName,
                new StreamFormat(
                    BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16)),
                    BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20)),
                    BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24)),
                    Encoding(file.FullName, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28)))));

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
    /// <remarks>
    /// A stretch record is not a packet and never comes back as one. What it does is change the
    /// format everything behind it is read at, and hang itself on the next packet — see
    /// <see cref="CapturePacket.Opening"/> — so a device that took this source over says so on the
    /// first block it really handed over rather than on a record with no audio in it. A record with
    /// nothing behind it is a changeover the recording was cut off in the middle of, and it says
    /// exactly as much as it should: nothing.
    /// </remarks>
    public IEnumerable<CapturePacket> Packets()
    {
        file.Position = BlockSpool.HeaderBytes;
        Interlocked.Exchange(ref discarded, 0);

        var magic = new byte[BlockSpool.MagicBytes];
        var header = new byte[BlockSpool.BlockHeaderBytes - BlockSpool.MagicBytes];
        var stretch = new byte[BlockSpool.StretchBytes - BlockSpool.MagicBytes];
        var trailer = new byte[BlockSpool.ChecksumBytes];
        var whole = 0L;
        var format = Format;
        StreamFormat? opening = null;

        while (true)
        {
            if (Read(magic) < magic.Length)
            {
                break;
            }

            if (magic.AsSpan().SequenceEqual(BlockSpool.StretchMagic))
            {
                if (Read(stretch) < stretch.Length || Opening(stretch) is not { } taking)
                {
                    break;
                }

                whole += magic.Length + stretch.Length;
                format = taking;
                opening = taking;
                continue;
            }

            if (!magic.AsSpan().SequenceEqual(BlockSpool.BlockMagic) || Read(header) < header.Length)
            {
                break;
            }

            // Whole frames of the format this stretch of the file is in, because a device hands
            // over frames: a length that is not is a length read out of something that is not a
            // block, and half a frame of audio would shift every sample after it onto the wrong
            // channel.
            var samples = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
            if (samples is <= 0 or > BlockSpool.MostBytesInABlock
                || (samples % (format.Channels * format.BytesPerSample)) != 0)
            {
                break;
            }

            var body = new byte[samples];
            if (Read(body) < samples || Read(trailer) < trailer.Length)
            {
                break;
            }

            if (BinaryPrimitives.ReadUInt64LittleEndian(trailer) != BlockSpool.Checksum(header, body))
            {
                break;
            }

            whole += magic.Length + header.Length + samples + trailer.Length;
            yield return new CapturePacket(
                Channel,
                BinaryPrimitives.ReadInt64LittleEndian(header),
                MonotonicInstant.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8))),
                body,
                (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20)) & 1) == 1,
                opening);

            // Said once, on the first block the new device really handed over. Every block after it
            // is that stretch's and says nothing, which is what keeps a stretch a seam rather than
            // something every packet has to carry.
            opening = null;
        }

        Interlocked.Exchange(ref discarded, file.Length - BlockSpool.HeaderBytes - whole);
    }

    /// <summary>
    /// The format a stretch record declares, or nothing when the record is not whole or says
    /// something no device hands over.
    /// </summary>
    /// <remarks>
    /// Nothing rather than a throw, and it is the same answer a torn block gets: the tail of a
    /// recording the machine died in the middle of is dropped, and everything above it is still the
    /// meeting. A record that hashes to what was written and still names a format this build cannot
    /// read is the other case — it is not a torn write, it is a spool describing a device this
    /// build has no way to decode — and that one is refused where the format is refused, which is
    /// the same place the file's own header is refused.
    /// </remarks>
    private StreamFormat? Opening(ReadOnlySpan<byte> stretch)
    {
        var said = stretch[..16];
        if (BinaryPrimitives.ReadUInt64LittleEndian(stretch[16..]) != BlockSpool.Checksum(said, []))
        {
            return null;
        }

        return Readable(
            file.Name,
            new StreamFormat(
                BinaryPrimitives.ReadInt32LittleEndian(said),
                BinaryPrimitives.ReadInt32LittleEndian(said[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(said[8..]),
                Encoding(file.Name, BinaryPrimitives.ReadInt32LittleEndian(said[12..]))));
    }

    public void Dispose() => file.Dispose();

    /// <summary>
    /// The format, if a block of it could be read at all. Checked here rather than left to whatever
    /// meets it first: a header saying no channels or an eight bit width would otherwise open, and
    /// come back as a resampler dividing by zero or a WAV nothing will play — a failure about the
    /// recording, arriving somewhere that cannot say which file was wrong.
    /// </summary>
    private static StreamFormat Readable(string path, StreamFormat format)
    {
        if (format.SampleRate <= 0 || format.Channels <= 0)
        {
            throw new AudioCaptureException(
                $"'{path}' says its samples arrived at {format.SampleRate} Hz on "
                + $"{format.Channels} channels, which is not a recording.");
        }

        try
        {
            Levels.EnsureMeterable(format);
        }
        catch (AudioCaptureException unreadable)
        {
            throw new AudioCaptureException($"'{path}': {unreadable.Message}", unreadable);
        }

        return format;
    }

    private static SampleEncoding Encoding(string path, int stored) =>
        stored == (int)SampleEncoding.Pcm || stored == (int)SampleEncoding.IeeeFloat
            ? (SampleEncoding)stored
            : throw new AudioCaptureException(
                $"'{path}' says its samples are encoded as {stored}, which this build "
                + "cannot read.");

    private int Read(Span<byte> into) => file.ReadAtLeast(into, into.Length, throwOnEndOfStream: false);
}
