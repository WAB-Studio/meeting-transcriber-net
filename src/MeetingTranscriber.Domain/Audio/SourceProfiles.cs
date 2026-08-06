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
    /// The one channel whose speaker the contract already names, or null when the profile has
    /// none. Everybody else on the recording is a label somebody still has to resolve.
    /// </summary>
    /// <remarks>
    /// Only the microphone is ever deterministic. Channel 0 carries the whole meeting — a room, a
    /// call with four people on the other end — which is the reason it is diarized at all, so a
    /// profile claiming to put each speaker on a channel of its own would have been describing
    /// audio nobody records.
    /// </remarks>
    public static AudioChannel? DeterministicSpeakerChannel(this SourceProfile profile) => profile switch
    {
        SourceProfile.Multichannel => AudioChannel.Me,
        SourceProfile.Diarize => null,
        _ => throw new AudioContractException($"Unknown source profile '{profile}'."),
    };

    /// <summary>
    /// Whether the provider is asked to tell speakers apart. Both profiles ask: the difference
    /// between them is how many channels the audio has, never whether the meeting channel holds
    /// more than one person.
    /// </summary>
    public static bool RequestsDiarization(this SourceProfile profile)
    {
        // Called for the side effect of refusing a profile the domain does not have, so a new
        // member cannot reach the provider under a request nobody chose for it.
        _ = profile.ChannelCount();
        return true;
    }

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
