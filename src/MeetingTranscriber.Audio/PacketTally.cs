using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What one source's packets add up to while it is being recorded: how much of the meeting their
/// device positions cover, how much of that never reached the application, and how far apart the
/// device read one from the next.
/// </summary>
/// <remarks>
/// <para>
/// It counts frames the device reported and never bytes that arrived, which is the whole reason it
/// exists. The two agree exactly until something is lost, and a stretch nobody was handed shows up
/// here as a jump in the positions and in a byte count as nothing at all — so a recording measured
/// by its bytes comes back shorter than the meeting with nothing saying so.
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
    private readonly StreamFormat format;
    private MonotonicInstant previous;
    private long first;
    private long next;
    private long packets;
    private long lost;
    private long unvouched;
    private long closest;
    private long furthest;
    private bool started;
    private bool vouched;
    private bool stepped;

    /// <summary>A tally for a source handing over <paramref name="format"/>.</summary>
    public PacketTally(StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        this.format = format;
    }

    /// <summary>How many blocks the device has handed over.</summary>
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
                return Length(started ? next - first : 0);
            }
        }
    }

    /// <summary>How much of that stretch the device counted and never handed over.</summary>
    public Duration Lost
    {
        get
        {
            lock (gate)
            {
                return Length(lost);
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
    /// The shortest the device went between reading two consecutive packets, and the longest.
    /// Both are the device's own clock: instants stamped on the thread that collected the packets
    /// would instead read as a burst of no time at all and then the whole of one poll.
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

        var frames = Samples.FramesIn(packet.Samples.Length, format);

        lock (gate)
        {
            packets++;

            if (started)
            {
                lost += Math.Max(0, packet.DevicePosition - next);
            }
            else
            {
                started = true;
                first = packet.DevicePosition;
            }

            next = packet.DevicePosition + frames;

            if (!packet.TimingIsSound)
            {
                unvouched++;
                return;
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

    private Duration Length(long frames) =>
        Duration.FromMilliseconds(frames * 1000 / format.SampleRate);
}
