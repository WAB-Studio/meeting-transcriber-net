namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when this machine will not give the application the audio it asked for: no device of
/// that name, a device Windows refuses to open, a format nothing here can read, or a stream that
/// ended before it was told to.
/// </summary>
/// <remarks>
/// Distinct from <see cref="Domain.Audio.AudioContractException"/> on purpose. That one means the
/// audio disagrees with what the application promises about channels and profiles, which is a
/// defect in this code; this one means the machine said no, which is an answer a person acts on.
/// </remarks>
public sealed class AudioCaptureException : InvalidOperationException
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
