namespace MeetingTranscriber.Audio;

/// <summary>
/// Where a packet's frames belong, for a source whose device numbers none of them.
/// </summary>
/// <remarks>
/// <para>
/// An endpoint says how many frames it had produced before each packet, and everything downstream
/// lays the recording out by that number. The virtual device behind a process's audio does not: it
/// hands over every packet at frame zero. What it does stamp is the instant it read the packet, and
/// that is the same clock the whole timeline is built on — so a packet is placed by when the device
/// read it, which is the rule already, rather than by how much audio had reached the application,
/// which is the rule this codebase exists to avoid.
/// </para>
/// <para>
/// A packet is never placed before the end of the one before it. Instants a millisecond apart from
/// packets ten milliseconds long would otherwise let ordinary jitter overlap two of them, and an
/// overlap is a stream that cannot be laid out at all. What survives the clamp is the thing worth
/// having: a real dropout, where the instants jump by more than the audio between them, still opens
/// the gap it was, and gets recorded as silence of that length rather than closed up.
/// </para>
/// </remarks>
public sealed class FramePositions
{
    private readonly int sampleRate;
    private MonotonicInstant anchor;
    private long next;
    private bool started;

    /// <summary>Positions for a source arriving at <paramref name="sampleRate"/> frames a second.</summary>
    public FramePositions(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.sampleRate = sampleRate;
    }

    /// <summary>
    /// Where the next packet's first frame goes. Called once per packet, in the order the device
    /// handed them over, because each answer is measured from the one before it.
    /// </summary>
    /// <param name="at">The instant the device read the packet.</param>
    /// <param name="timingIsSound">
    /// Whether the device vouched for that instant. When it did not there is nothing to place the
    /// packet by, so it goes immediately after the packet before it — the samples are still the
    /// meeting, and dropping them over a clock reading would lose a real block of it.
    /// </param>
    /// <param name="frames">How many frames the packet carries.</param>
    public long For(MonotonicInstant at, bool timingIsSound, int frames)
    {
        var where = Where(at, timingIsSound);
        next = where + frames;
        return where;
    }

    private long Where(MonotonicInstant at, bool timingIsSound)
    {
        if (!started)
        {
            started = true;
            anchor = at;
            return 0;
        }

        return timingIsSound
            ? Math.Max(next, at.Since(anchor) * sampleRate / MonotonicInstant.TicksPerSecond)
            : next;
    }
}
