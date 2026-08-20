namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when this machine will not give the application the audio it asked for: no device of
/// that name, a device Windows refuses to open, a format nothing here can read, or a stream that
/// ended before it was told to.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="Domain.Audio.AudioContractException"/> on purpose. That one means the
/// audio disagrees with what the application promises about channels and profiles, which is a
/// defect in this code; this one means the machine said no, which is an answer a person acts on.
/// </para>
/// <para>
/// Not sealed, and what a type under it has to earn is a caller rather than a cause. Every one of
/// them is still this machine not handing over the audio that was asked for, so everything
/// catching this to mean "the recording could not happen" goes on catching all of them, and a
/// narrower type on its own buys nothing: a cause with nowhere to be read is a message. The rule
/// is a catch that can be pointed at, whose answer has to come out differently — and the
/// difference is read there, at that catch, rather than by the type standing outside the family.
/// Everywhere else naming one of them names this one, which is what the rule comes to in practice.
/// </para>
/// <para>
/// The two that exist sit either side of what such a catch decides: whether there is still a
/// recording. <see cref="AudioDeviceWedgedException"/> is worse than a refusal — the device never
/// answered at all, so the thread that asked is still inside it, and the one place that would
/// offer somebody a second recording has to stop making the offer. A
/// <see cref="NoSinglePlaybackException"/> is not a failure at all — the recording is whole, and
/// what cannot exist is only the single-format file poured for convenience — so the one caller
/// with other work to do is entitled to say it and carry on.
/// </para>
/// </remarks>
public class AudioCaptureException : InvalidOperationException
{
    public AudioCaptureException(string message)
        : base(message)
    {
    }

    public AudioCaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
