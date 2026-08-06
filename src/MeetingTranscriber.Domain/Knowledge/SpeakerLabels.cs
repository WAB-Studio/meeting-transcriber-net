using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// The stored form of a speaker: what a provider's speaker number becomes once it is written down.
/// </summary>
/// <remarks>
/// <para>
/// It is a contract and not a formatting choice. <c>speaker_assignments</c> is keyed on the meeting
/// and this string, so two different speakers of one meeting sharing a label would put one person's
/// name on somebody else's words — and a provider numbers speakers within a channel, which means
/// speaker 0 of the meeting and speaker 0 of the microphone are two people who both arrive as 0.
/// The channel is therefore part of the label whenever the recording has channels at all. A
/// diarized single track has none, and its labels say so by carrying no channel.
/// </para>
/// <para>
/// Zero based, because this is the provider's own number rather than something a person reads. The
/// one based <c>Speaker 1</c> the Python system stored is a rendering concern here, and a label
/// only ever reaches a person when nobody has assigned that speaker yet.
/// </para>
/// </remarks>
public static class SpeakerLabels
{
    /// <summary>
    /// The label for the <paramref name="speaker"/> a provider numbered on <paramref name="channel"/>,
    /// or on no channel at all when the recording is a single track.
    /// </summary>
    public static string For(AudioChannel? channel, int speaker)
    {
        if (speaker < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speaker), speaker, "A provider numbers speakers from zero.");
        }

        return channel is null
            ? $"speaker_{speaker}"
            : $"ch{CapturedAudio.IndexOf(channel.Value)}:speaker_{speaker}";
    }
}
