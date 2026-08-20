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
    /// <param name="jitterMs">
    /// How far the instant beside a packet may be from where the device really was. Zero is a
    /// counter nothing perturbs, which no machine has: WASAPI's reported instant moves by around a
    /// millisecond, and that jitter is the reason `SourceClock` refuses to measure a rate over a
    /// short window at all. A drift measurement taken without it is taken against a clock the
    /// product does not have.
    /// </param>
    /// <param name="countsAtRate">
    /// The rate the device numbers its own frames in, when that is not the rate it hands them over
    /// at. Zero is the ordinary device, whose two numbers are in one unit. Anything else is the
    /// webcam microphone this exists for: opened at the endpoint's mix format, handing over frames
    /// converted to it and a position that was never converted with them, so one packet carries two
    /// numbers in two units and only one of them is the client's.
    /// </param>
    internal static IEnumerable<CapturePacket> Packets(
        AudioChannel channel,
        StreamFormat format,
        double realRate,
        double fromSeconds,
        double untilSeconds,
        Func<double, float> signal,
        int packetFrames = 480,
        Func<long, bool>? delivers = null,
        double jitterMs = 0,
        double countsAtRate = 0)
    {
        var total = (long)((untilSeconds - fromSeconds) * realRate);
        var mono = new float[packetFrames];

        // Whatever the device counts in, it is the frames it hands over that say how much of the
        // meeting has gone by — so the loop runs on those and only the reported number is scaled.
        var counts = countsAtRate > 0 ? countsAtRate / format.SampleRate : 1;

        for (long produced = 0; produced + packetFrames <= total; produced += packetFrames)
        {
            if (delivers is not null && !delivers(produced))
            {
                continue;
            }

            for (var frame = 0; frame < packetFrames; frame++)
            {
                mono[frame] = signal(fromSeconds + ((produced + frame) / realRate));
            }

            yield return new CapturePacket(
                channel,
                (long)(produced * counts),
                MonotonicInstant.FromMilliseconds(
                    ((CounterStartSeconds + fromSeconds + (produced / realRate)) * 1000)
                    + Jitter(channel, produced, jitterMs)),
                Encode(mono, format));
        }
    }

    /// <summary>
    /// How far off this one packet's instant is. Deliberately a function of the packet rather than
    /// a random number: a probe that measures drift has to be the same measurement tomorrow, and a
    /// suite that fails one run in fifty teaches nobody anything.
    /// </summary>
    private static double Jitter(AudioChannel channel, long position, double jitterMs)
    {
        if (jitterMs <= 0)
        {
            return 0;
        }

        // Knuth's multiplicative hash over the packet's own two numbers, folded into [-1, 1].
        var scrambled = (ulong)((position + 1) * (((long)channel * 7_919) + 1)) * 2_654_435_761UL;
        return ((((scrambled >> 11) % 2_000) / 1_000d) - 1) * jitterMs;
    }

    /// <summary>
    /// The two sources handed over the way a capture hands them over: whichever packet was read at
    /// the earlier instant goes first, so the timeline sees them interleaved rather than one source
    /// and then the other.
    /// </summary>
    /// <param name="clumpedIntoSeconds">
    /// How late a packet may be handed over. Zero is a machine that never fell behind: every packet
    /// arrives the instant its device read it. Anything else is the thread that drains a device
    /// running late and catching up in one go — every packet read inside the same window is handed
    /// over together at the end of it, which is the delivery jitter a busy machine really produces.
    /// </param>
    /// <remarks>
    /// Streaming, and deliberately not a sort: two hours of packets is a couple of million byte
    /// arrays, and materialising them to order them would cost gigabytes to answer a question
    /// already answered by each source arriving in order.
    /// </remarks>
    internal static IEnumerable<CapturePacket> Merged(
        IEnumerable<CapturePacket> loopback,
        IEnumerable<CapturePacket> microphone,
        double clumpedIntoSeconds = 0)
    {
        var clump = (long)(clumpedIntoSeconds * MonotonicInstant.TicksPerSecond);
        long DeliveredAt(CapturePacket packet) =>
            clump <= 0 ? packet.CapturedAt.Ticks : ((packet.CapturedAt.Ticks + clump - 1) / clump) * clump;

        using var left = loopback.GetEnumerator();
        using var right = microphone.GetEnumerator();
        var hasLeft = left.MoveNext();
        var hasRight = right.MoveNext();

        while (hasLeft || hasRight)
        {
            if (hasLeft && (!hasRight || DeliveredAt(left.Current) <= DeliveredAt(right.Current)))
            {
                yield return left.Current;
                hasLeft = left.MoveNext();
            }
            else
            {
                yield return right.Current;
                hasRight = right.MoveNext();
            }
        }
    }

    /// <summary>
    /// The same packets, with the first one saying it opens a stretch of its own at
    /// <paramref name="format"/> — which is what a device that takes a channel over hands the
    /// timeline, and what a spool records where the changeover happened.
    /// </summary>
    /// <remarks>
    /// Said on the first packet and on no other, because that is what a device change is: one seam
    /// and then an ordinary source. A helper rather than a line in each test, since a stretch
    /// declared twice is a stretch of no length between two of them and would pass anyway.
    /// </remarks>
    internal static IEnumerable<CapturePacket> TakingOver(
        StreamFormat format, IEnumerable<CapturePacket> packets)
    {
        var seam = true;
        foreach (var packet in packets)
        {
            yield return seam ? packet with { Opening = format } : packet;
            seam = false;
        }
    }

    /// <summary>
    /// The same packets with the first <paramref name="head"/> of them carrying WASAPI's timestamp
    /// error, which is what a device that has just started is entitled to hand over.
    /// </summary>
    /// <remarks>
    /// The instant on those goes with the flag, and that is the whole point of the fixture: the
    /// flag says the device could not tell when it read the count, so what is left in the field
    /// beside it is whatever the driver had — a zero, a stale value, the tick it was holding. A
    /// test that lowered the flag over a correct instant would be probing the flag rather than what
    /// reading a number the device disowns costs, which an adversarial pass measured as the rest of
    /// a meeting laid down seven seconds early.
    /// </remarks>
    /// <param name="head">How many of the first packets the device will not vouch for.</param>
    /// <param name="packets">The packets it hands over.</param>
    /// <param name="instead">
    /// What is left in the instant of a packet the device disowned. Nothing at all is a driver that
    /// zeroed the field, which reads as an hour before this meeting started.
    /// </param>
    internal static IEnumerable<CapturePacket> Unvouched(
        int head, IEnumerable<CapturePacket> packets, double instead = 0)
    {
        var taken = 0;
        foreach (var packet in packets)
        {
            yield return taken++ < head
                ? packet with
                {
                    TimingIsSound = false,
                    CapturedAt = MonotonicInstant.FromMilliseconds(instead),
                }
                : packet;
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
