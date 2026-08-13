using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// Now, through the one clock every command that writes reads. It reaches the corpus as the moment
/// an artifact was confirmed, so it is the machine's clock and never an argument: a person who
/// could type it could date a paid file to whenever they liked.
/// </summary>
internal static class Clock
{
    public static UtcTimestamp Now() => UtcTimestamp.From(TimeProvider.System.GetUtcNow());
}
