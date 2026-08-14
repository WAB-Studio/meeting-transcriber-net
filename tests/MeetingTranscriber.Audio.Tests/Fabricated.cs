using System.Buffers.Binary;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A device that never existed, handing over packets the way a real one does: a frame counter of
/// its own, an instant on the machine's clock for each packet, and a rate that is only as close to
/// its label as the test says it is.
/// </summary>
/// <remarks>
/// This is the whole reason the timeline takes packets rather than opening streams. Two hours of a
/// clock running two hundred parts per million fast is a minute of arithmetic here and a
/// two-hour meeting on a machine nobody has.
/// </remarks>
internal static class Fabricated
{
    /// <summary>
    /// Far enough from zero that nothing can quietly assume the counter starts there, which a real
    /// performance counter never does.
    /// </summary>
    private const double CounterStartSeconds = 3_600;

    /// <summary>
    /// The packets a device would hand over between <paramref name="fromSeconds"/> and
    /// <paramref name="untilSeconds"/> of the shared clock.
    /// </summary>
    /// <param name="channel">Which channel it feeds.</param>
    /// <param name="format">What it says it is handing over.</param>
    /// <param name="realRate">
    /// The frames per second it really produces. Equal to the format's rate for a device whose
    /// crystal is perfect, which no device's is.
    /// </param>
    /// <param name="fromSeconds">The shared-clock second its stream opened.</param>
    /// <param name="untilSeconds">The shared-clock second it stops at.</param>
    /// <param name="signal">The sound in the room, as a function of the shared clock's seconds.</param>
    /// <param name="packetFrames">How many frames it hands over at a time.</param>
    /// <param name="delivers">
    /// Whether a packet starting at that device position reaches the application at all. A packet
    /// that does not is a stretch the device produced and nobody ever saw — the frame counter goes
    /// on counting it, which is exactly how a hole becomes visible.
    /// </param>
    internal static IEnumerable<CapturePacket> Packets(
        AudioChannel channel,
        StreamFormat format,
        double realRate,
        double fromSeconds,
        double untilSeconds,
        Func<double, float> signal,
        int packetFrames = 480,
        Func<long, bool>? delivers = null)
    {
        var total = (long)((untilSeconds - fromSeconds) * realRate);
        var mono = new float[packetFrames];

        for (long position = 0; position + packetFrames <= total; position += packetFrames)
        {
            if (delivers is not null && !delivers(position))
            {
                continue;
            }

            for (var frame = 0; frame < packetFrames; frame++)
            {
                mono[frame] = signal(fromSeconds + ((position + frame) / realRate));
            }

            yield return new CapturePacket(
                channel,
                position,
                MonotonicInstant.FromMilliseconds(
                    (CounterStartSeconds + fromSeconds + (position / realRate)) * 1000),
                Encode(mono, format));
        }
    }

    /// <summary>A burst of <paramref name="lengthMs"/> at every whole multiple of <paramref name="everySeconds"/>.</summary>
    /// <remarks>
    /// A marker rather than music: what the alignment tests need is an edge whose arrival can be
    /// found in the output to within a millisecond or two, at instants both sources share.
    /// </remarks>
    internal static Func<double, float> Bursts(double everySeconds, double lengthMs = 20, float amplitude = 0.9f) =>
        seconds => seconds % everySeconds < lengthMs / 1000 ? amplitude : 0f;

    /// <summary>Silence, for a source a test is only running so that the other one has a partner.</summary>
    internal static Func<double, float> Quiet => _ => 0f;

    /// <summary>Lays one mono sample out across every channel the device claims to interleave.</summary>
    private static byte[] Encode(ReadOnlySpan<float> mono, StreamFormat format)
    {
        var width = format.BytesPerSample;
        var block = new byte[mono.Length * format.Channels * width];

        for (var frame = 0; frame < mono.Length; frame++)
        {
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var at = block.AsSpan(((frame * format.Channels) + channel) * width, width);
                if (format.Encoding is SampleEncoding.IeeeFloat)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(at, mono[frame]);
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(
                        at, (short)Math.Clamp(MathF.Round(mono[frame] * short.MaxValue), short.MinValue, short.MaxValue));
                }
            }
        }

        return block;
    }
}
