using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Processing.Jobs;

namespace MeetingTranscriber.Processing.Tests.Jobs;

/// <summary>
/// Settling what a restart found: a job left <see cref="JobState.Running"/> stops on a person,
/// under the corpus's own lease and under nothing else.
/// </summary>
public sealed class JobsARestartFoundTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    /// <summary>
    /// Goes red with the sweep narrowed to <see cref="JobState.Pending"/> instead of
    /// <see cref="JobState.Running"/>.
    /// </summary>
    [Fact]
    public void A_job_a_restart_found_running_stops_on_a_person()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            jobId = RecordedMeetings.Started(context, meeting, When);
        }

        using (var lease = RunnerLease.TryTake(corpus.Root))
        {
            lease.ShouldNotBeNull();
            var settled = JobsARestartFound.Holding(lease);

            settled.Stopped.ShouldBe([jobId]);
            settled.Left.ShouldBeEmpty();
        }

        using var reopened = corpus.Open();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.AwaitingUser);
        job.AwaitingReason.ShouldNotBeNull();
    }

    [Fact]
    public void A_job_that_was_not_running_is_left_exactly_as_it_was()
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

        using (var lease = RunnerLease.TryTake(corpus.Root))
        {
            lease.ShouldNotBeNull();
            var settled = JobsARestartFound.Holding(lease);

            settled.Stopped.ShouldBeEmpty();
            settled.Left.ShouldBeEmpty();
        }

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Pending);
    }

    [Fact]
    public void A_restart_that_found_nothing_running_says_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using (corpus.OpenMigrated())
        {
        }

        var settled = JobsARestartFound.In(corpus.Root);

        settled.Stopped.ShouldBeEmpty();
        settled.Left.ShouldBeEmpty();
    }

    /// <summary>
    /// The lease is what tells a job a live process is sending apart from one a dead process left.
    /// Goes red with <see cref="JobsARestartFound.In"/> ignoring it.
    /// </summary>
    [Fact]
    public void A_corpus_whose_queue_another_instance_is_running_is_not_stopped_under_it()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            jobId = RecordedMeetings.Started(context, meeting, When);
        }

        using var holding = RunnerLease.TryTake(corpus.Root);
        holding.ShouldNotBeNull();

        var settled = JobsARestartFound.In(corpus.Root);

        settled.Stopped.ShouldBeEmpty();
        settled.Left.Count.ShouldBe(1);
        settled.Left[0].ShouldContain("held by another process");

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Running);
    }

    [Fact]
    public void A_lease_has_one_holder_at_a_time_and_goes_with_it()
    {
        using var corpus = new TemporaryCorpus();

        var first = RunnerLease.TryTake(corpus.Root);
        first.ShouldNotBeNull();

        RunnerLease.TryTake(corpus.Root).ShouldBeNull();

        first.Dispose();

        using var second = RunnerLease.TryTake(corpus.Root);
        second.ShouldNotBeNull();
    }
}
