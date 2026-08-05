namespace MeetingTranscriber.Domain.Jobs;

/// <summary>
/// Where a job stands. Which transitions between these are legal is not decided here.
/// </summary>
public enum JobState
{
    Pending = 1,
    Running = 2,

    /// <summary>
    /// Where a job lands when a restart finds it still running and retrying it might pay
    /// twice. Without a backend there is no exactly-once around Deepgram, so an uncertain
    /// charge is a decision for a person and nothing moves it on automatically.
    /// </summary>
    AwaitingUser = 3,

    Succeeded = 4,
    FailedRetryable = 5,
    FailedPermanent = 6,
    Cancelled = 7,
}
