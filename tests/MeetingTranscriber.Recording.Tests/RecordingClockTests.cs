using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-158.9, over the half of the recording screen a machine with no sound card can run: whether
/// there is a clock while a meeting is being recorded, and what it counts.
/// </summary>
/// <remarks>
/// What is not here is a window, for the reason <see cref="RecordingMetersTests"/> says: reaching
/// one needs a UI thread and a packaged host. What that leaves unprobed is the number being drawn
/// at all, which is the UI probe's and needs a meeting somebody sat through.
/// </remarks>
public class RecordingClockTests
{
    private static readonly UtcTimestamp Opened = UtcTimestamp.Parse("2026-09-01T14:00:00.000Z");

    /// <summary>The states a meeting is really being recorded in.</summary>
    /// <remarks>
    /// Derived from the enum for the reason <see cref="RecordingMetersTests"/>' pair is: between
    /// this and <see cref="WithNoMeetingRunning"/> they are every state there is by construction,
    /// so a state added and not thought about is held to one of the two rather than to neither.
    /// Which states run is not left to that rule to say — <c>RecordingMetersTests</c> spells the
    /// set out, and a clock that ran in a state the meters were dark in would be caught there.
    /// </remarks>
    public static TheoryData<RecorderState> WhileTheMeetingRuns() =>
        [.. States().Where(state => state.IsRecording())];

    /// <summary>Every state in which there is no meeting to show a clock for.</summary>
    public static TheoryData<RecorderState> WithNoMeetingRunning() =>
        [.. States().Where(state => !state.IsRecording())];

    /// <summary>
    /// ISC-158.9, in both of the states a meeting can be running in, and counting from when the
    /// devices opened rather than from anything later. Paused is the one a rule written as
    /// "recording" would drop, and it is the state somebody is most likely to be reading the
    /// number in: what says a pause is a stretch of the meeting rather than a break in it is the
    /// clock going on climbing through it, which is also what the file does.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhileTheMeetingRuns))]
    public void How_long_the_meeting_has_been_running_is_on_screen_for_as_long_as_it_runs(
        RecorderState state)
    {
        var clock = RecordingClock.Of(state, Opened, Opened + Duration.FromSeconds(754));

        clock.Showing.ShouldBeTrue();
        clock.Ran.ShouldBe(Duration.FromSeconds(754));
    }

    /// <summary>
    /// ISC-158.9's other edge. A clock left standing after the devices are gone is a screen saying
    /// a recording is still going — through the minutes it takes to make a long meeting, which is
    /// exactly when somebody is looking at it.
    /// </summary>
    [Theory]
    [MemberData(nameof(WithNoMeetingRunning))]
    public void No_clock_runs_when_no_meeting_is_being_recorded(RecorderState state)
    {
        var clock = RecordingClock.Of(state, Opened, Opened + Duration.FromSeconds(754));

        clock.Showing.ShouldBeFalse();
        clock.Ran.ShouldBe(Duration.Zero);
    }

    /// <summary>
    /// A state saying a meeting is being recorded with no recording behind it. The two are read
    /// off one field a line apart so nothing produces it, and it is here because the answer has to
    /// be a screen with no clock rather than a throw out of a redraw.
    /// </summary>
    [Fact]
    public void A_screen_with_no_recording_behind_it_shows_no_clock() =>
        RecordingClock.Of(RecorderState.Recording, startedAt: null, Opened)
            .Showing.ShouldBeFalse();

    /// <summary>
    /// A machine that stepped its clock back mid meeting, which is an NTP correction or a resume
    /// from sleep rather than a fault. A <c>Duration</c> refuses to be negative, so subtracting
    /// blind would throw out of a redraw with both devices open and the meeting never stopped.
    /// </summary>
    [Fact]
    public void A_clock_that_ran_backwards_reads_as_no_time_rather_than_throwing() =>
        RecordingClock.Of(
            RecorderState.Recording,
            startedAt: Opened + Duration.FromSeconds(90),
            now: Opened).Ran.ShouldBe(Duration.Zero);

    private static RecorderState[] States() => Enum.GetValues<RecorderState>();
}
