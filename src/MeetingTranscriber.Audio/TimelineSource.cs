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
    private readonly StreamFormat format;
    private readonly SourceClock clock;
    private readonly WdlResampler resampler = new();
    private readonly SampleBuffer buffer = new();
    private float[] decoded = [];
    private float[] resampled = [];
    private short[] written = [];
    private float[] silence = [];
    private long nextPosition;
    private long missingFrames;

    internal TimelineSource(AudioChannel channel, StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        this.channel = channel;
        this.format = format;
        clock = new SourceClock(format.SampleRate);

        // Interpolation with two filter passes rather than a windowed sinc. This runs over the
        // whole meeting at the moment somebody stops it, and sixty-four taps across two hours of
        // 48 kHz is a minute of a person watching a progress bar for a difference they would have
        // to be told about. If a transcript ever reads as though the fricatives were mangled,
        // SetMode(true, 0, true) is the knob and the cost is the reason it is not the default.
        resampler.SetMode(interp: true, filtercnt: 2, sinc: false);
        resampler.SetFeedMode(wantInputDriven: true);
    }

    /// <summary>Which of the two channels this source feeds.</summary>
    internal AudioChannel Channel => channel;

    /// <summary>Whether any packet of this source has arrived.</summary>
    internal bool Started => clock.Started;

    /// <summary>When this source's first frame was read, on the shared clock.</summary>
    internal MonotonicInstant Anchor => clock.Anchor;

    /// <summary>Frames of the interchange format this source has produced since its first one.</summary>
    internal long Produced { get; private set; }

    /// <summary>How many of those are still waiting to be handed on.</summary>
    internal int Waiting => buffer.Count;

    /// <summary>Takes the oldest <paramref name="into"/>.Length frames this source produced.</summary>
    internal void Read(Span<short> into) => buffer.Take(into);

    /// <summary>What this source turned out to be, once its last packet is in.</summary>
    internal SourceSummary Summarise(Duration waited) => new(
        channel,
        clock.Rate,
        Duration.FromMilliseconds(missingFrames * 1000 / format.SampleRate),
        waited);

    /// <summary>
    /// Takes one packet. A position ahead of where the last packet ended is a stretch the device
    /// dropped and becomes silence of exactly that length; a position behind it is a stream that
    /// cannot be laid out at all, which stops here rather than quietly overwriting a second of the
    /// meeting with a later one.
    /// </summary>
    internal void Take(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var frames = Samples.FramesIn(packet.Samples.Length, format);
        var first = !clock.Started;
        clock.Observe(packet.DevicePosition, packet.CapturedAt);

        if (first)
        {
            nextPosition = packet.DevicePosition;
        }
        else if (packet.DevicePosition < nextPosition)
        {
            throw new AudioCaptureException(
                $"The {channel} source went back from frame {nextPosition} to {packet.DevicePosition}.");
        }
        else if (packet.DevicePosition > nextPosition)
        {
            var missing = packet.DevicePosition - nextPosition;
            missingFrames += missing;
            FeedSilence(missing, nextPosition);
        }

        if (frames > 0)
        {
            EnsureDecoded(frames);
            Samples.ToMono(packet.Samples.Span, format, decoded);
            Feed(decoded.AsSpan(0, frames), packet.DevicePosition);
        }

        nextPosition = packet.DevicePosition + frames;
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
        var target = (endPosition - clock.AnchorPosition) * CapturedAudio.SampleRate / rate;
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
            var made = resampler.ResampleOut(resampled, 0, taken, resampled.Length, 1);
            if (made > 0)
            {
                Samples.ToPcm16(resampled.AsSpan(0, made), written);
                buffer.Add(written.AsSpan(0, made));
                Produced += made;
            }
        }
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
