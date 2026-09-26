using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// <c>import-response --meeting &lt;id&gt; --as-next-version</c>: filing a response the corpus
/// already refused, from a prompt, with no charge and no call to the provider.
/// </summary>
/// <remarks>
/// Every fact drives <see cref="CommandLine.Of"/>. This door has nothing to hold back from a
/// prompt — it spends nothing and asks nobody — so there is no reason to reach for
/// <see cref="MeetingCommands.ImportResponse"/> directly the way <see cref="TranscribingAgainTests"/>
/// does for the door that talks to the console keyboard. The arrangement is what O-20260925-21
/// calls "an unfinished <c>transcription_runs</c> row of that meeting with a kept refused file": a
/// meeting already transcribed once, and a second call whose filing the corpus refused, left with
/// its job waiting on a person and its paid bytes kept at <c>deepgram.refused.&lt;run&gt;.json</c>.
/// </remarks>
public sealed class FilingARefusedResponseTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    [Fact]
    public void A_paid_response_the_corpus_refused_is_filed_as_the_next_version_and_settles_its_job()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, runId) = ARefusedSecondCall(corpus);

        var run = CommandLine.Of(
            "import-response",
            DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe),
            "--corpus", corpus.Root.FullName,
            "--meeting", meeting.ToString(),
            "--as-next-version");

        run.Code.ShouldBe(Cli.Ok, run.Error);

        using var reopened = corpus.Open();

        var second = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting
            && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)));
        second.Sha256.ShouldBe(
            CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe))));

        var transcriptionRun = reopened.TranscriptionRuns.Single(row => row.Id == runId);
        transcriptionRun.FinishedAt.ShouldNotBeNull();
        transcriptionRun.ResponseArtifactId.ShouldBe(second.Id);

        var job = reopened.ProcessingJobs.Single(row => row.Id == transcriptionRun.JobId);
        job.State.ShouldBe(JobState.Succeeded);

        var kept = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meeting, TranscribingAMeeting.RefusedResponseFileName(runId)));
        kept.Refresh();
        kept.Exists.ShouldBeTrue();
    }

    [Fact]
    public void A_file_no_unfinished_run_kept_is_refused_and_nothing_is_filed()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, _) = ARefusedSecondCall(corpus);

        var run = CommandLine.Of(
            "import-response",
            DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelSilentMe),
            "--corpus", corpus.Root.FullName,
            "--meeting", meeting.ToString(),
            "--as-next-version");

        run.Code.ShouldBe(Cli.Refused);

        using var reopened = corpus.Open();
        reopened.Artifacts
            .Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(1);
    }

    [Fact]
    public void The_same_response_filed_twice_is_refused_the_second_time()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, _) = ARefusedSecondCall(corpus);
        var file = DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe);

        var first = CommandLine.Of(
            "import-response", file,
            "--corpus", corpus.Root.FullName,
            "--meeting", meeting.ToString(),
            "--as-next-version");
        first.Code.ShouldBe(Cli.Ok, first.Error);

        var second = CommandLine.Of(
            "import-response", file,
            "--corpus", corpus.Root.FullName,
            "--meeting", meeting.ToString(),
            "--as-next-version");
        second.Code.ShouldBe(Cli.Refused);

        using var reopened = corpus.Open();
        reopened.Artifacts.Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(2);
    }

    [Fact]
    public void The_flag_without_a_meeting_is_a_line_typed_wrong()
    {
        var run = CommandLine.Of(
            "import-response",
            DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe),
            "--corpus", "wherever",
            "--started-at", "2026-03-04T14:00:00Z",
            "--profile", "multichannel",
            "--as-next-version");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--as-next-version");
    }

    /// <summary>
    /// The second "Trap" on <see cref="MeetingIntake.ReceiveWhatWasRefused"/>: an earlier attempt
    /// already filed the response as an artifact but never got to finish its own run or settle its
    /// job — reached here directly, by writing the artifact by hand, rather than by forcing a save
    /// to fail mid-way. <c>ReceiveAgainInto</c> is expected to take its <c>AlreadyHere</c> branch
    /// (no third version minted) while this door still finishes the run and settles the job.
    /// </summary>
    [Fact]
    public void A_run_whose_artifact_is_already_filed_still_finishes_and_settles_its_job()
    {
        using var corpus = new TemporaryCorpus();
        var fixtureBytes = File.ReadAllBytes(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));

        Guid meeting;
        Guid runId;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);

            DurableArtifact.Write(
                context,
                meeting,
                ArtifactKind.DeepgramResponse,
                CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)),
                When,
                into => into.Write(fixtureBytes));

            var job = AwaitingUserJob(context, meeting);
            var run = UnfinishedRun(context, meeting, job.Id);
            runId = run.Id;

            context.SaveChanges();
        }

        var kept = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meeting, TranscribingAMeeting.RefusedResponseFileName(runId)));
        File.WriteAllBytes(kept.FullName, fixtureBytes);

        var run2 = CommandLine.Of(
            "import-response",
            DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe),
            "--corpus", corpus.Root.FullName,
            "--meeting", meeting.ToString(),
            "--as-next-version");

        run2.Code.ShouldBe(Cli.Ok, run2.Error);

        using var reopened = corpus.Open();

        // No third version: ReceiveAgainInto found these bytes already filed and took its
        // AlreadyHere branch rather than minting a new one.
        reopened.Artifacts
            .Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(2);

        var artifact = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)));

        var transcriptionRun = reopened.TranscriptionRuns.Single(row => row.Id == runId);
        transcriptionRun.FinishedAt.ShouldNotBeNull();
        transcriptionRun.ResponseArtifactId.ShouldBe(artifact.Id);

        reopened.ProcessingJobs.Single(row => row.Id == transcriptionRun.JobId).State.ShouldBe(JobState.Succeeded);
    }

    /// <summary>
    /// A meeting already transcribed once, whose second call the corpus refused to file: the run is
    /// there, unfinished, its job waiting on a person, and the paid bytes are kept where
    /// <c>TranscribingAMeeting</c> would have left them.
    /// </summary>
    private static (Guid Meeting, Guid RunId) ARefusedSecondCall(TemporaryCorpus corpus)
    {
        Guid meeting;
        Guid runId;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);

            var job = AwaitingUserJob(context, meeting);
            var run = UnfinishedRun(context, meeting, job.Id);
            runId = run.Id;

            context.SaveChanges();
        }

        var kept = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meeting, TranscribingAMeeting.RefusedResponseFileName(runId)));
        File.Copy(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe), kept.FullName);

        return (meeting, runId);
    }

    /// <summary>A job queued, started and immediately stopped on a person — what a refused filing leaves.</summary>
    private static ProcessingJob AwaitingUserJob(CorpusDbContext context, Guid meeting)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid()}", When);
        context.ProcessingJobs.Add(job);
        job.Start(When);
        job.AwaitUser("What the provider sent back was paid for and is kept, where nothing files it.");
        return job;
    }

    /// <summary>An unfinished call to the provider, tracked under the given job.</summary>
    private static TranscriptionRun UnfinishedRun(CorpusDbContext context, Guid meeting, Guid jobId)
    {
        var run = new TranscriptionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = jobId,
            Provider = "deepgram",
            Model = "nova-3",
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            AudioSha256 = new string('b', 64),
            BillableConfigHash = new string('c', 64),
            CreatedAt = When,
        };
        context.TranscriptionRuns.Add(run);
        return run;
    }
}
