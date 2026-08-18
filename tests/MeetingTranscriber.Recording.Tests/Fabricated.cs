using System.Buffers.Binary;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

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
    private const int PacketFrames = 480;

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
