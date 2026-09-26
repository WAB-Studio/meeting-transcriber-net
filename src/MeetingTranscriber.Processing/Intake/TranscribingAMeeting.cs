using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Intake;

/// <summary>
/// One call to the provider, bound to a key and a channel this type never opens. What sends the
/// audio and reads back the response is the caller's, so this can be driven from a test with no
/// socket and from the application with the key this install keeps.
/// </summary>
/// <remarks>
/// It throws <see cref="DeepgramCallException"/> when the provider refused, failed or could not be
/// reached; <see cref="DeepgramKeyException"/> when there is no key to send with, before anything is
/// sent; and <see cref="OperationCanceledException"/> when <paramref name="stopping"/> is the one
/// that ended the call.
/// </remarks>
/// <param name="audio">The file to send.</param>
/// <param name="asked">What is being asked for.</param>
/// <param name="response">Where every byte that comes back is written.</param>
/// <param name="stopping">What a caller with a deadline, or a person, cancels.</param>
public delegate Task<long> SendingToTheProvider(
    FileInfo audio, DeepgramRequest asked, Stream response, CancellationToken stopping);

/// <summary>What a call to the provider came to.</summary>
public enum TranscriptionOutcome
{
    /// <summary>The response this call brought back is in the corpus, rendered or not.</summary>
    Filed = 1,

    /// <summary>The meeting already had a response before anything was sent.</summary>
    AlreadyTranscribed = 2,

    /// <summary>Nothing left the machine, or the provider refused before transcribing.</summary>
    NothingWasCharged = 3,

    /// <summary>Everything else, including a whole response the corpus would not file.</summary>
    MayHaveBeenCharged = 4,
}

/// <summary>What one call came to, and why when it is worth saying.</summary>
/// <param name="Said">
/// Never blank for the two failures. Null for <see cref="TranscriptionOutcome.AlreadyTranscribed"/>,
/// and null for <see cref="TranscriptionOutcome.Filed"/> unless the render after the filing failed.
/// </param>
/// <param name="Failure">
/// What was observed, set exactly when <paramref name="Outcome"/> is
/// <see cref="TranscriptionOutcome.NothingWasCharged"/>. No guard enforces that here: <c>Apply</c>'s
/// own throw on a missing kind is loud enough, and every construction of this type is in the two
/// files that own it.
/// </param>
public sealed record TranscriptionEnded(TranscriptionOutcome Outcome, string? Said, JobFailure? Failure = null);

/// <summary>
/// A meeting being transcribed: sending its audio, and turning what came back — or what stopped it —
/// into a fact this corpus can hold.
/// </summary>
/// <remarks>
/// <para>
/// It takes a job id and not a meeting id, because what may run this is <c>JobRunner</c> alone, and
/// what a job id proves is that something has already decided this attempt should happen — the
/// job's own state, re-read here rather than trusted from whoever is calling.
/// </para>
/// <para>
/// <b>This method holds no lease and re-reads the job exactly once.</b> That is deliberate and not
/// a gap: <c>RunnerLease</c> is taken once, before a pump's first pass, and held for as long as the
/// pump runs — never per call here — so what keeps two processes from both spending on the same job
/// is the caller holding that lease for the whole of a pass, not anything this method checks. A
/// caller that invokes this twice for one job without that discipline is not one this method can
/// defend against, the same way <c>ProcessingJob</c>'s own moves trust the transaction the caller
/// opened around them.
/// </para>
/// <para>
/// <b>It answers an outcome rather than throwing.</b> A call to the provider is money that may have
/// moved whether the call above this one gets to finish reading the answer, so every path below —
/// a refusal, a connection that never opened, a response this build cannot render — ends in a
/// <see cref="TranscriptionEnded"/> a caller can act on, and only a job that was never really started
/// or a genuine cancellation ever leaves as an exception.
/// </para>
/// <para>
/// <b>The run row goes first.</b> It is written, with the temporary already open beside it, before
/// the call is made — so a process that dies mid-call leaves a row saying an attempt was made
/// rather than nothing at all. <see cref="TranscriptionRun.ApprovedAt"/> is empty on a first
/// transcription, whose press shows no price yet (ISC-85). It is the instant the minutes were
/// typed back on a transcription asked for again.
/// </para>
/// <para>
/// <b>Nothing after the send observes <paramref name="stopping"/>.</b> Once the provider has
/// answered, its bytes are paid for whatever a caller's token says next, so every await from that
/// line on is handed no token: filing a paid response is not a wait a deadline gets to end.
/// </para>
/// </remarks>
public static class TranscribingAMeeting
{
    private const string Provider = "deepgram";

