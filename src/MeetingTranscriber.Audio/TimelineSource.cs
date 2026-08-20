using System.Diagnostics.CodeAnalysis;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Dsp;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One source on its way onto the shared timeline: its packets decoded, the stretches it never
/// delivered put back as real silence, and the whole thing resampled onto the interchange format
/// at the speed the machine's clock says it actually ran.
/// </summary>
/// <remarks>
/// <para>
/// The correction is the resampling ratio and nothing else. A source running a hundred parts per
/// million fast is not trimmed at the end and not nudged with a dropped block: every stretch of it
/// is converted at a ratio steered to land where the shared clock says that stretch belongs, so
/// the recording is aligned at every point of itself rather than only at its close. That is what
/// makes the correction inaudible — a tenth of a percent of pitch, spread over seconds — and it is
/// also what makes a citation land on the right words an hour into a meeting.
/// </para>
/// <para>
/// The loop is closed on the output rather than trusted open: each feed asks where the shared
/// clock says this source should have reached, subtracts what it has actually produced, and picks
/// the ratio that covers the difference. Anything the resampler's own filter delay or a rounded
/// packet boundary costs is therefore absorbed by the next feed instead of accumulating.
/// </para>
/// <para>
/// <b>A source is a sequence of stretches, not one device.</b> A microphone unplugged mid meeting
/// is replaced by whatever Windows moves to, and what takes over numbers its frames from its own
/// zero and is free to hand over another format entirely — so each stretch gets its own format, its
/// own clock, its own frame counter and its own resampler, and only what this source has produced
/// in the end runs through all of them. Everything between the last frame of one stretch and the
/// first of the next is audio that never arrived: silence of exactly that length, counted as
/// missing. Most sources are one stretch and nothing below is reached for them.
/// </para>
/// <para>
/// The gap is the one silence here that does not go through a resampler, and that is not an
/// inconsistency. Inside a stretch, silence has to be converted at the same ratio as the audio
/// around it or the filter loses phase and the count is wrong at the far end. At a seam there is
/// nothing either side: the stretch that ended has been emptied and the one taking over has not
/// begun, so zeroes at the interchange rate are exactly as long as they claim to be.
/// </para>
/// </remarks>
internal sealed class TimelineSource
{
    /// <summary>How much the ratio may be steered away from the device's label, either way.</summary>
    /// <remarks>
    /// Wider than <see cref="SourceClock"/>'s own tolerance on purpose: the leftover is the room
    /// the loop has to work off an error it has already accumulated, and half a percent recovers
    /// fifty milliseconds in ten seconds without anything being audible.
    /// </remarks>
    private const double Steering = 0.01;

    /// <summary>How many source frames go through the resampler in one go.</summary>
    private const int FeedFrames = 16_384;

    private readonly AudioChannel channel;
    private readonly SampleBuffer buffer = new();

    /// <summary>The stretch being taken now: its format, its clock, its counter, its resampler.</summary>
    private StreamFormat format;
    private SourceClock clock;
    private SourcePositions positions;
    private WdlResampler resampler;

    private float[] decoded = [];
    private float[] resampled = [];
    private short[] written = [];
    private float[] silence = [];
    private short[] quiet = [];

    /// <summary>When this source's very first frame was read, which is what it is placed by.</summary>
    private MonotonicInstant origin;

    /// <summary>
    /// Frames of this source produced before the first frame of the stretch being taken now, so
    /// that where a stretch belongs on the recording is settled once rather than steered towards.
    /// </summary>
    private long stretchStart;

    /// <summary>
    /// The device position this stretch's first frame sat at, which is what every later frame of it
    /// is measured from. Its own rather than the clock's anchor: the two are the same packet, and
    /// reading it off the clock would tie where a stretch is laid out to which packet happened to
    /// be worth measuring a rate against.
    /// </summary>
    private long stretchFirstPosition;

    /// <summary>
    /// How much of this source's own timeline the recording has already been written without,
    /// which is nothing unless it was given up on. Set by the timeline at the moment it gives up
    /// and read once, by the stretch that comes back — see <see cref="GivenUpAt"/>.
    /// </summary>
    private long writtenWithout;

    private long nextPosition;
    private long missing;
    private int stretches;
    private bool started;
    private bool speaking;
    private bool counterGivenUp;

    internal TimelineSource(AudioChannel channel, StreamFormat format)
    {
        this.channel = channel;
        Open(format);
    }

    /// <summary>Which of the two channels this source feeds.</summary>
    internal AudioChannel Channel => channel;

    /// <summary>Whether any packet of this source has arrived.</summary>
    internal bool Started => started;

