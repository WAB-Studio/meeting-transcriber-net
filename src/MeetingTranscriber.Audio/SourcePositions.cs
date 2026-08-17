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
/// It does not ask why the counter is wrong, and it does not have to. A counter counting in another
/// rate and a driver whose counter is simply broken are the same news — this source's numbers cannot
/// be laid out — and the answer to both is to stop reading them. The clock beside each packet is
/// the same clock the whole timeline is built on, so a source placed by it is placed by the rule
/// that was already in force, and a stretch the device really dropped still opens the gap it was.
/// </para>
/// <para>
/// The cost is named rather than hidden, and it is larger than it first looks. A source placed this
/// way is measured against the very clock its positions were computed from, so it reports its own
/// rate as exactly the rate it was opened at whatever it really ran at: the drift correction has
/// nothing to steer by, and the check that stops a device whose two counters disagree cannot fire
/// on it. For a source whose device numbers nothing that is right rather than a hole — the audio
/// engine mixes at one rate and there is no crystal to disagree with. Here there is one. A device
/// that both counts in its own unit and runs at a rate other than its label would be recorded at
/// its label, and what says so is the stretch that comes back as missing, because the frames handed
/// over and the clock are then the two numbers that disagree. Measuring such a device needs the
/// rate its counter counts in, which nothing on this side of WASAPI reports, and it is a board task
/// rather than a guess made here.
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
    private readonly int sampleRate;
    private FramePositions? placed;
    private long origin;
    private long next;
    private bool started;

    /// <summary>Positions for a source handing over <paramref name="sampleRate"/> frames a second.</summary>
    internal SourcePositions(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.sampleRate = sampleRate;
    }

    /// <summary>
    /// Whether this source's device numbered its frames in something other than the frames it
    /// handed over, and its counter was given up on.
    /// </summary>
    internal bool CounterGivenUp { get; private set; }

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

        CounterGivenUp |= packet.DevicePosition < next;

        // Never behind where the last packet ended. The two answers are measured from different
        // things — one from the device's counter, the other from the clock — so at the changeover
        // they can disagree by whatever the source has drifted and by whatever it lost: a stretch
        // the device dropped puts its counter ahead of both the frames handed over and the clock,
        // and the clock's answer then lands short of a position already used. Sending a source
        // backwards is the one thing giving the counter up exists to avoid, so the changeover
        // costs the difference as a shorter first packet rather than as an overlap.
        var where = CounterGivenUp ? Math.Max(elsewhere, next) : packet.DevicePosition;
        next = where + frames;

        return where;
    }
}
