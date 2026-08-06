using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Jobs;

public class ProcessingJobTests
{
    private static readonly UtcTimestamp Queued =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 5, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_queued_job_has_never_run_and_is_due_immediately()
    {
        var job = NewJob();

        job.State.ShouldBe(JobState.Pending);
        job.Attempt.ShouldBe(0);
        job.StartedAt.ShouldBeNull();
        job.FinishedAt.ShouldBeNull();
        job.NextAttemptAt.ShouldBeNull();
        job.IsDue(Queued).ShouldBeTrue();
    }

    [Fact]
    public void A_job_without_an_idempotency_key_is_not_queued_at_all()
    {
        Should.Throw<ArgumentException>(
            () => ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), JobKind.Transcribe, "  ", Queued));
    }

    [Fact]
    public void Starting_a_job_counts_the_attempt_and_dates_it()
    {
        var job = NewJob();

        job.Start(Later(1));

        job.State.ShouldBe(JobState.Running);
        job.Attempt.ShouldBe(1);
        job.StartedAt.ShouldBe(Later(1));
        job.IsDue(Later(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_job_that_finished_carries_when_it_did_and_nothing_scheduled()
    {
        var job = NewJob();
        job.Start(Later(1));

        job.Succeed(Later(2));

        job.State.ShouldBe(JobState.Succeeded);
        job.FinishedAt.ShouldBe(Later(2));
        job.NextAttemptAt.ShouldBeNull();
        job.LastError.ShouldBeNull();
    }

    [Fact]
    public void A_retryable_failure_waits_for_its_own_time_and_then_runs_again()
    {
        var job = NewJob();
        job.Start(Later(1));

        job.FailRetryable("deepgram answered 503", Later(10));

        job.State.ShouldBe(JobState.FailedRetryable);
        job.LastError.ShouldBe("deepgram answered 503");
        job.IsDue(Later(9)).ShouldBeFalse();
        job.IsDue(Later(10)).ShouldBeTrue();

        Should.Throw<JobTransitionException>(() => job.Start(Later(9)));

        job.Start(Later(11));
        job.Attempt.ShouldBe(2);
        job.LastError.ShouldBeNull();
        job.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void A_restart_leaves_a_job_that_was_running_on_a_person_and_never_retries_it()
    {
        var job = NewJob();
        job.Start(Later(1));

        job.RecoverAfterRestart().ShouldBeTrue();

        job.State.ShouldBe(JobState.AwaitingUser);
        job.LastError.ShouldNotBeNull();
        // Nothing is scheduled, so no amount of time passing makes the runner take it.
        job.NextAttemptAt.ShouldBeNull();
        job.IsDue(Later(1)).ShouldBeFalse();
        job.IsDue(Later(100_000)).ShouldBeFalse();
        Should.Throw<JobTransitionException>(() => job.Start(Later(100_000)));

        // And the attempt that was cut off is not counted twice.
        job.Attempt.ShouldBe(1);
    }

    [Fact]
    public void A_restart_does_not_touch_a_job_that_was_not_running()
    {
        var queued = NewJob();
        var finished = NewJob();
        finished.Start(Later(1));
        finished.Succeed(Later(2));

        queued.RecoverAfterRestart().ShouldBeFalse();
        finished.RecoverAfterRestart().ShouldBeFalse();

        queued.State.ShouldBe(JobState.Pending);
        queued.IsDue(Later(1)).ShouldBeTrue();
        finished.State.ShouldBe(JobState.Succeeded);
    }

    [Fact]
    public void A_recovered_job_runs_again_only_because_somebody_put_it_back()
    {
        var job = NewJob();
        job.Start(Later(1));
        job.RecoverAfterRestart();

        job.Requeue();

        job.State.ShouldBe(JobState.Pending);
        job.IsDue(Later(2)).ShouldBeTrue();
        Should.NotThrow(() => job.Start(Later(2)));
        job.Attempt.ShouldBe(2);
    }

    [Fact]
    public void A_recovered_job_can_be_settled_by_what_the_restart_found_on_disk()
    {
        var job = NewJob();
        job.Start(Later(1));
        job.RecoverAfterRestart();

        job.Succeed(Later(3));

        job.State.ShouldBe(JobState.Succeeded);
        job.Attempt.ShouldBe(1);
    }

    [Fact]
    public void A_cost_a_person_has_not_approved_keeps_the_job_out_of_the_queue()
    {
        var job = NewJob();

        job.AwaitUser("transcribing this meeting costs money nobody approved yet");

        job.State.ShouldBe(JobState.AwaitingUser);
        job.IsDue(Later(100)).ShouldBeFalse();
    }

    [Fact]
    public void A_scheduled_retry_is_dropped_when_the_job_stops_waiting()
    {
        var job = NewJob();
        job.Start(Later(1));
        job.FailRetryable("deepgram answered 503", Later(10));

        job.AwaitUser("four attempts in a row failed");

        job.NextAttemptAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(JobState.Succeeded)]
    [InlineData(JobState.FailedPermanent)]
    [InlineData(JobState.Cancelled)]
    public void Nothing_moves_a_job_that_is_already_finished(JobState terminal)
    {
        var job = Finished(terminal);

        Should.Throw<JobTransitionException>(() => job.Start(Later(20)));
        Should.Throw<JobTransitionException>(() => job.Succeed(Later(20)));
        Should.Throw<JobTransitionException>(() => job.FailRetryable("again", Later(30)));
        Should.Throw<JobTransitionException>(() => job.FailPermanently("again", Later(20)));
        Should.Throw<JobTransitionException>(() => job.Cancel(Later(20)));
        Should.Throw<JobTransitionException>(() => job.AwaitUser("look at this"));
        Should.Throw<JobTransitionException>(() => job.Requeue());
        job.RecoverAfterRestart().ShouldBeFalse();

        job.State.ShouldBe(terminal);
    }

    [Fact]
    public void A_job_that_never_ran_cannot_report_a_result()
    {
        var job = NewJob();

        Should.Throw<JobTransitionException>(() => job.Succeed(Later(1)));
        Should.Throw<JobTransitionException>(() => job.FailRetryable("nothing ran", Later(2)));
    }

    [Fact]
    public void A_failure_without_a_reason_is_not_recorded()
    {
        var job = NewJob();
        job.Start(Later(1));

        Should.Throw<ArgumentException>(() => job.FailRetryable(" ", Later(2)));
        Should.Throw<ArgumentException>(() => job.FailPermanently(string.Empty, Later(2)));
        job.State.ShouldBe(JobState.Running);
    }

    [Fact]
    public void Cancelling_a_job_ends_it_where_it_stood()
    {
        var job = NewJob();

        job.Cancel(Later(1));

        job.State.ShouldBe(JobState.Cancelled);
        job.FinishedAt.ShouldBe(Later(1));
        job.IsDue(Later(2)).ShouldBeFalse();
    }

    private static ProcessingJob NewJob(JobKind kind = JobKind.Transcribe) => ProcessingJob.Queue(
        Guid.NewGuid(),
        Guid.NewGuid(),
        kind,
        $"{kind}/11111111-1111-1111-1111-111111111111",
        Queued);

    private static ProcessingJob Finished(JobState terminal)
    {
        var job = NewJob();
        job.Start(Later(1));

        switch (terminal)
        {
            case JobState.Succeeded:
                job.Succeed(Later(2));
                break;
            case JobState.FailedPermanent:
                job.FailPermanently("the audio file is gone", Later(2));
                break;
            case JobState.Cancelled:
                job.Cancel(Later(2));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminal), terminal, "Not a terminal state.");
        }

        return job;
    }

    private static UtcTimestamp Later(int minutes) => Queued + Duration.FromMilliseconds(minutes * 60_000L);
}
