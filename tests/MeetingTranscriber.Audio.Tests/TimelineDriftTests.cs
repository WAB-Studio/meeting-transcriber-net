using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What two devices that disagree do to a long recording. Every test here runs on fabricated
/// packets, which is the whole reason the timeline takes packets rather than opening streams: the
/// product's largest technical risk is a two hour meeting on two crystals, and this is the only
/// place that meeting can be recorded on a machine with no sound card in it.
/// </summary>
public class TimelineDriftTests
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat MonoFloat = new(48_000, 1, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// Where the two devices' crystals really sit. Fifty parts per million slow and two hundred
    /// fast are both ordinary — a real audio clock is within a hundred of its label — and the two
    /// hundred and fifty between them is 1.8 seconds over two hours, which is the drift the
    /// correction has to take out and the reason 50 ms is a claim worth making.
    /// </summary>
    private const double LoopbackCrystal = 48_000 * 0.999_95;

    private const double MicrophoneCrystal = 44_100 * 1.000_2;

    /// <summary>
    /// ISC-35. The headline measurement, and it is taken at every marker rather than at the last
    /// one: a recording that comes back together only at its end was wrong for two hours, and every
    /// citation inside it lands on the wrong words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two hours of 48 kHz is a quarter of a billion frames per source, so nothing here keeps the
    /// recording — <see cref="Onsets"/> watches it go past — and nothing keeps the packets, which
    /// is what <see cref="Fabricated.Merged"/> streams for.
    /// </para>
    /// <para>
    /// 50 ms is the product's number and not the measurement's: every marker of this run landed
    /// inside one frame of 16 kHz on both channels, which is as close as an onset can be read. The
    /// slack is not the probe being weak — resampling each source at its label instead of at the
    /// rate its clock was measured at puts the same run 50 ms apart four minutes in, and 1.8
    /// seconds apart by the end.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_hours_of_two_devices_that_disagree_stay_under_fifty_milliseconds_apart()
    {
        const double Hours = 2;
        const double Marker = 60;
        const double Seconds = Hours * 60 * 60;

        var onsets = new Onsets();
        var timeline = SharedTimeline.Of(StereoFloat, CheapMicrophone, onsets);

        foreach (var packet in Fabricated.Merged(
            Fabricated.Packets(
                AudioChannel.Loopback, StereoFloat, LoopbackCrystal, 0, Seconds, Fabricated.Bursts(Marker)),
            Fabricated.Packets(
                AudioChannel.Microphone, CheapMicrophone, MicrophoneCrystal, 0, Seconds, Fabricated.Bursts(Marker),
                packetFrames: 441)))
        {
            timeline.Take(packet);
        }

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange((long)((Seconds * 1_000) - 100), (long)(Seconds * 1_000) + 100);
        summary.On(AudioChannel.Loopback).MeasuredRate.ShouldBe(LoopbackCrystal, tolerance: 1);
        summary.On(AudioChannel.Microphone).MeasuredRate.ShouldBe(MicrophoneCrystal, tolerance: 1);

        var loopback = onsets.On(AudioChannel.Loopback);
        var microphone = onsets.On(AudioChannel.Microphone);
        var expected = (int)(Seconds / Marker);

        loopback.Count.ShouldBe(expected);
        microphone.Count.ShouldBe(expected);

        for (var marker = 0; marker < expected; marker++)
        {
            // Apart from each other is the claim; away from where the shared clock says the sound
            // happened is the stronger thing that has to be true for it to mean anything, since two
            // channels that drifted together would satisfy the first one and lose the meeting.
            Math.Abs(loopback[marker] - microphone[marker])
                .ShouldBeLessThan(0.050, $"marker {marker} at {marker * Marker} s");
            loopback[marker].ShouldBe(marker * Marker, tolerance: 0.050);
            microphone[marker].ShouldBe(marker * Marker, tolerance: 0.050);
        }
    }

    /// <summary>
    /// ISC-123. A device handed over late and a device running fast are two different numbers and
    /// the recording says which is which. Read as one, a quarter second of a stream opening late
    /// looks like a clock that needs correcting, and the correction it triggers moves the audio that
    /// was already in the right place.
    /// </summary>
    [Fact]
    public void A_source_that_opened_late_and_runs_fast_reports_the_wait_apart_from_the_rate()
    {
        const double Fast = 48_000 * 1.002;
        const double Late = 0.25;

        var onsets = new Onsets();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, onsets);

        foreach (var packet in Fabricated.Merged(
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 60, Fabricated.Bursts(10)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, Fast, Late, 60, Fabricated.Bursts(10))))
        {
            timeline.Take(packet);
        }

        var summary = timeline.Close();

        // The wait is reported as the wait it is, and it is not in the rate.
        summary.On(AudioChannel.Microphone).Waited.Milliseconds.ShouldBeInRange(247, 253);
        summary.On(AudioChannel.Microphone).MeasuredRate.ShouldBe(Fast, tolerance: 20);
        summary.On(AudioChannel.Loopback).Waited.Milliseconds.ShouldBe(0);
        summary.On(AudioChannel.Loopback).MeasuredRate.ShouldBe(48_000, tolerance: 20);

        // And the rate is not in the wait: the microphone missed the marker at zero because it was
        // not open yet, and every marker it did hear is where the loopback heard it.
        var loopback = onsets.On(AudioChannel.Loopback);
        var microphone = onsets.On(AudioChannel.Microphone);

        loopback.Count.ShouldBe(6);
        microphone.Count.ShouldBe(5);

        for (var marker = 0; marker < microphone.Count; marker++)
        {
            microphone[marker].ShouldBe((marker + 1) * 10, tolerance: 0.005);
            loopback[marker + 1].ShouldBe((marker + 1) * 10, tolerance: 0.005);
        }
    }

    /// <summary>
    /// ISC-124. Two devices are drained by two threads on a machine doing other things, so which
    /// source's packet reaches the timeline first is the operating system's business and changes
    /// from one meeting to the next. Only what a packet says may decide where its audio goes.
    /// </summary>
    /// <remarks>
    /// The bound is real and it is the half minute a source may fall behind by: a source handed over
    /// after that is a device the recording has given up on, which is <see cref="SharedTimelineTests"/>'s
    /// claim and a different thing from being handed over late.
    /// </remarks>
    [Fact]
    public void Handing_a_source_over_in_clumps_seconds_late_records_the_same_meeting()
    {
        var smooth = Record(clumpedIntoSeconds: 0);
        var jittery = Record(clumpedIntoSeconds: 5);

        jittery.Recording.Frames.ShouldBe(smooth.Recording.Frames);
        jittery.Recording.FirstDifferenceFrom(smooth.Recording).ShouldBeNull();
        jittery.Summary.Length.ShouldBe(smooth.Summary.Length);
        jittery.Summary.On(AudioChannel.Microphone).Waited
            .ShouldBe(smooth.Summary.On(AudioChannel.Microphone).Waited);

        static (Collected Recording, TimelineSummary Summary) Record(double clumpedIntoSeconds)
        {
            var collected = new Collected();
            var timeline = SharedTimeline.Of(StereoFloat, CheapMicrophone, collected);

            foreach (var packet in Fabricated.Merged(
                Fabricated.Packets(
                    AudioChannel.Loopback, StereoFloat, LoopbackCrystal, 0, 60, Fabricated.Bursts(1)),
                Fabricated.Packets(
                    AudioChannel.Microphone, CheapMicrophone, MicrophoneCrystal, 0.04, 60, Fabricated.Bursts(1),
                    packetFrames: 441),
                clumpedIntoSeconds))
            {
                timeline.Take(packet);
            }

            return (collected, timeline.Close());
        }
    }

    /// <summary>
    /// ISC-125. Somebody stops the recording and one device's last packets are already in. The
    /// meeting is as long as the last thing anybody said, not as long as the source that stopped
    /// first — cutting it there would drop the end of a conversation with nothing saying so.
    /// </summary>
    [Fact]
    public void A_source_that_stopped_early_does_not_cut_the_meeting_short()
    {
        var collected = new Collected();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, collected);

        foreach (var packet in Fabricated.Merged(
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 20, Fabricated.Bursts(1)),
            Fabricated.Packets(AudioChannel.Microphone, MonoFloat, 48_000, 0, 15, Fabricated.Bursts(1))))
        {
            timeline.Take(packet);
        }

        var summary = timeline.Close();

        summary.Length.Milliseconds.ShouldBeInRange(19_940, 20_060);
        summary.On(AudioChannel.Microphone).Missing.Milliseconds.ShouldBeInRange(4_940, 5_060);
        summary.On(AudioChannel.Loopback).Missing.Milliseconds.ShouldBe(0);

        // The microphone's last whole packet is the one before it stopped, so its last marker is the
        // one at fourteen seconds; the loopback goes on marking every second to the end.
        collected.OnsetAfter(AudioChannel.Microphone, 13.9).ShouldNotBeNull().ShouldBe(14, tolerance: 0.005);
        collected.Loudest(AudioChannel.Microphone, 14.5, 20).ShouldBe(0);
        collected.OnsetAfter(AudioChannel.Loopback, 18.9).ShouldNotBeNull().ShouldBe(19, tolerance: 0.005);
    }
}
