using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Jobs;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// What a stop's own queue becomes once the runner has a look at it — the join
/// <see cref="MeetingRecordingsTests"/> and <c>JobRunnerTests</c> each stop one step short of,
/// proved together here because holding a lease and finishing a recording both need a real corpus
/// on disk.
/// </summary>
public sealed class AfterAStopTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp now = UtcTimestamp.Parse("2026-09-25T09:30:00.000Z");

    public void Dispose() => corpus.Dispose();

    [Fact]
    public async Task A_recording_stopped_with_transcription_asked_for_comes_back_transcribed_with_nothing_pressed()
    {
        var meeting = Stopped(AfterARecording.Transcribe);

        var run = await RunOnePass();

        run.Ran.ShouldHaveSingleItem();
        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Single(row => row.MeetingId == meeting).State.ShouldBe(JobState.Succeeded);
        reopened.Artifacts
            .Where(row => row.MeetingId == meeting)
            .Select(row => row.Kind)
            .ShouldContain(ArtifactKind.DeepgramResponse);
    }

    [Fact]
    public async Task A_recording_stopped_asking_for_a_summary_is_transcribed_and_nothing_more()
    {
        var meeting = Stopped(AfterARecording.TranscribeAndSummarise);

        var run = await RunOnePass();

        run.Ran.ShouldHaveSingleItem();
        run.Left.ShouldBeEmpty();

        using var reopened = corpus.Open();
        var jobs = reopened.ProcessingJobs.Where(row => row.MeetingId == meeting).ToList();
        jobs.ShouldHaveSingleItem();
        jobs[0].Kind.ShouldBe(JobKind.Transcribe);
        jobs[0].State.ShouldBe(JobState.Succeeded);
    }

    [Fact]
    public async Task A_recording_stopped_with_nothing_asked_for_is_never_sent()
    {
        Stopped(AfterARecording.DoNothing);

        var run = await RunOnePass();

        run.Ran.ShouldBeEmpty();
        run.Left.ShouldBeEmpty();
    }

    /// <summary>
    /// Records, spools two seconds and stops a meeting under <paramref name="settled"/>, and
    /// answers the meeting id.
    /// </summary>
    private Guid Stopped(AfterARecording settled)
    {
        using var context = corpus.OpenMigrated();
        new CorpusSettings(context).WhenARecordingEnds(settled, now);

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var finished = MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));
        return finished.MeetingId;
    }

    /// <summary>One pass over this test's corpus, sending with the fixture that files.</summary>
    private async Task<JobsRun> RunOnePass()
    {
        using var lease = RunnerLease.TryTake(corpus.Root);
        lease.ShouldNotBeNull();

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        return await JobRunner.RunWhatIsDueAsync(
            lease, TimeProvider.System, send, TestContext.Current.CancellationToken);
    }
}
