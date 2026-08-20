using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A channel whose device is taken away mid meeting and replaced by another one, driven entirely by
/// packets nobody recorded.
/// </summary>
/// <remarks>
/// The whole point is that it runs on a machine with nothing plugged into it. What a real unplug
/// produces is two devices with nothing in common — one counter ending, another starting at its own
/// zero, a format that is free to be anything, and a stretch of the meeting in between that nobody
/// was handed — and every one of those is arithmetic here.
/// </remarks>
public class DeviceChangeTests
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat MonoFloat = new(48_000, 1, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// ISC-78. The microphone goes away nine seconds in and another one takes over three seconds
    /// later. The recording runs to the end either way, and what the second device caught is on it.
    /// </summary>
    [Fact]
    public void A_channel_whose_device_changed_records_to_the_end()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(timeline, Machine(0, 20), Unplugged(at: 9, backAt: 12, until: 20));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(19_900, 20_100);
        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);

        // Both halves of the microphone are really on the recording, at the seconds their bursts
        // were played rather than pushed along by the changeover.
        collected.OnsetAfter(AudioChannel.Microphone, 4.9).ShouldNotBeNull().ShouldBe(5.0, tolerance: 0.02);
        collected.OnsetAfter(AudioChannel.Microphone, 14.9).ShouldNotBeNull().ShouldBe(15.0, tolerance: 0.02);
    }

    /// <summary>
    /// ISC-78. The counter of whatever replaces a device starts again at its own zero, which used
    /// to read as a source going backwards. It is a stretch of its own and the recording is one
    /// recording.
    /// </summary>
    [Fact]
    public void The_replacement_numbering_its_frames_from_zero_is_not_a_source_going_backwards()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        // What a real replacement hands over: the same channel, counting from its own zero, at an
        // instant six seconds into a recording the device before it was placed by.
        var second = Fabricated.Packets(
            AudioChannel.Microphone, MonoFloat, 48_000, 6, 12, Fabricated.Bursts(1));

        Feed(timeline, Machine(0, 12), First(0, 6).Concat(Fabricated.TakingOver(MonoFloat, second)));

        var summary = timeline.Close();

        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);
        summary.On(AudioChannel.Microphone).CounterGivenUp.ShouldBeFalse(
            "a counter starting again is a different device and not a device numbering its frames "
            + "in a unit of its own, and the two are told apart by the recording saying which "
            + "happened rather than by the timeline guessing");
        collected.OnsetAfter(AudioChannel.Microphone, 7.9).ShouldNotBeNull().ShouldBe(8.0, tolerance: 0.02);
    }

    /// <summary>
    /// ISC-78. The stretch between the two devices is the audio that never arrived: silence of the
    /// length it really was, said in the summary, and never closed up.
    /// </summary>
    [Fact]
    public void The_stretch_between_the_two_devices_is_a_gap_of_the_length_it_really_was()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(timeline, Machine(0, 20), Unplugged(at: 9, backAt: 12, until: 20));

        var summary = timeline.Close();

        // Three seconds of a meeting nobody was handed, and it is the microphone's missing rather
        // than a recording three seconds shorter than the meeting.
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(2_900, 3_100);
        collected.Loudest(AudioChannel.Microphone, 9.2, 11.8).ShouldBe(0);

        // Channel 0 never stopped, so it carries the same three seconds as audio. A recording that
        // closed the seam up would have moved these two against each other for the rest of it.
        collected.Loudest(AudioChannel.Loopback, 10, 10.1).ShouldBeGreaterThan(0.5f);
        summary.On(AudioChannel.Loopback).Missing.Milliseconds.ShouldBeLessThan(100);
    }

    /// <summary>
    /// ISC-78. Windows moves to whatever endpoint it has, and that one mixes at its own rate in its
    /// own width. A stretch carries its own format, so the meeting is not lost to the replacement
    /// being a cheaper device than the one that went.
    /// </summary>
    [Fact]
    public void The_replacement_is_recorded_at_whatever_format_it_hands_over()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Machine(0, 20),
            First(0, 9).Concat(Fabricated.TakingOver(
                CheapMicrophone,
                Fabricated.Packets(
                    AudioChannel.Microphone,
                    CheapMicrophone,
                    44_100,
                    12,
                    20,
                    Fabricated.Bursts(1),
                    packetFrames: 441))));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(19_900, 20_100);
        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);

        // 44 100 frames a second going in and 48 000 coming out, landing on the second the burst
        // was played: the stretch is resampled onto the one timeline like any other source.
        collected.OnsetAfter(AudioChannel.Microphone, 14.9).ShouldNotBeNull().ShouldBe(15.0, tolerance: 0.02);
        collected.OnsetAfter(AudioChannel.Microphone, 18.9).ShouldNotBeNull().ShouldBe(19.0, tolerance: 0.02);
    }

    /// <summary>
    /// ISC-78. Half a minute with nothing arriving is a source the recording goes on without, and
    /// what used to happen next was that anything it said afterwards ended the whole rebuild. A
    /// device that comes back is placed from where the recording got to, and the meeting keeps both
    /// ends of itself.
    /// </summary>
    /// <remarks>
    /// The one case where the seam is longer than the timeline is willing to wait, so what the
    /// recording did without the microphone is already on the disk. It cannot go back over it — so
    /// what the replacement says lands from there on, and the whole of what was missed is missing.
    /// </remarks>
    [Fact]
    public void A_device_replaced_after_the_recording_gave_up_on_it_still_reaches_the_meeting()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(timeline, Machine(0, 80), Unplugged(at: 5, backAt: 60, until: 80));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(79_800, 80_200);
        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);

        // What the replacement caught is on the recording at the seconds it was caught, and not
        // fifty-five seconds late behind a queue of silence for the stretch already written.
        collected.OnsetAfter(AudioChannel.Microphone, 69.9).ShouldNotBeNull().ShouldBe(70.0, tolerance: 0.1);
        collected.Loudest(AudioChannel.Microphone, 10, 55).ShouldBe(0);
    }

    /// <summary>
    /// ISC-78. A device change and a device that dropped a stretch are not the same news, and the
    /// summary keeps them apart: the first is a channel that came to name two devices, the second
    /// is one device that lost audio.
    /// </summary>
    [Fact]
    public void A_source_that_never_changed_device_is_one_stretch()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Machine(0, 6),
            Fabricated.Packets(
                AudioChannel.Microphone,
                MonoFloat,
                48_000,
                0,
                6,
                Fabricated.Bursts(1),
                delivers: position => position < 2 * 48_000 || position >= 3 * 48_000));

        var summary = timeline.Close();

        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(1);
        summary.On(AudioChannel.Loopback).Stretches.ShouldBe(1);
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(950, 1_050);
    }

    /// <summary>
    /// ISC-78. The first block of a device that has just started is the block it is likeliest not
    /// to vouch for the instant of, and where the whole stretch goes is measured from that instant.
    /// So the head of a stretch is waited out rather than placed on a number the device disowned —
    /// which an adversarial pass measured landing the rest of the meeting seven seconds early, with
    /// a summary byte-identical to a correct one.
    /// </summary>
    [Fact]
    public void A_replacement_whose_first_blocks_carry_no_sound_instant_still_lands_where_it_belongs()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Machine(0, 20),
            First(0, 9).Concat(Fabricated.Unvouched(
                3,
                Fabricated.TakingOver(
                    MonoFloat,
                    Fabricated.Packets(
                        AudioChannel.Microphone, MonoFloat, 48_000, 12, 20, Fabricated.Bursts(1))))));

        var summary = timeline.Close();

        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(2_900, 3_150);
        collected.OnsetAfter(AudioChannel.Microphone, 14.9).ShouldNotBeNull().ShouldBe(15.0, tolerance: 0.02);
        collected.Loudest(AudioChannel.Microphone, 9.2, 11.8).ShouldBe(0);
    }

    /// <summary>
    /// ISC-78. The same block, after the recording gave up on the channel, used to leave the source
    /// fifty-five seconds behind and then refuse the packet after it — so the meeting could not be
    /// rebuilt at all, out of spools that were whole on the disk.
    /// </summary>
    [Fact]
    public void A_replacement_after_a_give_up_survives_a_first_block_with_no_sound_instant()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Machine(0, 80),
            First(0, 5).Concat(Fabricated.Unvouched(
                3,
                Fabricated.TakingOver(
                    MonoFloat,
                    Fabricated.Packets(
                        AudioChannel.Microphone, MonoFloat, 48_000, 60, 80, Fabricated.Bursts(1))))));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(79_800, 80_200);
        summary.On(AudioChannel.Microphone).Stretches.ShouldBe(2);
        collected.OnsetAfter(AudioChannel.Microphone, 69.9).ShouldNotBeNull().ShouldBe(70.0, tolerance: 0.1);
    }

    /// <summary>
    /// ISC-78. A microphone in a bad state from the moment the meeting started, given up on, and
    /// then replaced forty seconds in. Its own timeline starts at nothing while the recording has
    /// already been written past it, so what it says goes where the recording got to — laid out
    /// from frame zero it was placed forty seconds behind, and the packet after it lost the meeting.
    /// </summary>
    [Fact]
    public void A_replacement_for_a_device_that_never_spoke_lands_where_the_recording_got_to()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        Feed(
            timeline,
            Machine(0, 60),
            Fabricated.TakingOver(
                MonoFloat,
                Fabricated.Packets(
                    AudioChannel.Microphone, MonoFloat, 48_000, 40, 60, Fabricated.Bursts(1))));

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(59_800, 60_200);
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(39_000, 41_500);
        collected.OnsetAfter(AudioChannel.Microphone, 44.9).ShouldNotBeNull().ShouldBe(45.0, tolerance: 0.1);
        collected.Loudest(AudioChannel.Microphone, 5, 35).ShouldBe(0);
    }

    /// <summary>Channel 0, which never stops in any of this: the meeting the microphone is part of.</summary>
    private static IEnumerable<CapturePacket> Machine(double fromSeconds, double untilSeconds) =>
        Fabricated.Packets(
            AudioChannel.Loopback, StereoFloat, 48_000, fromSeconds, untilSeconds, Fabricated.Bursts(1));

    /// <summary>The microphone the recording opened on.</summary>
    private static IEnumerable<CapturePacket> First(double fromSeconds, double untilSeconds) =>
        Fabricated.Packets(
            AudioChannel.Microphone, MonoFloat, 48_000, fromSeconds, untilSeconds, Fabricated.Bursts(1));

    /// <summary>
    /// A microphone taken away at <paramref name="at"/> and replaced at <paramref name="backAt"/>:
    /// one device's packets, nothing at all, and then another device's from its own zero.
    /// </summary>
    private static IEnumerable<CapturePacket> Unplugged(double at, double backAt, double until) =>
        First(0, at).Concat(Fabricated.TakingOver(
            MonoFloat,
            Fabricated.Packets(
                AudioChannel.Microphone, MonoFloat, 48_000, backAt, until, Fabricated.Bursts(1))));

    private static void Feed(
        SharedTimeline timeline,
        IEnumerable<CapturePacket> loopback,
        IEnumerable<CapturePacket> microphone)
    {
        foreach (var packet in Fabricated.Merged(loopback, microphone))
        {
            timeline.Take(packet);
        }
    }
}
