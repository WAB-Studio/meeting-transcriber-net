namespace MeetingTranscriber.Domain.Jobs;

/// <summary>
/// The pieces of work a meeting goes through. They progress independently, so a meeting can be
/// transcribed while its summary failed and a backup is still waiting.
/// </summary>
public enum JobKind
{
    Capture = 1,
    Finalize = 2,
    Transcribe = 3,
    Extract = 4,
    Render = 5,
    Backup = 6,
}
