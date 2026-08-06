using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Jobs;

/// <summary>
/// One unit of queued work against a meeting, and the only thing allowed to move it between
/// states.
/// </summary>
/// <remarks>
/// Everything that changes with the state changes with it here: the attempt count, the schedule
/// and the error travel with the move rather than being written next to it. That is why the
/// setters are private — a caller that could assign <c>State</c> could leave a job running with
/// a retry already scheduled, and no CHECK constraint would notice.
/// </remarks>
public class ProcessingJob
{
    private ProcessingJob(string idempotencyKey) => IdempotencyKey = idempotencyKey;

    public Guid Id { get; private set; }

    public Guid MeetingId { get; private set; }

    public JobKind Kind { get; private set; }

    public JobState State { get; private set; } = JobState.Pending;

    /// <summary>How many times this job has been started. Zero until it first runs.</summary>
    public int Attempt { get; private set; }

    /// <summary>Unique within a kind, which is what stops the same work being queued twice.</summary>
    public string IdempotencyKey { get; private set; }

    public UtcTimestamp CreatedAt { get; private set; }

    /// <summary>When the latest attempt began.</summary>
    public UtcTimestamp? StartedAt { get; private set; }

    /// <summary>When the job reached a state it never leaves. Null while it can still move.</summary>
    public UtcTimestamp? FinishedAt { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>
    /// The earliest the runner may start this job. Null means as soon as it is seen, and it is
    /// cleared by every move that takes the job out of the queue so nothing is left scheduled.
    /// </summary>
    public UtcTimestamp? NextAttemptAt { get; private set; }

    /// <summary>Queues a new job. It is pending, has never run, and is due immediately.</summary>
    public static ProcessingJob Queue(
        Guid id,
        Guid meetingId,
        JobKind kind,
        string idempotencyKey,
        UtcTimestamp createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return new ProcessingJob(idempotencyKey)
        {
            Id = id,
            MeetingId = meetingId,
            Kind = kind,
            State = JobState.Pending,
            CreatedAt = createdAt,
        };
    }

    /// <summary>True when the runner may start this job on its own at <paramref name="now"/>.</summary>
    public bool IsDue(UtcTimestamp now) =>
        State.IsQueued() && (NextAttemptAt is not { } next || next <= now);

    /// <summary>Takes the job for an attempt.</summary>
    public void Start(UtcTimestamp now)
    {
        if (State.IsQueued() && !IsDue(now))
        {
            throw new JobTransitionException(
                $"A {Kind} job in '{State}' is not due until {NextAttemptAt}, and it is {now}.");
        }

        MoveTo(JobState.Running);
        Attempt++;
        StartedAt = now;
        FinishedAt = null;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>The work landed.</summary>
    public void Succeed(UtcTimestamp now)
    {
        MoveTo(JobState.Succeeded);
        FinishedAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>
    /// The attempt failed in a way that repeating may fix. The runner picks the job up again by
    /// itself once <paramref name="notBefore"/> arrives.
    /// </summary>
    public void FailRetryable(string error, UtcTimestamp notBefore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        MoveTo(JobState.FailedRetryable);
        LastError = error;
        NextAttemptAt = notBefore;
        FinishedAt = null;
    }

    /// <summary>The attempt failed in a way no retry fixes.</summary>
    public void FailPermanently(string error, UtcTimestamp now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        MoveTo(JobState.FailedPermanent);
        LastError = error;
        FinishedAt = now;
        NextAttemptAt = null;
    }

    /// <summary>Dropped before it could finish. Nothing is retried and nothing is owed.</summary>
    public void Cancel(UtcTimestamp now)
    {
        MoveTo(JobState.Cancelled);
        FinishedAt = now;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Hands the job to a person and takes it out of the queue. Used for anything the app must
    /// not decide alone: a cost to approve, or an attempt whose outcome it cannot establish.
    /// </summary>
    public void AwaitUser(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        MoveTo(JobState.AwaitingUser);
        LastError = reason;
        NextAttemptAt = null;
    }

    /// <summary>Puts a job a person released back into the queue.</summary>
    public void Requeue(UtcTimestamp? notBefore = null)
    {
        MoveTo(JobState.Pending);
        NextAttemptAt = notBefore;
    }

    /// <summary>
    /// Settles what a restart found. A job still marked running was cut off mid attempt, and
    /// with no backend there is no way to ask whether the work — or the charge — went through,
    /// so it stops on a person instead of being retried.
    /// </summary>
    /// <returns>True when this job was the interrupted kind and moved.</returns>
    public bool RecoverAfterRestart()
    {
        if (State is not JobState.Running)
        {
            return false;
        }

        AwaitUser($"A restart found this {Kind} job still running; whether attempt {Attempt} landed is unknown.");
        return true;
    }

    private void MoveTo(JobState next)
    {
        State.EnsureCanMoveTo(next);
        State = next;
    }
}
