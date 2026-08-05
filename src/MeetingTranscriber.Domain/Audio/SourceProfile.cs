namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// How the audio of a meeting was obtained, which decides both its channel count and
/// whether speakers are known or have to be resolved by a person.
/// </summary>
public enum SourceProfile
{
    /// <summary>
    /// Captured by the application: two channels, <see cref="AudioChannel.Others"/>
    /// then <see cref="AudioChannel.Me"/>. Every speaker turn belongs to a channel.
    /// </summary>
    Multichannel = 1,

    /// <summary>
    /// Imported file with a single track. Speakers are diarized labels, kept as labels
    /// until a person assigns them.
    /// </summary>
    Diarize = 2,
}
