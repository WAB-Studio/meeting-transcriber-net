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

    /// <summary>
    /// ISC-158.6's rule half: the machine is asked again while the window is open, and what it
    /// answers is what the screen offers. A microphone that was not there when the window opened
    /// is recordable with, and one that has gone stops being an answer to the question.
    /// </summary>
    /// <remarks>
    /// What no probe here reaches is Windows saying so. That needs a device to arrive, which no
    /// build agent has and nothing here can fabricate; it is run by hand on a machine with a
    /// microphone to unplug. What is held is everything downstream of the machine's answer, which
    /// is where the meeting is lost: a picker still offering an endpoint that has gone is a record
    /// button that throws.
    /// </remarks>
    [Fact]
    public void A_microphone_that_has_gone_stops_being_chosen()
    {
        var left = Everything.AsTheMicrophonesAreNow([]);

        left.Microphone.ShouldBeNull();
        Screen(RecorderState.Choosing, left).Allows(RecorderPress.Start).ShouldBeFalse();
    }

    /// <summary>
    /// The same device as the machine describes it now, which is the case a match on the id alone
    /// would pass while showing the wrong thing: nothing arrived or went, and what moved is which
    /// endpoint Windows calls the default — the one word the picker puts beside a name.
    /// </summary>
    [Fact]
    public void A_microphone_that_is_still_there_is_taken_as_the_machine_now_describes_it()
    {
        var noLongerDefault = AMicrophone with { IsDefault = false };

        Everything.AsTheMicrophonesAreNow([noLongerDefault]).Microphone.ShouldBe(noLongerDefault);
    }

    [Fact]
    public void Nothing_chosen_stays_nothing_chosen_however_the_machine_changes()
    {
        RecorderChoices.Nothing.AsTheMicrophonesAreNow([AMicrophone]).Microphone.ShouldBeNull();
    }

    /// <summary>
    /// The other half of the same rule, over the programs. A program somebody chose and then
    /// closed is not one channel 0 can follow, and carrying the choice would follow a process id
    /// that now belongs to whatever Windows handed it to.
    /// </summary>
    [Fact]
    public void A_program_that_has_stopped_playing_stops_being_chosen()
    {
        var left = Everything.AsTheSourcesAreNow([RecorderSource.TheWholeMachine]);

        left.Source.ShouldBeNull();
        Screen(RecorderState.Choosing, left).Allows(RecorderPress.Start).ShouldBeFalse();
    }

    [Fact]
    public void A_program_still_playing_stays_chosen()
    {
        Everything.AsTheSourcesAreNow([RecorderSource.TheWholeMachine, AProgram]).ShouldBe(Everything);
    }

    /// <summary>
    /// ISC-172's automated half. The room below may have the whole window in every state a screen
    /// can be in, a meeting being recorded included: nothing here refuses it, and what makes that
    /// safe is that the strip stands in for the recorder half for exactly as long as there is a
    /// meeting to say anything about — so the half travelling out of the way never takes the last
    /// thing saying what a meeting is doing with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Over every state and both arrangements rather than the rows somebody had in mind, because
    /// the failure is a state added later and not thought about: the question is asked of the
    /// screen, and a new state answers it one way or the other whether or not anybody chose.
    /// </para>
    /// <para>
    /// Exactly one of the two is on screen while a meeting is under way, and that is the assertion
    /// rather than two independent ones — a screen with both up says a meeting's length twice, and
    /// a screen with neither is the failure the claim names.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RecorderState.Choosing, false)]
    [InlineData(RecorderState.WithoutACorpus, false)]
    [InlineData(RecorderState.Starting, true)]
    [InlineData(RecorderState.Recording, true)]
    [InlineData(RecorderState.Paused, true)]
    [InlineData(RecorderState.Finishing, true)]
    public void One_of_the_recorder_and_the_strip_says_what_a_meeting_under_way_is_doing(
        RecorderState state,
        bool underWay)
    {
        var docked = Screen(state, Everything);
        var raised = Raised(state, Everything);

        // Docked, the recorder half is the one that says it, whatever the meeting is doing.
        docked.TheRecorderIsOnScreen.ShouldBeTrue();
        docked.TheStripIsOnScreen.ShouldBeFalse();

        // Raised, the strip is — and it is there for every state there is a meeting in, which is
        // the four rather than the two a clock runs in.
        raised.TheRecorderIsOnScreen.ShouldBeFalse();
        raised.TheStripIsOnScreen.ShouldBe(underWay);
    }

    /// <summary>
    /// The other half of the same claim: the press that stops a meeting is on screen wherever the
    /// list is. Asserted as what the claim says — wherever stop can be pressed at all, the thing
    /// carrying it is up — and not as the two sets being equal, which they are not: the strip is
    /// up through a save, where there is nothing left to stop.
    /// </summary>
    /// <remarks>
    /// An implication rather than an equality because the two come off different tables.
    /// <see cref="RecorderStates.Reaches"/> says where stop is offered and
    /// <see cref="RecorderStates.IsInAMeeting"/> says where the strip is, and a state added to the
    /// first and not the second would put stop off the screen for as long as the list is up, which
    /// is the exact thing ISC-172 forbids. Written as an equality this would go green over that.
    /// </remarks>
    [Theory]
    [InlineData(RecorderState.Choosing)]
    [InlineData(RecorderState.WithoutACorpus)]
    [InlineData(RecorderState.Starting)]
    [InlineData(RecorderState.Recording)]
    [InlineData(RecorderState.Paused)]
    [InlineData(RecorderState.Finishing)]
    public void Stopping_is_never_offered_where_the_strip_carrying_it_is_not(RecorderState state)
    {
        var raised = Raised(state, Everything);

        if (raised.Allows(RecorderPress.Stop))
        {
            raised.TheStripIsOnScreen.ShouldBeTrue();
        }
    }

    /// <summary>
    /// And that stop really is offered on it, so the theory above cannot pass by nothing ever
    /// being stoppable.
    /// </summary>
    [Fact]
    public void Stop_is_pressable_on_the_strip_while_a_meeting_is_being_recorded()
    {
        Raised(RecorderState.Recording, Everything).Allows(RecorderPress.Stop).ShouldBeTrue();
        Raised(RecorderState.Paused, Everything).Allows(RecorderPress.Stop).ShouldBeTrue();
    }

    /// <summary>
    /// Nothing offers to open the microphone again while it is recording. It is the same rule the
    /// whole machine follows and for the same reason: what is not available is not on screen, so a
    /// meeting where nothing broke has no press to make about it.
    /// </summary>
    [Fact]
    public void The_microphone_is_not_offered_to_be_opened_again_while_it_is_recording() =>
        Screen(RecorderState.Recording, Everything)
            .Allows(RecorderPress.TryTheMicrophoneAgain).ShouldBeFalse();

    [Fact]
    public void The_microphone_is_offered_to_be_opened_again_once_its_device_has_died() =>
        Died(RecorderState.Recording).Allows(RecorderPress.TryTheMicrophoneAgain).ShouldBeTrue();

    /// <summary>
    /// And it is offered once, not for as long as opening it takes. What says the microphone died
    /// is a reading up to a second old, so without this a second press lands on a channel the first
    /// one already brought back — and the meeting is told the microphone could not be opened
    /// immediately after being told it is recording again.
    /// </summary>
    [Fact]
    public void The_microphone_is_not_offered_again_while_it_is_already_being_opened() =>
        (Died(RecorderState.Recording) with { TheMicrophoneIsBeingOpenedAgain = true })
            .Allows(RecorderPress.TryTheMicrophoneAgain).ShouldBeFalse();

    /// <summary>
    /// Paused as well as recording, which is the opposite of the whole machine and right for the
    /// same reason read the other way: the offer there comes from a level and a paused channel
    /// hears nothing by definition. A stream that ended did not end because nobody spoke into it,
    /// and a meeting somebody paused to go and plug the microphone back in is exactly when this
    /// gets pressed.
    /// </summary>
    [Fact]
    public void The_microphone_is_opened_again_from_a_paused_meeting_too() =>
        Died(RecorderState.Paused).Allows(RecorderPress.TryTheMicrophoneAgain).ShouldBeTrue();

    /// <summary>
    /// Anti: and never once the meeting is over. A microphone that died belongs to a recording
    /// whose devices are already let go of, so the press would reach nothing — and the states it
    /// would reach it in are the two nobody would think to check.
    /// </summary>
    [Theory]
    [InlineData(RecorderState.Starting)]
    [InlineData(RecorderState.Finishing)]
    [InlineData(RecorderState.Choosing)]
    [InlineData(RecorderState.WithoutACorpus)]
    public void The_microphone_is_never_opened_again_outside_a_running_meeting(RecorderState state) =>
        Died(state).Allows(RecorderPress.TryTheMicrophoneAgain).ShouldBeFalse();

    private static RecorderScreen Screen(RecorderState state, RecorderChoices chosen) =>
        new() { State = state, Chosen = chosen };

    private static RecorderScreen Raised(RecorderState state, RecorderChoices chosen) =>
        Screen(state, chosen) with { TheRoomBelowHasTheWindow = true };

    /// <summary>A screen whose microphone lost its device.</summary>
    private static RecorderScreen Died(RecorderState state) =>
        Screen(state, Everything) with { TheMicrophoneDied = true };
}
