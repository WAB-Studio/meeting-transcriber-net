using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>One meeting and what the application still owes it.</summary>
/// <param name="Meeting">The row, which is what a person reads the meeting by.</param>
/// <param name="Owed">The stage it is at and where that stands.</param>
public sealed record MeetingAndWork(Meeting Meeting, OwedWork Owed)
{
    /// <summary>
    /// Whether <em>Reintentar</em> belongs on this meeting's row. A meeting on its way out is
    /// owed nothing whatever stage it reached, so it is never offered even when something on it
    /// is stopped on a person; and the only thing a person can settle is a stop — nothing else on
    /// this record is trying it again's to answer.
    /// </summary>
    public bool MayBeTriedAgain => Meeting.LifecycleState is LifecycleState.Active && Owed.WaitsOnSomebody;
}

/// <summary>
/// What the application owes every meeting in a corpus, and the two answers a person can give
/// about it: take the stage, or leave it.
/// </summary>
/// <remarks>
/// <para>
/// The reading half stores nothing. It loads what the corpus already holds — which files each
/// meeting has, which jobs it carries — and hands both to <see cref="OwedWork"/>, which is where
/// the rule lives. Ask it again after the application was closed and reopened and every meeting
/// comes back at the same stage waiting for the same thing, because that is not something being
/// remembered: it is being worked out from rows and files that never went anywhere.
/// </para>
/// <para>
/// The writing half is four methods and none of them are mirrors. Taking a stage queues its job.
/// Leaving it records that it was turned down — and cancels whatever was queued for it, because
/// work nobody has run is work nobody has paid for, and the press that spends money should not be
/// the one with no way back. Neither moves the meeting: a stage that was left is the same stage,
/// still offering the same action, which is what makes ignoring safe to press. Trying again is the
/// one answer this type gives a meeting stopped on a person, and it goes through
/// <see cref="ProcessingJob.Requeue"/> — the one move <c>arquitectura.md</c> §5.4 lets a person
/// make on a job that already ran. A stop whose paid response the corpus kept has a second one, at
/// a prompt: <c>MeetingIntake.ReceiveWhatWasRefused</c> files it and settles the job without
/// sending anything. The fourth queues a transcription of a meeting that already has one, because
/// somebody typed its minutes back at a prompt: it starts nothing, exactly as
/// <see cref="Taken"/> does for the other three, and <c>JobRunner.SendAgainAsync</c> is what sends
/// it.
/// </para>
/// <para>
/// All four re-read the meeting before they write. A screen that has been open a while is a
/// screen showing what was true when it was drawn, and the press that matters most — the one that
/// spends money — is exactly the one a stale screen would get wrong.
/// </para>
/// </remarks>
public sealed class MeetingWork(CorpusDbContext context, TimeProvider clock)
{
    /// <summary>
    /// One instant that never advances, for a caller holding the instant rather than a clock: a
    /// stop, where the job a recording's end creates is created at the moment that recording
    /// ended. The same seam <c>HumanLayer</c> offers a render, and here for the same reason — a
    /// caller that already knows the instant should not be able to hand this type a live clock and
    /// hope it goes unread.
    /// </summary>
    public MeetingWork(CorpusDbContext context, UtcTimestamp at)
        : this(context, new Frozen(at))
    {
    }

    private UtcTimestamp Now => UtcTimestamp.From(clock.GetUtcNow());

