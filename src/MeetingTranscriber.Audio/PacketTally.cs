using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What one source's packets add up to while it is being recorded: how much of the meeting their
/// device positions cover, how much of that never reached the application, and how far apart the
/// device read one from the next.
/// </summary>
/// <remarks>
/// <para>
/// It counts positions and never bytes that arrived, which is the whole reason it exists. The two
/// agree exactly until something is lost, and a stretch nobody was handed shows up here as a jump
/// in the positions and in a byte count as nothing at all — so a recording measured by its bytes
/// comes back shorter than the meeting with nothing saying so.
/// </para>
/// <para>
/// Which positions is <see cref="SourcePositions"/>' to say and not this type's, and asking it is
/// what keeps these numbers and the rebuilt recording's the same. A device numbering its frames in
/// its own rate would otherwise be reported here as losing most of a meeting the file it becomes
/// loses nothing of, and this is the number a person is shown the moment a capture stops.
/// </para>
/// <para>
/// Packets arrive on the capture thread and the numbers are read on whichever thread is reporting
/// them, so both go through one lock, the same way <see cref="SourceMeter"/> does.
/// </para>
/// </remarks>
public sealed class PacketTally
{
    /// <summary>WASAPI's unit for a length of time: 100 nanoseconds.</summary>
    private const long TicksPerMillisecond = MonotonicInstant.TicksPerSecond / 1000;

    private readonly Lock gate = new();

    /// <summary>The device feeding this source now, and where its packets sit.</summary>
    private StreamFormat format;
    private SourcePositions positions;

    private MonotonicInstant previous;
    private long first;
    private long next;
    private long packets;
    private long lost;
    private long unvouched;
    private long closest;
    private long furthest;

    /// <summary>What every device before the one feeding this source now added up to.</summary>
    private long coveredMs;
    private long lostMs;

    /// <summary>
    /// The instant this source's first vouched packet was read at, which is what a seam between two
    /// devices is measured from.
    /// </summary>
    private MonotonicInstant origin;
    private bool anchored;

    private bool started;
    private bool vouched;
    private bool stepped;

    /// <summary>A tally for a source handing over <paramref name="format"/>.</summary>
    public PacketTally(StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        this.format = format;
        positions = new SourcePositions(format.SampleRate);
    }

    /// <summary>
    /// How many blocks this source has been handed, across every device that fed it.
    /// </summary>
    public long Packets
    {
        get
        {
            lock (gate)
            {
                return packets;
            }
        }
    }

    /// <summary>
    /// The stretch of the meeting this source's positions cover, from its first packet's first
    /// frame to its last packet's last one. What is in the file is this less <see cref="Lost"/>.
    /// </summary>
    public Duration Covers
    {
        get
        {
            lock (gate)
            {
                return Duration.FromMilliseconds(coveredMs + Milliseconds(started ? next - first : 0));
            }
        }
    }

    /// <summary>
    /// How much of that stretch was counted and never handed over. The device's own count while its
    /// counter can be laid out, and the clock beside its packets once that counter has been given
    /// up on — the same two answers, from the same place, as the recording this rebuilds into.
    /// </summary>
    public Duration Lost
    {
        get
        {
            lock (gate)
            {
                return Duration.FromMilliseconds(lostMs + Milliseconds(lost));
            }
        }
    }

    /// <summary>
    /// How many packets the device would not vouch for the position and instant of. Their samples
    /// are still the meeting; only the two numbers are worthless, so nothing is measured against
    /// them.
    /// </summary>
    public long Unvouched
    {
        get
        {
            lock (gate)
            {
                return unvouched;
            }
        }
    }

    /// <summary>
    /// The shortest the source went between reading two consecutive packets, and the longest. A
    /// device change does not start them again, so both span every device that fed the channel.
    /// Both are read off the device's own clock: instants stamped on the thread that collected the
    /// packets would instead read as a burst of no time at all and then the whole of one poll.
    /// </summary>
    public Duration Closest
    {
        get
        {
            lock (gate)
            {
                return stepped ? Duration.FromMilliseconds(closest / TicksPerMillisecond) : Duration.Zero;
            }
        }
    }

