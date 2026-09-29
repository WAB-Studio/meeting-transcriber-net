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

        context.ExtractionRefusals
            .Where(row => row.ExtractionRunId == run.Id)
            .Select(row => new ExtractionRefusal(row.Condition, row.Path, row.Statement))
            .ToList()
            .ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "abstract", null)]);
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

    /// <summary>ISC-143.</summary>
    /// <remarks>
    /// A fact over the door's own write and not over a row a test helper built by hand — a door
    /// that stored the wrong <c>Condition</c> or a <c>Statement</c> of <c>null</c> would leave
    /// every other fact in this file and in <c>MeetingReadingTests</c> green, because none of them
    /// reads a refusal <see cref="ExtractionIntake.Receive"/> itself wrote back through the screen.
    /// </remarks>
    [Fact]
    public void A_refusal_the_door_stores_is_what_the_meeting_says()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var broken = Accepted(meeting);
        Remove(broken, "decisions[0].evidence");

        var received = ExtractionIntake.Receive(context, AttemptFor(jobId, prepared.Hash, broken), Recorded);
        received.Accepted.ShouldBeFalse();

        var screen = new MeetingReading(context, TimeProvider.System).Of(meeting).Screen;

        screen.WhyTheSummaryWasRefused.ShouldBe(
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."));
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

            // The job is re-read, tracked, before Prepare or the stage ever run (Decides 10), so an
            // invalid job refuses here with nothing prepared and nothing staged at all — not only
            // a row-side refusal.
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

    /// <summary>O-20260926-03.</summary>
    [Fact]
    public async Task A_meeting_rendered_again_while_the_door_waits_for_the_lock_is_checked_as_it_is_now()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var attempt = AttemptFor(jobId, prepared.Hash, Accepted(meeting));

        // A second connection holds the write lock over a change to this meeting's turns — the
        // shape a render takes — for 500 ms, well inside BusyTimeoutMilliseconds. The door's own
        // BeginTransaction blocks on it, so Prepare below only ever runs once that lock is free.
        using var holder = corpus.OpenMigrated();
        using var held = holder.Database.BeginTransaction();
        var turn = holder.Utterances.Single(row => row.MeetingId == meeting && row.Ordinal == 0);
        turn.Text += " Renderizado de nuevo mientras la puerta esperaba.";
        holder.SaveChanges();

        var receiving = Task.Run(() => ExtractionIntake.Receive(context, attempt, Recorded));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        held.Commit();

        var received = await receiving;

        received.Accepted.ShouldBeFalse();
        received.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.InputNotAsPrepared, "$", null)]);
    }

    /// <summary>Decides 12.</summary>
    [Fact]
    public void A_first_refusal_that_can_be_corrected_is_kept_and_leaves_the_job_running()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var broken = Accepted(meeting);
        Remove(broken, "abstract");

        var received = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, broken), Recorded, mayBeHandedBack: true);

        received.Accepted.ShouldBeFalse();
        received.HandedBack.ShouldBeTrue();
        received.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "abstract", null)]);

        var job = context.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.Running);
        job.Failure.ShouldBeNull();

        context.ExtractionRefusals.Count(row => row.ExtractionRunId == received.RunId).ShouldBe(1);
    }

    /// <summary>Decides 9 and Decides 12.</summary>
    [Fact]
    public void A_correction_is_filed_only_against_the_refused_run_it_corrects()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var broken = Accepted(meeting);
        Remove(broken, "abstract");
        var first = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, broken), Recorded, mayBeHandedBack: true);
        first.HandedBack.ShouldBeTrue();

        // A second job's run, also refused and handed-back-eligible, is not this job's to
        // correct: the precondition asks the job named by the attempt, never the run alone.
        var otherJob = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract-2", Recorded);
        otherJob.Start(Recorded);
        MeetingRows.Add(context, otherJob);
        var otherFirst = ExtractionIntake.Receive(
            context, AttemptFor(otherJob.Id, prepared.Hash, broken), Recorded, mayBeHandedBack: true);
        otherFirst.HandedBack.ShouldBeTrue();

        var crossJobAttempt = new ExtractionIntake.ExtractionAttempt(
            jobId, "claude-code", "1.0", "opus", "1", prepared.Hash, Utf8(Accepted(meeting)), Corrects: otherFirst.RunId);
        Should.Throw<InvalidOperationException>(() => ExtractionIntake.Receive(context, crossJobAttempt, Recorded));
        context.ExtractionRuns.Count(row => row.JobId == jobId).ShouldBe(1);

        // Filed against the run it names: refused again here, and the row still carries
        // CorrectsRunId — the corpus records which run a correction is without ordering by time.
        var correctionAttempt = new ExtractionIntake.ExtractionAttempt(
            jobId, "claude-code", "1.0", "opus", "1", prepared.Hash, Utf8(broken), Corrects: first.RunId);
        var second = ExtractionIntake.Receive(context, correctionAttempt, Recorded);

        second.Accepted.ShouldBeFalse();
        context.ExtractionRuns.Single(row => row.Id == second.RunId).CorrectsRunId.ShouldBe(first.RunId);
        context.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.FailedPermanent);

        // jobId now carries two runs and is terminal: a further correction against it throws
        // before anything is written, rather than filing a third run under a job that is done.
        var thirdAttempt = new ExtractionIntake.ExtractionAttempt(
            jobId, "claude-code", "1.0", "opus", "1", prepared.Hash, Utf8(Accepted(meeting)), Corrects: second.RunId);
        Should.Throw<InvalidOperationException>(() => ExtractionIntake.Receive(context, thirdAttempt, Recorded));
        context.ExtractionRuns.Count(row => row.JobId == jobId).ShouldBe(2);
    }

    /// <summary>
    /// An adversarial review flagged the earlier reading of the precondition — "the job's only
    /// run" — as breaking a job the runner retries after a hand-back whose own correction round
    /// never answers: nothing is filed for it, so the run sits on the job uncorrected, and the
    /// next attempt's own hand-back is still this job's one to spend.
    /// </summary>
    [Fact]
    public void A_correction_is_filed_even_when_an_earlier_uncorrected_attempt_is_still_on_the_job()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var (meeting, jobId, prepared) = ArrangeStartedExtraction(
            context, ["Este es el primer turno de la reunion."]);

        var broken = Accepted(meeting);
        Remove(broken, "abstract");

        // Stands in for the run an earlier, retried attempt left behind: handed back, and never
        // corrected, because that attempt's correction round itself came back DidNotAnswer.
        var earlier = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, broken), Recorded, mayBeHandedBack: true);
        earlier.HandedBack.ShouldBeTrue();

        var current = ExtractionIntake.Receive(
            context, AttemptFor(jobId, prepared.Hash, broken), Recorded, mayBeHandedBack: true);
        current.HandedBack.ShouldBeTrue();

        var correctionAttempt = new ExtractionIntake.ExtractionAttempt(
            jobId, "claude-code", "1.0", "opus", "1", prepared.Hash, Utf8(Accepted(meeting)), Corrects: current.RunId);
        var corrected = ExtractionIntake.Receive(context, correctionAttempt, Recorded);

        corrected.Accepted.ShouldBeTrue();
        context.ExtractionRuns.Single(row => row.Id == corrected.RunId).CorrectsRunId.ShouldBe(current.RunId);
        context.ExtractionRuns.Count(row => row.JobId == jobId).ShouldBe(3);
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
