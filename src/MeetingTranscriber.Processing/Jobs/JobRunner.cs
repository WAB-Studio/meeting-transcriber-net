using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;

namespace MeetingTranscriber.Processing.Jobs;

/// <summary>
/// What one pass came to: the jobs it took an attempt on — whether or not that attempt reached the
/// provider — and one line per thing it has to say about it.
/// </summary>
public sealed record JobsRun(IReadOnlyList<Guid> Ran, IReadOnlyList<string> Left);

/// <summary>
/// Sends every <see cref="JobKind.Transcribe"/> job that is due, one corpus at a time, under the
/// corpus's own <see cref="RunnerLease"/> — and the one a person asked to be sent again, through
/// <see cref="SendAgainAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One job at a time, in order, under the lease that already proves nobody else is doing this.</b>
/// <see cref="RunWhatIsDueAsync"/> never starts a second call before the first has answered — a
/// meeting's own row is what a person reads, and two calls racing to file the same meeting's response
/// is a defect this never has to reason about because it never happens. Holding the lease across the
/// whole pass, and not just around the send, is what a second process's launch or a second pump would
/// otherwise read as "nobody is running this corpus's queue" while this one still is.
/// </para>
/// <para>
/// <b>Nothing here retries.</b> <see cref="TranscribingAMeeting.TranscribeAsync"/> already decides
/// what a call came to; this only turns that answer into a job move, guarded on the attempt that
/// started the call, so a pass that is still writing an outcome does not overwrite what a later
/// attempt — this pump's own, on its next look, or another launch's restart sweep — has since done
/// to the same row. <see cref="JobState.CanMoveTo"/> alone would let a stale write land after a
/// fresher one, which is what the re-read before the write is for.
/// </para>
/// <para>
/// <b>The guard is a convention <see cref="RunWhatIsDueAsync"/> and <see cref="SendAgainAsync"/> both
/// keep, not one the row enforces.</b> The re-read and the write below it are two round trips with
/// nothing between them: no transaction holds the row, and <c>ProcessingJob</c> carries no
/// concurrency token, so what actually closes the gap is that these two are the one writer of a
/// job's terminal move today, one job at a time, under a lease that makes either the only caller
/// touching this corpus at all. A second writer of an outcome — a person's retry answers a job
/// differently and does not compete here, but a future path that could — would have to repeat this
/// same re-read-and-compare by hand; nothing below stops it from skipping that and overwriting a
/// fresher attempt.
/// </para>
/// <para>
/// <b>A pass lets out two things only: running out of memory, and a cancellation of the caller's own
/// token.</b> Every other failure — a call that threw something this runner did not expect, a write
/// the corpus refused — becomes a line in <see cref="JobsRun.Left"/> instead, because a pass is
/// something <see cref="PumpAsync"/> runs unattended and there is nobody here to catch an exception
/// for. A genuine cancellation is not absorbed: it is what <see cref="PumpAsync"/> itself asks for
/// when it is told to stop, and swallowing it here would turn "please stop" into "try again in five
/// seconds".
/// </para>
/// </remarks>
public static class JobRunner
{
    /// <summary>
    /// How often the pump looks at the queue. A stop is a person's act and a call takes minutes, so
    /// five seconds late costs nobody anything — what this bounds is how long a job sits queued with
    /// nothing wrong, not how quickly a person's own press is answered.
    /// </summary>
    public static readonly TimeSpan HowOftenTheQueueIsLookedAt = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sends every <see cref="JobKind.Transcribe"/> job that is due in <paramref name="lease"/>'s
    /// corpus, one at a time.
    /// </summary>
    /// <remarks>
    /// Holding <paramref name="lease"/> is the whole of what lets a pass run at all — nothing here
    /// asks whether anybody else might be sending, because holding the lease is what already answers
    /// that. <see cref="RunnerLease.Root"/> is the corpus this reads and writes.
    /// </remarks>
    /// <param name="lease">The corpus's runner lease, already held.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="send">What actually reaches the provider, for each job this pass takes.</param>
    /// <param name="stopping">
    /// Cancels the call in flight. A pass a caller cancelled leaves that one job exactly where
    /// <see cref="TranscribingAMeeting.TranscribeAsync"/> left it — the next holder of this corpus's
    /// lease is what settles it.
    /// </param>
    public static async Task<JobsRun> RunWhatIsDueAsync(
        RunnerLease lease, TimeProvider clock, SendingToTheProvider send, CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(send);

        var root = lease.Root;
        var ran = new List<Guid>();
        var left = new List<string>();

        try
        {
            var now = UtcTimestamp.From(clock.GetUtcNow());

            List<ProcessingJob> due;
            using (var reading = CorpusDatabase.Open(root))
            {
                // The kind and the two states IsDue(now).IsQueued() would otherwise accept are both
                // narrowed here, in SQL — everything IsQueued() means, translated by hand because
                // the extension method itself is not. What is not translatable is the NextAttemptAt
                // comparison IsDue also makes, so that half still runs over the narrowed rows in
                // memory: without the states narrowed here too, this would pull every Transcribe job
                // this corpus has ever finished into memory on every single look, forever.
                due = [.. reading.ProcessingJobs
                    .Where(job => job.Kind == JobKind.Transcribe
                        && (job.State == JobState.Pending || job.State == JobState.FailedRetryable))
                    .OrderBy(job => job.CreatedAt)
                    .ToList()
                    .Where(job => job.IsDue(now))];
            }

            foreach (var candidate in due)
            {
                if (stopping.IsCancellationRequested)
                {
                    break;
                }

                var startedAt = UtcTimestamp.From(clock.GetUtcNow());
                var taken = Take(root, candidate.Id, startedAt);

                if (taken is not { } job)
                {
                    // Taken, moved or gone between the read above and here — another pass over this
                    // same corpus cannot happen while this one holds the lease, so what did this is
                    // a person, or a test standing in for one. Either way it is not this pass's to
                    // report: the job is exactly where whoever moved it left it.
                    continue;
                }

                ran.Add(candidate.Id);

                var ended = await Called(
                        () => TranscribingAMeeting.TranscribeAsync(root, candidate.Id, send, clock, stopping),
                        stopping)
                    .ConfigureAwait(false);

                left.AddRange(Settle(
                    root, candidate.Id, job.MeetingId, job.Attempt, ended, UtcTimestamp.From(clock.GetUtcNow())));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The one other thing a pass lets out — see the class remarks.
            throw;
        }
        catch (Exception thrown) when (thrown is not OutOfMemoryException)
        {
            // Everything that is not a job's own call failing — the corpus itself would not open,
            // most of all — becomes one line rather than a throw nobody unattended is there to
            // catch. Whatever of the pass ran before this is kept: `ran` and `left` already hold it.
            left.Add(thrown.Message);
        }

        return new JobsRun(ran, left);
    }

    /// <summary>
    /// Sends <paramref name="jobId"/> again, once a person has agreed to it at a prompt, under
    /// <paramref name="lease"/>'s corpus.
    /// </summary>
    /// <remarks>
    /// The take, the call and the guarded settle are the same three steps <see cref="RunWhatIsDueAsync"/>
    /// runs for each job it finds due — <see cref="Take"/>, <see cref="Called"/> and
    /// <see cref="Settle"/> — asked here for the one job a person named instead of for every job a
    /// look of the queue finds. There is one copy of the guard and one <see cref="Apply"/> either
    /// way.
    /// </remarks>
    /// <param name="lease">The corpus's runner lease, already held.</param>
    /// <param name="jobId">The <see cref="JobKind.Transcribe"/> job a person asked to be sent again.</param>
    /// <param name="approvedAt">When the minutes were typed back, carried onto the run.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="send">What actually reaches the provider.</param>
    /// <param name="stopping">Cancels the call in flight.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="jobId"/> is not a transcription waiting to be sent. Nothing is sent.
    /// </exception>
    public static async Task<JobsRun> SendAgainAsync(
        RunnerLease lease,
        Guid jobId,
        UtcTimestamp approvedAt,
        TimeProvider clock,
        SendingToTheProvider send,
        CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(send);

        var root = lease.Root;
        var startedAt = UtcTimestamp.From(clock.GetUtcNow());

        var taken = Take(root, jobId, startedAt)
            ?? throw new InvalidOperationException(
                $"Job {jobId} is not a transcription waiting to be sent, so nothing is sent for it.");

        var ended = await Called(
                () => TranscribingAMeeting.TranscribeAgainAsync(root, jobId, approvedAt, send, clock, stopping),
                stopping)
            .ConfigureAwait(false);

        var left = Settle(
            root, jobId, taken.MeetingId, taken.Attempt, ended, UtcTimestamp.From(clock.GetUtcNow()));

        return new JobsRun([jobId], left);
    }

    /// <summary>What one job taken to be sent was, before the call it is taken for.</summary>
    private readonly record struct TakenJob(Guid MeetingId, int Attempt);

    /// <summary>
    /// Moves <paramref name="jobId"/> to <see cref="JobState.Running"/> in its own transaction, or
    /// answers nothing when it is not a <see cref="JobKind.Transcribe"/> job due at
    /// <paramref name="startedAt"/>.
    /// </summary>
    private static TakenJob? Take(DirectoryInfo root, Guid jobId, UtcTimestamp startedAt)
    {
        using var taking = CorpusDatabase.Open(root);
        using var transaction = taking.Database.BeginTransaction();

        var job = taking.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);
        if (job is null || job.Kind != JobKind.Transcribe || !job.IsDue(startedAt))
        {
            return null;
        }

        job.Start(startedAt);
        taking.SaveChanges();
        transaction.Commit();

        return new TakenJob(job.MeetingId, job.Attempt);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> and turns anything it throws but a cancellation of
    /// <paramref name="stopping"/> into a <see cref="TranscriptionOutcome.MayHaveBeenCharged"/>
    /// answer, because a job taken but never sent-and-settled is not one either caller can leave
    /// unresolved.
    /// </summary>
    private static async Task<TranscriptionEnded> Called(
        Func<Task<TranscriptionEnded>> attempt, CancellationToken stopping)
    {
        try
        {
            return await attempt().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Left exactly as the call left it — Running, with no move written. The next holder of
            // this corpus's lease is what settles it, the same as a process that died mid-call.
            // Rethrown rather than turned into a line: this is the one thing a pass lets out other
            // than running out of memory.
            throw;
        }
        catch (Exception thrown) when (thrown is not OutOfMemoryException)
        {
            // Nothing this runner recognises — the call itself only ever answers or throws for a
            // job that was never really started, so reaching here is a defect somewhere else in the
            // stack. Treated as MayHaveBeenCharged and not as a crash: a pass over a queue of
            // several meetings owes the rest of them a try, and the same arm already asks a person
            // about anything this end cannot read.
            return new TranscriptionEnded(
                TranscriptionOutcome.MayHaveBeenCharged,
                "The transcription stopped in a way this runner did not expect: "
                + $"{thrown.Message} Whether the provider charged for it is not something "
                + "this end can tell, so nothing is sent again on its own.");
        }
    }

    /// <summary>
    /// Turns what a call came to into the job's own terminal move, guarded on the attempt that made
    /// the call, and answers what is left to say about it.
    /// </summary>
    private static IReadOnlyList<string> Settle(
        DirectoryInfo root, Guid jobId, Guid meetingId, int attempt, TranscriptionEnded ended, UtcTimestamp now)
    {
        using var writing = CorpusDatabase.Open(root);
        var fresh = writing.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);

        if (fresh is null || fresh.State != JobState.Running || fresh.Attempt != attempt)
        {
            return
            [
                $"{meetingId}: the job was {fresh?.State} at attempt {fresh?.Attempt} by the time "
                + $"its call ended, so it was left as it stood: {ended.Said}",
            ];
        }

        Apply(fresh, ended, now);
        var left = new List<string>();

        try
        {
            writing.SaveChanges();
        }
        catch (Exception refused) when (refused is not OutOfMemoryException)
        {
            left.Add($"{meetingId}: the job's own move could not be written: {refused.Message}");
        }

        if (ended.Said is not null)
        {
            left.Add($"{meetingId}: {ended.Said}");
        }

        return left;
    }

