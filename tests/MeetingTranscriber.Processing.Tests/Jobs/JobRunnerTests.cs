using System.Diagnostics;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Jobs;

namespace MeetingTranscriber.Processing.Tests.Jobs;

/// <summary>
/// Sending every due transcription, one corpus at a time, under the corpus's own lease.
/// </summary>
/// <remarks>
/// Every job here is queued through <see cref="MeetingWork.Take"/> or
/// <see cref="RecordedMeetings.Started"/>, and every pass takes the lease through
/// <see cref="RunnerLease.TryTake"/> — the same two doors a real launch and a real press go
/// through, so nothing here proves a shortcut the application does not have.
/// </remarks>
public sealed class JobRunnerTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    /// <summary>Goes red with the query narrowed to <see cref="JobState.Running"/>.</summary>
    [Fact]
    public async Task A_queued_transcription_comes_back_transcribed_with_nothing_pressed()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, jobId;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken);

        run.Ran.ShouldBe([jobId]);
        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);
        reopened.Artifacts.Count(row => row.MeetingId == meeting).ShouldBeGreaterThan(0);
    }

    /// <summary>Goes red with due-ness asked of the state alone, ignoring <c>NextAttemptAt</c>.</summary>
    [Fact]
    public async Task Nothing_waiting_on_a_person_is_ever_started_by_itself()
    {
        using var corpus = new TemporaryCorpus();

        using (var context = corpus.OpenMigrated())
        {
            var waiting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var waitingJob = new MeetingWork(context, When).Take(waiting);
            waitingJob.Start(When);
            waitingJob.AwaitUser("a restart found this job running");
            context.SaveChanges();

            var retrying = RecordedMeetings.Recorded(
                context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
            var retryingJob = new MeetingWork(context, When).Take(retrying);
            retryingJob.Start(When);

            // An hour past whenever this really runs, and not past `When`: the pass below is asked
            // over `TimeProvider.System`, so a bound fixed to the fixture's own instant could have
            // already passed by the time a real clock reads it.
            var notBefore = UtcTimestamp.From(TimeProvider.System.GetUtcNow()) + Duration.FromMilliseconds(3_600_000);
            retryingJob.FailRetryable("a transient failure", notBefore);
            context.SaveChanges();
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Ran.ShouldBeEmpty();
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task A_job_of_a_kind_nothing_runs_is_left_where_it_is()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = ProcessingJob.Queue(
                Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", When);
            context.ProcessingJobs.Add(job);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Ran.ShouldBeEmpty();
        called.ShouldBeFalse();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Pending);
    }

    /// <summary>Goes red with <c>NothingWasCharged</c> answered with <c>FailRetryable</c>.</summary>
    [Fact]
    public async Task A_call_the_provider_refused_leaves_the_stage_offered_again()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        SendingToTheProvider send = (_, _, _, _) => throw new DeepgramCallException(
            "Deepgram would not accept this machine's key.", mayHaveBeenCharged: false);

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Left.Count.ShouldBe(1);

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.FailedPermanent);
        job2.LastError.ShouldNotBeNull();
    }

    /// <summary>Goes red with <c>MayHaveBeenCharged</c> answered with <c>FailPermanently</c>.</summary>
    [Fact]
    public async Task A_call_that_may_already_have_been_charged_for_stops_on_a_person()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        SendingToTheProvider send = (_, _, _, _) => throw new DeepgramCallException(
            "Deepgram failed the request on its own side.", mayHaveBeenCharged: true);

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Left.Count.ShouldBe(1);

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.AwaitingUser);
        job2.AwaitingReason.ShouldNotBeNull();
    }

    /// <summary>Goes red with the cancellation falling through to a written outcome.</summary>
    [Fact]
    public async Task A_send_the_application_walked_out_of_leaves_the_job_where_a_restart_will_find_it()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        using var walkingOut = new CancellationTokenSource();
        SendingToTheProvider send = (_, _, _, stopping) =>
        {
            walkingOut.Cancel();
            stopping.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        };

        await Should.ThrowAsync<OperationCanceledException>(() => JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, walkingOut.Token));

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.Running);
        job2.Attempt.ShouldBe(1);
    }

    /// <summary>Asserted on the queued job by its id, and never on <paramref name="called"/> being true.</summary>
    [Fact]
    public async Task A_meeting_transcribed_while_its_job_waited_is_not_sent_and_its_job_succeeds()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);

            // Transcribed leaves the response filed and its own job Pending — the shape a meeting
            // whose response was filed by a run this pass never made looks like from the outside.
            MeetingRows.Transcribed(context, meeting, When, responseSha256: new string('e', 64));
            context.SaveChanges();
            jobId = context.ProcessingJobs.Single(row => row.MeetingId == meeting).Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Ran.ShouldBe([jobId]);
        called.ShouldBeFalse();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);
    }

    [Fact]
    public async Task A_job_another_runner_already_took_is_left_alone()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using (var elsewhere = corpus.Open())
        {
            elsewhere.ProcessingJobs.Single(row => row.Id == jobId).Start(When);
            elsewhere.SaveChanges();
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Ran.ShouldBeEmpty();
        run.Left.ShouldBeEmpty();
        called.ShouldBeFalse();

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.Running);
        job2.Attempt.ShouldBe(1);
    }

    /// <summary>Goes red with the write guarded on <c>CanMoveTo</c> alone.</summary>
    [Fact]
    public async Task A_pass_whose_job_was_taken_again_under_it_writes_nothing()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            // A restart and a retry, both under a second connection, while this pass's own call is
            // still out — the same shape a lease lost and regained under a live call would leave.
            using (var elsewhere = corpus.Open())
            {
                var racing = elsewhere.ProcessingJobs.Single(row => row.Id == jobId);
                racing.RecoverAfterRestart();
                racing.Requeue();
                racing.Start(When + Duration.FromSeconds(1));
                elsewhere.SaveChanges();
            }

            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Left.Count.ShouldBe(1);

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.Running);
        job2.Attempt.ShouldBe(2);
    }

    /// <summary>A corpus.db of garbage bytes gives one line and no throw.</summary>
    [Fact]
    public async Task A_pass_over_a_corpus_that_will_not_open_says_so_and_does_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        await File.WriteAllBytesAsync(
            corpus.DatabasePath, "not a database"u8.ToArray(), TestContext.Current.CancellationToken);

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Ran.ShouldBeEmpty();
        run.Left.Count.ShouldBe(1);
        called.ShouldBeFalse();
    }

    /// <summary>Goes red with the delay first: the job never succeeds.</summary>
    [Fact]
    public async Task The_pump_looks_once_before_it_waits()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        using var stopping = new CancellationTokenSource();

        var pump = JobRunner.PumpAsync(
            corpus.Root,
            new TimersNeverFire(),
            FixtureBody(DeepgramFixtures.TwoChannelShort),
            TimeSpan.FromMinutes(10),
            stopping.Token);

        var succeeded = false;
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var reading = corpus.Open();
            if (reading.ProcessingJobs.Single(row => row.Id == jobId).State == JobState.Succeeded)
            {
                succeeded = true;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        await stopping.CancelAsync();
        await pump;

        succeeded.ShouldBeTrue(
            "PumpAsync waited before its first look, so a corpus with due work sat unsent.");
    }

    [Fact]
    public async Task A_pump_that_cannot_take_the_corpus_sends_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using (corpus.OpenMigrated())
        {
        }

        using var holding = RunnerLease.TryTake(corpus.Root);
        holding.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        using var stopping = new CancellationTokenSource();
        stopping.CancelAfter(TimeSpan.FromSeconds(1));

        await JobRunner.PumpAsync(
            corpus.Root, TimeProvider.System, send, TimeSpan.FromMilliseconds(50), stopping.Token);

        called.ShouldBeFalse();
    }

    [Fact]
    public async Task A_pump_that_takes_a_corpus_stops_what_a_dead_process_left_running_first()
    {
        using var corpus = new TemporaryCorpus();
        Guid runningJob, pendingJob;

        using (var context = corpus.OpenMigrated())
        {
            var stuck = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            runningJob = RecordedMeetings.Started(context, stuck, When);

            var queued = RecordedMeetings.Recorded(
                context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
            var job = new MeetingWork(context, When).Take(queued);
            context.SaveChanges();
            pendingJob = job.Id;
        }

        var calls = 0;
        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            Interlocked.Increment(ref calls);
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        using var stopping = new CancellationTokenSource();

        var pump = JobRunner.PumpAsync(
            corpus.Root, TimeProvider.System, send, TimeSpan.FromSeconds(30), stopping.Token);

        var settled = false;
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var reading = corpus.Open();
            if (reading.ProcessingJobs.Single(row => row.Id == pendingJob).State == JobState.Succeeded)
            {
                settled = true;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        await stopping.CancelAsync();
        await pump;

        settled.ShouldBeTrue();
        calls.ShouldBe(1);

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == runningJob).State.ShouldBe(JobState.AwaitingUser);
        reopened.ProcessingJobs.Single(row => row.Id == pendingJob).State.ShouldBe(JobState.Succeeded);
    }

    /// <summary>
    /// Goes red when <c>Start</c> is stamped with the pass's own <c>now</c> instead of a fresh
    /// read at the moment it is taken, and red when <c>Apply</c> is stamped that same way instead
    /// of a fresh read once the call has ended.
    /// </summary>
    [Fact]
    public async Task Each_job_is_stamped_with_when_its_own_call_began_and_ended()
    {
        using var corpus = new TemporaryCorpus();
        Guid job1Id, job2Id;

        using (var context = corpus.OpenMigrated())
        {
            var first = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job1 = new MeetingWork(context, When).Take(first);
            context.SaveChanges();
            job1Id = job1.Id;

            var second = RecordedMeetings.Recorded(
                context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
            var job2 = new MeetingWork(context, When + Duration.FromSeconds(1)).Take(second);
            context.SaveChanges();
            job2Id = job2.Id;
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var clock = new MovingClock(When.Value);
        var calls = 0;

        // Two different fixtures: the same response bytes filed for two different meetings is a
        // conflict the corpus itself refuses, and that refusal is not what this test is about.
        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            clock.Advance(TimeSpan.FromSeconds(60));
            var fixture = Interlocked.Increment(ref calls) == 1
                ? DeepgramFixtures.TwoChannelShort
                : DeepgramFixtures.TwoChannelLong;
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(fixture));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, clock, send, TestContext.Current.CancellationToken);

        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        var stamped1 = reopened.ProcessingJobs.Single(row => row.Id == job1Id);
        var stamped2 = reopened.ProcessingJobs.Single(row => row.Id == job2Id);

        stamped1.StartedAt.ShouldBe(When);
        stamped1.FinishedAt.ShouldBe(When + Duration.FromSeconds(60));
        stamped2.StartedAt.ShouldBe(When + Duration.FromSeconds(60));
        stamped2.FinishedAt.ShouldBe(When + Duration.FromSeconds(120));
    }

    private static SendingToTheProvider FixtureBody(string fixture) =>
        async (_, _, response, stopping) =>
        {
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(fixture));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

    /// <summary>
    /// A <see cref="TimeProvider"/> whose timer never calls back, for proving a pump looks before
    /// it waits: <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> still completes
    /// on the caller's own token even when the provider's own timer would never fire it.
    /// </summary>
    private sealed class TimersNeverFire : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new DeadTimer();

        private sealed class DeadTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A clock that only moves when a test moves it, for telling two stamps taken moments apart
    /// in the same call apart from one another.
    /// </summary>
    private sealed class MovingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
