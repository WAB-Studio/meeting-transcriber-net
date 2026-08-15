using System.Buffers.Binary;

using MeetingTranscriber.Domain.Audio;

using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The file one source of a recording is written to while it is being recorded: its format once,
/// and then one self-contained block per packet the device handed over.
/// </summary>
/// <remarks>
/// <para>
/// A WAV cannot be this file. Its length lives in a header written when the stream closes, so a
/// recording only becomes readable at the moment the application is allowed to finish — which is
/// exactly the moment a crash takes away. Here the last block is the end of the file, and a
/// recording that was cut off is worth every block that reached the disk before it.
/// </para>
/// <para>
/// So a block carries what the packet carried and not what a byte count could be made to imply:
/// the device's own frame position, the instant it read that position, and whether it vouched for
/// that instant. Those three are what puts a stretch of audio where it belongs — see
/// <see cref="CapturePacket"/> — and a spool that stored only samples would come back as a
/// recording with every hole closed up and both channels sliding apart.
/// </para>
/// <para>
/// The format is in the file's own header rather than in a card beside it. One file is then enough
/// to read one source back, which is the property that matters when what is left of a meeting is
/// whatever survived: a spool whose samples can only be decoded by a second file has two ways to
/// be lost instead of one.
/// </para>
/// </remarks>
public static class BlockSpool
{
    /// <summary>What a source's blocks are stored under, beside the other source's.</summary>
    public const string Extension = ".blocks";

    /// <summary>
    /// The shape of this file. A reader refuses a version it was not written for rather than
    /// reading the fields it recognises out of a layout that moved.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Bytes of the file header: the magic, the version, the channel, the format, and a checksum
    /// over all of it but the magic.
    /// </summary>
    /// <remarks>
    /// These three are part of the format and not an implementation detail of writing it. What a
    /// recording costs on disk, and where in the file one block ends and the next begins, are
    /// arithmetic anybody holding a spool is entitled to do.
    /// </remarks>
    public const int HeaderBytes = 40;

    /// <summary>Bytes in front of a block's samples: the magic, the position, the instant and how many.</summary>
    public const int BlockHeaderBytes = 28;

    /// <summary>Bytes behind a block's samples, which is what says the block is whole.</summary>
    public const int ChecksumBytes = 8;

    /// <summary>
    /// The most one block may claim to hold. A device hands over a fraction of a second at a time,
    /// so this is far above anything real — its job is that a length read out of a torn tail cannot
    /// send the reader off to allocate whatever the garbage happened to say.
    /// </summary>
    internal const int MostBytesInABlock = 16 * 1024 * 1024;

    /// <summary>Marks the file. Eight bytes, so a file that is not one of these says so at byte 0.</summary>
    internal static ReadOnlySpan<byte> FileMagic => "MTBLOCKS"u8;

    /// <summary>Marks a block, so "this is not a block" is decidable before anything is hashed.</summary>
    internal static ReadOnlySpan<byte> BlockMagic => "BLK1"u8;

    /// <summary>The blocks of <paramref name="channel"/>, in a folder holding one recording.</summary>
    /// <remarks>
    /// Named after the channel and nothing else, so the pair of files says which is which without a
    /// card listing them — and so an unknown channel is refused here rather than writing a
    /// recording under a name nothing will look for.
    /// </remarks>
    public static FileInfo FileFor(DirectoryInfo folder, AudioChannel channel)
    {
        ArgumentNullException.ThrowIfNull(folder);
        CapturedAudio.IndexOf(channel);

        return new FileInfo(Path.Combine(
            folder.FullName, $"{channel.ToString().ToLowerInvariant()}{Extension}"));
    }

    /// <summary>
    /// The file <see cref="ToWav"/> pours <paramref name="blocks"/> into: the same source under the
    /// same name, in a container something can play.
    /// </summary>
    /// <remarks>
    /// Named here rather than by each caller so that a capture can refuse a folder holding either
    /// of the two files it is going to write. A spool is refused by the file system when it is
    /// already there; the file beside it is produced again from that spool every time, so nothing
    /// but this pairing stops a recording from landing on another recording's playback.
    /// </remarks>
    public static FileInfo PlaybackFor(FileInfo blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return new FileInfo(Path.ChangeExtension(blocks.FullName, ".wav"));
    }

