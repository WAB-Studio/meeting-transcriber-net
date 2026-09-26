using System.Text.Json.Nodes;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Summaries;

using static MeetingTranscriber.Processing.Tests.Summaries.JsonNodeMutations;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// The door an extraction is filed through: checking it against the meeting it claims to be about,
/// and writing what that found — accepted or refused — in one transaction.
/// </summary>
public class ExtractionIntakeTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void An_extraction_that_holds_up_is_the_meeting_s_summary()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion.", "Y este es el segundo."]);

        var received = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, Accepted(meeting)), Recorded);

        received.Accepted.ShouldBeTrue();
        received.Refusals.ShouldBeEmpty();

        context.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);

        var read = new MeetingReading(context, TimeProvider.System).Of(meeting);
        read.Screen.Stage.ShouldBe(MeetingStage.Summarised);
        read.Screen.Left.Abstract.ShouldBe("Se decidio la fecha de lanzamiento.");
        read.Screen.Left.Things.Select(thing => thing.Says).ShouldBe(["Lanzar el viernes."]);

        var citation = context.Decisions.Single(row => row.ExtractionRunId == received.RunId).Evidence;
        citation.SourceArtifactSha256.ShouldBe(prepared.TranscribedFrom);
    }

    /// <summary>ISC-90.</summary>
    [Fact]
    public void A_summary_that_fails_validation_is_stored_as_a_failed_run_and_not_as_a_summary()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var broken = Accepted(meeting);
        Remove(broken, "abstract");

        var received = ExtractionIntake.Receive(context, AttemptFor(jobId, prepared.Hash, broken), Recorded);

        received.Accepted.ShouldBeFalse();
        received.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "abstract", null)]);

        var run = context.ExtractionRuns.Single(row => row.Id == received.RunId);
        run.RawOutputHash.ShouldNotBeNull();
        run.AcceptedAt.ShouldBeNull();
        run.OutputArtifactId.ShouldBeNull();

        context.ExtractionRefusals.Count(row => row.ExtractionRunId == run.Id).ShouldBe(1);
        context.Summaries.Any(row => row.ExtractionRunId == run.Id).ShouldBeFalse();
        context.Decisions.Any(row => row.ExtractionRunId == run.Id).ShouldBeFalse();
        context.ActionItems.Any(row => row.ExtractionRunId == run.Id).ShouldBeFalse();
        context.OpenQuestions.Any(row => row.ExtractionRunId == run.Id).ShouldBeFalse();
        context.Artifacts.Any(row => row.MeetingId == meeting && row.Kind == ArtifactKind.Extraction)
            .ShouldBeFalse();

        var job = context.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.ExtractionRefused);
    }

    /// <summary>ISC-142.</summary>
    [Fact]
    public void A_refused_extraction_leaves_the_one_already_accepted_as_the_summary()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var first = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, Accepted(meeting)), Recorded);
        first.Accepted.ShouldBeTrue();

        var secondJob = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract-2", Recorded);
        secondJob.Start(Recorded);
        MeetingRows.Add(context, secondJob);

        var broken = Accepted(meeting);
        Remove(broken, "abstract");
        var second = ExtractionIntake.Receive(
            context, AttemptFor(secondJob.Id, prepared.Hash, broken), Recorded);
        second.Accepted.ShouldBeFalse();

        var read = new MeetingReading(context, TimeProvider.System).Of(meeting);
        read.Screen.Left.Abstract.ShouldBe("Se decidio la fecha de lanzamiento.");
        read.Screen.Left.Things.Select(thing => thing.Says).ShouldBe(["Lanzar el viernes."]);
    }

    [Fact]
    public void The_output_is_kept_byte_for_byte_under_the_run_that_accepted_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var output = Utf8(Accepted(meeting));
        var attempt = new ExtractionIntake.ExtractionAttempt(
            jobId, "claude-code", "1.0", "opus", "1", prepared.Hash, output);

        var received = ExtractionIntake.Receive(context, attempt, Recorded);

        var file = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meeting, $"extractions/{received.RunId}.json"));

        file.Exists.ShouldBeTrue();
        File.ReadAllBytes(file.FullName).ShouldBe(output);

        var run = context.ExtractionRuns.Single(row => row.Id == received.RunId);
        CorpusFiles.Sha256Of(file).ShouldBe(run.RawOutputHash);
    }

    [Fact]
    public void A_job_that_is_not_a_started_extraction_is_refused_before_anything_is_written()
    {
        using (var corpus = new TemporaryCorpus())
        using (var context = corpus.OpenMigrated())
        {
            var meeting = MeetingRows.Recorded(
                context, Recorded, ["Este es el primer turno de la reunion."], responseSha256: new string('a', 64));
            var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", Recorded);
            MeetingRows.Add(context, job); // Never started: still Pending.

            var prepared = MeetingInput.Prepare(context, meeting);
            var attempt = AttemptFor(job.Id, prepared.Hash, Accepted(meeting));

            // The attempt is a valid, accepted extraction, so the temporary is staged before this
            // throws — proving the `using var staged` cleanup and not only the row-side refusal.
            Should.Throw<InvalidOperationException>(() => ExtractionIntake.Receive(context, attempt, Recorded));
            context.ExtractionRuns.Any().ShouldBeFalse();
            NoUnfinishedFileIsLeftBehind(corpus, meeting);
        }

        using (var corpus = new TemporaryCorpus())
        using (var context = corpus.OpenMigrated())
        {
            var meeting = MeetingRows.Recorded(
                context, Recorded, ["Este es el primer turno de la reunion."], responseSha256: new string('a', 64));
            var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/transcribe", Recorded);
            job.Start(Recorded);
            MeetingRows.Add(context, job); // The wrong kind, and running.

            var prepared = MeetingInput.Prepare(context, meeting);
            var attempt = AttemptFor(job.Id, prepared.Hash, Accepted(meeting));

            Should.Throw<InvalidOperationException>(() => ExtractionIntake.Receive(context, attempt, Recorded));
            context.ExtractionRuns.Any().ShouldBeFalse();
            NoUnfinishedFileIsLeftBehind(corpus, meeting);
        }
    }

    [Fact]
    public void An_extraction_is_refused_in_a_transaction_of_its_own()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        using var held = context.Database.BeginTransaction();

        Should.Throw<InvalidOperationException>(() =>
                ExtractionIntake.Receive(context, AttemptFor(jobId, prepared.Hash, Accepted(meeting)), Recorded))
            .Message.ShouldContain("already holds one");

        context.ExtractionRuns.Any().ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_with_no_turns_is_refused_before_anything_is_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, []);
        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        var attempt = new ExtractionIntake.ExtractionAttempt(
            job.Id, "claude-code", "1.0", "opus", "1", new string('z', 64), Utf8(Accepted(meeting)));

        Should.Throw<InvalidOperationException>(() => ExtractionIntake.Receive(context, attempt, Recorded));
        context.ExtractionRuns.Any().ShouldBeFalse();
    }

    /// <summary>The card's own "retrying a summary never calls the transcription service again".</summary>
    [Fact]
    public void Nothing_that_files_a_summary_reaches_the_transcription_provider()
    {
        var folder = new DirectoryInfo(
            Path.Combine(RepositoryTree.Src.FullName, "MeetingTranscriber.Processing", "Summaries"));

        var code = string.Join(
            '\n', RepositoryTree.SourceUnder(folder).Select(SourceText.WithoutProse));

        foreach (var forbidden in new[] { "Deepgram", "SendingToTheProvider", "TranscribingAMeeting", "JobRunner" })
        {
            code.ShouldNotContain(forbidden);
        }
    }

    /// <summary>
    /// Nothing under this meeting's folder is a write that never finished — proving
    /// <c>StagedArtifact</c>'s temporary was let go rather than only that no row landed.
    /// </summary>
    private static void NoUnfinishedFileIsLeftBehind(TemporaryCorpus corpus, Guid meeting)
    {
        var folder = CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, MeetingManifest.FileName)).Directory!;
        if (folder.Exists)
        {
            folder.EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}", SearchOption.AllDirectories).ShouldBeEmpty();
        }
    }

    private static (Guid Meeting, Guid JobId, MeetingInput Prepared) ArrangeStartedExtraction(
        CorpusDbContext context, IReadOnlyList<string> said)
    {
        var meeting = MeetingRows.Recorded(context, Recorded, said, responseSha256: new string('a', 64));

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        return (meeting, job.Id, MeetingInput.Prepare(context, meeting));
    }

    private static ExtractionIntake.ExtractionAttempt AttemptFor(Guid jobId, string inputHash, JsonNode document) =>
        new(jobId, "claude-code", "1.0", "opus", "1", inputHash, Utf8(document));

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
}
