using System.Buffers.Binary;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// A device that never existed, handing over blocks the way a real one does, so that a meeting can
/// be finished on a machine with no sound card.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately much smaller than the engine suite's fabricator of the same name, and deliberately
/// not shared with it. What these tests need of a device is that it produced whole blocks at
/// plausible positions; drifting crystals, jittered instants and undelivered packets are the audio
/// engine's subject, and a copy of that machinery here would be a second thing to keep true for
/// tests that never ask it anything.
/// </para>
/// <para>
/// The obvious home for a shared one is <c>MeetingTranscriber.Testing</c>, and it is closed: that
/// project stops at <c>Infrastructure</c> on purpose, and a packet is an <c>Audio</c> type, so
/// putting this there would give every suite that opens a corpus a path to the audio engine. The
/// two ways out of the duplication are both worse than the duplication, so it stays — and this is
/// the note saying it was weighed rather than missed.
/// </para>
/// </remarks>
internal static class Fabricated
{
    /// <summary>What channel 0's device says it hands over.</summary>
    internal static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);

    /// <summary>What channel 1's says, which is nothing like it — as a real pair rarely is.</summary>
    internal static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private const int PacketFrames = 480;

    /// <summary>
    /// Both spools of a meeting, written the way a capture writes them, so a meeting can be
    /// finished or recovered on a machine with no sound card.
    /// </summary>
    internal static void Spools(DirectoryInfo into, double seconds)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Create();
        Write(into, AudioChannel.Loopback, StereoFloat, 48_000, seconds);
        Write(into, AudioChannel.Microphone, CheapMicrophone, 44_100, seconds);
    }

    /// <summary>One source's spool, in the format that source's device hands over.</summary>
    internal static void Write(
        DirectoryInfo into, AudioChannel channel, StreamFormat format, double rate, double seconds)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(into, channel), channel, format);
        foreach (var packet in Packets(channel, format, rate, 0, seconds))
        {
            writer.Write(packet);
        }
    }

    /// <summary>What an ordinary recording of this meeting wrote about itself when it opened.</summary>
    internal static SpoolCard CardFor(Guid meetingId, UtcTimestamp startedAt) => new(
        meetingId,
        Guid.NewGuid(),
        startedAt,
        CapturedAudio.Profile,
        [
            new SpooledSource(AudioChannel.Loopback, "Speakers", "{0.0.0.00000000}.{loopback}"),
            new SpooledSource(AudioChannel.Microphone, "Headset", "{0.0.1.00000000}.{mic}"),
        ],
        FellBack: null);

    /// <summary>
    /// What a process killed mid-recording leaves: a file ending inside the block it was writing.
    /// </summary>
    /// <remarks>
    /// Truncation rather than corruption, because that is the shape the failure actually has. The
    /// blocks before it landed whole and the one being written did not, and what the recording is
    /// worth is every one of the first and none of the last.
    /// </remarks>
    internal static void KilledMidBlock(FileInfo spool, long inside)
    {
        ArgumentNullException.ThrowIfNull(spool);

        using var file = spool.Open(FileMode.Open, FileAccess.Write, FileShare.None);
        file.SetLength(file.Length - inside);
    }

    /// <summary>
    /// Takes the file's own header apart, so what is left is no longer a spool at all — a disk
    /// that gave back bytes it never received, which is what a spool refuses to read as audio.
    /// </summary>
    internal static void NoLongerASpool(FileInfo spool)
    {
        ArgumentNullException.ThrowIfNull(spool);

        using var file = spool.Open(FileMode.Open, FileAccess.Write, FileShare.None);
        file.Write(new byte[8]);
    }

    /// <summary>The blocks a device would hand over across that many seconds.</summary>
    internal static IEnumerable<CapturePacket> Packets(
        AudioChannel channel, StreamFormat format, double realRate, double from, double until)
    {
        var total = (long)((until - from) * realRate);
        var mono = new float[PacketFrames];

        for (long produced = 0; produced + PacketFrames <= total; produced += PacketFrames)
        {
            for (var frame = 0; frame < PacketFrames; frame++)
            {
                // A tone rather than silence: a recording of nothing is one a build could produce
                // by losing the audio, and these assert on the bytes that came out.
                mono[frame] = 0.6f * MathF.Sin(
                    (float)(2 * Math.PI * 440 * ((produced + frame) / realRate)));
            }

            yield return new CapturePacket(
                channel,
                produced,
                MonotonicInstant.FromMilliseconds(((from + (produced / realRate)) * 1_000) + 3_600_000),
                Encode(mono, format));
        }
    }

    /// <summary>One block's worth of samples in the format the device says it hands over.</summary>
    private static byte[] Encode(ReadOnlySpan<float> mono, StreamFormat format)
    {
        var bytes = new byte[mono.Length * format.Channels * format.BytesPerSample];
        var at = 0;

        foreach (var sample in mono)
        {
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var slot = bytes.AsSpan(at, format.BytesPerSample);
                if (format.Encoding == SampleEncoding.IeeeFloat)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(slot, sample);
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(
                        slot, (short)(sample * -(float)short.MinValue));
                }

                at += format.BytesPerSample;
            }
        }

        return bytes;
    }
}
