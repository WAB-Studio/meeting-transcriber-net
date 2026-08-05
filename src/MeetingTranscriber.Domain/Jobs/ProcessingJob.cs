using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Jobs;

/// <summary>One unit of queued work against a meeting.</summary>
public class ProcessingJob
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public JobKind Kind { get; set; }

    public JobState State { get; set; } = JobState.Pending;

    public int Attempt { get; set; }

    /// <summary>Unique within a kind, which is what stops the same work being queued twice.</summary>
    public required string IdempotencyKey { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp? StartedAt { get; set; }

    public UtcTimestamp? FinishedAt { get; set; }

    public string? LastError { get; set; }

    public UtcTimestamp? NextAttemptAt { get; set; }
}