    /// <summary>
    /// Whether this source has a device open that has not yet laid down a frame — the moment
    /// between two of them, and the moment before the first one speaks.
    /// </summary>
    /// <remarks>
    /// What it is for is the timeline's own give-up: a source it went on without says where it
    /// belongs on the first block of the device taking over that it can place, and the blocks ahead
    /// of that one are the head it is waiting out. Without this the packet after a head that was
    /// waited out reads as a source that stalled and came back as itself, which is the one thing
    /// with nowhere to go.
    /// </remarks>
    internal bool Between => !speaking;

    /// <summary>When this source's first frame was read, on the shared clock.</summary>
    internal MonotonicInstant Anchor => origin;

    /// <summary>
    /// What the device really ran at, against that clock — the device of the stretch this source
    /// ended on.
    /// </summary>
    /// <remarks>
    /// A source that changed device ran at as many rates as it had devices, and there is no single
    /// answer to give: what <see cref="Stretches"/> says is how many of them there were, so a rate
    /// read off a source of more than one stretch is read as the last one's rather than as the
    /// meeting's.
    /// </remarks>
    internal double Rate => clock.Rate;

    /// <summary>
    /// How many devices fed this source over the recording. One for almost every recording; more
    /// when a device was unplugged or Windows moved the channel to another one.
    /// </summary>
    internal int Stretches => stretches;

    /// <summary>
    /// Whether any of this source's devices numbered its frames in something other than the frames
    /// it handed over, and that counter was given up on. <see cref="Rate"/> is then the label and
    /// not a measurement, because what a rate is measured from is the counter that was given up on.
    /// </summary>
    internal bool CounterGivenUp => counterGivenUp || positions.CounterGivenUp;

    /// <summary>Frames of the interchange format this source has produced since its first one.</summary>
    internal long Produced { get; private set; }

    /// <summary>How many of those are still waiting to be handed on.</summary>
    internal int Waiting => buffer.Count;

    /// <summary>Takes the oldest <paramref name="into"/>.Length frames this source produced.</summary>
    internal void Read(Span<short> into) => buffer.Take(into);

    /// <summary>
    /// Frames of the recording this source produced nothing for, counted in the frames the silence
    /// really became rather than in the frames the device would have sent. A stretch the device
    /// dropped goes through the same resampling as the audio around it, so counting it at the label
    /// instead would misreport a ten minute dropout by seconds against the silence actually
    /// written; the seam between two devices is counted at the interchange rate, which is what it
    /// really became.
    /// </summary>
    internal long Missing => missing;

    /// <summary>
    /// Empties the resampler of what it was still holding. It keeps the last of its input back
    /// until something follows it, so without this the end of every recording is quietly a few
    /// milliseconds short of the meeting — and a recording of one packet is nothing at all.
    /// </summary>
    internal void Finish() => Drain();

    /// <summary>
    /// Takes one packet. A position ahead of where the last packet ended is a stretch the device
    /// dropped and becomes silence of exactly that length; a position behind it is a device whose
    /// counter is not in the frames it hands over, and <see cref="SourcePositions"/> is what decides
    /// that and what is placed instead — so nothing here has to know which of the two happened.
    /// </summary>
    /// <param name="packet">
    /// The packet. One carrying a format is the first of another device's stretch, and what came
    /// before it is closed off, the seam counted, and everything below started again.
    /// </param>
    internal void Take(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Opening is { } opening)
        {
            // Everything the device that is leaving was still holding, before the seam after it —
            // and before the format changes, because emptying it is done at that device's own rate.
            Drain();
            counterGivenUp |= positions.CounterGivenUp;
            Open(opening);
        }

        var first = !speaking;

        if (first && !packet.TimingIsSound)
        {
            // The source's very first packet, and there is nothing to measure the rest of it
            // against — not the recording, and not another stretch of this source.
            if (!started)
            {
                throw new AudioCaptureException(
                    $"The {channel} source opened with a packet whose position and instant the "
                    + "device would not vouch for, and there is nothing to measure the rest of it "
                    + "against.");
            }

            // A stretch's first packet, and it decides where the whole stretch is laid out. An
            // instant the device disowned would put the seam wherever that number happened to fall
            // — and, worse, put every frame of the rest of the meeting there with it, silently,
            // because the frames after it are measured from this one. So the head of a stretch is
            // waited out rather than placed: what it costs is the few milliseconds of it, which
            // land inside the seam that is already missing, and what it buys is that a device
            // taking over is either in the right place or not there at all.
            return;
        }

        var frames = Samples.FramesIn(packet.Samples.Length, format);

        // Settled before anything reads it. The clock below measures how fast this device ran from
        // the positions it is given, so handing it the device's own number for a device that counts
        // in another rate would measure that rate and stop a recording this now records.
        var position = positions.For(packet, frames);

