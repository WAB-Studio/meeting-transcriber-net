using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// A label somebody or the recording settled onto a person, as the corpus holds it: the input
/// <see cref="WhoIsWho"/> reads names off.
/// </summary>
public sealed record AssignedVoice(string Label, Guid PersonId, string PersonName, SpeakerAssignmentSource By);

/// <summary>
/// One voice of one meeting: the label its turns carry, what it is called until somebody names it,
/// how much it said, the turn it is quoted by, and who it is once somebody has said.
/// </summary>
public sealed record Voice(
    string Label,
    bool IsTheMicrophonesOwn,
    int Number,
    int TurnsSaid,
    Turn Quoted,
    Guid? PersonId,
    string? PersonName,
    bool SettledByTheRecording);

/// <summary>
/// Who spoke in one meeting, as the screen that names voices and every reading after it needs it —
/// one voice per label the turns carry, what each is called until somebody names it, and who it is
/// once they have.
/// </summary>
/// <remarks>
/// It sits beside <see cref="MeetingScreen"/> rather than in <c>Recording</c> or on a screen,
/// because what it decides — who spoke, and in what order — is about a meeting and not about the
/// application asking. <see cref="Of"/> asks <see cref="Speakers.Resolve"/> for the microphone's own
/// voice rather than deciding it again, which is the only rule this type leans on rather than owns.
/// </remarks>
public sealed record WhoIsWho(IReadOnlyList<Voice> Voices)
{
    /// <summary>
    /// Builds the voices of one meeting out of its turns and whatever a person or the recording has
    /// already settled onto one of their labels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The microphone's own voice, when its label is one the turns carry, always comes first;
    /// every other voice follows in the order of its first turn, numbered 1, 2, 3 from there. A
    /// single track has no microphone channel, so every voice on it is numbered.
    /// </para>
    /// <para>
    /// Built from the turns and never from <paramref name="assigned"/>: a label somebody resolved
    /// that no turn of this render carries — <c>ForgetVoicesNoTurnHas</c>'s own case — is left out
    /// rather than shown as a voice with nothing to quote.
    /// </para>
    /// </remarks>
    public static WhoIsWho Of(
        SourceProfile profile, IReadOnlyList<Turn> turns, IReadOnlyList<AssignedVoice> assigned)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(assigned);

        var segments = turns
            .Select(turn => new SpeechSegment(
                turn.Start, turn.End, turn.Channel, turn.SpeakerLabel, turn.Text, turn.Confidence))
            .ToArray();

        var mine = Speakers.Resolve(profile, segments).Mine;
        var byLabel = assigned.ToDictionary(voice => voice.Label, StringComparer.Ordinal);

        var ordered = new List<string>();
        if (mine is not null && turns.Any(turn => string.Equals(turn.SpeakerLabel, mine, StringComparison.Ordinal)))
        {
            ordered.Add(mine);
        }

        foreach (var turn in turns)
        {
            if (!string.Equals(turn.SpeakerLabel, mine, StringComparison.Ordinal)
                && !ordered.Contains(turn.SpeakerLabel, StringComparer.Ordinal))
            {
                ordered.Add(turn.SpeakerLabel);
            }
        }

        var voices = new List<Voice>();
        var number = 0;

        foreach (var label in ordered)
        {
            var said = turns.Where(turn => string.Equals(turn.SpeakerLabel, label, StringComparison.Ordinal))
                .ToArray();
            var quoted = said
                .OrderByDescending(turn => turn.End.Milliseconds - turn.Start.Milliseconds)
                .ThenBy(turn => turn.Ordinal)
                .First();

            var isMine = string.Equals(label, mine, StringComparison.Ordinal);
            byLabel.TryGetValue(label, out var settled);

            voices.Add(new Voice(
                label,
                isMine,
                isMine ? 0 : ++number,
                said.Length,
                quoted,
                settled?.PersonId,
                settled?.PersonName,
                settled?.By is SpeakerAssignmentSource.Channel));
        }

        return new WhoIsWho(voices);
    }

    /// <summary>The voice under this label, or none.</summary>
    public Voice? ForLabel(string label) =>
        Voices.FirstOrDefault(voice => string.Equals(voice.Label, label, StringComparison.Ordinal));

    /// <summary>Who this label is, once somebody has said, or null while nobody has.</summary>
    public string? NameOf(string label) => ForLabel(label)?.PersonName;
}
