using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// The door an extraction is filed through: checked against the meeting it claims to be about,
/// then written whole — accepted or refused — in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>An extraction that passes the check is accepted by this door itself, at the instant the
/// check passes, and no person accepts it.</b> arquitectura.md §7.3 says an extraction is accepted
/// only if the conditions hold, and nothing on any screen is a press that does the accepting.
/// </para>
/// <para>
/// <b>A refusal fails the job for good, with <see cref="JobFailure.ExtractionRefused"/>, and the
/// stage stays offered — with one exception.</b> A first attempt whose every refusal
/// <see cref="ExtractionCorrection.MayBeHandedBack"/> accepts is handed back instead: the job stays
/// <c>Running</c>, <see cref="ExtractionReceived.HandedBack"/> says so, and <c>SummarisingAMeeting</c>
/// is what asks the one correction this earns. Everywhere else, <c>OwedWork.Of</c> already reads a
/// stage back as owed once its newest job of that kind failed permanently, which is what makes
/// <em>Resumir</em> a new job and never a retry this door — or anything else — runs on its own.
/// </para>
/// <para>
/// <b>The run is written whole, with its outcome, in one transaction.</b> Unlike a transcription's
/// own run, nothing is written before this call: <c>SummarisingAMeeting</c> holds no row open
/// across a provider call, so the first byte this door ever writes about an attempt is the whole of
/// it — accepted, refused, or handed back for one correction.
/// </para>
/// <para>
/// <b>An accepted output is kept byte for byte at <c>meetings/&lt;id&gt;/extractions/&lt;run id&gt;.json</c>,
/// as <see cref="ArtifactKind.Extraction"/>, and a refused one leaves no file at all.</b> The
/// summary, decisions, actions and open questions are projected once, at acceptance, and never
/// again.
/// </para>
/// <para>
/// This is the door <c>ClaudeCodeSummaries</c> — and any other <see cref="ISummaryProvider"/> —
/// hands an unwrapped extraction to, through <c>SummarisingAMeeting</c>. Nothing here reaches a
/// transcription provider — a source guard over every file in this folder is what proves that,
/// rather than a review having to notice a stray reference.
/// </para>
/// </remarks>
public static class ExtractionIntake
{
    /// <summary>One attempt at summarising a meeting, exactly as it is handed to this door.</summary>
    /// <param name="SessionId">The conversation the provider opened for this attempt, when it reported one.</param>
    /// <param name="Corrects">
    /// The refused run this attempt corrects, or nothing on a first attempt. When set, the run must
    /// belong to this job and carry no correction of its own yet — anything else throws before
    /// anything is written.
    /// </param>
    public sealed record ExtractionAttempt(
        Guid JobId,
        string Provider,
        string? ProviderVersion,
        string? Model,
        string PromptVersion,
        string InputHash,
        byte[] Output,
        string? SessionId = null,
        Guid? Corrects = null);

    /// <summary>What filing one attempt came to.</summary>
    /// <param name="HandedBack">
    /// True when this run was refused, the caller's own <c>mayBeHandedBack</c> was true, every
    /// refusal is one <see cref="ExtractionCorrection.MayBeHandedBack"/> accepts, and the job was
    /// left <c>Running</c> for one more attempt rather than failed. Never true for a correction's
    /// own run, which this door never hands back a second time.
    /// </param>
    public sealed record ExtractionReceived(
        Guid RunId, bool Accepted, bool HandedBack, IReadOnlyList<ExtractionRefusal> Refusals);

