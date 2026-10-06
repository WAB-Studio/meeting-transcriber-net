using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Summaries;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Jobs;

/// <summary>
/// What one pass came to: the jobs it took an attempt on — whether or not that attempt reached the
/// provider — and one line per thing it has to say about it.
/// </summary>
/// <remarks>
/// <see cref="Left"/> names the meeting a transcription line is about and the job a summary line is
/// about — <c>Settle</c>'s lines start <c>"{meetingId}: ..."</c>, <c>SettleSummary</c>'s
/// <c>"{jobId}: ..."</c>. Nothing reads this list today (see the class remarks), so the two have
/// never had to agree; a future reader parsing it by position rather than by eye needs to know
/// which prefix means which before it does.
/// </remarks>
public sealed record JobsRun(IReadOnlyList<Guid> Ran, IReadOnlyList<string> Left);

/// <summary>
/// Sends every due <see cref="JobKind.Transcribe"/> job, and then every due
/// <see cref="JobKind.Extract"/> one when a summariser was handed over, one corpus at a time,
/// under the corpus's own <see cref="RunnerLease"/> — and the one transcription a person asked to
/// be sent again, through <see cref="SendAgainAsync"/>.
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
/// <b>Transcriptions before summaries, and each ordered by when it was created.</b> A summary's own
/// input is the transcription that precedes it, so a pass that reached a due <c>Extract</c> job
/// ahead of a due <c>Transcribe</c> one would be sending a request whose answer could have changed
/// the corpus underneath it moments later. <paramref name="summarise"/> being <c>null</c> is what a
/// launch with nothing to summarise with hands over — its due <c>Extract</c> jobs are left exactly
/// where they were, queued rather than skipped, for the next pass that is given one to find.
/// </para>
/// <para>
/// <b>Two lanes, and one job in each.</b> <see cref="RunWhatIsDueAsync"/> is the whole queue in one
/// serial pass, and every caller that wants a pass gets exactly that. <see cref="PumpAsync"/> is not
/// such a caller: a summary can run for ten minutes, and somebody who stops a recording expects it to
/// start transcribing, so at every look the pump sends the due transcriptions itself and runs at most
/// one lane of summaries beside them, started at a look and not awaited by it. Neither lane ever has
/// two calls in flight, so a meeting's own row is still never raced by two calls of one kind. The one
/// summary job a lane is running is named to the restart sweep, which leaves it alone.
/// </para>
/// <para>
/// <b>Nothing here retries a transcription.</b> <see cref="TranscribingAMeeting.TranscribeAsync"/>
/// already decides what a call came to; this only turns that answer into a job move, guarded on the
/// attempt that started the call, so a pass that is still writing an outcome does not overwrite what
/// a later attempt — this pump's own, on its next look, or another launch's restart sweep — has
/// since done to the same row. <see cref="JobState.CanMoveTo"/> alone would let a stale write land
/// after a fresher one, which is what the re-read before the write is for. A summary is retried, up
/// to <see cref="TimesASummaryIsTried"/> times, because a provider that ran and gave back nothing
/// usable is not the same fault as a transcription's own — see <see cref="RunSummaryAsync"/>.
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
    /// How many attempts a summary job is given before it fails for good. A first attempt that gets
    /// no answer waits <see cref="WaitBeforeTryingASummaryAgain"/> and is tried again; a third that
    /// still gets no answer fails the job permanently rather than waiting a third time.
    /// </summary>
    public const int TimesASummaryIsTried = 3;

    /// <summary>
    /// How often, while a summary is running, the runner reads its job again to find out whether
    /// somebody stopped it from the screen. A summary takes minutes and a stop is a person's act, so
    /// a second is close enough that nobody presses <em>Detener</em> and wonders why nothing moved.
    /// </summary>
    public static readonly TimeSpan HowOftenARunningSummaryIsWatched = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long to wait before trying a summary again, after <paramref name="attempt"/> got no
    /// answer. One minute after the first attempt, five after the second — the bound is what stops
    /// one error spending quota over and over, not a schedule with a reason of its own.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="attempt"/> is not the first or the second — the third is what
    /// <see cref="TimesASummaryIsTried"/> ends, and nothing here waits to try a fourth.
    /// </exception>
    public static Duration WaitBeforeTryingASummaryAgain(int attempt) => attempt switch
    {
        1 => Duration.FromTimeSpan(TimeSpan.FromMinutes(1)),
        2 => Duration.FromTimeSpan(TimeSpan.FromMinutes(5)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(attempt), attempt, "A summary job only waits to be tried again after its first or second attempt."),
    };

    /// <summary>
    /// Sends every due <see cref="JobKind.Transcribe"/> job in <paramref name="lease"/>'s corpus,
    /// and then every due <see cref="JobKind.Extract"/> one when <paramref name="summarise"/> is
    /// given, each kind ordered by when it was queued.
    /// </summary>
    /// <remarks>
    /// Holding <paramref name="lease"/> is the whole of what lets a pass run at all — nothing here
    /// asks whether anybody else might be sending, because holding the lease is what already answers
    /// that. <see cref="RunnerLease.Root"/> is the corpus this reads and writes.
    /// </remarks>
    /// <param name="lease">The corpus's runner lease, already held.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="send">What actually reaches the transcription provider, for each job this pass takes.</param>
    /// <param name="stopping">
    /// Cancels the call in flight. A pass a caller cancelled leaves that one job exactly where the
    /// call left it — the next holder of this corpus's lease is what settles it.
    /// </param>
    /// <param name="summarise">
    /// What reaches the summary provider, or <c>null</c> to leave every due <see cref="JobKind.Extract"/>
    /// job queued.
    /// </param>
    public static Task<JobsRun> RunWhatIsDueAsync(
        RunnerLease lease,
        TimeProvider clock,
        SendingToTheProvider send,
        CancellationToken stopping = default,
        ISummaryProvider? summarise = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(send);

        return RunAsync(lease, clock, send, stopping, summarise, transcriptions: true, inFlight: null);
    }

    /// <summary>
    /// What <see cref="RunWhatIsDueAsync"/> and the pump's two lanes share: every due job of the
    /// kinds asked for, one at a time and each kind ordered by when it was queued.
    /// </summary>
    /// <remarks>
    /// Summaries are read only when <paramref name="summarise"/> is given. <paramref name="inFlight"/>,
    /// when given, is told which summary job is about to be taken, before it is taken, and is cleared
    /// once that job is settled — the order <see cref="JobsARestartFound.Holding(RunnerLease, Func{Guid?})"/>
    /// depends on.
    /// </remarks>
    private static async Task<JobsRun> RunAsync(
        RunnerLease lease,
        TimeProvider clock,
        SendingToTheProvider send,
        CancellationToken stopping,
        ISummaryProvider? summarise,
        bool transcriptions,
        SummaryInFlight? inFlight)
    {
        var root = lease.Root;
        var ran = new List<Guid>();
        var left = new List<string>();

        try
        {
            var now = UtcTimestamp.From(clock.GetUtcNow());

            List<ProcessingJob> due;
            using (var reading = CorpusDatabase.Open(root))
            {
                due = transcriptions ? DueOfKind(reading, JobKind.Transcribe, now) : [];

                if (summarise is not null)
                {
                    due.AddRange(DueOfKind(reading, JobKind.Extract, now));
                }
            }

            foreach (var candidate in due)
            {
                if (stopping.IsCancellationRequested)
                {
                    break;
                }

                var startedAt = UtcTimestamp.From(clock.GetUtcNow());

                if (candidate.Kind == JobKind.Extract)
                {
                    inFlight?.Name(candidate.Id);
                }

                TakenJob? taken;

                try
                {
                    taken = Take(root, candidate.Id, startedAt, candidate.Kind);
                }
                catch
                {
                    // A name that outlived a take that never happened would keep the sweep from
                    // ever looking at this job again.
                    inFlight?.Name(null);
                    throw;
                }

                if (taken is not { } job)
                {
                    inFlight?.Name(null);
                    // Taken, moved or gone between the read above and here — another pass over this
                    // same corpus cannot happen while this one holds the lease, so what did this is
                    // a person, or a test standing in for one. Either way it is not this pass's to
                    // report: the job is exactly where whoever moved it left it.
                    continue;
                }

                ran.Add(candidate.Id);

                if (candidate.Kind == JobKind.Transcribe)
                {
                    var ended = await Called(
                            () => TranscribingAMeeting.TranscribeAsync(root, candidate.Id, send, clock, stopping),
                            stopping)
                        .ConfigureAwait(false);

                    left.AddRange(Settle(
                        root, candidate.Id, job.MeetingId, job.Attempt, ended, UtcTimestamp.From(clock.GetUtcNow())));
                }
                else
                {
                    try
                    {
                        left.AddRange(await RunSummaryAsync(
                                root, candidate.Id, job.Attempt, summarise!, clock, stopping)
                            .ConfigureAwait(false));
                    }
                    finally
                    {
                        inFlight?.Name(null);
                    }
                }
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
    /// <paramref name="lease"/>'s corpus. Only ever a transcription: it is the one job a person asks
    /// to be tried again from a prompt over a charge, and a pass over the rest of the queue is not
    /// what answering that prompt asks for.
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

        var taken = Take(root, jobId, startedAt, JobKind.Transcribe)
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

    /// <summary>The one summary job a pump's lane is running, for the restart sweep to leave alone.</summary>
    private sealed class SummaryInFlight
    {
        private readonly Lock _gate = new();
        private Guid? _job;

        public Guid? Job
        {
            get
            {
                lock (_gate)
                {
                    return _job;
                }
            }
        }

        public void Name(Guid? job)
        {
            lock (_gate)
            {
                _job = job;
            }
        }
    }

    /// <summary>What one job taken to be sent was, before the call it is taken for.</summary>
    private readonly record struct TakenJob(Guid MeetingId, int Attempt);

    /// <summary>
    /// Every due job of <paramref name="kind"/>, ordered by <c>CreatedAt</c>. The kind and the two
    /// states <see cref="ProcessingJob.IsDue"/> would otherwise accept are both narrowed here, in
    /// SQL — everything <c>IsQueued()</c> means, translated by hand because the extension method
    /// itself is not. What is not translatable is the <c>NextAttemptAt</c> comparison
    /// <c>IsDue</c> also makes, so that half still runs over the narrowed rows in memory: without
    /// the states narrowed here too, this would pull every job of this kind this corpus has ever
    /// finished into memory on every single look, forever.
    /// </summary>
    private static List<ProcessingJob> DueOfKind(CorpusDbContext reading, JobKind kind, UtcTimestamp now) =>
        [.. reading.ProcessingJobs
            .Where(job => job.Kind == kind
                && (job.State == JobState.Pending || job.State == JobState.FailedRetryable))
            .OrderBy(job => job.CreatedAt)
            .ToList()
            .Where(job => job.IsDue(now))];

    /// <summary>
    /// Moves <paramref name="jobId"/> to <see cref="JobState.Running"/> in its own transaction, or
    /// answers nothing when it is not a <paramref name="kind"/> job due at <paramref name="startedAt"/>.
    /// </summary>
    private static TakenJob? Take(DirectoryInfo root, Guid jobId, UtcTimestamp startedAt, JobKind kind)
    {
        using var taking = CorpusDatabase.Open(root);
        using var transaction = taking.Database.BeginTransaction();

        var job = taking.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);
        if (job is null || job.Kind != kind || !job.IsDue(startedAt))
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
    /// <remarks>
    /// <para>
    /// <b>A filed transcription queues the summary right after the job's own move commits</b> when
    /// the corpus says <see cref="AfterARecording.TranscribeAndSummarise"/>, through
    /// <see cref="MeetingWork.TakeIfItIsOffered"/>. The setting is read here, when the transcription
    /// settles, so a change made between the stop and this moment applies.
    /// </para>
    /// <para>
    /// A transaction of its own and a <c>catch</c> of its own, after the move is committed: the
    /// transcription was paid for and is filed, and nothing a summary's row does may take its move
    /// back. A summary that could not be queued is a line in the result, and the meeting offers
    /// <em>Resumir</em> on its own row as it would have. A meeting that does not offer one — asked
    /// for already, or ignored — answers nothing, which is not a failure and not a line. Only
    /// <see cref="TranscriptionOutcome.Filed"/> queues one: a meeting already transcribed was
    /// settled by whoever transcribed it.
    /// </para>
    /// </remarks>
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
        var moved = false;

        try
        {
            writing.SaveChanges();
            moved = true;
        }
        catch (Exception refused) when (refused is not OutOfMemoryException)
        {
            left.Add($"{meetingId}: the job's own move could not be written: {refused.Message}");
        }

        if (moved && ended.Outcome == TranscriptionOutcome.Filed)
        {
            try
            {
                if (new CorpusSettings(writing).WhenARecordingEnds() == AfterARecording.TranscribeAndSummarise)
                {
                    _ = new MeetingWork(writing, now).TakeIfItIsOffered(meetingId, JobKind.Extract);
                }
            }
            catch (Exception refused) when (refused is not OutOfMemoryException)
            {
                left.Add($"{meetingId}: the summary could not be queued: {refused.Message}");
            }
        }

        if (ended.Said is not null)
        {
            left.Add($"{meetingId}: {ended.Said}");
        }

        return left;
    }

    /// <summary>
    /// Runs one <see cref="JobKind.Extract"/> job's attempt, watching it for a stop from the screen
    /// while it is in flight, and turns what it came to into the job's own move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The watch, and the token it may cancel.</b> <paramref name="stopping"/> — the pump's own
    /// token — is linked into a second one, and that second one is what <see cref="SummarisingAMeeting.SummariseAsync"/>
    /// is actually given. A background loop reads <paramref name="jobId"/>'s row every
    /// <see cref="HowOftenARunningSummaryIsWatched"/>; the moment it is no longer <c>Running</c> at
    /// <paramref name="attempt"/> — <see cref="MeetingTranscriber.Infrastructure.Meetings.MeetingWork.StopTheSummary"/>
    /// already moved it, from this process or another — the loop cancels the linked token, which is
    /// what makes the adapter kill the process tree. The same token ends the watch, once the call
    /// itself is over: the <c>finally</c> below cancels it either way, and the watcher's own loop
    /// stops on exactly that cancellation, so one token is what both the call and the watch answer
    /// to.
    /// </para>
    /// <para>
    /// <b>Why watch at all, rather than re-read once the call returns.</b> A transcription's own
    /// call is seconds long, so <see cref="Settle"/> only ever has to ask "did somebody move this
    /// job while the call was out" once the call is already over — the gap a stop could land in is
    /// too short to matter. A summary can run for minutes, and <em>Detener</em> is a press somebody
    /// expects to act on promptly, not once the very call it is stopping happens to finish on its
    /// own. Watching while the call is in flight is what buys that: the alternative would leave a
    /// press with nothing to interrupt until the run it was asked to stop had already ended.
    /// </para>
    /// <para>
    /// <b>Which cancellation is which.</b> A call that ends because <paramref name="stopping"/>
    /// itself was cancelled is the one thing a pass lets out — rethrown, exactly as a transcription's
    /// own <see cref="Called"/> rethrows it. A call that ends because only the linked token was
    /// cancelled is a stop somebody already wrote to the corpus: <see cref="SettleSummary"/> is never
    /// reached, because there is nothing left for this pass to move or to say.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<string>> RunSummaryAsync(
        DirectoryInfo root,
        Guid jobId,
        int attempt,
        ISummaryProvider summarise,
        TimeProvider clock,
        CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var watcher = WatchTheRunningSummary(root, jobId, attempt, clock, linked);

        try
        {
            SummaryEnded ended;

            try
            {
                ended = await SummarisingAMeeting.SummariseAsync(root, jobId, summarise, clock, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return [];
            }

            return SettleSummary(root, jobId, attempt, ended, clock);
        }
        finally
        {
            // Ends the watch whether the call above answered, was refused, or is what the watch
            // itself just cancelled — a no-op in that last case. The watcher never throws anything
            // but its own housekeeping already swallows (see its own remarks), so nothing here
            // needs a second guard around awaiting it.
            await linked.CancelAsync().ConfigureAwait(false);
            await watcher.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads <paramref name="jobId"/>'s row every <see cref="HowOftenARunningSummaryIsWatched"/>,
    /// on a context of its own, and cancels <paramref name="linked"/> the moment it is no longer
    /// <c>Running</c> at <paramref name="attempt"/>.
    /// </summary>
    private static async Task WatchTheRunningSummary(
        DirectoryInfo root,
        Guid jobId,
        int attempt,
        TimeProvider clock,
        CancellationTokenSource linked)
    {
        try
        {
            while (true)
            {
                await Task.Delay(HowOftenARunningSummaryIsWatched, clock, linked.Token).ConfigureAwait(false);

                using var reading = CorpusDatabase.Open(root);
                var job = reading.ProcessingJobs.AsNoTracking().FirstOrDefault(row => row.Id == jobId);

                if (job is null || job.State != JobState.Running || job.Attempt != attempt)
                {
                    await linked.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The call this was watching ended on its own before the next look, or
            // RunSummaryAsync's own finally cancelled the token once the call was over — either way
            // there is nothing left to watch.
        }
        catch (Exception thrown) when (thrown is not OutOfMemoryException)
        {
            // A poll's own housekeeping failing — a locked database, a transient SQLite busy
            // timeout over a call that can run for minutes — is never let out. RunSummaryAsync
            // already has the real outcome once SummariseAsync itself returns or throws; a stray
            // failure in the loop watching it must never replace that with a line about the watch
            // instead of the summary.
            _ = thrown;
        }
    }

    /// <summary>
    /// Turns what a summary call came to into the job's own move, guarded on the attempt that made
    /// the call the same way <see cref="Settle"/> guards a transcription's.
    /// </summary>
    /// <remarks>
    /// A filed summary has nothing left to move — the door already moved the job, accepted or
    /// refused for good — but it may have something to say about the meeting: when the accepted
    /// document carries a title and nobody has named the meeting, <see cref="NameTheMeeting"/> names
    /// it. That is here and not in the intake because the intake files what was said and never
    /// decides what a meeting is called.
    /// </remarks>
    private static IReadOnlyList<string> SettleSummary(
        DirectoryInfo root, Guid jobId, int attempt, SummaryEnded ended, TimeProvider clock)
    {
        if (ended.Outcome == SummaryOutcome.Filed)
        {
            return NameTheMeeting(root, jobId, clock);
        }

        var now = UtcTimestamp.From(clock.GetUtcNow());

        using var writing = CorpusDatabase.Open(root);
        var fresh = writing.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);

        if (fresh is null || fresh.State != JobState.Running || fresh.Attempt != attempt)
        {
            return
            [
                $"{jobId}: the job was {fresh?.State} at attempt {fresh?.Attempt} by the time its "
                + $"call ended, so it was left as it stood: {ended.Said}",
            ];
        }

        ApplySummary(fresh, ended, attempt, now);
        var left = new List<string>();

        try
        {
            writing.SaveChanges();
        }
        catch (Exception refused) when (refused is not OutOfMemoryException)
        {
            left.Add($"{jobId}: the job's own move could not be written: {refused.Message}");
        }

        if (ended.Said is not null)
        {
            left.Add($"{jobId}: {ended.Said}");
        }

        return left;
    }

    /// <summary>
    /// Names the meeting what the summary this job had accepted calls it, when nobody has named the
    /// meeting. A failure is a line and never touches the summary, which is already filed.
    /// </summary>
    private static IReadOnlyList<string> NameTheMeeting(DirectoryInfo root, Guid jobId, TimeProvider clock)
    {
        try
        {
            using var context = CorpusDatabase.Open(root);

            var accepted = context.ExtractionRuns
                .AsNoTracking()
                .Where(run => run.JobId == jobId && run.AcceptedAt != null && run.OutputArtifactId != null)
                .Select(run => new { run.MeetingId, run.OutputArtifactId })
                .FirstOrDefault();

            if (accepted is null)
            {
                return [];
            }

            var kept = context.Artifacts
                .AsNoTracking()
                .Where(artifact => artifact.Id == accepted.OutputArtifactId)
                .Select(artifact => artifact.RelativePath)
                .First();

            var title = ExtractionReader.Read(File.ReadAllBytes(CorpusFiles.Locate(root, kept).FullName))
                .Document?.Title;

            if (title is not null)
            {
                _ = new MeetingReading(context, clock).NameIfNobodyHas(accepted.MeetingId, title);
            }

            return [];
        }
        catch (Exception refused) when (refused is not OutOfMemoryException)
        {
            return [$"{jobId}: the meeting could not be named from its summary: {refused.Message}"];
        }
    }

    /// <summary>
    /// Turns what a summary call came to into the one move it decides for <paramref name="job"/>,
    /// which is still <see cref="JobState.Running"/> at the attempt that made the call.
    /// </summary>
    private static void ApplySummary(ProcessingJob job, SummaryEnded ended, int attempt, UtcTimestamp now)
    {
        switch (ended.Outcome)
        {
            case SummaryOutcome.DidNotAnswer when attempt < TimesASummaryIsTried:
                job.FailRetryable(
                    ended.Said ?? "The summariser did not answer.",
                    now + WaitBeforeTryingASummaryAgain(attempt));
                break;

            case SummaryOutcome.DidNotAnswer:
                job.FailPermanently(
                    JobFailure.SummariserFailed,
                    ended.Said ?? "The summariser did not answer, even after being tried again.",
                    now);
                break;

            case SummaryOutcome.NotSent:
                // ended.Failure!.Value throws InvalidOperationException on a missing kind rather
                // than guessing one — a defect one level up, in whichever site answered NotSent
                // with no failure attached.
                job.FailPermanently(ended.Failure!.Value, ended.Said!, now);
                break;

            default:
                throw new InvalidOperationException($"Unknown summary outcome '{ended.Outcome}'.");
        }
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
    /// then never again. That is deliberate and not a missed optimisation: a <c>Running</c> row under
    /// a lease this pump already holds is, apart from the one summary its lane is sending, a write
    /// this same runner made and could not save — the job is exactly as stuck as one a dead process
    /// left, and the next look's recovery is what moves it to a person rather than leaving it to
    /// outlive the pump that orphaned it. The summary in flight is named to the sweep and left
    /// alone. A recovery that fails on its own — a busy lock, a corpus mid-write from somewhere
    /// else — costs one skipped pass and is retried at the next look rather than skipped for good.
    /// </para>
    /// <para>
    /// <b>A transcription never waits behind a summary.</b> Each look sends the due transcriptions
    /// and awaits them, and then, when <paramref name="summarise"/> is given and no summary lane is
    /// running, starts one lane over the due summaries and does not await it. A lane works through
    /// its summaries one at a time and ends when none is due; the next look starts another. Stopping
    /// the pump waits for the lane's call to end or be cancelled, and then lets the lease go.
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
    /// <param name="send">What actually reaches the transcription provider.</param>
    /// <param name="howOften">How long a look waits before the next one.</param>
    /// <param name="stopping">Ends the pump. Nothing about a call already in flight is rushed by it.</param>
    /// <param name="summarise">
    /// What reaches the summary provider, or <c>null</c> to leave every due
    /// <see cref="JobKind.Extract"/> job queued.
    /// </param>
    public static async Task PumpAsync(
        DirectoryInfo root,
        TimeProvider clock,
        SendingToTheProvider send,
        TimeSpan howOften,
        CancellationToken stopping,
        ISummaryProvider? summarise = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(send);

        RunnerLease? lease = null;
        var inFlight = new SummaryInFlight();
        Task? lane = null;

        try
        {
            while (true)
            {
                try
                {
                    if (CorpusDatabase.HoldsACorpus(root))
                    {
                        lease ??= RunnerLease.TryTake(root);

                        if (lease is not null && JobsARestartFound.Holding(lease, () => inFlight.Job).Left.Count == 0)
                        {
                            _ = await RunAsync(lease, clock, send, stopping, null, transcriptions: true, inFlight: null)
                                .ConfigureAwait(false);

                            if (summarise is not null && lane is not { IsCompleted: false })
                            {
                                var held = lease;
                                lane = Task.Run(
                                    () => RunAsync(held, clock, send, stopping, summarise, transcriptions: false, inFlight),
                                    CancellationToken.None);
                            }
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
            // send — a call already in flight is TranscribingAMeeting's or SummarisingAMeeting's own
            // to have left in a state the next lease holder can settle.
        }
        finally
        {
            if (lane is not null)
            {
                try
                {
                    await lane.ConfigureAwait(false);
                }
                catch (Exception thrown) when (thrown is not OutOfMemoryException)
                {
                    // A lane cancelled by the stop, or one that failed on its own, has nothing left
                    // to report: its job is where its call left it, for the next lease holder.
                    _ = thrown;
                }
            }

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
                // ended.Failure!.Value throws InvalidOperationException on a missing kind rather
                // than guessing one — a defect one level up, in whichever site answered
                // NothingWasCharged with no kind attached.
                job.FailPermanently(ended.Failure!.Value, ended.Said!, now);
                break;

            case TranscriptionOutcome.MayHaveBeenCharged:
                job.AwaitUser(ended.Said!);
                break;

            default:
                throw new InvalidOperationException($"Unknown transcription outcome '{ended.Outcome}'.");
        }
    }
}
