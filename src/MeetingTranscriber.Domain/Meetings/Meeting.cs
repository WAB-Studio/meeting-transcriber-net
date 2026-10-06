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

    public UtcTimestamp StartedAt { get; set; }

    public Duration? Duration { get; set; }

    public SourceProfile SourceProfile { get; set; }

    public required string Language { get; set; }

    public LifecycleState LifecycleState { get; set; } = LifecycleState.Active;

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }

    public UtcTimestamp? DeletedAt { get; set; }

    /// <summary>
    /// When a person put this meeting away, or nothing while it is out. It records a person's act,
    /// so it is a source: nothing derives it and nothing can put it back. It is not a lifecycle
    /// state, because every reader that asks for <see cref="LifecycleState.Active"/> would then
    /// silently drop the meeting — an export included, which is how somebody moves to another
    /// machine. Only the meetings list leaves an archived meeting out.
    /// </summary>
    public UtcTimestamp? ArchivedAt { get; set; }

    /// <summary>
    /// When a person deleted this meeting's audio, or nothing while the audio was never deleted.
    /// It records a person's act, and it is what lets the screen say the recording was deleted
    /// instead of saying nobody has recorded one.
    /// </summary>
    public UtcTimestamp? AudioRemovedAt { get; set; }
}