    /// <summary>
    /// Checks one attempt against the meeting it claims to be about, and writes what it found.
    /// </summary>
    /// <param name="mayBeHandedBack">
    /// Whether a refusal this door could correct should be handed back rather than failed. True for
    /// a first attempt and always false for a correction — <paramref name="attempt"/>'s own
    /// <see cref="ExtractionAttempt.Corrects"/> is what tells the two apart, so this door never
    /// hands one back twice whatever this is called with.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/> already holds a transaction; <paramref name="attempt"/> does not
    /// name a <see cref="JobKind.Extract"/> job that has been started; or
    /// <see cref="ExtractionAttempt.Corrects"/> is set and does not name a run of this job that is
    /// not already corrected. Nothing is written.
    /// </exception>
    public static ExtractionReceived Receive(
        CorpusDbContext context, ExtractionAttempt attempt, UtcTimestamp now, bool mayBeHandedBack = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attempt);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "An extraction is filed in a transaction of its own, and this context already "
                + "holds one. Nothing was filed.");
        }

        using var transaction = context.Database.BeginTransaction();

        // Re-read, tracked, inside the transaction: the write lock this opens with is what makes
        // this check, everything Prepare and ExtractionCheck read below, and the move that follows
        // one thing. Nothing has been added to the context yet when this refuses — once the write
        // lock is held, the same way MeetingWork's writers re-read inside their transaction.
        var job = context.ProcessingJobs.FirstOrDefault(row => row.Id == attempt.JobId);
        if (job is null || job.Kind != JobKind.Extract || job.State != JobState.Running)
        {
            throw NotAStartedExtraction(attempt.JobId);
        }

        var meetingId = job.MeetingId;

        var handedBackRefusals = attempt.Corrects is { } correctsRunId
            ? RefusalsBeingCorrected(context, job.Id, correctsRunId)
            : [];

        // Prepared again, inside the transaction, under the write lock BeginTransaction above
        // already took: a render landing between this attempt being sent and this door opening
        // would leave attempt.InputHash stale against what the meeting says now, and
        // ExtractionCheck.Of's own InputNotAsPrepared refusal below is what catches that — loudly,
        // rather than filing citations against turns that have already moved (O-20260926-03).
        var prepared = MeetingInput.Prepare(context, meetingId);
        var verdict = ExtractionCheck.Of(attempt.Output, attempt.InputHash, prepared);

        // A correction's document is judged a second time, against what the first attempt was
        // asked to fix: a document ExtractionCheck.Of itself found nothing wrong with can still
        // bring back a statement it was told to remove, citing something else (Decides 12).
        var refusals = attempt.Corrects is null
            ? verdict.Refusals
            : ExtractionCorrection.Judge(handedBackRefusals, verdict.Accepted, verdict.Refusals);
        var accepted = refusals.Count == 0 ? verdict.Accepted : null;

        var runId = Guid.NewGuid();

        // Staged inside the transaction, once the verdict is known: the write lock is already held
        // by BeginTransaction above, so nothing else can land between this stage and the commit
        // below. The `using` is load-bearing — Dispose lets go of the `.partial` and deletes it, so
        // a throw between here and the commit leaves nothing behind.
        using var staged = accepted is not null
            ? StagedArtifact.Stage(
                context,
                meetingId,
                ArtifactKind.Extraction,
                CorpusFiles.PathFor(meetingId, $"extractions/{runId}.json"),
                stream => stream.Write(attempt.Output))
            : null;

        var run = new ExtractionRun
        {
            Id = runId,
            MeetingId = meetingId,
            JobId = job.Id,
            Provider = attempt.Provider,
            ProviderVersion = attempt.ProviderVersion,
            Model = attempt.Model,
            PromptVersion = attempt.PromptVersion,
            SchemaVersion = ExtractionReader.SchemaVersion,
            InputHash = attempt.InputHash,
            RawOutputHash = CorpusFiles.Sha256Of(new MemoryStream(attempt.Output)),
            SessionId = attempt.SessionId,
            CorrectsRunId = attempt.Corrects,
            CreatedAt = now,
        };

        context.ExtractionRuns.Add(run);

        var handedBack = false;

        if (accepted is not null)
        {
            context.Summaries.Add(new Summary
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                ExtractionRunId = run.Id,
                Abstract = accepted.Abstract,
                Body = accepted.Summary,
                CreatedAt = now,
            });

            AddDecisions(context, meetingId, run.Id, accepted, prepared, now);
            AddActions(context, meetingId, run.Id, accepted, prepared, now);
            AddQuestions(context, meetingId, run.Id, accepted, prepared, now);

            job.Succeed(now);

            // Moves the file into place and saves everything added so far — the run, the claims
            // and the job move above all land in this one call, inside this transaction.
            var filed = staged!.Commit(now);

            run.OutputArtifactId = filed.Id;
            run.AcceptedAt = now;
        }
        else
        {
            for (var ordinal = 0; ordinal < refusals.Count; ordinal++)
            {
                var refusal = refusals[ordinal];
                context.ExtractionRefusals.Add(new ExtractionRunRefusal
                {
                    ExtractionRunId = run.Id,
                    Ordinal = ordinal,
                    Condition = refusal.Condition,
                    Path = refusal.Path,
                    Statement = refusal.Statement,
                });
            }

            var message = RefusedMessage(refusals);
            run.LastError = message;

            // Handed back only on a first attempt (attempt.Corrects is null) of a job that has
            // never been corrected before. Filing a correction always ends the job — accepted or
            // refused for good, either moves it out of Running — so today nothing reaches this
            // door with an existing correction to find here; the check is the job's one lifetime
            // correction (Runs.cs's own doc on CorrectsRunId, held structurally by
            // ux_extraction_runs_one_correction_per_job) stated where a caller could otherwise
            // depend on the door alone, in case a later job state ever lets a corrected job run
            // again.
            var alreadyCorrectedOnce = context.ExtractionRuns
                .Any(row => row.JobId == job.Id && row.CorrectsRunId != null);

            if (mayBeHandedBack && attempt.Corrects is null && !alreadyCorrectedOnce
                && ExtractionCorrection.MayBeHandedBack(refusals))
            {
                handedBack = true;
            }
            else
            {
                job.FailPermanently(JobFailure.ExtractionRefused, message, now);
            }
        }

        // The two fields set after the commit above, OutputArtifactId and AcceptedAt, land here —
        // the only save a refusal needs at all.
        context.SaveChanges();
        transaction.Commit();

        return new ExtractionReceived(run.Id, accepted is not null, handedBack, refusals);
    }

    private static void AddDecisions(
        CorpusDbContext context,
        Guid meetingId,
        Guid runId,
        ExtractionDocument document,
        MeetingInput prepared,
        UtcTimestamp now)
    {
        for (var index = 0; index < document.Decisions.Count; index++)
        {
            var decision = document.Decisions[index];
            context.Decisions.Add(new Decision
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                ExtractionRunId = runId,
                Ordinal = index,
                Statement = decision.Statement,
                Evidence = CitationFor(prepared, decision.Evidence!),
                CreatedAt = now,
            });
        }
    }

    private static void AddActions(
        CorpusDbContext context,
        Guid meetingId,
        Guid runId,
        ExtractionDocument document,
        MeetingInput prepared,
        UtcTimestamp now)
    {
        for (var index = 0; index < document.Actions.Count; index++)
        {
            var action = document.Actions[index];
            context.ActionItems.Add(new ActionItem
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                ExtractionRunId = runId,
                Ordinal = index,
                Statement = action.Statement,
                DueDate = action.DueDate,
                Evidence = CitationFor(prepared, action.Evidence!),
                CreatedAt = now,
            });
        }
    }

    private static void AddQuestions(
        CorpusDbContext context,
        Guid meetingId,
        Guid runId,
        ExtractionDocument document,
        MeetingInput prepared,
        UtcTimestamp now)
    {
        for (var index = 0; index < document.OpenQuestions.Count; index++)
        {
            var question = document.OpenQuestions[index];
            context.OpenQuestions.Add(new OpenQuestion
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                ExtractionRunId = runId,
                Ordinal = index,
                Question = question.Question,
                Evidence = CitationFor(prepared, question.Evidence!),
                CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// One citation, off its evidence and the turn it names. <see cref="Citation.End"/> is the
    /// cited turn's own end and not the evidence's: a model reporting where its quote ends inside a
    /// turn is not citing anything else, and the quoted text is already checked against that
    /// turn's own words.
    /// </summary>
    private static Citation CitationFor(MeetingInput prepared, ExtractedEvidence evidence)
    {
        var turn = prepared.Turns.First(candidate => candidate.Ordinal == evidence.UtteranceOrdinal);
        return new Citation
        {
            MeetingId = prepared.MeetingId,
            UtteranceOrdinal = evidence.UtteranceOrdinal,
            Start = evidence.Start,
            End = turn.End,
            SpeakerLabel = evidence.SpeakerLabel,
            QuotedText = evidence.QuotedText,
            SourceArtifactSha256 = prepared.TranscribedFrom,
        };
    }

    /// <summary>
    /// The one refused run <paramref name="correctsRunId"/> names, with its own refusals read back
    /// — what <see cref="ExtractionCorrection.Judge"/> judges a correction's document against.
    /// </summary>
    /// <remarks>
    /// Asks whether <paramref name="correctsRunId"/> belongs to <paramref name="jobId"/> and is not
    /// already corrected — never whether it is the job's only run. A job the runner retried after an
    /// earlier hand-back's own correction round did not answer carries an orphaned, never-corrected
    /// run from that earlier attempt; this attempt's own hand-back is still this job's one to spend,
    /// and a stricter "exactly one run" reading would refuse it over a run it has nothing to do with.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="jobId"/> has no run <paramref name="correctsRunId"/>, or a run of
    /// <paramref name="jobId"/> already carries a correction. Nothing is written.
    /// </exception>
    private static IReadOnlyList<ExtractionRefusal> RefusalsBeingCorrected(
        CorpusDbContext context, Guid jobId, Guid correctsRunId)
    {
        var runs = context.ExtractionRuns
            .Where(row => row.JobId == jobId)
            .Select(row => new { row.Id, row.CorrectsRunId })
            .ToList();

        if (!runs.Any(run => run.Id == correctsRunId))
        {
            throw new InvalidOperationException(
                $"Job {jobId} has no run {correctsRunId} for this attempt to correct. Nothing was filed.");
        }

        if (runs.Any(run => run.CorrectsRunId is not null))
        {
            throw new InvalidOperationException(
                $"Job {jobId} already carries a correction. A job is corrected at most once. Nothing was filed.");
        }

        return context.ExtractionRefusals
            .Where(row => row.ExtractionRunId == correctsRunId)
            .OrderBy(row => row.Ordinal)
            .Select(row => new ExtractionRefusal(row.Condition, row.Path, row.Statement))
            .ToList();
    }

    private static InvalidOperationException NotAStartedExtraction(Guid jobId) =>
        new($"Job {jobId} is not a summary that has been started, so nothing is filed for it.");

    private static string RefusedMessage(IReadOnlyList<ExtractionRefusal> refusals)
    {
        var first = refusals[0];
        return $"The extraction was refused on {refusals.Count} count(s); the first is "
            + $"{WireNames<ExtractionCondition>.Of(first.Condition)} at {first.Path}.";
    }
}
