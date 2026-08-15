using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What two clocks that disagree do to a long recording. Every test here runs on fabricated
/// packets, which is the whole reason the timeline takes packets rather than opening streams: the
/// product's largest technical risk is a two hour meeting on two crystals, and this is the only
/// place that meeting can be recorded on a machine with no sound card in it.
/// </summary>
/// <remarks>
/// The line against <see cref="SharedTimelineTests"/> is time. What the timeline does with a
/// packet — where it lands, what a hole becomes, which failures stop a recording — is that class;
/// what a device's clock does to a recording as it runs is this one.
/// </remarks>
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
    /// How far a device's reported instant wanders from where it really was. `SourceClock` says
    /// around a millisecond, and it is the reason that class will not measure a rate over a short
    /// window; a drift probe without it measures a clock the product does not have.
    /// </summary>
    private const double JitterMs = 1;

    /// <summary>
    /// ISC-66. The headline measurement, and it is taken at every marker rather than at the last
    /// one: a recording that comes back together only at its end was wrong for two hours, and every
    /// citation inside it lands on the wrong words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two hours of 48 kHz is a quarter of a billion frames per source, so nothing here keeps the
    /// recording — <see cref="Onsets"/> watches it go past — and nothing keeps the packets, which
    /// is what <see cref="Fabricated.Merged"/> streams for. The packets are 10 ms of audio because
    /// that is what the endpoints on this machine really handed over: 2015 of them over 20 seconds,
    /// recorded under ISC-65.
    /// </para>
    /// <para>
    /// 50 ms is the product's number and not the measurement's, and the slack is not the probe
    /// being weak: resampling each source at its label instead of at the rate its clock was
    /// measured at puts the same run past 50 ms at the fourth marker, and 1.8 seconds apart by the
    /// end.
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
                AudioChannel.Loopback, StereoFloat, LoopbackCrystal, 0, Seconds, Fabricated.Bursts(Marker),
                jitterMs: JitterMs),
            Fabricated.Packets(
                AudioChannel.Microphone, CheapMicrophone, MicrophoneCrystal, 0, Seconds, Fabricated.Bursts(Marker),
                packetFrames: 441, jitterMs: JitterMs)))
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
    /// ISC-67, both halves of it. A device handed over late and a device running fast are two different
    /// numbers, and the recording keeps them apart. Read as one, a quarter second of a stream
    /// opening late looks like a clock that needs correcting, and the correction it triggers moves
    /// the audio that was already in the right place.
    /// </summary>
    [Fact]
    public void A_source_that_opened_late_and_runs_fast_reports_the_wait_apart_from_the_rate()
    {
        const double Fast = 48_000 * 1.002;
        const double Late = 0.25;

        var onsets = new Onsets();
        var timeline = SharedTimeline.Of(StereoFloat, MonoFloat, onsets);

        foreach (var packet in Fabricated.Merged(
            Fabricated.Packets(
                AudioChannel.Loopback, StereoFloat, 48_000, 0, 60, Fabricated.Bursts(10), jitterMs: JitterMs),
            Fabricated.Packets(
                AudioChannel.Microphone, MonoFloat, Fast, Late, 60, Fabricated.Bursts(10), jitterMs: JitterMs)))
        {
            timeline.Take(packet);
        }

        var summary = timeline.Close();

        // The wait is reported as the wait it is, and it is not in the rate.
        summary.On(AudioChannel.Microphone).Waited.Milliseconds.ShouldBeInRange(246, 254);
        summary.On(AudioChannel.Microphone).MeasuredRate.ShouldBe(Fast, tolerance: 20);
        summary.On(AudioChannel.Loopback).Waited.Milliseconds.ShouldBe(0);
        summary.On(AudioChannel.Loopback).MeasuredRate.ShouldBe(48_000, tolerance: 20);

        // And the rate is not in the wait. The microphone missed the marker at zero
        // because it was not open yet, and every marker it did hear is where the loopback heard it
        // — a quarter second read as drift would have pulled all five of them off instead.
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
}