    /// <summary>
    /// Throws unless <paramref name="folder"/> is free of everything a recording writes into it:
    /// both sources' blocks, and the file each one is played back into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule about names and nothing else, so it is answered without a device — the same split
    /// that keeps which endpoint a typed name means testable on a machine that has none.
    /// </para>
    /// <para>
    /// Both files and not only the spools. A spool is refused by the file system when it is
    /// claimed, which is the check no second writer can slip past; a playback is replaced every
    /// time it is produced, which is right for a file made again from the spool beside it and
    /// wrong for one left by a recording whose spool is not here. This is what keeps the second
    /// case from arising, and it belongs before the meeting starts rather than after it, because
    /// what is at stake by then is a recording somebody was holding.
    /// </para>
    /// </remarks>
    public static void EnsureNothingRecordedIn(DirectoryInfo folder)
    {
        var taken = new[] { AudioChannel.Loopback, AudioChannel.Microphone }
            .Select(channel => FileFor(folder, channel))
            .SelectMany(blocks => new[] { blocks, PlaybackFor(blocks) })
            .FirstOrDefault(file => file.Exists);

        if (taken is not null)
        {
            throw new AudioCaptureException(
                $"'{taken.FullName}' is already there, and a recording is not written over another "
                + "one. Record into a folder of its own, or move what is in this one.");
        }
    }

    /// <summary>
    /// Writes what one source heard into a WAV of the format its device handed over, and says what
    /// the spool turned out to hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One source, unaligned and unresampled — what a person plays to hear whether a device was
    /// recording at all. It is not the recording: two sources become one pair of channels on the
    /// shared timeline, and that file is made when a meeting is stopped.
    /// </para>
    /// <para>
    /// It replaces whatever is at that name, which is what re-rendering is: the file is produced
    /// from the spool beside it and holds nothing that spool does not. What stops it landing on a
    /// recording it did not come from is that a capture refuses a folder already holding either
    /// file, and this being the only thing that names the pairing.
    /// </para>
    /// </remarks>
    public static Replayed ToWav(FileInfo blocks)
    {
        using var spool = SpoolReader.Open(blocks);
        var wav = PlaybackFor(blocks);
        using var writer = new WaveFileWriter(wav.FullName, WaveFormatOf(spool.Format));

        var written = 0;
        foreach (var packet in spool.Packets())
        {
            writer.Write(packet.Samples.Span);
            written++;
        }

        return new Replayed(spool.Format, written, spool.Discarded);
    }

    /// <summary>
    /// A checksum over one block, which is what tells a block that never finished landing from one
    /// that did.
    /// </summary>
    /// <remarks>
    /// FNV-1a, and deliberately not SHA-256 or a package: nothing here is guarding against somebody
    /// choosing the bytes, and nothing keys off the value. The one question is whether these bytes
    /// are the bytes that were written, asked of every block on the recording thread — so what it
    /// has to be is cheap, dependency-free and certain to notice the tail of zeroes a power cut
    /// leaves behind a file whose length was already updated.
    /// </remarks>
    internal static ulong Checksum(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        const ulong Basis = 14_695_981_039_346_656_037;
        const ulong Prime = 1_099_511_628_211;

        var hash = Basis;
        foreach (var value in first)
        {
            hash = (hash ^ value) * Prime;
        }

        foreach (var value in second)
        {
            hash = (hash ^ value) * Prime;
        }

        return hash;
    }

    /// <summary>NAudio's word for a format, which only the two files at the edges need.</summary>
    internal static WaveFormat WaveFormatOf(StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return format.Encoding is SampleEncoding.IeeeFloat
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
            : new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
    }
}

/// <summary>What reading a spool back produced, and what it refused to read.</summary>
/// <param name="Format">What the device handed over, as the spool's own header says.</param>
/// <param name="Blocks">Whole blocks, every one of them as its device reported it.</param>
/// <param name="Discarded">
/// Bytes at the end that were not a whole block. Non-zero means the recording was cut off, and
/// what it cost is the last packet rather than the meeting.
/// </param>
public sealed record Replayed(StreamFormat Format, int Blocks, long Discarded);