    /// <summary>
    /// Every meeting the corpus is holding and what is owed on it, newest meeting first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is left out is a meeting on its way out that nothing is stopped on a person about: the
    /// application owes nothing to a meeting somebody asked it to get rid of, and offering to pay
    /// for one would be the worst possible time to be asked. Keeping the ones that are stopped
    /// costs that nothing, because <see cref="StageStanding.StoppedOnAPerson"/> refuses both
    /// answers, so such a meeting comes back carrying no action at all — including trying again,
    /// which is offered only on a meeting that is here. What it does carry is a charge that may
    /// already have happened and nobody has settled — `status` counts those, and
    /// this is the only place that says which meeting — so dropping it would make the deletion the
    /// thing that hid the charge.
    /// </para>
    /// <para>
    /// Which of the two a meeting is, is decided once and on the answer rather than on the rows
    /// underneath it: it is listed when it is active, or when what is owed on it waits on
    /// somebody. The database is asked a question of its own first — the rows
    /// <see cref="OwedWork.StopsOnAPerson"/> matches, which is the expression
    /// <see cref="OwedWork.Of"/> puts to the rows it is handed — but only to narrow what is read,
    /// because a row this list will not show is a row it should not read, nor read a meeting's
    /// files and jobs to find out.
    /// </para>
    /// <para>
    /// Narrowing rather than deciding is the whole of why that is safe. A query and a rule that
    /// each decide membership have to agree, and they would be agreeing about different things —
    /// one asks whether a row exists, the other is a conclusion drawn from every row and file a
    /// meeting has. Here the query owes one thing instead: not to miss a meeting the rule would
    /// keep. Fetching one the rule then drops costs a row and reaches nobody, and no press can
    /// ride in on it, because what the screen gets is what the rule said. The one obligation
    /// left holds because a standing of <see cref="StageStanding.StoppedOnAPerson"/> is that
    /// expression matching one of the meeting's rows and nothing else — `MeetingStageTests` pins
    /// that over every state a job can be in and over a kind the stage does not offer.
    /// </para>
    /// <para>
    /// One thing whoever builds deletion inherits: `processing_jobs.meeting_id` cascades, so
    /// deleting the row takes the awaiting job with it and this list falls quiet again. Either the
    /// job outlives the meeting, or a meeting with an unsettled charge is not deletable yet.
    /// </para>
    /// <para>
    /// Three queries rather than one per meeting. The rows are small and the counts are a corpus's
    /// worth rather than a recording's, and only the files that decide a stage are read at all —
    /// a meeting's spool blocks are thousands of rows saying nothing about how far it has got.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MeetingAndWork> Listed()
    {
        var mightBeStopped = context.ProcessingJobs
            .Where(OwedWork.StopsOnAPerson)
            .Select(job => job.MeetingId);

        var meetings = context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active
                || mightBeStopped.Contains(meeting.Id))
            .OrderByDescending(meeting => meeting.StartedAt)

            // And the id under it, which settles nothing a person reads and everything about
            // whether this is the same list twice. Two meetings that started in the same
            // millisecond leave SQLite free to answer in either order, and what asks this question
            // over and over is `MeetingsWatch`: an order that moved between two looks would read as
            // the corpus having changed, and the list would rebuild every card of itself for as
            // long as the window stayed open. `MeetingWorkTests` pins it, over two corpora that
            // differ only in the order the rows were written.
            .ThenBy(meeting => meeting.Id)
            .ToList();

        var wanted = meetings.Select(meeting => meeting.Id).ToArray();
        var files = Files(wanted);
        var jobs = Jobs(wanted);

