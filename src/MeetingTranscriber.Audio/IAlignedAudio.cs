namespace MeetingTranscriber.Audio;

/// <summary>
/// Where the timeline puts the frames it has lined up: interleaved stereo in the interchange
/// format <see cref="Domain.Audio.CapturedAudio"/> declares, in the channel order it fixes.
/// </summary>
/// <remarks>
/// It is an interface because two hours of a meeting is more than a machine holds at once, so the
/// timeline hands its frames on as it makes them instead of returning them at the end. What is on
/// the other side — a file being hashed, a test counting samples — is nothing the alignment knows
/// or needs to know.
/// </remarks>
public interface IAlignedAudio
{
    /// <summary>
    /// Takes the next stretch of the recording. The span is whole interleaved frames and is only
    /// valid for the length of the call — the timeline reuses the buffer behind it.
    /// </summary>
    void Write(ReadOnlySpan<short> frames);
}