        // An unsound packet's samples are still the meeting and its position is still where they
        // go; only the instant beside it is worthless, and a rate measured against that would be
        // worse than the label it replaced. So the packet is laid out below like any other and it
        // is the observation, and only the observation, that is skipped.
        //
        if (packet.TimingIsSound)
        {
            clock.Observe(position, packet.CapturedAt);
        }

        if (first)
        {
            speaking = true;
            stretchFirstPosition = position;
            Begin(packet);
            nextPosition = position;
        }
        else if (position < nextPosition)
        {
            // Not reachable from any device: positions come back from one place and that place
            // never goes back on itself. It is here because the alternative to saying so is a
            // second of the meeting quietly written over by a later one.
            throw new AudioCaptureException(
                $"The {channel} source was placed at frame {position} after reaching {nextPosition}, "
                + "which is a fault in this build rather than anything the device did.");
        }
        else if (position > nextPosition)
        {
            var before = Produced;
            FeedSilence(position - nextPosition, nextPosition);
            missing += Produced - before;
        }

        if (frames > 0)
        {
            EnsureDecoded(frames);
            Samples.ToMono(packet.Samples.Span, format, decoded);
            Feed(decoded.AsSpan(0, frames), position);
        }

        nextPosition = position + frames;
    }

    /// <summary>
    /// Starts a stretch: its format, its clock, its frame counter and a resampler of its own.
    /// </summary>
    [MemberNotNull(nameof(format), nameof(clock), nameof(positions), nameof(resampler))]
    private void Open(StreamFormat opening)
    {
        ArgumentNullException.ThrowIfNull(opening);

        // Asked before a packet can arrive rather than answered by the first one: a width nothing
        // here can read is knowable now, and a source that never speaks would otherwise close as
        // clean silence on a format this build cannot read at all.
        Samples.ReaderFor(opening);

        format = opening;
        clock = new SourceClock(channel, opening.SampleRate);
        positions = new SourcePositions(opening.SampleRate);
        speaking = false;
        stretches++;

        // A resampler of its own rather than the last one told a new rate. It holds the tail of its
        // input and a filter state built from it, and both belong to the device that is leaving —
        // carried across, they would be that device's last milliseconds converted at the next one's
        // ratio and laid down inside the seam.
        resampler = new WdlResampler();

        // Interpolation with two filter passes rather than a windowed sinc. This runs over the
        // whole meeting at the moment somebody stops it, and sixty-four taps across two hours of
        // 48 kHz is a minute of a person watching a progress bar for a difference they would have
        // to be told about. If a transcript ever reads as though the fricatives were mangled,
        // SetMode(true, 0, true) is the knob and the cost is the reason it is not the default.
        resampler.SetMode(interp: true, filtercnt: 2, sinc: false);
        resampler.SetFeedMode(wantInputDriven: true);
    }

    /// <summary>
    /// Settles where this stretch's first frame goes on the recording, and puts the seam in front
    /// of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured from this source's own origin on the shared clock, which is the rule every packet
    /// inside a stretch is placed by too — the device that took over shares no counter with the one
    /// that left, so its own numbers say nothing about where it belongs. Never before what this
    /// source has already produced, and never before what the recording has already been written
    /// without: audio placed behind either would be laid down over frames that are already the
    /// meeting.
    /// </para>
    /// <para>
    /// One rule and not two, the source's first stretch included. Its origin is its own first
    /// frame, so the arithmetic below settles it at nothing — unless the recording has already been
    /// written past it, which is a source given up on before it ever spoke and then handed a
    /// working device. That one used to be laid out from frame zero, forty seconds behind a
    /// recording that had gone on without it, and it is the same question as every other stretch's.
    /// </para>
    /// </remarks>
    private void Begin(CapturePacket packet)
    {
        if (!started)
        {
            started = true;
            origin = packet.CapturedAt;
        }

        var since = (long)Math.Round(
            packet.CapturedAt.Since(origin) * (double)CapturedAudio.SampleRate
            / MonotonicInstant.TicksPerSecond);

        var already = Math.Max(Produced, writtenWithout);
        stretchStart = Math.Max(already, since);

        // The whole seam is audio that never arrived. What is queued of it is only the part the
        // recording has not already been written without, because those frames are on the disk as
        // silence already — queueing them a second time would push this device's audio that far
        // late for the rest of the meeting.
        var seam = stretchStart - Produced;
        var queued = stretchStart - already;

        missing += seam;
        Produced += seam - queued;
        Quiet(queued);
    }

    /// <summary>
    /// Says that the recording went on without this source and reached <paramref name="frames"/> of
    /// its timeline while it was quiet.
    /// </summary>
    /// <remarks>
    /// Said once, at the moment the recording gives up rather than on every packet, and it is the
    /// timeline's to say: how far the file has been written is not something a source can see, and
    /// a running cursor handed in packet by packet would be the timeline's own bookkeeping crossing
    /// into it. What it changes is where the device that takes over is placed — the recording did
    /// not wait and cannot go back, so the stretch begins from there and everything before it is
    /// the audio that never arrived.
    /// </remarks>
    internal void GivenUpAt(long frames) => writtenWithout = Math.Max(writtenWithout, frames);

    /// <summary>Queues <paramref name="frames"/> of the interchange format's own silence.</summary>
    private void Quiet(long frames)
    {
        if (frames <= 0)
        {
            return;
        }

        if (quiet.Length < FeedFrames)
        {
            quiet = new short[FeedFrames];
        }

        for (long done = 0; done < frames;)
        {
            var take = (int)Math.Min(quiet.Length, frames - done);
            buffer.Add(quiet.AsSpan(0, take));
            done += take;
        }

        Produced += frames;
    }

    /// <summary>
    /// Empties the resampler of the stretch being taken now. It keeps the last of its input back
    /// until something follows it, so without this every seam and the end of every recording is
    /// quietly a few milliseconds short of the meeting.
    /// </summary>
    private void Drain()
    {
        if (!speaking)
        {
            return;
        }

        // Asking for room and then handing over less than was asked for is how the resampler is
        // told there is no more coming.
        resampler.ResamplePrepare(1, 1, out _, out _);
        EnsureResampled(FeedFrames);
        Keep(resampler.ResampleOut(resampled, 0, 0, resampled.Length, 1));
    }

    /// <summary>Puts <paramref name="frames"/> of nothing where the device delivered nothing.</summary>
    private void FeedSilence(long frames, long from)
    {
        if (silence.Length < FeedFrames)
        {
            silence = new float[FeedFrames];
        }

        for (long done = 0; done < frames;)
        {
            var take = (int)Math.Min(FeedFrames, frames - done);
            Resample(silence.AsSpan(0, take), from + done + take);
            done += take;
        }
    }

    /// <summary>
    /// Puts <paramref name="input"/> through in bounded pieces, so a device that slept for ten
    /// minutes costs the same working memory as one that never missed a packet.
    /// </summary>
    private void Feed(ReadOnlySpan<float> input, long from)
    {
        for (var done = 0; done < input.Length;)
        {
            var take = Math.Min(FeedFrames, input.Length - done);
            Resample(input.Slice(done, take), from + done + take);
            done += take;
        }
    }

    /// <summary>
    /// Converts one piece, at the ratio that puts its last frame where the shared clock says the
    /// device had reached by then.
    /// </summary>
    private void Resample(ReadOnlySpan<float> input, long endPosition)
    {
        var rate = clock.Rate;
        var target = stretchStart
            + ((endPosition - stretchFirstPosition) * CapturedAudio.SampleRate / rate);
        var wanted = target - Produced;

        // Non-positive means this source is already ahead of the clock, and the clamp turns the
        // infinity into "convert as slowly as you are allowed to" — which is the answer.
        var ratio = wanted > 0 ? input.Length * (double)CapturedAudio.SampleRate / wanted : double.MaxValue;
        resampler.SetRates(
            Math.Clamp(ratio, format.SampleRate * (1 - Steering), format.SampleRate * (1 + Steering)),
            CapturedAudio.SampleRate);

        for (var fed = 0; fed < input.Length;)
        {
            var room = resampler.ResamplePrepare(input.Length - fed, 1, out var into, out var offset);
            var taken = Math.Min(room, input.Length - fed);
            if (taken <= 0)
            {
                throw new AudioCaptureException($"The {channel} resampler stopped taking samples.");
            }

            input.Slice(fed, taken).CopyTo(into.AsSpan(offset, taken));
            fed += taken;

            EnsureResampled(taken);
            Keep(resampler.ResampleOut(resampled, 0, taken, resampled.Length, 1));
        }
    }

    /// <summary>Puts what the resampler just made into the interchange format and queues it.</summary>
    private void Keep(int made)
    {
        if (made <= 0)
        {
            return;
        }

        Samples.ToPcm16(resampled.AsSpan(0, made), written);
        buffer.Add(written.AsSpan(0, made));
        Produced += made;
    }

    private void EnsureDecoded(int frames)
    {
        if (decoded.Length < frames)
        {
            decoded = new float[frames];
        }
    }

    private void EnsureResampled(int input)
    {
        // What the slowest ratio the steering allows could turn that many input frames into, plus
        // room for whatever the filter was still holding from the piece before.
        var room = (int)Math.Ceiling(input * CapturedAudio.SampleRate / (format.SampleRate * (1 - Steering))) + 256;
        if (resampled.Length < room)
        {
            resampled = new float[room];
            written = new short[room];
        }
    }
}
