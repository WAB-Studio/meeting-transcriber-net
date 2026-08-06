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

    public string? Title { get; set; }

    /// <summary>
    /// What a person wrote down so somebody who was not there can read the meeting. Nothing
    /// infers it, which is why it is stored beside the title and not with the projections.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>The shape it was classified as, when one was used.</summary>
    public Guid? TemplateId { get; set; }

    public UtcTimestamp StartedAt { get; set; }

    public Duration? Duration { get; set; }

    public SourceProfile SourceProfile { get; set; }

    public required string Language { get; set; }

    public LifecycleState LifecycleState { get; set; } = LifecycleState.Active;

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }

    public UtcTimestamp? DeletedAt { get; set; }
}
