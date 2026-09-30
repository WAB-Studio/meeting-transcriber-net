using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Jobs;
using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Processing.Tests.Summaries;

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
            "Deepgram would not accept this machine's key.", JobFailure.KeyRefused);

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);

        run.Left.Count.ShouldBe(1);

        using var reopened = corpus.Open();
        var job2 = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job2.State.ShouldBe(JobState.FailedPermanent);
        job2.LastError.ShouldNotBeNull();
        job2.Failure.ShouldBe(JobFailure.KeyRefused);
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
            "Deepgram failed the request on its own side.", whyNothingWasCharged: null);

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

    /// <summary>Goes red when <c>SendAgainAsync</c> runs a pass.</summary>
    [Fact]
    public async Task Sending_again_sends_the_job_it_was_handed_and_no_other_due_one()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingA, jobA, meetingB, jobB;

        using (var context = corpus.OpenMigrated())
        {
            meetingA = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            jobA = new MeetingWork(context, When).Take(meetingA).Id;
            context.SaveChanges();

            meetingB = RecordedMeetings.Recorded(
                context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
            MeetingIntake.ReceiveInto(
                context, meetingB, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
            jobB = QueueDirectly(context, meetingB);
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var run = await JobRunner.SendAgainAsync(
            lease, jobB, When + Duration.FromSeconds(2), TimeProvider.System,
            FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe), TestContext.Current.CancellationToken);

        run.Ran.ShouldBe([jobB]);

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobA).State.ShouldBe(JobState.Pending);
        reopened.ProcessingJobs.Single(row => row.Id == jobB).State.ShouldBe(JobState.Succeeded);
        reopened.Artifacts.Any(row =>
                row.MeetingId == meetingB
                && row.RelativePath == CorpusFiles.PathFor(meetingB, ResponseVersions.Named(2)))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_job_sent_again_that_may_have_been_charged_stops_on_a_person()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, job;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
            job = QueueDirectly(context, meeting);
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        SendingToTheProvider send = (_, _, _, _) =>
            throw new InvalidOperationException("the socket vanished");

        var run = await JobRunner.SendAgainAsync(
            lease, job, When + Duration.FromSeconds(1), TimeProvider.System, send,
            TestContext.Current.CancellationToken);

        run.Ran.ShouldBe([job]);

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == job).State.ShouldBe(JobState.AwaitingUser);
    }

    [Fact]
    public async Task Sending_again_a_job_that_is_not_waiting_to_be_sent_sends_nothing()
    {
        using var corpus = new TemporaryCorpus();
        Guid job;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            job = QueueDirectly(context, meeting);
            context.ProcessingJobs.Single(row => row.Id == job).Start(When);
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

        await Should.ThrowAsync<InvalidOperationException>(() => JobRunner.SendAgainAsync(
            lease, job, When, TimeProvider.System, send, TestContext.Current.CancellationToken));

        called.ShouldBeFalse();
    }

    /// <summary>Goes red with <c>Filed</c> not special-cased: the door already moved the job.</summary>
    [Fact]
    public async Task A_queued_summary_comes_back_summarised_with_nothing_pressed()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = ArrangeSummarisable(corpus, When);
        var provider = new FakeSummaries().Answering(new SummaryProviderAnswer.Extracted(
            Utf8(Accepted(meeting)), "1.0", "opus", null));

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        run.Ran.ShouldBe([jobId]);
        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);
    }

    /// <summary>Goes red with the two kinds ordered by <c>CreatedAt</c> alone.</summary>
    [Fact]
    public async Task A_pass_sends_the_transcriptions_before_the_summaries()
    {
        using var corpus = new TemporaryCorpus();
        Guid transcribeJob;
        Guid extractJob;

        using (var context = corpus.OpenMigrated())
        {
            // The summary's own job is queued first, so an order taken from CreatedAt alone would
            // run it ahead of the transcription.
            var (_, extract) = ArrangeSummarisable(corpus, When);
            extractJob = extract;

            var transcribing = RecordedMeetings.Recorded(
                context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
            transcribeJob = new MeetingWork(context, When + Duration.FromSeconds(1)).Take(transcribing).Id;
            context.SaveChanges();
        }

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var provider = new FakeSummaries().Answering(new SummaryProviderAnswer.DidNotAnswer("busy"));

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        // The pass is one worker taking one job at a time (the class's own remarks), so `Ran`
        // already reports call order — and the summary was asked exactly once, not ahead of the
        // transcription and not skipped.
        run.Ran.ShouldBe([transcribeJob, extractJob]);
        provider.Requests.Count.ShouldBe(1);
    }

    /// <summary>Goes red with the retry bound misread — a fourth attempt, or none at all.</summary>
    [Fact]
    public async Task A_summariser_that_does_not_answer_is_tried_again_later_and_then_not_at_all()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = ArrangeSummarisable(corpus, When);

        var clock = new MovingClock(When.Value);
        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var provider = new FakeSummaries().Answering(
            new SummaryProviderAnswer.DidNotAnswer("first"),
            new SummaryProviderAnswer.DidNotAnswer("second"),
            new SummaryProviderAnswer.DidNotAnswer("third"));

        await JobRunner.RunWhatIsDueAsync(
            lease, clock, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        using (var reopened = corpus.Open())
        {
            var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
            job.State.ShouldBe(JobState.FailedRetryable);
            job.NextAttemptAt.ShouldBe(When + JobRunner.WaitBeforeTryingASummaryAgain(1));
        }

        clock.Advance(JobRunner.WaitBeforeTryingASummaryAgain(1).ToTimeSpan());
        await JobRunner.RunWhatIsDueAsync(
            lease, clock, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        UtcTimestamp secondAttemptAt;
        using (var reopened = corpus.Open())
        {
            var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
            job.State.ShouldBe(JobState.FailedRetryable);
            secondAttemptAt = job.NextAttemptAt!.Value;
        }

        clock.Advance((secondAttemptAt - UtcTimestamp.From(clock.GetUtcNow())).ToTimeSpan());
        await JobRunner.RunWhatIsDueAsync(
            lease, clock, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        using (var reopened = corpus.Open())
        {
            var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
            job.State.ShouldBe(JobState.FailedPermanent);
            job.Failure.ShouldBe(JobFailure.SummariserFailed);
        }
    }

    /// <summary>Goes red with a summariser that is not there retried instead of failed at once.</summary>
    [Fact]
    public async Task Without_a_summariser_on_this_machine_the_summary_fails_for_good_at_once()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = ArrangeSummarisable(corpus, When);

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var provider = new FakeSummaries().Answering(
            new SummaryProviderAnswer.NotAvailable("Claude Code was not found on this machine."));

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        run.Left.Count.ShouldBe(1);

        using var reopened = corpus.Open();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.NoSummariserOnThisMachine);
    }

    /// <summary>Goes red with an Extract job taken even though no summariser was handed over.</summary>
    [Fact]
    public async Task A_pass_given_no_summariser_leaves_the_summaries_queued()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = ArrangeSummarisable(corpus, When);

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var run = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken);

        run.Ran.ShouldBeEmpty();

        using (var reopened = corpus.Open())
        {
            reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Pending);
        }

        // Given a summariser on the next pass, the very same job it left alone is the one it takes —
        // "queued", not quietly dropped.
        var provider = new FakeSummaries().Answering(new SummaryProviderAnswer.Extracted(
            Utf8(Accepted(meeting)), "1.0", "opus", null));

        var later = await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        later.Ran.ShouldBe([jobId]);
    }

    /// <summary>Goes red with <c>Take</c> not told the kind — an Extract job sent as a transcription.</summary>
    [Fact]
    public async Task Sending_again_sends_only_a_transcription()
    {
        using var corpus = new TemporaryCorpus();
        var (_, extractJob) = ArrangeSummarisable(corpus, When);

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        await Should.ThrowAsync<InvalidOperationException>(() => JobRunner.SendAgainAsync(
            lease, extractJob, When, TimeProvider.System, send, TestContext.Current.CancellationToken));

        called.ShouldBeFalse();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == extractJob).State.ShouldBe(JobState.Pending);
    }

    /// <summary>
    /// Goes red with the watcher's token not handed on to the provider: a pass stopped from the
    /// screen would then either hang on a provider that never answers, or write a second move over
    /// the one <c>MeetingWork.StopTheSummary</c> already made.
    /// </summary>
    [Fact]
    public async Task A_summary_somebody_stopped_is_let_go_within_a_look()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = ArrangeSummarisable(corpus, When);

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        var provider = new FakeSummaries().ThatNeverAnswers();

        var pass = JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TestContext.Current.CancellationToken, provider);

        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var reading = corpus.Open();
            if (reading.ProcessingJobs.Single(row => row.Id == jobId).State == JobState.Running)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        using (var stopping = corpus.OpenMigrated())
        {
            new MeetingWork(stopping, When).StopTheSummary(meeting);
            stopping.SaveChanges();
        }

        var run = await pass.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Cancelled);
    }

    /// <summary>
    /// The pump's own cancellation, mid-summary, still rethrows — the same fact
    /// <see cref="A_send_the_application_walked_out_of_leaves_the_job_where_a_restart_will_find_it"/>
    /// proves for a transcription. Goes red with the linked-token catch clauses in
    /// <c>RunSummaryAsync</c> reordered, or with a pump shutdown read as a screen stop and so
    /// swallowed instead of let out.
    /// </summary>
    [Fact]
    public async Task A_summary_the_application_walked_out_of_leaves_the_job_where_a_restart_will_find_it()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = ArrangeSummarisable(corpus, When);

        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        using var walkingOut = new CancellationTokenSource();
        var provider = new LambdaProvider((_, stopping) =>
        {
            walkingOut.Cancel();
            stopping.ThrowIfCancellationRequested();
            return Task.FromResult<SummaryProviderAnswer>(new SummaryProviderAnswer.DidNotAnswer("unreachable"));
        });

        await Should.ThrowAsync<OperationCanceledException>(() => JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            walkingOut.Token, provider));

        using var reopened = corpus.Open();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.Running);
        job.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// A transcription queued while a summary is in flight is sent before that summary answers.
    /// Goes red with the summary lane awaited inline by the look, which is one serial pump again.
    /// </summary>
    [Fact]
    public async Task A_transcription_queued_while_a_summary_runs_is_sent_before_the_summary_answers()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, summaryJob) = ArrangeSummarisable(corpus, When);
        var (provider, release) = ASummaryThatWaits(meeting);

        using var stopping = new CancellationTokenSource();
        var pump = JobRunner.PumpAsync(
            corpus.Root, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TimeSpan.FromMilliseconds(50), stopping.Token, provider);

        try
        {
            (await StateBecomesAsync(corpus, summaryJob, JobState.Running)).ShouldBeTrue();

            Guid transcription;
            using (var context = corpus.OpenMigrated())
            {
                var second = RecordedMeetings.Recorded(
                    context, SourceProfile.Multichannel, When + Duration.FromSeconds(1));
                transcription = new MeetingWork(context, When).Take(second).Id;
                context.SaveChanges();
            }

            (await StateBecomesAsync(corpus, transcription, JobState.Succeeded)).ShouldBeTrue(
                "A transcription sat behind a summary that had not answered.");

            using (var reading = corpus.Open())
            {
                reading.ProcessingJobs.Single(row => row.Id == summaryJob).State.ShouldBe(JobState.Running);
            }

            release.SetResult();
            (await StateBecomesAsync(corpus, summaryJob, JobState.Succeeded)).ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await stopping.CancelAsync();
            await pump;
        }
    }

    /// <summary>
    /// The restart sweep each look makes leaves alone the one summary this pump is running.
    /// Goes red with the pump naming no job in flight, which stops it on a person a look later.
    /// </summary>
    [Fact]
    public async Task A_look_while_a_summary_runs_does_not_stop_it()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, summaryJob) = ArrangeSummarisable(corpus, When);
        var (provider, release) = ASummaryThatWaits(meeting);

        using var stopping = new CancellationTokenSource();
        var pump = JobRunner.PumpAsync(
            corpus.Root, TimeProvider.System, FixtureBody(DeepgramFixtures.TwoChannelShort),
            TimeSpan.FromMilliseconds(50), stopping.Token, provider);

        try
        {
            (await StateBecomesAsync(corpus, summaryJob, JobState.Running)).ShouldBeTrue();

            var looked = Stopwatch.StartNew();
            while (looked.Elapsed < TimeSpan.FromSeconds(1))
            {
                using var reading = corpus.Open();
                reading.ProcessingJobs.Single(row => row.Id == summaryJob).State.ShouldBe(JobState.Running);

                await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
            }

            release.SetResult();
            (await StateBecomesAsync(corpus, summaryJob, JobState.Succeeded)).ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await stopping.CancelAsync();
            await pump;
        }
    }

    /// <summary>A provider that answers the meeting's accepted summary only once the test lets it.</summary>
    private static (LambdaProvider Provider, TaskCompletionSource Release) ASummaryThatWaits(Guid meeting)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new LambdaProvider(async (_, stopping) =>
        {
            await release.Task.WaitAsync(stopping);
            return new SummaryProviderAnswer.Extracted(Utf8(Accepted(meeting)), "1.0", "opus", "session-1");
        });

        return (provider, release);
    }

    private static async Task<bool> StateBecomesAsync(TemporaryCorpus corpus, Guid jobId, JobState state)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var reading = corpus.Open();
            if (reading.ProcessingJobs.Single(row => row.Id == jobId).State == state)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        return false;
    }

    /// <summary>A meeting with turns, and a queued <see cref="JobKind.Extract"/> job over it.</summary>
    private static (Guid Meeting, Guid JobId) ArrangeSummarisable(TemporaryCorpus corpus, UtcTimestamp createdAt)
    {
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, createdAt,
            ["Este es el primer turno de la reunion.", "Y este es el segundo."],
            responseSha256: new string('a', 64));

        // MeetingRows.Recorded's own transcription job is left Pending — fine for the door tests
        // it was built for, which never run a pass, but a real RunWhatIsDueAsync would pick it up
        // beside the Extract job this test is actually about. Settled here so the meeting reads
        // exactly like one a pass already finished transcribing.
        var transcribed = context.ProcessingJobs.Single(
            row => row.MeetingId == meeting && row.Kind == JobKind.Transcribe);
        transcribed.Start(createdAt);
        transcribed.Succeed(createdAt);
        context.SaveChanges();

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", createdAt);
        MeetingRows.Add(context, job);

        return (meeting, job.Id);
    }

    private static JsonNode Accepted(Guid meetingId) => JsonNode.Parse($$"""
        {
          "schema_version": "1",
          "meeting_id": "{{meetingId}}",
          "abstract": "Se decidio la fecha de lanzamiento.",
          "summary": "",
          "participants": ["{{MeetingRows.SpeakerLabel}}"],
          "decisions": [
            {
              "statement": "Lanzar el viernes.",
              "evidence": {
                "utterance_ordinal": 0,
                "start_ms": 1000,
                "end_ms": 1500,
                "speaker_label": "{{MeetingRows.SpeakerLabel}}",
                "quoted_text": "primer turno"
              }
            }
          ],
          "actions": [],
          "open_questions": []
        }
        """)!;

    private static byte[] Utf8(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    /// <summary>
    /// Wraps a provider and runs <paramref name="onExtract"/> beside every call to
    /// <see cref="ISummaryProvider.ExtractAsync"/>, so a test can record when the summary side of a
    /// pass actually ran without needing its own fake.
    /// </summary>
    /// <summary>
    /// A provider whose one call is whatever the test hands it, for the one fact
    /// <see cref="FakeSummaries"/> has no shape for: reacting to the token it is given rather than
    /// answering from a fixed queue.
    /// </summary>
    private sealed class LambdaProvider(
        Func<ExtractionRequest, CancellationToken, Task<SummaryProviderAnswer>> extract) : ISummaryProvider
    {
        public string Name => "lambda";

        public Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping) =>
            Task.FromResult(new SummaryAvailability(Availability.Answers, "lambda 1", null));

        public Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping) =>
            extract(request, stopping);
    }

    /// <summary>A job queued directly, left <see cref="JobState.Pending"/> for the caller to start.</summary>
    private static Guid QueueDirectly(CorpusDbContext context, Guid meeting)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid():n}", When);
        context.ProcessingJobs.Add(job);
        context.SaveChanges();
        return job.Id;
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
