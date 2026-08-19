using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-158.5 and ISC-139.2, over the half of the recording screen a machine with no sound card can
/// run: what is offered, and what a press would have to be answered with first.
/// </summary>
/// <remarks>
/// What is not here is a window. Reaching one needs a UI thread and a packaged host, neither of
/// which a build agent has, so the rules live where they can be asked and the window reads them
/// off. What that leaves unprobed is a control wired to the wrong question, and the only thing
/// that reaches it is somebody recording a meeting.
/// </remarks>
public class RecorderScreenTests
{
    private static readonly AudioDevice AMicrophone = new("{a-microphone}", "A microphone", IsDefault: true);

    private static readonly RecorderSource AProgram =
        RecorderSource.Following(new AudioProcess(1234, "a-program", StartedBy: 1));

    private static readonly RecorderChoices Everything = new()
    {
        Microphone = AMicrophone,
        Source = AProgram,
        Spoken = "es",
    };

    public static TheoryData<string, RecorderChoices> AnswersShortOfOne() => new()
    {
        { "the microphone", Everything with { Microphone = null } },
        { "what channel 0 follows", Everything with { Source = null } },
        { "what will be spoken", Everything with { Spoken = null } },
        { "what will be spoken, left blank", Everything with { Spoken = "  " } },
    };

    [Fact]
    public void A_screen_opens_with_nothing_said_and_nothing_to_press()
    {
        var screen = Screen(RecorderState.Choosing, RecorderChoices.Nothing);

        screen.Chosen.Settled.ShouldBeFalse();
        screen.Available.ShouldBeEmpty();
    }

    /// <summary>
    /// The claim itself, one unanswered question at a time, so a build that stopped asking for one
    /// of the three fails at that one rather than passing on the other two.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnswersShortOfOne))]
    public void Recording_cannot_start_with_one_of_the_three_unanswered(string missing, RecorderChoices chosen)
    {
        var screen = Screen(RecorderState.Choosing, chosen);

        chosen.Settled.ShouldBeFalse(missing);
        screen.Allows(RecorderPress.Start).ShouldBeFalse(missing);
    }

    [Fact]
    public void Recording_starts_once_all_three_have_been_answered()
    {
        var screen = Screen(RecorderState.Choosing, Everything);

        Everything.Settled.ShouldBeTrue();
        screen.Available.ShouldBe([RecorderPress.Start]);
    }

