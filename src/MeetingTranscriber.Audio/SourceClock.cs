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
/// Two things keep the measurement from being worse than the problem. The reported instant jitters
/// by around a millisecond, so over a short window the ratio is nonsense — a second's worth of
/// packets can suggest a thousand parts per million that are not there — and the nominal rate is
/// used until the window is long enough for that jitter to disappear into it. And whatever the
/// window says, a real audio clock is within a hundred parts per million of its label, so a
/// measurement fifty times further out than that is a broken counter rather than a fast device,
/// and the clamp is what keeps a broken counter from pitch-shifting a meeting.
/// </para>
/// </remarks>
internal sealed class SourceClock
{
    /// <summary>How far from its label a device is believed, either way.</summary>
    private const double Tolerance = 0.005;

    /// <summary>How long the window has to be before the measurement is used at all.</summary>
    private const long Settles = 5 * MonotonicInstant.TicksPerSecond;

    private readonly int nominal;
    private long latestPosition;
    private MonotonicInstant latest;

    internal SourceClock(int nominalRate)
    {
        if (nominalRate <= 0)
        {
            throw new AudioCaptureException($"A device cannot run at {nominalRate} Hz.");
        }

        nominal = nominalRate;
    }

    /// <summary>Whether this source has reported anything at all yet.</summary>
    internal bool Started { get; private set; }

    /// <summary>The instant the source's first reported frame was read at.</summary>
    internal MonotonicInstant Anchor { get; private set; }

    /// <summary>The device position that instant belongs to.</summary>
    internal long AnchorPosition { get; private set; }

    /// <summary>
    /// The device's real rate in frames per second of the machine's clock. The label until the
    /// window is long enough to say otherwise, and never further than <see cref="Tolerance"/>
    /// from it.
    /// </summary>
    internal double Rate
    {
        get
        {
            var ticks = latest.Since(Anchor);
            if (ticks < Settles)
            {
                return nominal;
            }

            var measured = (latestPosition - AnchorPosition) * (double)MonotonicInstant.TicksPerSecond / ticks;
            return Math.Clamp(measured, nominal * (1 - Tolerance), nominal * (1 + Tolerance));
        }
    }

    /// <summary>
    /// Takes in what one packet said: at <paramref name="at"/>, the device had produced
    /// <paramref name="position"/> frames.
    /// </summary>
    internal void Observe(long position, MonotonicInstant at)
    {
        if (!Started)
        {
            Started = true;
            Anchor = at;
            AnchorPosition = position;
        }

        latestPosition = position;
        latest = at;
    }
}
