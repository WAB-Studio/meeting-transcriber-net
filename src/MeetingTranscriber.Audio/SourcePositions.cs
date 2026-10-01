namespace MeetingTranscriber.Audio;

/// <summary>
/// Where one source's packets sit on the recording, counted in the frames that source hands over —
/// its device's own numbers while those can be laid out, and the clock beside them once they cannot.
/// </summary>
/// <remarks>
/// <para>
/// A shared-mode client is handed the format it asked for, converted; the frame counter beside it is
/// not converted with it. A webcam microphone opened at the endpoint's 48 kHz hands over 480 frames
/// a packet and advances its counter by 160, which are its own 16 kHz frames — so the two numbers on
/// one packet are in two units, and only one of them is the client's. Read as though they agreed,
/// the counter reads as going backwards on the second packet and the whole meeting is refused.
/// </para>
/// <para>
/// What is worth noticing is that the reading which used to refuse the recording is the detection.
/// A counter in the client's frames advances by exactly the frames delivered, or by more when the
/// device dropped a stretch nobody was handed — never by less, which would be a device claiming it
/// produced fewer frames than it just handed over. So a position short of where the last packet
/// ended is not a device that lost audio, and it is the one thing this looks for.
/// </para>
/// <para>
/// A counter that is wrong is not always a counter that cannot be used: a device that counts in a
/// rate of its own counts evenly, and the unit is in the packets. The counter advances by the
/// device's own frames while the frames handed over are those frames converted at the label ratio,
/// so the two summed over a second of packets name the rate it counts in. One pair does not — the
/// webcam's first packet was 463 frames and every other 480, and that pair alone names a rate no
/// device has — so the decision is never taken on less than a second of them.
/// </para>
/// <para>
/// Until then the source is placed by the clock, as a source whose device numbers nothing is, and
/// two sums are kept over every consecutive pair of packets from the one that revealed the
/// mismatch on: the counter's advance across the pair, and the frames the earlier packet handed
/// over. The pair that starts at the source's first packet is never counted, because a first
/// packet can be short. Once the frames summed reach a second of the label, the candidate rate is
/// the label times the advance over the frames, and it is read only if it lands within half a
/// percent of one rate of a closed table below the label. Over a second quantisation is gone, and
/// a part in ten thousand off such a rate is drift, which is why the candidate is snapped and never
/// used as it stands. The table is closed because the rates a device counts in are the rates audio
/// hardware is built to; a counter no rate of it explains is one nothing can be said about.
/// </para>
/// <para>
/// A counter that merely read low once and then counted in the frames handed over sums to a ratio
/// of about one, which nothing below the label is within half a percent of, so it is given up and
/// the meeting is recorded, never rescaled ahead of itself. What was placed before the decision
/// keeps the clock's placement; nothing already placed is moved. From the switch on a position is
/// the one the switch was placed at plus the counter's advance since, in frames of the label, so
/// the switch opens neither a gap nor an overlap. A rescaled position that then falls more than
/// one counting unit behind where the last packet ended gives the counter up as well, and the
/// source goes back to the clock.
/// </para>
/// <para>
/// The decision is taken once. A stretch the device really dropped inside that first second adds
/// to the advance, so the candidate misses every rate and the counter is given up for the rest of
/// the source, which is the same news as a counter nothing explains and costs the drift measurement
/// and nothing else.
/// </para>
/// <para>
/// A source placed by the clock is measured against the very clock its positions were computed
/// from, so it reports its own rate as exactly the rate it was opened at: the drift correction has
/// nothing to steer by, and what says a device ran at another rate is the stretch that comes back
/// as missing. That is the cost of a counter that is given up, and it is why a rate is the label
/// from the moment the mismatch is seen until one is read. Two readings say which kind of label it
/// is: <see cref="CounterGivenUp"/> is the decision taken, and <see cref="CounterUndecided"/> is
/// the window still open — a source that ends inside its window was placed by the clock
/// throughout and was never decided either way.
/// </para>
/// <para>
/// The check still holds in full for every source whose counter was usable, which is the case it
/// was written against: a crystal running at its own speed reports its frames in the client's unit
/// and disagrees with its clock, never advancing by less than it handed over, so nothing here is
/// ever reached for it.
/// </para>
/// </remarks>
internal sealed class SourcePositions
{
    private const double Tolerance = 0.005;

    /// <summary>
    /// The rates a device is known to count in. Closed on purpose: the candidate a second of
    /// packets gives is never exact, and the only way to tell a unit from drift is to ask which
    /// rate audio hardware is built to run at it is within half a percent of.
    /// </summary>
    private static readonly int[] CountingRates =
        [8_000, 11_025, 12_000, 16_000, 22_050, 24_000, 32_000, 44_100, 48_000, 88_200, 96_000, 176_400];

    private readonly int sampleRate;
    private FramePositions? placed;
    private long origin;
    private long next;
    private bool started;
    private Phase phase;

    private long previousDevice;
    private int previousFrames;
    private long advanced;
    private long handedOver;

    private int countingRate;
    private long anchorDevice;
    private long anchorPlaced;

    /// <summary>Positions for a source handing over <paramref name="sampleRate"/> frames a second.</summary>
    internal SourcePositions(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.sampleRate = sampleRate;
    }

    private enum Phase
    {
        /// <summary>The counter has not disagreed with the frames handed over.</summary>
        Trusted,

