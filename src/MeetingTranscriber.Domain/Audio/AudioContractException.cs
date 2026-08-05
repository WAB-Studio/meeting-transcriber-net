namespace MeetingTranscriber.Domain.Audio;

/// <summary>Raised when audio, or its description, breaks the channel and profile contract.</summary>
public sealed class AudioContractException : InvalidOperationException
{
    public AudioContractException(string message)
        : base(message)
    {
    }

    public AudioContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