    /// <summary>
    /// The whole machine is not takeable while nothing is being recorded, however settled the
    /// choices are: there is no meeting for it to move.
    /// </summary>
    [Fact]
    public void The_whole_machine_is_not_takeable_before_a_meeting_is_running()
    {
        var screen = Screen(RecorderState.Choosing, Everything) with { WholeMachineOffered = true };

        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_being_recorded_is_paused_or_stopped_and_never_started_again()
    {
        var screen = Screen(RecorderState.Recording, Everything);

        screen.Available.ShouldBe([RecorderPress.Pause, RecorderPress.Stop], ignoreOrder: true);
    }

    [Fact]
    public void A_paused_meeting_is_resumed_or_stopped_without_being_resumed_first()
    {
        var screen = Screen(RecorderState.Paused, Everything);

        screen.Available.ShouldBe([RecorderPress.Resume, RecorderPress.Stop], ignoreOrder: true);
    }

    [Fact]
    public void A_meeting_being_made_takes_no_press_at_all()
    {
        var screen = Screen(RecorderState.Finishing, Everything);

        screen.Available.ShouldBeEmpty();
    }

    /// <summary>
    /// Least of all record again. Opening two devices takes as long as the slower of them, which
    /// is long enough to press a button twice, and the second press would open a second meeting
    /// over the top of the first.
    /// </summary>
    [Fact]
    public void A_meeting_being_started_takes_no_press_at_all()
    {
        var screen = Screen(RecorderState.Starting, Everything);

        screen.Available.ShouldBeEmpty();
    }

    [Fact]
    public void With_nowhere_to_record_into_nothing_is_pressable()
    {
        var screen = Screen(RecorderState.WithoutACorpus, Everything);

        screen.Available.ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-139.2. The offer is the whole of the consent on a screen: what is not available is not
    /// on screen, so before the recording has offered it there is nothing to press.
    /// </summary>
    [Fact]
    public void The_whole_machine_cannot_be_taken_before_the_recording_has_offered_it()
    {
        var screen = Screen(RecorderState.Recording, Everything);

        screen.WholeMachineOffered.ShouldBeFalse();
        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeFalse();
    }

    [Fact]
    public void The_whole_machine_is_takeable_once_the_recording_has_offered_it()
    {
        var screen = Screen(RecorderState.Recording, Everything) with { WholeMachineOffered = true };

        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeTrue();
    }

    [Fact]
    public void The_whole_machine_is_taken_once_and_is_not_on_offer_afterwards()
    {
        var screen = Screen(RecorderState.Recording, Everything) with
        {
            WholeMachineOffered = true,
            WholeMachineTaken = true,
        };

        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeFalse();
    }

    /// <summary>
    /// A meeting already recording everything the machine plays has nowhere to move to, so the
    /// offer says nothing even if something made it.
    /// </summary>
    [Fact]
    public void A_meeting_already_recording_the_whole_machine_is_never_offered_it()
    {
        var screen = Screen(
            RecorderState.Recording,
            Everything with { Source = RecorderSource.TheWholeMachine }) with
        { WholeMachineOffered = true };

        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeFalse();
    }

    /// <summary>
    /// Paused, the meeting is hearing nothing from anything, so the rule the offer rests on would
    /// be true of a program that is playing perfectly well.
    /// </summary>
    [Fact]
    public void The_whole_machine_is_never_taken_while_the_meeting_is_paused()
    {
        var screen = Screen(RecorderState.Paused, Everything) with { WholeMachineOffered = true };

        screen.Allows(RecorderPress.RecordTheWholeMachine).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false, RecorderStep.Nothing, RecorderState.Choosing)]
    [InlineData(true, false, RecorderStep.Nothing, RecorderState.Recording)]
    [InlineData(true, true, RecorderStep.Nothing, RecorderState.Paused)]
    [InlineData(false, false, RecorderStep.Starting, RecorderState.Starting)]
    [InlineData(true, false, RecorderStep.Finishing, RecorderState.Finishing)]
    public void The_state_is_read_off_the_meeting(
        bool started, bool paused, RecorderStep step, RecorderState expected)
    {
        RecorderStates.Of(corpus: true, started, paused, step).ShouldBe(expected);
    }

    /// <summary>
    /// Stop lets the devices go before it starts making the meeting, so a meeting that was paused
    /// when stop was pressed is still paused as far as anything can see. Asking that first would
    /// leave resume on screen for the minutes it takes to write the file.
    /// </summary>
    [Fact]
    public void A_meeting_being_made_is_never_shown_as_paused()
    {
        RecorderStates.Of(corpus: true, started: true, paused: true, step: RecorderStep.Finishing)
            .ShouldBe(RecorderState.Finishing);
    }

    /// <summary>
    /// The other end of the same rule. Nothing is recorded yet while the devices open, and a
    /// screen that read that as nothing having been chosen would offer record a second time.
    /// </summary>
    [Fact]
    public void A_meeting_being_started_is_never_shown_as_one_nobody_has_started()
    {
        RecorderStates.Of(corpus: true, started: false, paused: false, step: RecorderStep.Starting)
            .ShouldBe(RecorderState.Starting);
    }

    /// <summary>
    /// Nowhere to record into outranks everything, including a meeting somebody is in the middle
    /// of: a corpus that was refused is refused before anything could have been started, so this
    /// is the state a screen opens in rather than one it falls into.
    /// </summary>
    [Fact]
    public void Nowhere_to_record_into_is_the_answer_before_any_other()
    {
        RecorderStates.Of(corpus: false, started: true, paused: false, step: RecorderStep.Finishing)
            .ShouldBe(RecorderState.WithoutACorpus);
    }

    [Fact]
    public void A_state_the_table_does_not_have_is_refused_rather_than_read_as_nothing_pressable()
    {
        Should.Throw<RecordingException>(() => ((RecorderState)99).Reaches());
    }

    private static RecorderScreen Screen(RecorderState state, RecorderChoices chosen) =>
        new() { State = state, Chosen = chosen };
}
