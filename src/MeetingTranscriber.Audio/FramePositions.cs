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
/// <para>
/// A consequence worth saying out loud: a source placed this way measures its own rate as exactly
/// the rate it was opened at, so the check that stops a device whose two counters disagree can
/// never fire on it. That is right rather than a hole. The disagreement that check exists for is a
/// crystal running at its own speed, and there is no crystal here — the audio engine mixes at one
/// rate and hands over what it mixed. Frames short against the clock are a dropout and not a slow
/// device, which is exactly what opening the gap says.
/// </para>
/// </remarks>
public sealed class FramePositions
{
    private readonly int sampleRate;
    private MonotonicInstant anchor;
    private long anchoredAt;
    private long next;
    private bool anchored;

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
    /// meeting, and dropping them over a clock reading would lose a real block of it. A stream that
    /// opens with one of those is not anchored to it either: every later packet is measured from
    /// the anchor, so anchoring on a reading the device disowned would put the whole recording
    /// wherever that number happened to fall.
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
        if (!timingIsSound)
        {
            return next;
        }

        if (!anchored)
        {
            anchored = true;
            anchor = at;

            // Whatever the packets before this one covered, which is nothing unless the stream
            // opened with instants the device disowned.
            anchoredAt = next;
            return next;
        }

        return Math.Max(next, anchoredAt + (at.Since(anchor) * sampleRate / MonotonicInstant.TicksPerSecond));
    }
}
