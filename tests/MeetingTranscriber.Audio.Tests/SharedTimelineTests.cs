using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The two sources becoming one recording, driven entirely by packets nobody recorded. This is the
/// largest technical risk in the product, and the point of every test here is that it can be run
/// on a machine with no sound card at all.
/// </summary>
public class SharedTimelineTests
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat MonoFloat = new(48_000, 1, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// ISC-112. Nothing is opened, nothing is played, and the two devices are arithmetic â€” and a
    /// recording comes out with both of them on it.
    /// </summary>
    [Fact]
    public void Two_fabricated_sources_come_out_as_one_pair_of_channels()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 5, Fabricated.Bursts(1)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 5, Fabricated.Bursts(1, lengthMs: 20)));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(4_940, 5_060);
        collected.Frames.ShouldBeInRange((5 * CapturedAudio.SampleRate) - 1_000, (5 * CapturedAudio.SampleRate) + 1_000);
        collected.Loudest(AudioChannel.Loopback, 2, 2.1).ShouldBeGreaterThan(0.5f);
        collected.Loudest(AudioChannel.Microphone, 2, 2.1).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// ISC-112. The gap between two devices being handed over is part of the recording. Closing it
    /// up would move one side of the conversation against the other for the whole meeting.
    /// </summary>
    [Fact]
    public void A_source_that_opened_later_starts_later_on_the_timeline()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 3, Fabricated.Bursts(1)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0.1, 3, _ => 0.9f));

        var summary = timeline.Close();

        summary.On(AudioChannel.Microphone).Waited.Milliseconds.ShouldBeInRange(97, 103);
        summary.On(AudioChannel.Loopback).Waited.Milliseconds.ShouldBe(0);
        collected.Loudest(AudioChannel.Microphone, 0, 0.09).ShouldBe(0);
        collected.Loudest(AudioChannel.Microphone, 0.15, 0.2).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// ISC-113. The packet after the hole carries a position half a second further on than the one
    /// before it, and that is where its audio goes. Laying it down where it arrived would pull the
    /// rest of the meeting half a second early and every citation in it with them.
    /// </summary>
    [Fact]
    public void A_packet_after_a_gap_lands_where_its_position_says()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(
                AudioChannel.Loopback,
                StereoFloat,
                48_000,
                0,
                3,
                Fabricated.Bursts(1),
                delivers: Dropping(fromSeconds: 0.3, toSeconds: 0.8)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 3, Fabricated.Quiet));

        timeline.Close();

        collected.OnsetAfter(AudioChannel.Loopback, 0.9).ShouldNotBeNull().ShouldBe(1.0, tolerance: 0.01);
        collected.OnsetAfter(AudioChannel.Loopback, 1.9).ShouldNotBeNull().ShouldBe(2.0, tolerance: 0.01);
    }

    /// <summary>
    /// ISC-114. What the device produced and nobody received is half a second of the meeting, and
    /// it stays half a second long â€” silent, named in the summary, and not closed up.
    /// </summary>
    [Fact]
    public void A_stretch_the_device_never_delivered_stays_a_gap_of_its_own_length()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(
                AudioChannel.Loopback,
                StereoFloat,
                48_000,
                0,
                3,
                _ => 0.9f,
                delivers: Dropping(fromSeconds: 0.3, toSeconds: 0.8)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 3, Fabricated.Quiet));

        var summary = timeline.Close();

        summary.On(AudioChannel.Loopback).Missing.Milliseconds.ShouldBeInRange(480, 520);
        summary.Length.Milliseconds.ShouldBeInRange(2_940, 3_060);
        collected.Loudest(AudioChannel.Loopback, 0.35, 0.75).ShouldBe(0);
        collected.Loudest(AudioChannel.Loopback, 0.9, 1.0).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// ISC-115. A meeting is recorded on whatever two devices the machine has, and they agree on
    /// nothing: different rates, different widths, one of them mono. One recording comes out.
    /// </summary>
    [Fact]
    public void Sources_that_agree_on_nothing_come_out_at_one_rate_and_one_width()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, CheapMicrophone, collected);

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 4, Fabricated.Bursts(1)),
            Fabricated.Packets(AudioChannel.Microphone, CheapMicrophone, 44_100, 0, 4, Fabricated.Bursts(1), packetFrames: 441));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(3_940, 4_060);
        collected.OnsetAfter(AudioChannel.Loopback, 2.9).ShouldNotBeNull().ShouldBe(3.0, tolerance: 0.01);
        collected.OnsetAfter(AudioChannel.Microphone, 2.9).ShouldNotBeNull().ShouldBe(3.0, tolerance: 0.01);
    }

    /// <summary>
    /// ISC-116. A microphone two hundred parts per million fast is a fifth of a second out by the
    /// end of a quarter hour. What this asserts is not that it comes back at the end but that it
    /// was never away: every marker of the recording lands on both channels together, so a
    /// citation an hour in is on the right words rather than only the last one being.
    /// </summary>
    [Fact]
    public void A_fast_clock_is_pulled_back_throughout_and_not_at_the_end()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);
        const double Fast = 48_000 * 1.002;

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 60, Fabricated.Bursts(10)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, Fast, 0, 60, Fabricated.Bursts(10)));

        var summary = timeline.Close();

        // The drift is measured rather than assumed: had it not been, the two channels would be
        // 120 ms apart by the last marker.
        summary.On(AudioChannel.Microphone).MeasuredRate.ShouldBe(Fast, tolerance: 20);
        summary.On(AudioChannel.Loopback).MeasuredRate.ShouldBe(48_000, tolerance: 20);

        for (var marker = 10; marker <= 50; marker += 10)
        {
            var loopback = collected.OnsetAfter(AudioChannel.Loopback, marker - 1).ShouldNotBeNull();
            var microphone = collected.OnsetAfter(AudioChannel.Microphone, marker - 1).ShouldNotBeNull();

            loopback.ShouldBe(marker, tolerance: 0.004);
            microphone.ShouldBe(marker, tolerance: 0.004);
        }
    }

    /// <summary>
    /// The loud failure. A frame counter that goes backwards cannot be laid out at all, and the
    /// quiet alternative is a second of the meeting written over by a later one.
    /// </summary>
    [Fact]
    public void A_source_whose_position_goes_backwards_stops_the_recording()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());
        var packets = Fabricated
            .Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet)
            .Take(5)
            .ToList();

        foreach (var packet in packets.Take(4))
        {
            timeline.Take(packet);
        }

        // The clock goes on forwards, so what is refused here is the frame counter and not the
        // instant beside it.
        var backwards = packets[4] with { DevicePosition = packets[1].DevicePosition };

        Should.Throw<AudioCaptureException>(() => timeline.Take(backwards))
            .Message.ShouldContain("went back");
    }

    [Fact]
    public void A_closed_timeline_takes_nothing_more_and_does_not_close_twice()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());
        var packet = Fabricated
            .Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet)
            .First();

        timeline.Take(packet);
        timeline.Close();

        Should.Throw<AudioCaptureException>(() => timeline.Take(packet));
        Should.Throw<AudioCaptureException>(() => timeline.Close());
    }

    /// <summary>
    /// The end of the meeting is part of the meeting. The resampler holds its last input back until
    /// something follows it, so nothing being asked of it at the close leaves every recording a few
    /// milliseconds short and a recording of one packet empty.
    /// </summary>
    [Fact]
    public void The_last_of_the_recording_is_not_left_inside_the_resampler()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, _ => 0.9f),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 1, Fabricated.Quiet));

        var summary = timeline.Close();

        // Every packet handed over is a whole millisecond of audio, so the recording is a whole
        // second and the tolerance is there for the rounding rather than for the tail.
        summary.Length.Milliseconds.ShouldBeInRange(999, 1_001);
        collected.Loudest(AudioChannel.Loopback, 0.99, 1.0).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// A device that stops for half a minute while the other one records has gone. Waiting for it
    /// is what turns this into a component that holds two hours of the survivor in memory and then
    /// loses the meeting to it — so the recording goes on, and the summary says the whole of that
    /// source is missing rather than the file quietly being half a conversation.
    /// </summary>
    [Fact]
    public void A_source_that_goes_quiet_does_not_hold_the_rest_of_the_meeting()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 45, _ => 0.9f),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 2, _ => 0.9f));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(44_900, 45_100);
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(42_900, 43_100);
        summary.On(AudioChannel.Loopback).Missing.Milliseconds.ShouldBe(0);
        collected.Loudest(AudioChannel.Microphone, 1, 2).ShouldBeGreaterThan(0.5f);
        collected.Loudest(AudioChannel.Microphone, 40, 44).ShouldBe(0);
        collected.Loudest(AudioChannel.Loopback, 40, 44).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// And once the recording has gone on without it, that source cannot be slipped back in behind
    /// what has already been written.
    /// </summary>
    [Fact]
    public void A_source_the_recording_left_behind_is_refused_rather_than_inserted()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 40, Fabricated.Quiet),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 2, Fabricated.Quiet));

        var late = Fabricated
            .Packets(AudioChannel.Microphone, MonoFloat, 48_000, 39, 40, Fabricated.Quiet)
            .First();

        Should.Throw<AudioCaptureException>(() => timeline.Take(late))
            .Message.ShouldContain("went quiet");
    }

    /// <summary>
    /// The clock a device is measured against is the one that does not go backwards. One that does
    /// is not a clock, and nothing it says about how fast the device ran can be believed.
    /// </summary>
    [Fact]
    public void A_source_whose_clock_goes_backwards_stops_the_recording()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());
        var packets = Fabricated
            .Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet)
            .Take(3)
            .ToList();

        timeline.Take(packets[0]);
        timeline.Take(packets[2]);

        var backwards = packets[2] with
        {
            DevicePosition = packets[2].DevicePosition + 480,
            CapturedAt = packets[1].CapturedAt,
        };

        Should.Throw<AudioCaptureException>(() => timeline.Take(backwards))
            .Message.ShouldContain("does not go back");
    }

    /// <summary>
    /// The failure the whole measurement rests on. A counter that says half as much time passed as
    /// the device counted frames for is not describing this device, and clamping it into something
    /// plausible would put one side of the conversation seconds from the other with the file saying
    /// nothing about it. The spool is still there to be rebuilt once somebody knows.
    /// </summary>
    [Fact]
    public void A_device_whose_two_counters_disagree_stops_the_recording_rather_than_being_clamped()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());

        var half = Should.Throw<AudioCaptureException>(() =>
        {
            foreach (var packet in Fabricated.Packets(
                AudioChannel.Loopback, StereoFloat, realRate: 24_000, 0, 20, Fabricated.Quiet))
            {
                timeline.Take(packet);
            }
        });

        half.Message.ShouldContain("48000 Hz");
    }

    /// <summary>
    /// A device that will not vouch for one packet's two numbers has still recorded that packet's
    /// audio. Only the observation is thrown away — treating the packet itself as the failure would
    /// lose a meeting over a flag WASAPI sets and clears.
    /// </summary>
    [Fact]
    public void A_packet_the_device_will_not_vouch_for_keeps_its_audio_and_loses_its_measurement()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Fabricated
                .Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 3, _ => 0.9f)
                .Select(packet => packet.DevicePosition == 48_000 ? packet with { TimingIsSound = false } : packet),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 3, Fabricated.Quiet));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(2_940, 3_060);
        summary.On(AudioChannel.Loopback).Missing.Milliseconds.ShouldBe(0);
        collected.Loudest(AudioChannel.Loopback, 1, 1.05).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// The first one is different: there is nothing yet to measure the rest of the source against,
    /// so a source that opens with numbers its device disowns cannot be placed at all.
    /// </summary>
    [Fact]
    public void A_source_that_opens_with_numbers_its_device_disowns_is_refused()
    {
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, new Collected());
        var first = Fabricated
            .Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet)
            .First() with
        { TimingIsSound = false };

        Should.Throw<AudioCaptureException>(() => timeline.Take(first))
            .Message.ShouldContain("vouch");
    }

    /// <summary>
    /// Asked when the timeline is built rather than answered by the first packet: a source that
    /// never speaks would otherwise close as clean silence on a format nothing here can read.
    /// </summary>
    [Fact]
    public void A_width_this_build_cannot_read_is_refused_before_a_packet_can_arrive()
    {
        var twentyFourBit = new StreamFormat(48_000, 2, 24, SampleEncoding.Pcm);

        Should.Throw<AudioCaptureException>(
                () => SharedTimeline.Of(StereoFloat, twentyFourBit, new Collected()))
            .Message.ShouldContain("24 bit");
    }

    /// <summary>
    /// One caller hands the packets over, in the order the devices read them. A lock would let two
    /// capture threads through and produce a recording with frames duplicated and frames missing,
    /// and nothing in the file would say so — so the second one is refused instead.
    /// </summary>
    [Fact]
    public void A_second_caller_is_refused_rather_than_quietly_serialised()
    {
        var reentrant = new Reentrant();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, reentrant);
        reentrant.Timeline = timeline;

        Feed(
            timeline,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 1, Fabricated.Quiet));

        reentrant.Caught.ShouldNotBeNull()
            .ShouldBeOfType<AudioCaptureException>()
            .Message.ShouldContain("at once");
    }

    /// <summary>
    /// A caller that reaches the timeline from inside the write it asked for is the same collision
    /// two capture threads would make, without a thread to make it flaky.
    /// </summary>
    private sealed class Reentrant : IAlignedAudio
    {
        internal SharedTimeline? Timeline { get; set; }

        internal Exception? Caught { get; private set; }

        public void Write(ReadOnlySpan<short> frames)
        {
            try
            {
                Timeline?.Close();
            }
            catch (AudioCaptureException refused)
            {
                Caught ??= refused;
            }
        }
    }

    /// <summary>
    /// Both or neither is the capture's contract, but a session that lost one of its streams still
    /// has a meeting on the other, and it comes out the length it really was.
    /// </summary>
    [Fact]
    public void A_source_that_never_said_anything_is_silence_rather_than_a_shorter_meeting()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        foreach (var packet in Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 2, _ => 0.9f))
        {
            timeline.Take(packet);
        }

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(1_940, 2_060);
        collected.Loudest(AudioChannel.Microphone, 0, 2).ShouldBe(0);
        collected.Loudest(AudioChannel.Loopback, 1, 2).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// Hands both sources over the way a capture does: whichever packet was read at the earlier
    /// instant goes first, so the timeline sees them interleaved rather than one source and then
    /// the other.
    /// </summary>
    private static void Feed(
        SharedTimeline timeline,
        IEnumerable<CapturePacket> loopback,
        IEnumerable<CapturePacket> microphone)
    {
        foreach (var packet in loopback.Concat(microphone).OrderBy(packet => packet.CapturedAt.Ticks))
        {
            timeline.Take(packet);
        }
    }

    /// <summary>A device that produces a stretch of the meeting and hands none of it over.</summary>
    private static Func<long, bool> Dropping(double fromSeconds, double toSeconds) =>
        position => position < fromSeconds * 48_000 || position >= toSeconds * 48_000;
}
