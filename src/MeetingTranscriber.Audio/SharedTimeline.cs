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
/// <para>
/// <b>One thread hands the packets over.</b> This is not the capture callback and never runs on
/// one: the recording is rebuilt from the spool when somebody stops it, in one pass, in the order
/// the devices read the packets. Nothing here is locked, because a lock would let two capture
/// threads through and quietly produce a recording with frames duplicated and frames missing —
/// so a second thread arriving is refused instead, loudly, where a lock would have hidden it.
/// </para>
/// </remarks>
public sealed class SharedTimeline
{
    /// <summary>How many frames are handed on in one write.</summary>
    private const int WriteFrames = 4096;

    /// <summary>
    /// How far behind the rest of the recording a source may fall before the recording goes on
    /// without it.
    /// </summary>
    /// <remarks>
    /// Both streams open within tens of milliseconds of each other and neither goes quiet on its
    /// own: a microphone hands over what a silent room sounds like, and channel 0 comes off a
    /// process loopback, which Windows hands silence rather than nothing when the processes it is
    /// following are playing nothing. Half a minute of one device saying nothing while the other
    /// records is that device gone, and waiting for it is what turns a streaming component into one
    /// that holds two hours of the survivor in memory and then loses the meeting to it.
    /// </remarks>
    private static readonly long Stalls = 30L * CapturedAudio.SampleRate;

    private readonly TimelineSource[] sources;
    private readonly IAlignedAudio into;
    private readonly long[] offsets;
    private readonly bool[] gone;
    private readonly short[][] blocks;
    private int inside;
    private bool anchored;
    private bool closed;
    private long emitted;

    private SharedTimeline(TimelineSource[] sources, IAlignedAudio into)
    {
        this.sources = sources;
        this.into = into;
        offsets = new long[sources.Length];
        gone = new bool[sources.Length];
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

        Alone();
        try
        {
            if (closed)
            {
                throw new AudioCaptureException($"A {packet.Channel} packet arrived after the timeline was closed.");
            }

            var index = CapturedAudio.IndexOf(packet.Channel);
            if (gone[index])
            {
                // A source given up on comes back as a device that has not spoken yet, and as
                // nothing else. That one shares no counter and no clock anchor with what it
                // replaced, so it has somewhere to go: it is placed from where the recording has
                // already got to, and the whole stretch nobody could be handed is the gap it was.
                // A late packet of the device that stalled has no such fresh start — it would be
                // laid down over frames that are already the meeting, which is what going on
                // without it means.
                //
                // Two ways of being that device and not one, because a seam is said once: the
                // packet that carries the format, and every packet behind it until one the source
                // can really place — a device that has just started is entitled to disown the
                // instant on its first block, and the one that says so is the one it is said on.
                if (packet.Opening is null && !sources[index].Between)
                {
                    throw new AudioCaptureException(
                        $"The {packet.Channel} source went quiet for {Stalls / CapturedAudio.SampleRate} seconds "
                        + "and the recording went on without it, so this packet has nowhere left to go.");
                }

                // Said once, here, because here is the event: how far the recording got without
                // this source is the one thing about the file a source cannot see, and the device
                // taking over is placed from there rather than from where its own clock alone
                // would put it.
                sources[index].GivenUpAt(Math.Max(0, emitted - offsets[index]));
                gone[index] = false;
            }

            sources[index].Take(packet);
            Emit(flushing: false);
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// Hands on everything left, padding the source that ran out first so both channels end
    /// together, and says what the recording turned out to be.
    /// </summary>
    public TimelineSummary Close()
    {
        Alone();
        try
        {
            if (closed)
            {
                throw new AudioCaptureException("This timeline is already closed.");
            }

            closed = true;

            foreach (var source in sources)
            {
                source.Finish();
            }

            Emit(flushing: true);

            return new TimelineSummary(
                Length(emitted),
                [.. sources.Select(Summarise)]);
        }
        finally
        {
            Leave();
        }
    }

    private static Duration Length(long frames) =>
        Duration.FromMilliseconds(frames * 1000 / CapturedAudio.SampleRate);

    /// <summary>
    /// Refuses a second thread rather than serialising it. See the class remarks: one caller hands
    /// the packets over, and the failure mode of getting that wrong is silent.
    /// </summary>
    private void Alone()
    {
        if (Interlocked.Increment(ref inside) != 1)
        {
            Interlocked.Decrement(ref inside);
            throw new AudioCaptureException(
                "Two callers reached this timeline at once. It is rebuilt in one pass on one "
                + "thread, in the order the devices read the packets.");
        }
    }

    private void Leave() => Interlocked.Decrement(ref inside);

    /// <summary>
    /// What one source turned out to be. Everything of the recording it produced nothing for is
    /// missing audio, whether that is a stretch the device dropped, the wait before it opened or
    /// the rest of the meeting after it went quiet — so a source that never spoke reports the whole
    /// recording, which is the plainest thing the numbers can say.
    /// </summary>
    private SourceSummary Summarise(TimelineSource source, int index)
    {
        var covered = source.Started ? offsets[index] + source.Produced : 0;

        return new SourceSummary(
            source.Channel,
            source.Rate,
            Length(source.Missing + offsets[index] + Math.Max(0, emitted - covered)),
            Length(offsets[index]),
            source.CounterGivenUp,
            source.Stretches);
    }

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
            // that wait is over, and so it is once a source that has spoken has half a minute of
            // the meeting nobody can be handed: whatever arrived is the recording.
            var ready = Array.TrueForAll(sources, source => source.Started)
                || ((flushing || Array.Exists(sources, source => source.Produced > Stalls))
                    && Array.Exists(sources, source => source.Started));

            if (!ready)
            {
                return;
            }

            Anchor();
        }

        var furthest = long.MinValue;
        for (var index = 0; index < sources.Length; index++)
        {
            furthest = Math.Max(furthest, Covers(index));
        }

        // A source the rest of the recording has left this far behind is a device that stopped,
        // and the meeting goes on without it rather than being held — and then lost — waiting.
        for (var index = 0; index < sources.Length; index++)
        {
            gone[index] |= furthest - Covers(index) > Stalls;
        }

        var limit = flushing ? furthest : long.MaxValue;
        if (!flushing)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                if (!gone[index])
                {
                    limit = Math.Min(limit, Covers(index));
                }
            }
        }

        while (emitted < limit)
        {
            Write((int)Math.Min(WriteFrames, limit - emitted));
        }
    }

    /// <summary>The first frame of the recording this source has nothing to say about yet.</summary>
    private long Covers(int index) =>
        sources[index].Started ? offsets[index] + sources[index].Produced : 0;

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
