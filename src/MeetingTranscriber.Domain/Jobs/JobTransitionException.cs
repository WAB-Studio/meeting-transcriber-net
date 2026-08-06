namespace MeetingTranscriber.Domain.Jobs;

/// <summary>Raised when a job is asked to move somewhere its current state does not reach.</summary>
public sealed class JobTransitionException : InvalidOperationException
{
    public JobTransitionException(string message)
        : base(message)
    {
    }

    public JobTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
