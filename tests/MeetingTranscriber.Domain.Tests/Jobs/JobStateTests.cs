using MeetingTranscriber.Domain.Jobs;

namespace MeetingTranscriber.Domain.Tests.Jobs;

/// <summary>
/// The transition table on its own. What a job does with a move is <see cref="ProcessingJobTests"/>;
/// here it is only which moves exist at all.
/// </summary>
public class JobStateTests
{
    [Fact]
    public void Every_state_says_where_it_can_go()
    {
        foreach (var state in Enum.GetValues<JobState>())
        {
            Should.NotThrow(() => state.Next());
        }
    }

    [Fact]
    public void A_state_the_enum_does_not_have_is_not_guessed_at()
    {
        Should.Throw<JobTransitionException>(() => ((JobState)99).Next());
    }

    [Theory]
    [InlineData(JobState.Succeeded)]
    [InlineData(JobState.FailedPermanent)]
    [InlineData(JobState.Cancelled)]
    public void A_finished_job_reaches_nothing(JobState terminal)
    {
        terminal.IsTerminal().ShouldBeTrue();

        foreach (var state in Enum.GetValues<JobState>())
        {
            terminal.CanMoveTo(state).ShouldBeFalse($"{terminal} should not reach {state}");
        }
    }

    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Running)]
    [InlineData(JobState.AwaitingUser)]
    [InlineData(JobState.FailedRetryable)]
    public void A_job_that_is_still_moving_is_not_terminal(JobState state)
    {
        state.IsTerminal().ShouldBeFalse();
    }

    [Fact]
    public void Only_the_states_the_runner_owns_are_picked_up_by_itself()
    {
        JobState.Pending.IsQueued().ShouldBeTrue();
        JobState.FailedRetryable.IsQueued().ShouldBeTrue();

        JobState.AwaitingUser.IsQueued().ShouldBeFalse();
        JobState.Running.IsQueued().ShouldBeFalse();
        JobState.Succeeded.IsQueued().ShouldBeFalse();
        JobState.FailedPermanent.IsQueued().ShouldBeFalse();
        JobState.Cancelled.IsQueued().ShouldBeFalse();
    }

    [Fact]
    public void Waiting_for_a_person_is_left_only_by_a_move_a_person_asked_for()
    {
        JobState.AwaitingUser.Next().ShouldBe(
            [JobState.Pending, JobState.Succeeded, JobState.FailedPermanent, JobState.Cancelled],
            ignoreOrder: true);
    }

    [Fact]
    public void A_retryable_failure_runs_again_without_going_back_through_the_queue()
    {
        JobState.FailedRetryable.CanMoveTo(JobState.Running).ShouldBeTrue();
        JobState.FailedRetryable.CanMoveTo(JobState.Pending).ShouldBeFalse();
    }

    [Fact]
    public void A_rejected_move_names_the_ones_that_exist()
    {
        var error = Should.Throw<JobTransitionException>(
            () => JobState.Pending.EnsureCanMoveTo(JobState.Succeeded));

        error.Message.ShouldContain("Running");
    }

    [Fact]
    public void A_rejected_move_out_of_a_finished_job_says_it_is_finished()
    {
        var error = Should.Throw<JobTransitionException>(
            () => JobState.Succeeded.EnsureCanMoveTo(JobState.Running));

        error.Message.ShouldContain("terminal");
    }
}