        return meetings
            .Select(meeting => new MeetingAndWork(
                meeting,
                OwedWork.Of(meeting.Id, files[meeting.Id], jobs[meeting.Id])))
            .Where(listed => listed.Meeting.LifecycleState is LifecycleState.Active
                || listed.Owed.WaitsOnSomebody)
            .ToList();
    }

    /// <summary>What is owed on one meeting.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public OwedWork On(Guid meetingId)
    {
        if (!context.Meetings.AsNoTracking().Any(meeting => meeting.Id == meetingId))
        {
            throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");
        }

        Guid[] one = [meetingId];
        return OwedWork.Of(meetingId, Files(one)[meetingId], Jobs(one)[meetingId]);
    }

    /// <summary>
    /// Somebody said yes to the meeting's next stage. Queues the work and hands back the job that
    /// will do it.
    /// </summary>
    /// <remarks>
    /// The job is queued here and started by nothing here: being told is a row, not a provider
    /// call. What sends it is <c>JobRunner</c>, which reads exactly the state this leaves —
    /// pending, due immediately — within one look at the queue, on this machine's key, with no
    /// price shown until the dialogue ISC-85 asks for exists.
    /// </remarks>
    /// <exception cref="MeetingStageException">
    /// This meeting's stage has no action, or its standing is one where taking it would do harm.
    /// </exception>
    public ProcessingJob Take(Guid meetingId) => Answer(meetingId, decline: false);

    /// <summary>
    /// Takes the meeting's next stage when that stage is <paramref name="kind"/> and the meeting is
    /// still offering it, and does nothing at all when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Take"/> with the question asked inside the transaction that answers it, which is
    /// the whole reason this exists rather than a caller reading <see cref="On"/> first. That
    /// caller's read and its write would be two transactions with a file write between them, and
    /// in that gap another window's press reaches the same stage — after which <see cref="Take"/>
    /// throws <see cref="MeetingStageException"/> at a caller that had just proved it would not.
    /// The one caller is a recording being finished, where such a throw arrives with the audio
    /// already committed and comes out of a handler that has no catch for it.
    /// </para>
    /// <para>
    /// Nothing, and not a refusal, because for that caller a stage somebody has already asked for
    /// is not a failure — it is the work being there. A recovery finish over a meeting whose
    /// transcription was queued the first time round has to come back with the meeting finished,
    /// and which of the two happened is said by the answer being the job or being null.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting.</param>
    /// <param name="kind">The stage the caller decided should be queued.</param>
    /// <returns>The job that was queued, or <c>null</c> when nothing was.</returns>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public ProcessingJob? TakeIfItIsOffered(Guid meetingId, JobKind kind)
    {
        // Joins a transaction the caller is already holding rather than refusing it, which is the
        // shape every other writer over this corpus spells: begin one only when the context is not
        // already in one, and commit only the one this method began. A caller holding one gets its
        // queueing inside it — a stop is the one caller today, and it is why the audio and the job
        // row can land or roll back together — and a caller holding none gets exactly what it had.
        using var write = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        var owed = On(meetingId);

        if (!owed.MayBeTaken || owed.Next != kind)
        {
            return null;
        }

        var job = Taken(meetingId, kind, Now);
        context.SaveChanges();
        write?.Commit();

        return job;
    }

    /// <summary>
    /// Somebody said no to the meeting's next stage, for now. Hands back the job that carries
    /// that answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meeting stays at the stage it was at and still offers the same action, so one ignored
    /// today can be transcribed next month; what changes is that the application stops counting
    /// the meeting among the things it is waiting on. A row rather than something held in the
    /// window, because an answer a person gave that a restart forgets is an application that asks
    /// again as though it had never been told.
    /// </para>
    /// <para>
    /// Which row depends on what is already there, and all three cases are the same answer. Work
    /// already asked for and not yet run is cancelled where it stands — that is what makes asking
    /// reversible, and it costs nothing to reverse because nothing has run. With nothing to
    /// cancel and an answer already on record, that answer comes back unchanged rather than
    /// becoming a second row: a person can press ignore all afternoon without growing the table.
    /// Otherwise it is a new job, queued and cancelled in the same breath.
    /// </para>
    /// </remarks>
    /// <exception cref="MeetingStageException">
    /// This meeting's stage has no action, or its standing is one nobody may answer for.
    /// </exception>
    public ProcessingJob Decline(Guid meetingId) => Answer(meetingId, decline: true);

    /// <summary>
    /// Somebody asked, on a meeting stopped on a person, to try the unsettled job again. Puts
    /// every one of them back in the queue and hands back the jobs that were moved.
    /// </summary>
    /// <remarks>
    /// The one move a person may make over a job that already ran (<c>arquitectura.md</c> §5.4):
    /// there is no way to ask the provider whether the earlier attempt landed, so what a press here
    /// buys is another attempt, not an answer about the first one. The runner sends it within one
    /// look at the queue.
    /// </remarks>
    /// <exception cref="MeetingStageException">
    /// This meeting is not here, or nothing on it is waiting on a person to try again.
    /// </exception>
    public IReadOnlyList<ProcessingJob> TryAgain(Guid meetingId)
    {
        using var write = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        var owed = On(meetingId);
        var meeting = context.Meetings.AsNoTracking().First(row => row.Id == meetingId);

        if (!new MeetingAndWork(meeting, owed).MayBeTriedAgain)
        {
            // Two different reasons share this refusal, and the message says which: a meeting
            // still here but with nothing waiting on a person is the ordinary case, and a
            // meeting on its way out is refused whatever its jobs say — `Listed`'s remarks give
            // that one its own reason.
            var why = meeting.LifecycleState is not LifecycleState.Active
                ? "it is on its way out"
                : "nothing on it is waiting on a person to try again";

            throw new MeetingStageException($"Meeting {meetingId} is {owed.Stage} and {owed.Standing}, and {why}.");
        }

        // Every job waiting on a person and not the stage's own kind alone: a charge that may
        // already have happened is the meeting's problem wherever in the meeting it happened,
        // which is `OwedWork.StopsOnAPerson`'s own rule and the reason this press exists at all.
        var jobs = context.ProcessingJobs
            .Where(job => job.MeetingId == meetingId)
            .Where(OwedWork.StopsOnAPerson)
            .ToList();

        jobs.ForEach(job => job.Requeue());
        context.SaveChanges();
        write?.Commit();

        return jobs;
    }

    /// <summary>
    /// The two answers, which are one read followed by one write and have to stay that way.
    /// </summary>
    /// <remarks>
    /// In one transaction because the read decides the write. Without it, two windows over one
    /// corpus can both see a stage nobody has answered and both queue the work, which is two
    /// charges for one meeting the day something runs them. SQLite serialises writers, so the
    /// second of the two waits and then either fails or finds what the first left.
    /// <para>
    /// It refuses rather than answering nothing, which is <see cref="TakeIfItIsOffered"/>'s whole
    /// difference: a person pressing a stage that has moved under them is owed a sentence, and a
    /// stop that finds the work already there is owed silence.
    /// </para>
    /// <para>
    /// Joins a transaction the caller already holds rather than refusing it, for the reason
    /// <see cref="TakeIfItIsOffered"/> gives. Both of this type's two callers today — a person
    /// pressing a button, and a caller reading the answer back — hold none of their own, so this is
    /// unobserved on either path; it is here so the two methods spell the one rule the same way.
    /// </para>
    /// </remarks>
    private ProcessingJob Answer(Guid meetingId, bool decline)
    {
        using var write = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        var owed = On(meetingId);
        var allowed = decline ? owed.MayBeLeft : owed.MayBeTaken;

        if (!allowed || owed.Next is not { } kind)
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} is {owed.Stage} and {owed.Standing}, which offers nothing to "
                + (decline ? "leave" : "take") + ".");
        }

        // Once, and read twice: a decline cancels rows and writes one, and two reads of the
        // clock a line apart would date them to two instants.
        var now = Now;
        var job = decline ? Left(meetingId, kind, now) : Taken(meetingId, kind, now);
        context.SaveChanges();
        write?.Commit();

        return job;
    }

    /// <summary>Queues the stage's work, and starts nothing.</summary>
    private ProcessingJob Taken(Guid meetingId, JobKind kind, UtcTimestamp now)
    {
        var job = ProcessingJob.Queue(Guid.NewGuid(), meetingId, kind, NextKey(meetingId, kind), now);
        context.ProcessingJobs.Add(job);
        return job;
    }

    /// <summary>Records that the stage was turned down, in whichever of the three ways applies.</summary>
    private ProcessingJob Left(Guid meetingId, JobKind kind, UtcTimestamp now)
    {
        var mine = context.ProcessingJobs
            .Where(job => job.MeetingId == meetingId && job.Kind == kind)
            .ToList();

        // Every one of them, not the first. Two rows of a kind still moving would leave one
        // running behind a card that says it was ignored.
        var live = mine.Where(job => !job.State.IsTerminal()).ToList();

        if (live.Count > 0)
        {
            live.ForEach(job => job.Cancel(now));
            return live[0];
        }

        if (mine.Find(job => job.State is JobState.Cancelled) is { } already)
        {
            return already;
        }

        var job = ProcessingJob.Queue(Guid.NewGuid(), meetingId, kind, NextKey(meetingId, kind), now);
        job.Cancel(now);
        context.ProcessingJobs.Add(job);
        return job;
    }

    /// <summary>
    /// Throws unless a meeting may be sent to the provider again. Read-only: it decides, and
    /// <see cref="TranscribeAgain"/> is the one caller that acts on the answer.
    /// </summary>
    /// <remarks>
    /// <c>TranscribingAMeeting</c> does not re-check the response-name rule below: a row that still
    /// slips past this and reaches the filing door is refused loudly there instead, in its own
    /// words.
    /// <para>
    /// The five below are checked in this order and stop on the first that holds. Whether the
    /// meeting is sound comes first — on its way out, never transcribed, or a response row this
    /// rule cannot place — because the sentences the last two throw presuppose a meeting worth
    /// reasoning about: "settle that one first" and "already has a transcription queued" both name
    /// a meeting whose stage is coherent, which the first three checks are what establish. A
    /// meeting stopped on a person is checked ahead of one with a Transcribe job still moving,
    /// because <see cref="OwedWork.WaitsOnSomebody"/> is meeting-wide — a charge that may already
    /// have happened is the meeting's problem wherever it happened — while the last check is scoped
    /// to this one stage's own queue.
    /// </para>
    /// </remarks>
    /// <exception cref="MeetingStageException">
    /// There is no such meeting; it is not <see cref="LifecycleState.Active"/>; it has never been
    /// transcribed; one of its responses is named outside <see cref="ResponseVersions"/>' series;
    /// something on it is stopped waiting for a person; or it already has a <see cref="JobKind.Transcribe"/>
    /// job that has not reached a terminal state.
    /// </exception>
    public void EnsureMayBeTranscribedAgain(Guid meetingId)
    {
        var owed = On(meetingId);
        var meeting = context.Meetings.AsNoTracking().First(row => row.Id == meetingId);

        if (meeting.LifecycleState is not LifecycleState.Active)
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} is on its way out, and nothing is bought for a meeting "
                + "somebody asked to get rid of.");
        }

        var responses = context.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.MeetingId == meetingId && artifact.Kind == ArtifactKind.DeepgramResponse)
            .ToList();

        if (responses.Count == 0)
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} has not been transcribed yet, so there is nothing to "
                + "transcribe again. Its first transcription is the press on its row.");
        }

        foreach (var response in responses)
        {
            if (ResponseVersions.VersionOf(response) is null)
            {
                throw new MeetingStageException(
                    $"Meeting {meetingId} names '{response.RelativePath}' as a response and that "
                    + "is not a name in the series, so where another would go cannot be settled.");
            }
        }

        if (owed.WaitsOnSomebody)
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} has a charge nobody has settled — something on it is "
                + "stopped waiting for a person — and another is not made on top of it. Settle "
                + "that one first.");
        }

        // `IsTerminal` reads `JobState.Next()`, which nothing here can translate to SQL — narrowed
        // to this meeting's own Transcribe jobs first, which are few, and checked in memory after.
        var transcriptions = context.ProcessingJobs
            .AsNoTracking()
            .Where(job => job.MeetingId == meetingId && job.Kind == JobKind.Transcribe)
            .ToList();

        if (transcriptions.Any(job => !job.State.IsTerminal()))
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} already has a transcription queued or under way.");
        }
    }

    /// <summary>
    /// Somebody typed a meeting's minutes back at a prompt: queues another transcription of it and
    /// hands back the job that will send it.
    /// </summary>
    /// <remarks>
    /// The job is queued here and started by nothing here, exactly as <see cref="Taken"/> leaves
    /// every other stage's job — <c>JobRunner.SendAgainAsync</c> is what sends it, on the corpus's
    /// own runner lease, once the caller has already taken it.
    /// </remarks>
    /// <exception cref="MeetingStageException">See <see cref="EnsureMayBeTranscribedAgain"/>.</exception>
    public ProcessingJob TranscribeAgain(Guid meetingId)
    {
        using var write = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        EnsureMayBeTranscribedAgain(meetingId);

        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meetingId, JobKind.Transcribe, NextKey(meetingId, JobKind.Transcribe), Now);
        context.ProcessingJobs.Add(job);
        context.SaveChanges();
        write?.Commit();

        return job;
    }

    /// <summary>
    /// A key no job of this kind has used, and readable enough to tell what it is about.
    /// </summary>
    /// <remarks>
    /// It counts because a stage can be answered more than once: declined in March, taken in
    /// April, and both are jobs of the same kind against the same meeting. The count is read
    /// inside the transaction that writes, which is what makes it a count of what is really there;
    /// the unique index over kind and key is the backstop for a writer that got round that.
    /// </remarks>
    private string NextKey(Guid meetingId, JobKind kind)
    {
        var already = context.ProcessingJobs.Count(job => job.MeetingId == meetingId && job.Kind == kind);
        return $"{meetingId}/{already + 1}";
    }

    private ILookup<Guid, ArtifactKind> Files(Guid[] meetings)
    {
        var milestones = MeetingStages.Milestones.ToArray();

        return context.Artifacts
            .AsNoTracking()
            .Where(artifact => meetings.Contains(artifact.MeetingId) && milestones.Contains(artifact.Kind))
            .Select(artifact => new { artifact.MeetingId, artifact.Kind })
            .ToList()
            .ToLookup(artifact => artifact.MeetingId, artifact => artifact.Kind);
    }

    private ILookup<Guid, ProcessingJob> Jobs(Guid[] meetings) => context.ProcessingJobs
        .AsNoTracking()
        .Where(job => meetings.Contains(job.MeetingId))
        .ToList()
        .ToLookup(job => job.MeetingId);

    /// <summary>
    /// A clock that answers one instant for ever. Four lines rather than a type shared with
    /// <c>HumanLayer</c>, which has the same four: what would be shared is a
    /// <see cref="TimeProvider"/> with one overridden method, and a project-wide name for that
    /// costs every reader more than the copy costs either file.
    /// </summary>
    private sealed class Frozen(UtcTimestamp at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at.Value;
    }
}
