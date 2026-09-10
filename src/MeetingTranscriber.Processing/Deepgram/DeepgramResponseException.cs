namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// A file this build cannot read as a response: missing something the parser needs, saying two
/// things about itself that disagree, or stopping partway.
/// </summary>
/// <remarks>
/// <para>
/// It is about a file and never about a call, which is the whole of what tells it from
/// <see cref="DeepgramCallException"/>. Both can be said of something that stopped partway, and
/// they are not the same event: that one is a call that did not deliver a response, and this one is
/// what the parser is handed afterwards — by <c>import-response</c>, most of the time, over a file
/// somebody copied off a machine that ran out of disk. The parser opens what it is given and never
/// knows which call left it that way.
/// </para>
/// <para>
/// It is deliberately not the exception for a response that disagrees with the profile it was
/// requested under. That is the audio contract, it is stated in one place, and it throws
/// <see cref="Domain.Audio.AudioContractException"/> wherever it is broken.
/// </para>
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
