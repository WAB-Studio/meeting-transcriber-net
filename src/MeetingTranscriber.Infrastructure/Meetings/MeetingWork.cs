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
public sealed record MeetingAndWork(Meeting Meeting, OwedWork Owed);

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
/// The writing half is two methods and they are not mirrors. Taking a stage queues its job.
/// Leaving it records that it was turned down — and cancels whatever was queued for it, because
/// work nobody has run is work nobody has paid for, and the press that spends money should not be
/// the one with no way back. Neither moves the meeting: a stage that was left is the same stage,
/// still offering the same action, which is what makes ignoring safe to press.
/// </para>
/// <para>
/// Both re-read the meeting before they write. A screen that has been open a while is a screen
/// showing what was true when it was drawn, and the press that matters most — the one that spends
/// money — is exactly the one a stale screen would get wrong.
/// </para>
/// </remarks>
public sealed class MeetingWork(CorpusDbContext context, TimeProvider clock)
{
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
    /// answers, so such a meeting comes back carrying no action at all. What it does carry is a
    /// charge that may already have happened and nobody has settled — `status` counts those, and
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
            // long as the window stayed open.
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
    /// The job is queued and nothing starts it, which is the whole of what this version promises:
    /// every stage that spends money or quota waits to be told, and being told is a row, not a
    /// provider call. What runs it is the runner's, and the state it is left in — pending, due
    /// immediately — is what the runner reads.
    /// </remarks>
    /// <exception cref="MeetingStageException">
    /// This meeting's stage has no action, or its standing is one where taking it would do harm.
    /// </exception>
    public ProcessingJob Take(Guid meetingId) => Answer(meetingId, decline: false);

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
    /// The two answers, which are one read followed by one write and have to stay that way.
    /// </summary>
    /// <remarks>
    /// In one transaction because the read decides the write. Without it, two windows over one
    /// corpus can both see a stage nobody has answered and both queue the work, which is two
    /// charges for one meeting the day something runs them. SQLite serialises writers, so the
    /// second of the two waits and then either fails or finds what the first left.
    /// </remarks>
    private ProcessingJob Answer(Guid meetingId, bool decline)
    {
        using var write = context.Database.BeginTransaction();

        var owed = On(meetingId);
        var allowed = decline ? owed.MayBeLeft : owed.MayBeTaken;

        if (!allowed || owed.Next is not { } kind)
        {
            throw new MeetingStageException(
                $"Meeting {meetingId} is {owed.Stage} and {owed.Standing}, which offers nothing to "
                + (decline ? "leave" : "take") + ".");
        }

        var job = decline ? Left(meetingId, kind) : Taken(meetingId, kind);
        context.SaveChanges();
        write.Commit();

        return job;
    }

    /// <summary>Queues the stage's work, and starts nothing.</summary>
    private ProcessingJob Taken(Guid meetingId, JobKind kind)
    {
        var job = ProcessingJob.Queue(Guid.NewGuid(), meetingId, kind, NextKey(meetingId, kind), Now);
        context.ProcessingJobs.Add(job);
        return job;
    }

    /// <summary>Records that the stage was turned down, in whichever of the three ways applies.</summary>
    private ProcessingJob Left(Guid meetingId, JobKind kind)
    {
        var now = Now;
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
}