        /// <summary>It disagreed, and a second of packets has not yet said in what unit.</summary>
        Window,

        /// <summary>A rate explains it, and positions are read in that rate.</summary>
        Rescaled,

        /// <summary>No rate explains it, or one stopped doing so.</summary>
        GivenUp,
    }

    /// <summary>
    /// Whether this source's device counted its frames in a way no rate explains, or one that
    /// stopped explaining them, so its counter was given up and its audio placed by the clock.
    /// </summary>
    /// <remarks>
    /// The decision, taken: true from the moment no rate explained the window, and never for a
    /// window still open — that is <see cref="CounterUndecided"/>. Either way the rate is the label
    /// and not a measurement.
    /// </remarks>
    internal bool CounterGivenUp => phase is Phase.GivenUp;

    /// <summary>
    /// Whether the packet that revealed a mismatch was followed by a window that had not yet
    /// explained it or ruled out every rate when this source ended or was replaced.
    /// </summary>
    /// <remarks>
    /// True from the packet that reveals the mismatch until a rate is read off the window that
    /// follows it or the counter is given up, and false once either has happened. A source that
    /// ends inside its window was placed by the clock throughout, so its rate is the label and not
    /// a measurement — but nothing was decided about its counter, which is the difference a
    /// diagnosis of drift needs: a source that ended in its first second must not be read as having
    /// given its counter up.
    /// </remarks>
    internal bool CounterUndecided => phase is Phase.Window;

    /// <summary>
    /// Where <paramref name="packet"/>'s first frame goes, in the frames this source hands over.
    /// Called once per packet, in the order the device handed them over, because every answer is
    /// measured from the one before it.
    /// </summary>
    /// <param name="packet">The packet, carrying its device's position and instant.</param>
    /// <param name="frames">How many frames of this source's format it carries.</param>
    internal long For(CapturePacket packet, int frames)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // Kept in step on every packet whether or not it is the one being read: the changeover
        // happens on the packet that reveals the mismatch, and there has to already be a position
        // to carry on from by then. It counts from zero, as a source whose device numbers nothing
        // does, and the frame this source's first packet claimed is added back on — so what is
        // placed here and what the device reported before it are numbers on the same recording.
        placed ??= new FramePositions(sampleRate);
        var elsewhere = origin + placed.For(packet.CapturedAt, packet.TimingIsSound, frames);

        if (!started)
        {
            started = true;
            origin = packet.DevicePosition;
            next = packet.DevicePosition + frames;
            return packet.DevicePosition;
        }

        if (phase == Phase.Trusted && packet.DevicePosition < next)
        {
            // The pair that revealed it is not counted: this packet is the later of it, and the
            // window starts with the pair this packet opens.
            phase = Phase.Window;
            previousDevice = packet.DevicePosition;
            previousFrames = frames;
        }
        else if (phase == Phase.Window)
        {
            advanced += packet.DevicePosition - previousDevice;
            handedOver += previousFrames;
            previousDevice = packet.DevicePosition;
            previousFrames = frames;
        }

        // Never behind where the last packet ended. The two answers are measured from different
        // things — one from the device's counter, the other from the clock — so at a changeover
        // they can disagree by whatever the source has drifted and by whatever it lost: a stretch
        // the device dropped puts its counter ahead of both the frames handed over and the clock,
        // and the clock's answer then lands short of a position already used. Sending a source
        // backwards is the one thing giving the counter up exists to avoid, so a changeover costs
        // the difference as a shorter first packet rather than as an overlap.
        var byTheClock = Math.Max(elsewhere, next);
        long where;

        switch (phase)
        {
            case Phase.Window:
                where = byTheClock;

                if (handedOver >= sampleRate)
                {
                    DecideTheRate(packet.DevicePosition, where);
                }

                break;
            case Phase.Rescaled:
                var rescaled = anchorPlaced
                    + (long)Math.Round((packet.DevicePosition - anchorDevice) * (double)sampleRate / countingRate);

                // One counting unit of slack, which is all rounding to the label can cost; past it
                // the counter is going back on itself in its own rate and is given up like any.
                if (rescaled < next - UnitInFrames())
                {
                    phase = Phase.GivenUp;
                    where = byTheClock;
                }
                else
                {
                    where = Math.Max(rescaled, next);
                }

                break;
            case Phase.GivenUp:
                where = byTheClock;
                break;
            default:
                where = packet.DevicePosition;
                break;
        }

        next = where + frames;

        return where;
    }

    private long UnitInFrames() => (sampleRate + countingRate - 1) / countingRate;

    /// <summary>
    /// Closes the window: reads the counter in the table's rate that explains the second just
    /// summed, switching on <paramref name="device"/> at <paramref name="placedAt"/>, or gives it
    /// up when none does.
    /// </summary>
    private void DecideTheRate(long device, long placedAt)
    {
        var candidate = sampleRate * (double)advanced / handedOver;
        var best = 0;
        var bestOff = double.MaxValue;

        foreach (var rate in CountingRates)
        {
            var off = Math.Abs(candidate - rate);

            if (rate < sampleRate && off <= rate * Tolerance && off < bestOff)
            {
                best = rate;
                bestOff = off;
            }
        }

        if (best == 0)
        {
            phase = Phase.GivenUp;
            return;
        }

        phase = Phase.Rescaled;
        countingRate = best;
        anchorDevice = device;
        anchorPlaced = placedAt;
    }
}
