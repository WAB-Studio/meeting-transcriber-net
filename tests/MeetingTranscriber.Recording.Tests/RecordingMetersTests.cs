using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-80 and ISC-150, over the half of the recording screen a machine with no sound card can
/// run: what each channel reads as while a meeting is being recorded, and the one warning about
/// this machine that costs nothing to be sure of.
/// </summary>
/// <remarks>
/// What is not here is a window, for the reason <see cref="RecorderScreenTests"/> says: reaching
/// one needs a UI thread and a packaged host. What that leaves unprobed is a label wired to the
/// wrong channel, and the only thing that reaches it is somebody recording a meeting.
/// </remarks>
public class RecordingMetersTests
{
    /// <summary>Roughly where speech sits: a twentieth of full scale, or about -26 dBFS.</summary>
    private static readonly LevelReading Speech = new(0.05f);

    private static readonly LevelReading Nothing = new(0f);

    private static readonly AudioDevice Speakers =
        new("{speakers}", "Desk speakers", IsDefault: true) { Kind = EndpointKind.Speakers };

    private static readonly AudioDevice AHeadset =
        new("{headset}", "A headset", IsDefault: true) { Kind = EndpointKind.Headset };

    /// <summary>The states a meeting is really being recorded in.</summary>
    /// <remarks>
    /// Both sets are derived from the enum and not listed by hand, and between them they are every
    /// state there is — which is what `Every_state_is_on_one_side_of_the_meters_or_the_other` holds.
    /// A state added and not thought about would otherwise be metered by neither theory and go red
    /// nowhere, and the states most likely to be added next are the ones a recovery screen needs.
    /// </remarks>
    public static TheoryData<RecorderState> WhileTheMeetingRuns() =>
        [.. States().Where(state => state.IsRecording())];

    /// <summary>Every state in which there is no meeting to meter.</summary>
    public static TheoryData<RecorderState> WithNoMeetingRunning() =>
        [.. States().Where(state => !state.IsRecording())];

    /// <summary>
    /// Which states meter, spelled out here rather than asked of the rule, so the two sets above
    /// are held against a statement of what running means and not against themselves.
    /// </summary>
    /// <remarks>
    /// A state added to the enum falls into the second set by construction and is held to showing
    /// no meters, which is the safe half. If it is one that should meter, this is what goes red —
    /// so the answer is written down somewhere rather than defaulted to.
    /// </remarks>
    [Fact]
    public void Only_a_meeting_that_is_running_is_metered() =>
        States().Where(state => state.IsRecording())
            .ShouldBe([RecorderState.Recording, RecorderState.Paused], ignoreOrder: true);

