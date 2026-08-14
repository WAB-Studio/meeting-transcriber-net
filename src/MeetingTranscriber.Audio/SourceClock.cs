using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Where one source's own frame counter sits against the machine's monotonic clock, and therefore
/// how fast that device really runs rather than how fast it says it runs.
/// </summary>
/// <remarks>
/// <para>
/// A device labelled 48 kHz is crystal-accurate about its own frames and only approximately right
/// about seconds — two devices at "48 kHz" are two crystals, and over two hours the difference is
/// the drift this whole component exists to remove. The rate here is that difference measured:
/// frames the device produced over the ticks the machine counted while it produced them.
/// </para>
/// <para>
/// The reported instant jitters by around a millisecond, so over a short window the ratio is
/// nonsense — a second's worth of packets can suggest a thousand parts per million that are not
/// there — and the device's label is used until the window is long enough for that jitter to
/// disappear into it. Long enough is whichever clock gets there first: a counter that has frozen
/// while the device goes on producing would otherwise keep the window short for ever and leave the
/// correction switched off with nothing said about it.
/// </para>
/// <para>
/// Once the window is long enough, a measurement further than <see cref="Tolerance"/> from the
/// label stops the recording. A real audio clock is within a hundred parts per million of what it
/// says it is, so fifty times that is a counter describing something other than this device, and
/// there is no answer that makes the two streams line up. Carrying on would put one side of the
/// conversation seconds away from the other with nothing in the file saying so, and the spool is
/// still there to be rebuilt once somebody knows.
/// </para>
/// </remarks>
internal sealed class SourceClock
{
    /// <summary>How far from its label a device can be and still be that device.</summary>
    private const double Tolerance = 0.005;

    /// <summary>How long the window has to be, on either clock, before it is measured at all.</summary>
    private const int SettlesSeconds = 5;

    private readonly AudioChannel channel;
    private readonly int nominal;
    private long latestPosition;
    private MonotonicInstant latest;

    internal SourceClock(AudioChannel channel, int nominalRate)
    {
        if (nominalRate <= 0)
        {
            throw new AudioCaptureException($"A device cannot run at {nominalRate} Hz.");
        }

        this.channel = channel;
        nominal = nominalRate;
        Rate = nominalRate;
    }

    /// <summary>Whether this source has reported anything at all yet.</summary>
    internal bool Started { get; private set; }

    /// <summary>The instant the source's first reported frame was read at.</summary>
    internal MonotonicInstant Anchor { get; private set; }

    /// <summary>The device position that instant belongs to.</summary>
    internal long AnchorPosition { get; private set; }

    /// <summary>
    /// The device's real rate in frames per second of the machine's clock. The label until the
    /// window is long enough to say otherwise.
    /// </summary>
    internal double Rate { get; private set; }

    /// <summary>
    /// Takes in what one packet said: at <paramref name="at"/>, the device had produced
    /// <paramref name="position"/> frames. An instant that does not move forward, or a rate the
    /// pair implies that no device could have, stops here.
    /// </summary>
    internal void Observe(long position, MonotonicInstant at)
    {
        if (!Started)
        {
            Started = true;
            Anchor = at;
            AnchorPosition = position;
            latestPosition = position;
            latest = at;
            return;
        }

        if (at < latest)
        {
            throw new AudioCaptureException(
                $"The {channel} source read frame {position} {latest.Since(at) / 10_000d:0.###} ms "
                + "before the packet in front of it, and a monotonic clock does not go back.");
        }

        latestPosition = position;
        latest = at;
        Measure();
    }

    /// <summary>
    /// Works out what the device really runs at, once either clock says enough of the recording
    /// has gone by for the answer to mean anything.
    /// </summary>
    private void Measure()
    {
        var frames = latestPosition - AnchorPosition;
        var ticks = latest.Since(Anchor);

        if (ticks < SettlesSeconds * MonotonicInstant.TicksPerSecond
            && frames < (long)SettlesSeconds * nominal)
        {
            return;
        }

        var measured = ticks > 0
            ? frames * (double)MonotonicInstant.TicksPerSecond / ticks
            : double.PositiveInfinity;

        if (Math.Abs(measured - nominal) > nominal * Tolerance)
        {
            throw new AudioCaptureException(
                $"The {channel} source produced {frames} frames in "
                + $"{ticks / (double)MonotonicInstant.TicksPerSecond:0.###} s, which is "
                + $"{measured:0} Hz for a device that says it runs at {nominal} Hz. "
                + "One of the two counters is not describing this device, and the two sources "
                + "cannot be lined up against it.");
        }

        Rate = measured;
    }
}
