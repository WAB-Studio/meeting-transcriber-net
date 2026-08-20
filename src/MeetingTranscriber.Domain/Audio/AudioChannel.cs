namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// A channel of audio captured by the application. The numeric value is the contract
/// itself: it is the interleaving order written to the WAV and the channel index
/// Deepgram reports back, so it can never change.
/// </summary>
/// <remarks>
/// Both members name a source of audio and not a person. A channel is deterministic about which
/// device the sound arrived through and says nothing about how many people were in front of it:
/// two people in one room share one microphone, and the diarizer is what tells them apart. Naming
/// them "the user" and "the others" is what made that look decided.
/// </remarks>
public enum AudioChannel
{
    /// <summary>Channel 0 — what was played: the selected process, or everything this machine plays.</summary>
    Loopback = 0,

    /// <summary>Channel 1 — what the selected microphone picked up.</summary>
    Microphone = 1,
}
