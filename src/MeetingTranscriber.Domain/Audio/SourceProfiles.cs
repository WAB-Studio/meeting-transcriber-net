namespace MeetingTranscriber.Domain.Audio;

/// <summary>The rules that bind a <see cref="SourceProfile"/> to the shape of its audio.</summary>
public static class SourceProfiles
{
    /// <summary>How many channels the audio of this profile has to have.</summary>
    public static int ChannelCount(this SourceProfile profile) => profile switch
    {
        SourceProfile.Multichannel => 2,
        SourceProfile.Diarize => 1,
        _ => throw new AudioContractException($"Unknown source profile '{profile}'."),
    };

    /// <summary>
    /// True when the profile puts each speaker on its own channel, so the assignment is
    /// deterministic instead of a diarized guess waiting for a person.
    /// </summary>
    public static bool HasDeterministicSpeakers(this SourceProfile profile) =>
        profile.ChannelCount() > 1;

    /// <summary>The name this profile is persisted and requested under.</summary>
    public static string ToWireName(this SourceProfile profile) => profile switch
    {
        SourceProfile.Multichannel => "multichannel",
        SourceProfile.Diarize => "diarize",
        _ => throw new AudioContractException($"Unknown source profile '{profile}'."),
    };

    /// <summary>Reads back a profile persisted by <see cref="ToWireName"/>.</summary>
    public static SourceProfile FromWireName(string name) => name switch
    {
        "multichannel" => SourceProfile.Multichannel,
        "diarize" => SourceProfile.Diarize,
        _ => throw new AudioContractException($"Unknown source profile '{name}'."),
    };

    /// <summary>Throws unless the audio has the channel count the profile promises.</summary>
    public static void EnsureChannelCount(this SourceProfile profile, int channelCount)
    {
        var expected = profile.ChannelCount();
        if (channelCount != expected)
        {
            throw new AudioContractException(
                $"Source profile '{profile.ToWireName()}' needs {expected} channel(s), got {channelCount}.");
        }
    }
}
