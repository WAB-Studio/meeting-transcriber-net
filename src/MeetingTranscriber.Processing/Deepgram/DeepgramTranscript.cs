using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// One channel of a response, and who the provider heard on it.
/// </summary>
/// <param name="Index">
/// The channel number the provider reported, which is the position of that channel in the audio
/// that was sent.
/// </param>
/// <param name="Channel">
/// Which side of the recording that position is, or null for a single track, which has no side to
/// be deterministic about.
/// </param>
/// <param name="SpeakerLabels">
/// Everybody the provider told apart on this channel, in the order it numbered them. More than one
/// on the microphone is not an error: two people in the same room share it.
/// </param>
public sealed record TranscribedChannel(
    int Index,
    AudioChannel? Channel,
    IReadOnlyList<string> SpeakerLabels)
{
    /// <summary>
    /// Whether anybody was heard on this channel. A channel that came back with nothing was still
    /// transcribed and still charged for — the usual cause is asking for the wrong language — so
    /// it is a fact about the meeting and never a reason to fail.
    /// </summary>
    public bool CarriedSpeech => SpeakerLabels.Count > 0;
}

/// <summary>
/// A Deepgram response as the rest of the system reads it: the speech that was in it, and what the
/// response says about the audio it came from.
/// </summary>
/// <remarks>
/// <see cref="Segments"/> is in the order the provider handed the utterances over, which is not
/// the order of the conversation. <see cref="Turns.Group"/> is what rebuilds that, and it is also
/// what drops the empty ones — this is a reading of the file and applies no rule of its own.
/// </remarks>
public sealed record DeepgramTranscript(
    SourceProfile Profile,
    Duration Audio,
    IReadOnlyList<TranscribedChannel> Channels,
    IReadOnlyList<SpeechSegment> Segments)
{
    /// <summary>The channels that were paid for and carried nobody.</summary>
    public IEnumerable<TranscribedChannel> SilentChannels =>
        Channels.Where(channel => !channel.CarriedSpeech);
}