    /// <inheritdoc cref="Closest"/>
    public Duration Furthest
    {
        get
        {
            lock (gate)
            {
                return Duration.FromMilliseconds(furthest / TicksPerMillisecond);
            }
        }
    }

    /// <summary>Takes in one packet as the device handed it over.</summary>
    /// <remarks>
    /// A packet whose instant the device would not vouch for is placed by its position like any
    /// other, and only the step to it goes unmeasured. WASAPI's timestamp error says it could not
    /// say when it read the count, never that the count is wrong — so skipping the packet here would
    /// lose a real block of the meeting out of the length over a clock this does not need to read.
    /// The same rule as <see cref="TimelineSource"/>, and it has to be, or the length a recording
    /// reports and the recording it rebuilds into would disagree.
    /// </remarks>
    public void Add(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        lock (gate)
        {
            if (packet.Opening is { } opening)
            {
                Changed(opening, packet);
            }

            var frames = Samples.FramesIn(packet.Samples.Length, format);
            packets++;

            // The same numbers the rebuild will lay the recording out on, from the same place, and
            // it has to be: a device counting in its own rate would otherwise report every packet
            // of the meeting as lost while the recording it rebuilds into loses nothing.
            var position = positions.For(packet, frames);

            if (started)
            {
                lost += Math.Max(0, position - next);
            }
            else
            {
                started = true;
                first = position;
            }

            next = position + frames;

            if (!packet.TimingIsSound)
            {
                unvouched++;
                return;
            }

            if (!anchored)
            {
                anchored = true;
                origin = packet.CapturedAt;
            }

            // Measured against the last instant the device did vouch for, which is why a packet it
            // did not vouch for is skipped here and not counted as a step of no length.
            if (vouched)
            {
                Step(packet.CapturedAt);
            }

            previous = packet.CapturedAt;
            vouched = true;
        }
    }

    /// <summary>How far apart the device read this packet and the one before it.</summary>
    /// <remarks>
    /// Never negative. A clock that went backwards is a recording that cannot be laid out, and the
    /// rebuild is where that has to stop it — refusing here would take a finished meeting down at
    /// the moment somebody asked how long it was.
    /// </remarks>
    private void Step(MonotonicInstant at)
    {
        var apart = Math.Max(0, at.Since(previous));

        closest = stepped ? Math.Min(closest, apart) : apart;
        furthest = Math.Max(furthest, apart);
        stepped = true;
    }

    /// <summary>
    /// Closes off the device that is leaving and starts the one taking over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything above is counted in one device's frames, and the device replacing it numbers its
    /// own from its own zero — so what the one leaving covered and lost is banked in the unit the
    /// two share before either number can be read as the other's.
    /// </para>
    /// <para>
    /// The seam between them is on the clock and nowhere else, for the same reason: no counter
    /// spans it. It is measured from this source's own origin and clamped at what it has already
    /// covered, which is the rule <see cref="TimelineSource"/> places a stretch by — and it has to
    /// be the same rule, or the length a person is shown when a capture stops and the length of the
    /// recording it rebuilds into would disagree at every changeover. It is time the meeting ran
    /// and nobody was handed, which is covered and lost at once.
    /// </para>
    /// </remarks>
    private void Changed(StreamFormat opening, CapturePacket packet)
    {
        coveredMs += Milliseconds(started ? next - first : 0);
        lostMs += Milliseconds(lost);

        if (anchored && packet.TimingIsSound)
        {
            var seam = Math.Max(0, (packet.CapturedAt.Since(origin) / TicksPerMillisecond) - coveredMs);
            coveredMs += seam;
            lostMs += seam;
        }

        format = opening;
        positions = new SourcePositions(opening.SampleRate);
        started = false;
        vouched = false;
        first = 0;
        next = 0;
        lost = 0;
    }

    private long Milliseconds(long frames) => frames * 1000 / format.SampleRate;
}