    /// <summary>Where a paid response the corpus refuses to file is kept, for one run.</summary>
    public static string RefusedResponseFileName(Guid run) => $"deepgram.refused.{run:N}.json";

    /// <summary>
    /// Sends a transcription job and turns what happens into a <see cref="TranscriptionEnded"/>.
    /// </summary>
    /// <param name="root">The corpus.</param>
    /// <param name="jobId">The <see cref="JobKind.Transcribe"/> job this attempt is for.</param>
    /// <param name="send">What actually reaches the provider.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="stopping">
    /// Cancels the send. Nothing after the send observes it — see the class remarks.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The job is missing, is not a transcription, or has not been started. Nothing is sent.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="stopping"/> ended the send itself. The run is left exactly as it was: the
    /// next holder of this corpus's lease is what settles the job.
    /// </exception>
    public static Task<TranscriptionEnded> TranscribeAsync(
        DirectoryInfo root,
        Guid jobId,
        SendingToTheProvider send,
        TimeProvider clock,
        CancellationToken stopping = default) =>
        TranscribeCoreAsync(root, jobId, again: false, approvedAt: null, send, clock, stopping);

    /// <summary>
    /// Sends a meeting's audio to the provider again, once its minutes have been typed back, and
    /// turns what happens into a <see cref="TranscriptionEnded"/> exactly as <see cref="TranscribeAsync"/>
    /// does for a first transcription.
    /// </summary>
    /// <remarks>
    /// The four places this differs from <see cref="TranscribeAsync"/> are named on the shared
    /// private core both call: whether the meeting already having a response answers
    /// <see cref="TranscriptionOutcome.AlreadyTranscribed"/>, where the temporary sits, whether the
    /// run carries <paramref name="approvedAt"/>, and which door of <see cref="MeetingIntake"/>
    /// files the response.
    /// </remarks>
    /// <param name="root">The corpus.</param>
    /// <param name="jobId">The <see cref="JobKind.Transcribe"/> job this attempt is for.</param>
    /// <param name="approvedAt">
    /// When somebody agreed to this call, having typed the meeting's minutes back. Written onto the
    /// run whatever this attempt comes to.
    /// </param>
    /// <param name="send">What actually reaches the provider.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="stopping">
    /// Cancels the send. Nothing after the send observes it — see the class remarks.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The job is missing, is not a transcription, or has not been started. Nothing is sent.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="stopping"/> ended the send itself. The run is left exactly as it was: the
    /// next holder of this corpus's lease is what settles the job.
    /// </exception>
    public static Task<TranscriptionEnded> TranscribeAgainAsync(
        DirectoryInfo root,
        Guid jobId,
        UtcTimestamp approvedAt,
        SendingToTheProvider send,
        TimeProvider clock,
        CancellationToken stopping = default) =>
        TranscribeCoreAsync(root, jobId, again: true, approvedAt, send, clock, stopping);