    /// <summary>
    /// ISC-80. A channel bringing back nothing reads as nothing, and one bringing back speech does
    /// not — which is the whole of the claim: a muted microphone and a microphone in a quiet room
    /// full of people have to be the same thing on screen as what they are, and they are only
    /// different if the screen shows the level at all.
    /// </summary>
    [Fact]
    public void A_channel_hearing_nothing_reads_as_silent_and_one_hearing_something_does_not()
    {
        var meters = Metered(RecorderState.Recording, Speakers, others: Nothing, mine: Speech);

        var others = meters.On(AudioChannel.Loopback).ShouldNotBeNull();
        others.IsSilent.ShouldBeTrue();
        others.Meter.ShouldBe(0);

        var mine = meters.On(AudioChannel.Microphone).ShouldNotBeNull();
        mine.IsSilent.ShouldBeFalse();
        mine.Meter.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// ISC-80's other half: while the meeting is still running, and in both of the states it can
    /// be running in. A paused meeting is the one that would be dropped by a rule written as
    /// "recording", and it is the state somebody is most likely to be watching the meters in.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhileTheMeetingRuns))]
    public void Both_channels_are_metered_for_as_long_as_the_meeting_is_running(RecorderState state)
    {
        var meters = Metered(state, Speakers, others: Nothing, mine: Speech);

        // Reached by channel and not by position. What order the readings arrive in is the
        // projection's, which needs two open devices and is not something this can hold — so
        // asserting it here would be asserting the order this test built them in.
        meters.Showing.ShouldBeTrue();
        meters.On(AudioChannel.Loopback).ShouldNotBeNull();
        meters.On(AudioChannel.Microphone).ShouldNotBeNull();
    }

    /// <summary>
    /// A meter left standing after the meeting is over is the last second of a recording that has
    /// stopped, and somebody reads it as one that is still going. Finishing is the case that
    /// matters: the devices are already let go of, and making a long meeting takes minutes.
    /// </summary>
    [Theory]
    [MemberData(nameof(WithNoMeetingRunning))]
    public void Nothing_is_metered_when_no_meeting_is_being_recorded(RecorderState state)
    {
        var meters = Metered(state, Speakers, others: Speech, mine: Speech);

        meters.Showing.ShouldBeFalse();
        meters.Channels.ShouldBeEmpty();
        meters.On(AudioChannel.Loopback).ShouldBeNull();
    }

    /// <summary>ISC-150.</summary>
    [Theory]
    [MemberData(nameof(WhileTheMeetingRuns))]
    public void A_meeting_playing_through_speakers_says_the_others_are_heard_twice(RecorderState state) =>
        Metered(state, Speakers, others: Speech, mine: Speech)
            .TheOthersAreHeardTwice.ShouldBeTrue();

    [Theory]
    [MemberData(nameof(WhileTheMeetingRuns))]
    public void A_meeting_playing_through_a_headset_says_nothing_of_the_kind(RecorderState state) =>
        Metered(state, AHeadset, others: Speech, mine: Speech)
            .TheOthersAreHeardTwice.ShouldBeFalse();

    /// <summary>
    /// The warning is about a microphone that is open, so it goes when the microphone does. It is
    /// worse than a meter left standing: a line about the room hearing somebody, on a screen where
    /// nothing is being recorded, is a sentence about a meeting that is not happening.
    /// </summary>
    [Theory]
    [MemberData(nameof(WithNoMeetingRunning))]
    public void Nothing_is_warned_about_when_no_meeting_is_being_recorded(RecorderState state) =>
        Metered(state, Speakers, others: Speech, mine: Speech)
            .TheOthersAreHeardTwice.ShouldBeFalse();

    /// <summary>
    /// A device that stopped on its own, while the rest of the meeting carries on being recorded.
    /// It is a channel of the conversation gone, and what says so is the channel it happened to —
    /// a screen that only said "a device stopped" would leave somebody guessing which half of the
    /// meeting they still have.
    /// </summary>
    [Fact]
    public void A_channel_whose_device_stopped_on_its_own_says_so_and_says_what_it_said()
    {
        var meters = Metered(
            RecorderState.Recording,
            Speakers,
            others: Speech,
            mine: Nothing,
            mineStopped: "The microphone was removed.");

        meters.On(AudioChannel.Loopback).ShouldNotBeNull().Stopped.ShouldBeFalse();

        var mine = meters.On(AudioChannel.Microphone).ShouldNotBeNull();
        mine.Stopped.ShouldBeTrue();
        mine.Said.ShouldBe("The microphone was removed.");
    }

    /// <summary>
    /// A device that ended without saying anything still says it ended. Windows is under no
    /// obligation to explain an endpoint that went away, and a screen that showed the fault only
    /// when there was a sentence to print would say nothing at all about the unplugged one.
    /// </summary>
    [Fact]
    public void A_device_that_ended_without_saying_anything_still_says_it_ended()
    {
        var reading = Reading(AudioChannel.Microphone, Nothing, stopped: true, said: null);

        reading.Stopped.ShouldBeTrue();
        reading.Said.ShouldBeNull();
    }

    /// <summary>
    /// A source past full scale is full and never more than full. A reading that clipped is
    /// something to see rather than something to draw off the end of the meter, and a float format
    /// is free to report past one.
    /// </summary>
    [Fact]
    public void A_channel_that_clipped_fills_the_meter_and_no_further()
    {
        Reading(AudioChannel.Microphone, new LevelReading(1f)).Meter.ShouldBe(1);
        Reading(AudioChannel.Microphone, new LevelReading(4f)).Meter.ShouldBe(1);
    }

    /// <summary>
    /// The bar is drawn in decibels and not off the peak itself. Speech at a twentieth of full
    /// scale is a twentieth of a bar drawn the other way, which reads as almost nothing for a
    /// meeting that is recording perfectly well — so it has to be past a quarter, not near zero.
    /// </summary>
    [Fact]
    public void A_channel_hearing_speech_draws_a_bar_somebody_can_see() =>
        Reading(AudioChannel.Microphone, Speech).Meter.ShouldBeGreaterThan(0.25);

    /// <summary>
    /// The one English word a level could put on screen, and where it is kept off it. A reading
    /// hands back the number or nothing at all, so the sentence for having heard nothing comes from
    /// the catalogue — which is the only thing a screen can be held to, since nothing probes the
    /// window itself.
    /// </summary>
    [Fact]
    public void A_reading_carries_a_number_or_no_words_at_all()
    {
        Reading(AudioChannel.Microphone, Nothing).Loudness.ShouldBeNull();
        Reading(AudioChannel.Microphone, Speech).Loudness.ShouldNotBeNull()
            .ShouldNotContain("silent", Case.Insensitive);
    }

    private static RecorderState[] States() => Enum.GetValues<RecorderState>();

    private static RecordingMeters Metered(
        RecorderState state,
        AudioDevice playback,
        LevelReading others,
        LevelReading mine,
        string? mineStopped = null) =>
        RecordingMeters.Of(
            state,
            playback,
            [
                Reading(AudioChannel.Loopback, others),
                Reading(AudioChannel.Microphone, mine, mineStopped is not null, mineStopped),
            ]);

    private static ChannelReading Reading(
        AudioChannel channel, LevelReading level, bool stopped = false, string? said = null) =>
        new()
        {
            Channel = channel,
            Capturing = channel == AudioChannel.Loopback ? "Desk speakers" : "A microphone",
            Level = level,
            Stopped = stopped,
            Said = said,
        };
}
