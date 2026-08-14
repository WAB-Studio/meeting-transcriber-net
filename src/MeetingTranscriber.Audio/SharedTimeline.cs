using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The one timeline both sources land on. Packets go in as the devices reported them; interleaved
/// stereo comes out, in the interchange format, with the two channels describing the same instant
/// at every frame.
/// </summary>
/// <remarks>
/// <para>
/// It knows nothing about WASAPI, about a window or about a file. What it takes is a packet with a
/// device position and an instant on it, and fabricated ones are as good as captured ones — which
/// is the only way two hours of drift can be tested at all, and the reason this is the component
/// the largest technical risk in the product was pulled into.
/// </para>
/// <para>
/// The origin is the earlier of the two sources' first frames, so a stream that opened forty
/// milliseconds after the other one starts forty milliseconds of silence in and not at zero. That
/// difference is real: it is the gap between two devices being handed over, and closing it up
/// would move one side of the conversation against the other for the whole meeting.
/// </para>
/// </remarks>
public sealed class SharedTimeline
{
    /// <summary>How many frames are handed on in one write.</summary>
    private const int WriteFrames = 4096;

    private readonly TimelineSource[] sources;
    private readonly IAlignedAudio into;
    private readonly long[] offsets;
    private readonly short[][] blocks;
    private bool anchored;
    private bool closed;
    private long emitted;

    private SharedTimeline(TimelineSource[] sources, IAlignedAudio into)
    {
        this.sources = sources;
        this.into = into;
        offsets = new long[sources.Length];
        blocks = [.. sources.Select(_ => new short[WriteFrames])];
    }

    /// <summary>
    /// A timeline for the two sources of one recording, each declaring the format its device
    /// hands over.
    /// </summary>
    /// <remarks>
    /// Two, named, and never a list: the application records both or neither, so a timeline that
    /// could be built with one source would only exist to make a half recording representable.
    /// </remarks>
    public static SharedTimeline Of(StreamFormat loopback, StreamFormat microphone, IAlignedAudio into)
    {
        ArgumentNullException.ThrowIfNull(loopback);
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(into);

        var sources = new TimelineSource[CapturedAudio.ChannelCount];
        sources[CapturedAudio.IndexOf(AudioChannel.Loopback)] = new TimelineSource(AudioChannel.Loopback, loopback);
        sources[CapturedAudio.IndexOf(AudioChannel.Microphone)] = new TimelineSource(AudioChannel.Microphone, microphone);

        return new SharedTimeline(sources, into);
    }

    /// <summary>Takes one packet, and hands on whatever both sources now cover.</summary>
    public void Take(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (closed)
        {
            throw new AudioCaptureException($"A {packet.Channel} packet arrived after the timeline was closed.");
        }

        sources[CapturedAudio.IndexOf(packet.Channel)].Take(packet);
        Emit(flushing: false);
    }

    /// <summary>
    /// Hands on everything left, padding the source that ran out first so both channels end
    /// together, and says what the recording turned out to be.
    /// </summary>
    public TimelineSummary Close()
    {
        if (closed)
        {
            throw new AudioCaptureException("This timeline is already closed.");
        }

        closed = true;
        Emit(flushing: true);

        return new TimelineSummary(
            Length(emitted),
            [.. sources.Select((source, index) => source.Summarise(Length(offsets[index])))]);
    }

    private static Duration Length(long frames) =>
        Duration.FromMilliseconds(frames * 1000 / CapturedAudio.SampleRate);

    /// <summary>
    /// Writes out every frame both sources cover — or, at the close, every frame either of them
    /// does.
    /// </summary>
    private void Emit(bool flushing)
    {
        if (!anchored)
        {
            // Until both sources have spoken there is no origin to measure from, and guessing one
            // would put the whole of the other source at the wrong offset for good. At the close
            // that wait is over: whatever arrived is the recording.
            var ready = flushing
                ? Array.Exists(sources, source => source.Started)
                : Array.TrueForAll(sources, source => source.Started);

            if (!ready)
            {
                return;
            }

            Anchor();
        }

        var limit = flushing ? long.MinValue : long.MaxValue;
        for (var index = 0; index < sources.Length; index++)
        {
            var covers = offsets[index] + sources[index].Produced;
            limit = flushing ? Math.Max(limit, covers) : Math.Min(limit, covers);
        }

        while (emitted < limit)
        {
            Write((int)Math.Min(WriteFrames, limit - emitted));
        }
    }

    /// <summary>Fixes the origin at the earlier of the sources' first frames.</summary>
    private void Anchor()
    {
        var origin = sources
            .Where(source => source.Started)
            .Min(source => source.Anchor);

        for (var index = 0; index < sources.Length; index++)
        {
            offsets[index] = sources[index].Started
                ? (long)Math.Round(
                    sources[index].Anchor.Since(origin)
                    * (double)CapturedAudio.SampleRate / MonotonicInstant.TicksPerSecond)
                : 0;
        }

        anchored = true;
    }

    private void Write(int frames)
    {
        for (var index = 0; index < sources.Length; index++)
        {
            var block = blocks[index].AsSpan(0, frames);

            // Before this source's first frame, and after its last one at the close, the
            // recording is silence rather than the other source's audio moved over to fill it.
            var lead = (int)Math.Clamp(offsets[index] - emitted, 0, frames);
            var body = Math.Min(frames - lead, sources[index].Waiting);

            block[..lead].Clear();
            if (body > 0)
            {
                sources[index].Read(block.Slice(lead, body));
            }

            block[(lead + body)..].Clear();
        }

        into.Write(CapturedAudio.Interleave(
            blocks[CapturedAudio.IndexOf(AudioChannel.Loopback)].AsSpan(0, frames),
            blocks[CapturedAudio.IndexOf(AudioChannel.Microphone)].AsSpan(0, frames)));

        emitted += frames;
    }
}