    /// <summary>
    /// Looks at <paramref name="root"/>'s queue every <paramref name="howOften"/>, sending what is
    /// due for as long as <paramref name="stopping"/> allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A folder holding no corpus yet is looked at and left alone, the same answer every other
    /// launch chore gives it — a corpus is never made here, only ever read.
    /// </para>
    /// <para>
    /// <b>The restart's jobs come first at every look, not only the first.</b> A pass runs on a look
    /// only when that same look's own recovery came back with nothing left to say — never once and
    /// then never again. That is deliberate and not a missed optimisation: this pump's own passes
    /// finish one job before starting the next, so the only way a look can ever find a
    /// <c>Running</c> row under a lease it already holds is a write this same runner made and could
    /// not save — the job is exactly as stuck as one a dead process left, and the next look's
    /// recovery is what moves it to a person rather than leaving it to outlive the pump that
    /// orphaned it. A recovery that fails on its own — a busy lock, a corpus mid-write from
    /// somewhere else — costs one skipped pass and is retried at the next look rather than skipped
    /// for good.
    /// </para>
    /// <para>
    /// <b>Every look absorbs everything but running out of memory and a cancellation of
    /// <paramref name="stopping"/>.</b> That includes an <see cref="UnauthorizedAccessException"/>
    /// out of <see cref="RunnerLease.TryTake"/> — what a <c>runner.mark</c> that came back read-only
    /// from a restore raises — so one bad look never ends the pump for the rest of the process; the
    /// next look tries again.
    /// </para>
    /// <para>
    /// It looks once before the first wait, so a corpus already carrying due work is not kept waiting
    /// out a first <paramref name="howOften"/> for nothing to have changed. It ends quietly, and lets
    /// the lease go, the moment <paramref name="stopping"/> is cancelled.
    /// </para>
    /// </remarks>
    /// <param name="root">The corpus to run.</param>
    /// <param name="clock">Where every timestamp this writes comes from, and what the wait is measured against.</param>
    /// <param name="send">What actually reaches the provider.</param>
    /// <param name="howOften">How long a look waits before the next one.</param>
    /// <param name="stopping">Ends the pump. Nothing about a call already in flight is rushed by it.</param>
    public static async Task PumpAsync(
        DirectoryInfo root,
        TimeProvider clock,
        SendingToTheProvider send,
        TimeSpan howOften,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(send);

        RunnerLease? lease = null;

        try
        {
            while (true)
            {
                try
                {
                    if (CorpusDatabase.HoldsACorpus(root))
                    {
                        lease ??= RunnerLease.TryTake(root);

                        if (lease is not null && JobsARestartFound.Holding(lease).Left.Count == 0)
                        {
                            _ = await RunWhatIsDueAsync(lease, clock, send, stopping).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception thrown) when (thrown is not OutOfMemoryException)
                {
                    // Absorbed. Whatever went wrong with this look — the lease, the recovery, a
                    // pass — is tried again at the next one, so nothing here ends the pump for the
                    // rest of the process.
                    _ = thrown;
                }

                await Task.Delay(howOften, clock, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The one way out: somebody asked this to stop. Nothing to report, and nothing left to
            // send — a call already in flight is TranscribingAMeeting's own to have left in a state
            // the next lease holder can settle.
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>
    /// Turns what a call came to into the one move it decides for <paramref name="job"/>, which is
    /// still <see cref="JobState.Running"/> at the attempt that made the call.
    /// </summary>
    private static void Apply(ProcessingJob job, TranscriptionEnded ended, UtcTimestamp now)
    {
        switch (ended.Outcome)
        {
            case TranscriptionOutcome.Filed:
            case TranscriptionOutcome.AlreadyTranscribed:
                job.Succeed(now);
                break;

            case TranscriptionOutcome.NothingWasCharged:
                // Never a FailRetryable: nothing in this application retries anything on its own,
                // and the one way out of an offer refused is a person pressing it again.
                job.FailPermanently(ended.Said!, now);
                break;

            case TranscriptionOutcome.MayHaveBeenCharged:
                job.AwaitUser(ended.Said!);
                break;

            default:
                throw new InvalidOperationException($"Unknown transcription outcome '{ended.Outcome}'.");
        }
    }
}
