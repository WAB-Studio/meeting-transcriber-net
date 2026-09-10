namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// The provider was asked and no readable transcription came out of it: it refused the request, it
/// failed, it could not be reached, or it answered and the answer did not arrive whole.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="DeepgramResponseException"/>, which is about a response that came
/// back whole and cannot be read. The two are different things to somebody standing at a prompt:
/// one is "the call did not happen the way you asked", the other is "what you paid for is on disk
/// and this build cannot read it", and only the second is worth keeping bytes for.
/// </para>
/// <para>
/// <b>No message here says a charge did not happen unless this end can know it.</b> Exactly two of
/// them can: a key the provider would not accept, and a request it refused outright — both answers
/// given before any audio was transcribed. Everything else — a connection that went nowhere or
/// died mid-flight, a call that ran out of time, a failure on the provider's own side, a 200 whose
/// body stopped partway — either may have been billed or certainly was, and says so. This is the
/// contract's own rule about a job a restart found running, one level down: a charge that may
/// already have happened is not something the app gets to be reassuring about.
/// </para>
/// <para>
/// <b>No message here ever carries the key or any part of one.</b> The key travels in one header
/// and nothing quotes a header back. What Deepgram itself said about a refusal is quoted — its
/// <c>err_msg</c> is its own sentence about the request, not about the secret — and whatever the
/// transport said travels as the inner exception, for whoever is diagnosing.
/// </para>
/// </remarks>
public sealed class DeepgramCallException : Exception
{
    public DeepgramCallException(string message)
        : base(message)
    {
    }

    public DeepgramCallException(string message, Exception cause)
        : base(message, cause)
    {
    }
}
