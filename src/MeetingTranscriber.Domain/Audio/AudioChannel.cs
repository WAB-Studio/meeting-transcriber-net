namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// A channel of audio captured by the application. The numeric value is the contract
/// itself: it is the interleaving order written to the WAV and the channel index
/// Deepgram reports back, so it can never change.
/// </summary>
public enum AudioChannel
{
    /// <summary>Channel 0 — the meeting: the selected process, or full loopback as fallback.</summary>
    Others = 0,

    /// <summary>Channel 1 — the user: the selected microphone.</summary>
    Me = 1,
}
