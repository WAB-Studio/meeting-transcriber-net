using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// Which speakers of one meeting the recording settles by itself, and which are left for a person.
/// </summary>
/// <param name="Mine">
/// The label the microphone settles onto the user, or null when it settles nobody. It is the only
/// assignment the system is allowed to make without being asked, and it is where
/// <see cref="SpeakerAssignmentSource.Channel"/> comes from.
/// </param>
/// <param name="NeedingAPerson">
/// Every other label heard in the meeting, in a stable order. They are pending until somebody says
/// who they are: there is no row in <c>speaker_assignments</c> for them and rendering falls back to
/// the label.
/// </param>
public sealed record SpeakerResolution(string? Mine, IReadOnlyList<string> NeedingAPerson)
{
    /// <summary>Whether anything here has to stop on a person.</summary>
    public bool NeedsAPerson => NeedingAPerson.Count > 0;
}

/// <summary>
/// How a recording's own shape decides who spoke, and where it stops deciding.
/// </summary>
/// <remarks>
/// <para>
/// A channel is deterministic about the device the audio came through, never about how many people
/// spoke into it. Two people in one room share the microphone and the diarizer tells them apart,
/// so the microphone channel coming back with more than one speaker is an ordinary meeting and not
/// a fault. What it is not is answerable: which of them is the user is exactly the thing the
/// recording cannot know, and guessing writes one person's name onto what the other said, into
/// citations that outlive the guess.
/// </para>
/// <para>
/// So the rule is narrow on purpose. One speaker on the microphone is the user, because there was
/// nobody else it could be. Anything else waits for somebody to say.
/// </para>
/// </remarks>
public static class Speakers
{
    /// <summary>
    /// Splits the speakers of one meeting into the one its microphone settles and the ones a person
    /// still has to.
    /// </summary>
    public static SpeakerResolution Resolve(SourceProfile profile, IEnumerable<SpeechSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var microphone = profile.MicrophoneChannel();
        var heard = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => (segment.Channel, segment.SpeakerLabel))
            .Distinct()
            .OrderBy(speaker => speaker.SpeakerLabel, StringComparer.Ordinal)
            .ToArray();

        var onMicrophone = heard
            .Where(speaker => microphone is not null && speaker.Channel == microphone)
            .Select(speaker => speaker.SpeakerLabel)
            .ToArray();

        // Exactly one, and only then. None means nobody spoke into it — a channel that was
        // transcribed and charged for and caught nothing — and there is no assignment to make.
        var mine = onMicrophone.Length == 1 ? onMicrophone[0] : null;

        return new SpeakerResolution(
            mine,
            heard.Select(speaker => speaker.SpeakerLabel).Where(label => label != mine).ToArray());
    }
}
