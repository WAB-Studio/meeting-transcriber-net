namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// A response that cannot be read: truncated, missing something the parser needs, or saying two
/// things about itself that disagree.
/// </summary>
/// <remarks>
/// It is deliberately not the exception for a response that disagrees with the profile it was
/// requested under. That is the audio contract, it is stated in one place, and it throws
/// <see cref="Domain.Audio.AudioContractException"/> wherever it is broken.
/// </remarks>
public sealed class DeepgramResponseException : Exception
{
    public DeepgramResponseException(string message)
        : base(message)
    {
    }

    public DeepgramResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
