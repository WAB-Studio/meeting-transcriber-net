using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// A recorded or imported meeting. Its identity is created before any audio exists, so it never
/// depends on a title, a file name or reaching a provider.
/// </summary>
public class Meeting
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identifier in the Python corpus this was imported from. Unique, so however often the
    /// import re-runs, the same legacy artifact never turns into two meetings.
    /// </summary>
    public string? LegacyId { get; set; }

    public Guid? ProjectId { get; set; }

    public string? Title { get; set; }

    public UtcTimestamp StartedAt { get; set; }

    public Duration? Duration { get; set; }

    public SourceProfile SourceProfile { get; set; }

    public required string Language { get; set; }

    public LifecycleState LifecycleState { get; set; } = LifecycleState.Active;

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }

    public UtcTimestamp? DeletedAt { get; set; }
}