    /// <summary>
    /// What <see cref="TranscribeAsync"/> and <see cref="TranscribeAgainAsync"/> both are, told
    /// apart by <paramref name="again"/> and never by whether <paramref name="approvedAt"/> carries
    /// a value — the two happen to agree today, because nothing asks for an approval on a first
    /// transcription (ISC-85), but that is a fact about what is built so far and not a rule this
    /// core is entitled to lean on.
    /// </summary>
    private static async Task<TranscriptionEnded> TranscribeCoreAsync(
        DirectoryInfo root,
        Guid jobId,
        bool again,
        UtcTimestamp? approvedAt,
        SendingToTheProvider send,
        TimeProvider clock,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(clock);

        Guid meetingId;
        DeepgramRequest asked;
        FileInfo audioFile;
        string audioMissingMessage;
        FileInfo temporary;
        FileStream writing;
        TranscriptionRun run;

        using (var context = CorpusDatabase.Open(root))
        {
            var job = context.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);
            if (job is null || job.Kind != JobKind.Transcribe || job.State != JobState.Running)
            {
                throw new InvalidOperationException(
                    $"Job {jobId} is not a transcription that has been started, so nothing is "
                    + "sent for it.");
            }

            meetingId = job.MeetingId;

            // Only a first transcription asks this: a re-transcription is sent precisely because
            // the meeting already has a response, and MeetingWork.EnsureMayBeTranscribedAgain —
            // not this method — is what refuses one asked for out of turn.
            if (!again && context.Artifacts.Any(row =>
                    row.MeetingId == meetingId && row.Kind == ArtifactKind.DeepgramResponse))
            {
                return new TranscriptionEnded(TranscriptionOutcome.AlreadyTranscribed, null);
            }

            var audio = context.Artifacts.FirstOrDefault(row =>
                row.MeetingId == meetingId && row.Kind == ArtifactKind.Audio);

            if (audio is null)
            {
                return new TranscriptionEnded(
                    TranscriptionOutcome.NothingWasCharged,
                    $"Meeting {meetingId} has no audio in this corpus, so there was nothing to "
                    + "send. Nothing was sent and nothing was charged.",
                    JobFailure.AudioMissing);
            }

            audioFile = CorpusFiles.Locate(root, audio.RelativePath);
            audioFile.Refresh();

            audioMissingMessage =
                $"The audio this meeting is transcribed from is not at '{audioFile.FullName}'. "
                + "Nothing was sent and nothing was charged.";

            if (!audioFile.Exists)
            {
                return new TranscriptionEnded(
                    TranscriptionOutcome.NothingWasCharged, audioMissingMessage, JobFailure.AudioMissing);
            }

            var meeting = context.Meetings.First(row => row.Id == meetingId);
            asked = new DeepgramRequest(meeting.SourceProfile, meeting.Language);

            // The highest placeable version already filed, plus one — which is always 1 on a first
            // transcription, because the AlreadyTranscribed check above already refused this path
            // the moment the meeting had any response at all. A row this cannot place is not
            // re-checked here: it is MeetingWork's refusal ahead of this call, and the filing door
            // below refuses it loudly if one slips through anyway.
            var next = NextPlaceableVersion(context, meetingId);

            temporary = CorpusFiles.UnfinishedBeside(
                CorpusFiles.Locate(root, CorpusFiles.PathFor(meetingId, ResponseVersions.Named(next))));

            try
            {
                writing = new FileStream(
                    temporary.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
            {
                return new TranscriptionEnded(
                    TranscriptionOutcome.NothingWasCharged,
                    $"The response could not be given somewhere to land: {failed.Message} "
                    + "Nothing was sent and nothing was charged.",
                    JobFailure.CorpusRefused);
            }

            run = new TranscriptionRun
            {
                Id = Guid.NewGuid(),
                MeetingId = meetingId,
                JobId = jobId,
                Provider = Provider,
                Model = DeepgramRequest.Model,
                SourceProfile = meeting.SourceProfile,
                Language = meeting.Language,
                AudioSha256 = audio.Sha256,
                BillableConfigHash = asked.BillableConfigHash,
                ApprovedAt = approvedAt,
                CreatedAt = UtcTimestamp.From(clock.GetUtcNow()),
            };

            try
            {
                context.TranscriptionRuns.Add(run);
                context.SaveChanges();
            }

            // Every refusal but running out of memory is answered NothingWasCharged here, because
            // nothing has been sent yet — the write below is what would have sent it, and it never
            // ran.
            catch (Exception refused) when (refused is not OutOfMemoryException)
            {
                // The write handle must close before the temporary can be removed: it was opened
                // with FileShare.Read, which admits another reader but refuses a delete while it
                // is open. Nothing has been sent yet, so nothing to undo but the file this attempt
                // opened for itself.
                await writing.DisposeAsync().ConfigureAwait(false);
                TryDelete(temporary);

                return new TranscriptionEnded(
                    TranscriptionOutcome.NothingWasCharged,
                    "The transcription could not be written into the corpus before anything was "
                    + $"sent: {refused.Message} Nothing was sent and nothing was charged.",
                    JobFailure.CorpusRefused);
            }
        }

        try
        {
            _ = await send(audioFile, asked, writing, stopping).ConfigureAwait(false);
        }
        catch (Exception thrown) when (thrown is not OutOfMemoryException)
        {
            // The write handle must close before the temporary can be removed: it was opened with
            // FileShare.Read, which admits another reader but refuses a delete while it is open.
            await writing.DisposeAsync().ConfigureAwait(false);
            TryDelete(temporary);

            if (thrown is OperationCanceledException && stopping.IsCancellationRequested)
            {
                throw;
            }

            var (outcome, said, failure) = Answered(thrown, audioFile, audioMissingMessage);
            WriteLastError(root, run.Id, said);
            return new TranscriptionEnded(outcome, said, failure);
        }

        // The read handle is opened before the write handle closes (Decides), so the temporary is
        // never unheld between the two: a sweep run in that gap would read it as a dead write and
        // take a response that was just paid for. It is `using` as a safety net against the handle
        // itself leaking on a throw this method does not otherwise guard against. `filing.SaveChanges()`
        // below, which can still fail after `ReceiveInto` or `ReceiveAgainInto` has already filed
        // the response, is guarded (O-20260925-13): the response is on disk and in the corpus by
        // that point, so `Filed` is the true answer there whatever the run's own bookkeeping does.
        // Every explicit `Dispose()` below stays: the file has to be let go before it is deleted or
        // moved, and a dispose the `using` runs after one of those is a no-op on a stream already
        // closed.
        using var reading = new FileStream(
            temporary.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await writing.DisposeAsync().ConfigureAwait(false);
        var hash = CorpusFiles.Sha256Of(reading);
        var now = UtcTimestamp.From(clock.GetUtcNow());

        using var filing = CorpusDatabase.Open(root);
        ReceivedMeeting received;

        try
        {
            received = again
                ? MeetingIntake.ReceiveAgainInto(filing, meetingId, temporary, now)
                : MeetingIntake.ReceiveInto(filing, meetingId, temporary, now);
        }
        catch (Exception unfiled) when (unfiled is not OutOfMemoryException)
        {
            // `filing` is let go and never saved again: whatever it touched before throwing is not
            // this corpus's to keep, and re-reading with a fresh context is the only way to ask
            // what is really there.
            return AfterAFailedFiling(
                root, meetingId, run.Id, hash, temporary, reading, now, unfiled.Message, again);
        }

        // O-20260925-13: the response is already filed and safe on disk by this point, so a
        // refusal here is answered Filed either way — what may still fail is only the record of
        // which run bought it, and that is worth a second attempt in a fresh context before it is
        // given up on.
        try
        {
            var tracked = filing.TranscriptionRuns.First(row => row.Id == run.Id);
            tracked.FinishedAt = now;
            tracked.ResponseArtifactId = received.Response.Id;
            filing.SaveChanges();
        }
        catch (Exception failed) when (failed is not OutOfMemoryException)
        {
            reading.Dispose();
            TryDelete(temporary);

            var said = WriteRunFinishedAgain(root, run.Id, received.Response.Id, now)
                ? "The response was filed and read; the record of which call bought it needed a "
                  + $"second attempt: {failed.Message}"
                : "The response was filed and read, and the record of which call bought it could "
                  + $"not be written: {failed.Message} Nothing was lost and nothing is sent again.";

            return new TranscriptionEnded(TranscriptionOutcome.Filed, said);
        }

        reading.Dispose();
        TryDelete(temporary);

        return new TranscriptionEnded(TranscriptionOutcome.Filed, null);
    }

    /// <summary>The highest placeable response version this meeting already has, plus one.</summary>
    private static int NextPlaceableVersion(CorpusDbContext context, Guid meetingId)
    {
        var highest = 0;

        foreach (var response in context.Artifacts.Where(row =>
                     row.MeetingId == meetingId && row.Kind == ArtifactKind.DeepgramResponse))
        {
            if (ResponseVersions.VersionOf(response) is { } version && version > highest)
            {
                highest = version;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// The run's own bookkeeping, written once more in a fresh context, after the first attempt
    /// failed alongside a response that is already filed and safe.
    /// </summary>
    private static bool WriteRunFinishedAgain(
        DirectoryInfo root, Guid runId, Guid responseId, UtcTimestamp now)
    {
        try
        {
            using var retry = CorpusDatabase.Open(root);
            var run = retry.TranscriptionRuns.FirstOrDefault(row => row.Id == runId);

            if (run is null)
            {
                return false;
            }

            run.FinishedAt = now;
            run.ResponseArtifactId = responseId;
            retry.SaveChanges();
            return true;
        }
        catch (Exception failed) when (failed is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// What the send's own exception says about whether this meeting may have been charged for.
    /// </summary>
    private static (TranscriptionOutcome Outcome, string Said, JobFailure? Failure) Answered(
        Exception thrown, FileInfo audio, string audioMissingMessage) => thrown switch
        {
            DeepgramKeyException keyless =>
                (TranscriptionOutcome.NothingWasCharged, keyless.Message, JobFailure.NoKeyOnThisMachine),

            // SendAsync opens the audio before it sends anything, so a file that vanished after this
            // method already confirmed it is the one case a missing file can still mean "nothing left
            // the machine" this late.
            FileNotFoundException gone
                when string.Equals(gone.FileName, audio.FullName, StringComparison.Ordinal) =>
                (TranscriptionOutcome.NothingWasCharged, audioMissingMessage, JobFailure.AudioMissing),

            DeepgramCallException failed => failed.WhyNothingWasCharged is { } kind
                ? (TranscriptionOutcome.NothingWasCharged, failed.Message, kind)
                : (TranscriptionOutcome.MayHaveBeenCharged, failed.Message, null),

            _ => (
                TranscriptionOutcome.MayHaveBeenCharged,
                "The call to the provider failed in a way this end cannot read: "
                + $"{thrown.Message} Whether it was charged is not something this end can tell, so "
                + "nothing is sent again on its own.",
                null),
        };

    /// <summary>
    /// What a response the corpus refused to file becomes: the same response somebody else already
    /// filed while this call was out, or a paid response kept under its run for a person to settle.
    /// </summary>
    private static TranscriptionEnded AfterAFailedFiling(
        DirectoryInfo root,
        Guid meetingId,
        Guid runId,
        string hash,
        FileInfo temporary,
        FileStream reading,
        UtcTimestamp now,
        string message,
        bool again)
    {
        using var context = CorpusDatabase.Open(root);

        var already = context.Artifacts.FirstOrDefault(row =>
            row.MeetingId == meetingId
            && row.Kind == ArtifactKind.DeepgramResponse
            && row.Sha256 == hash);

        if (already is not null)
        {
            var run = context.TranscriptionRuns.FirstOrDefault(row => row.Id == runId);
            if (run is not null)
            {
                run.FinishedAt = now;
                run.ResponseArtifactId = already.Id;
                context.SaveChanges();
            }

            reading.Dispose();
            TryDelete(temporary);

            // A render can fail on either side of the turns swap, and the swap commits on its own
            // (Decides 11), so this branch is reached whether the failure was before or after it.
            // The re-transcription path says which version was filed and that render alone is
            // owed, rather than promising a launch will fix it or naming which version a reader
            // is shown until then — that is `turn_sources`' own record, written when the owed
            // render runs. The first-transcription path keeps its words, because there is only
            // ever the one version for a launch to render again.
            var said = again
                ? $"The response was filed as version {ResponseVersions.VersionOf(already)} and "
                  + $"paid for, and what is read out of it was not all written: {message} "
                  + $"'render {meetingId}' writes it again."
                : $"The response was filed and what is read out of it was not: {message} The next "
                  + "launch renders it again.";

            return new TranscriptionEnded(TranscriptionOutcome.Filed, said);
        }

        reading.Dispose();

        var kept = CorpusFiles.Locate(root, CorpusFiles.PathFor(meetingId, RefusedResponseFileName(runId)));
        var keptWhere = kept.FullName;

        try
        {
            File.Move(temporary.FullName, kept.FullName, overwrite: false);
        }
        catch (Exception stuck) when (stuck is IOException or UnauthorizedAccessException)
        {
            keptWhere = temporary.FullName;
        }

        var unsettled = context.TranscriptionRuns.FirstOrDefault(row => row.Id == runId);
        if (unsettled is not null)
        {
            unsettled.LastError = message;
            context.SaveChanges();
        }

        return new TranscriptionEnded(
            TranscriptionOutcome.MayHaveBeenCharged,
            $"{message} What the provider sent back was paid for and is kept at '{keptWhere}', "
            + "where nothing files it on its own and check names it until somebody moves or "
            + "deletes it.");
    }

    private static void WriteLastError(DirectoryInfo root, Guid runId, string message)
    {
        try
        {
            using var context = CorpusDatabase.Open(root);
            var run = context.TranscriptionRuns.FirstOrDefault(row => row.Id == runId);
            if (run is not null)
            {
                run.LastError = message;
                context.SaveChanges();
            }
        }
        catch (Exception thrown) when (thrown is not OutOfMemoryException)
        {
            // Absorbed. The caller already has the outcome to act on; losing the reason on the
            // row is not worth turning a decided outcome into a throw.
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            File.Delete(file.FullName);
        }
        catch (Exception thrown) when (thrown is IOException or UnauthorizedAccessException)
        {
            // The corpus is left with a `.partial` a sweep will take on its own terms. Losing that
            // race is not worth turning an already-decided outcome into a throw.
        }
    }
}
