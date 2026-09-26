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
/// stage stays offered.</b> <c>OwedWork.Of</c> already reads a stage back as owed once its newest
/// job of that kind failed permanently, which is what makes <em>Resumir</em> a new job and never a
/// retry this door — or anything else — runs on its own.
/// </para>
/// <para>
/// <b>The run is written whole, with its outcome, in one transaction.</b> A run row written before
/// this call — the way a transcription writes its own before the call it is for — is #119's to
/// decide once there is a provider call in front of this door.
/// </para>
/// <para>
/// <b>An accepted output is kept byte for byte at <c>meetings/&lt;id&gt;/extractions/&lt;run id&gt;.json</c>,
/// as <see cref="ArtifactKind.Extraction"/>, and a refused one leaves no file at all.</b> The
/// summary, decisions, actions and open questions are projected once, at acceptance, and never
/// again.
/// </para>
/// <para>
/// This is the door #119's adapter hands an unwrapped extraction to, once there is a provider
/// behind it. Nothing here reaches a transcription provider — a source guard over every file in
/// this folder is what proves that, rather than a review having to notice a stray reference.
/// </para>
/// </remarks>
public static class ExtractionIntake
{
    /// <summary>One attempt at summarising a meeting, exactly as it is handed to this door.</summary>
    public sealed record ExtractionAttempt(
        Guid JobId,
        string Provider,
        string? ProviderVersion,
        string? Model,
        string PromptVersion,
        string InputHash,
        byte[] Output);

    /// <summary>What filing one attempt came to.</summary>
    public sealed record ExtractionReceived(Guid RunId, bool Accepted, IReadOnlyList<ExtractionRefusal> Refusals);

    /// <summary>
    /// Checks one attempt against the meeting it claims to be about, and writes what it found.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/> already holds a transaction, or <paramref name="attempt"/> does
    /// not name a <see cref="JobKind.Extract"/> job that has been started. Nothing is written.
    /// </exception>
    public static ExtractionReceived Receive(CorpusDbContext context, ExtractionAttempt attempt, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attempt);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "An extraction is filed in a transaction of its own, and this context already "
                + "holds one. Nothing was filed.");
        }

        // Read once, untracked, only to learn which meeting this is about — the throw below covers
        // a job that does not exist at all, and the kind and state are asked again, tracked, once
        // the write lock is held (Decides; the job read outside the transaction).
        var meetingId = context.ProcessingJobs
            .AsNoTracking()
            .Where(job => job.Id == attempt.JobId)
            .Select(job => (Guid?)job.MeetingId)
            .FirstOrDefault()
            ?? throw NotAStartedExtraction(attempt.JobId);

        // Prepared and checked outside this door's own transaction, and never re-checked once it
        // opens: a render landing in that gap would leave a citation's turn stale, and the citation
        // foreign key — not deferred here — is what turns that into a loud transaction failure
        // rather than a silent one, the same way `MeetingRenderer.RefuseStrandedClaims` leans on it.
        var prepared = MeetingInput.Prepare(context, meetingId);
        var verdict = ExtractionCheck.Of(attempt.Output, attempt.InputHash, prepared);
        var runId = Guid.NewGuid();

        // Staged before the transaction opens, as MeetingArchive.Filed stages a source: the write
        // lock is taken at BEGIN IMMEDIATE, so no other writer waits on this copy. The `using` is
        // load-bearing — Dispose lets go of the `.partial` and deletes it, so a throw between here
        // and the commit below leaves nothing behind.
        using var staged = verdict.IsAccepted
            ? StagedArtifact.Stage(
                context,
                meetingId,
                ArtifactKind.Extraction,
                CorpusFiles.PathFor(meetingId, $"extractions/{runId}.json"),
                stream => stream.Write(attempt.Output))
            : null;

        using var transaction = context.Database.BeginTransaction();

        // Re-read, tracked, inside the transaction: the write lock is what makes this check and the
        // move that follows one thing. Nothing has been added to the context yet when this refuses.
        var job = context.ProcessingJobs.FirstOrDefault(row => row.Id == attempt.JobId);
        if (job is null || job.Kind != JobKind.Extract || job.State != JobState.Running)
        {
            throw NotAStartedExtraction(attempt.JobId);
        }

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
            CreatedAt = now,
        };

        context.ExtractionRuns.Add(run);

        if (verdict.IsAccepted)
        {
            var document = verdict.Accepted!;

            context.Summaries.Add(new Summary
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                ExtractionRunId = run.Id,
                Abstract = document.Abstract,
                Body = document.Summary,
                CreatedAt = now,
            });

            AddDecisions(context, meetingId, run.Id, document, prepared, now);
            AddActions(context, meetingId, run.Id, document, prepared, now);
            AddQuestions(context, meetingId, run.Id, document, prepared, now);

            job.Succeed(now);

            // Moves the file into place and saves everything added so far — the run, the claims
            // and the job move above all land in this one call, inside this transaction.
            var filed = staged!.Commit(now);

            run.OutputArtifactId = filed.Id;
            run.AcceptedAt = now;
        }
        else
        {
            for (var ordinal = 0; ordinal < verdict.Refusals.Count; ordinal++)
            {
                var refusal = verdict.Refusals[ordinal];
                context.ExtractionRefusals.Add(new ExtractionRunRefusal
                {
                    ExtractionRunId = run.Id,
                    Ordinal = ordinal,
                    Condition = refusal.Condition,
                    Path = refusal.Path,
                    Statement = refusal.Statement,
                });
            }

            var message = RefusedMessage(verdict.Refusals);
            run.LastError = message;
            job.FailPermanently(JobFailure.ExtractionRefused, message, now);
        }

        // The two fields set after the commit above, OutputArtifactId and AcceptedAt, land here —
        // the only save a refusal needs at all.
        context.SaveChanges();
        transaction.Commit();

        return new ExtractionReceived(run.Id, verdict.IsAccepted, verdict.Refusals);
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

    private static InvalidOperationException NotAStartedExtraction(Guid jobId) =>
        new($"Job {jobId} is not a summary that has been started, so nothing is filed for it.");

    private static string RefusedMessage(IReadOnlyList<ExtractionRefusal> refusals)
    {
        var first = refusals[0];
        return $"The extraction was refused on {refusals.Count} count(s); the first is "
            + $"{WireNames<ExtractionCondition>.Of(first.Condition)} at {first.Path}.";
    }
}
