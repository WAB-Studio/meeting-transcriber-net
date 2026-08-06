namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// Whether a meeting is here, on its way out, or gone. It deliberately says nothing about
/// transcription, extraction or backup: those are jobs, and each carries its own state.
/// </summary>
public enum LifecycleState
{
    Active = 1,
    Deleting = 2,
    Deleted = 3,
}
