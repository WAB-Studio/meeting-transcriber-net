using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// A label somebody or the recording settled onto a person, as the corpus holds it: the input
/// <see cref="WhoIsWho"/> reads names off.
/// </summary>
public sealed record AssignedVoice(string Label, Guid PersonId, string PersonName, SpeakerAssignmentSource By);

/// <summary>
/// The stretch of a meeting one voice is offered to be heard alone in — nobody else's turn, on
/// either channel, overlaps any of it.
/// </summary>
public sealed record HeardAlone(Duration From, Duration To);

/// <summary>
/// One voice of one meeting: the label its turns carry, what it is called until somebody names it,
/// how much it said, the turn it is quoted by, the stretch it may be heard alone in, and who it is
/// once somebody has said.
/// </summary>
public sealed record Voice(
    string Label,
    bool IsTheMicrophonesOwn,
    int Number,
    int TurnsSaid,
    Turn Quoted,
    HeardAlone? Alone,
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
    /// How long a clip drawn to hear a voice alone plays, at most. A stretch longer than this is
    /// offered as its first fifteen seconds rather than in full — long enough to recognise somebody
    /// by, and short enough that listening to every voice on a meeting is not an errand.
    /// </summary>
    public static Duration LongestClip { get; } = Duration.FromMilliseconds(15_000);

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
    /// <para>
    /// <see cref="Voice.Quoted"/> is the turn inside the voice's own longest solo stretch, not
    /// simply its longest turn, for every voice — settled by the recording or not, offered a clip
    /// or not. One rule and not two: whether a card goes on to draw a clip off <c>Alone</c> is a
    /// screen's own question, and a quotation that named the turn a clip does not exist for would be
    /// this type answering two different questions about the same voice depending on who is asking.
    /// A voice's longest turn is the one somebody talked over the least of; quoting the stretch it
    /// is heard alone in, rather than a turn a third of which was somebody else's "mm-hmm", is the
    /// better sentence to read whether or not there ends up being anything to press play on. A voice
    /// with no solo stretch at all keeps its longest turn, exactly as before.
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
            var longestTurn = said
                .OrderByDescending(turn => turn.End.Milliseconds - turn.Start.Milliseconds)
                .ThenBy(turn => turn.Ordinal)
                .First();

            var others = turns
                .Where(turn => !string.Equals(turn.SpeakerLabel, label, StringComparison.Ordinal))
                .ToArray();
            var stretch = LongestSoloStretch(said, others);

            var quoted = stretch?.Turn ?? longestTurn;
            var alone = stretch is { } found
                ? new HeardAlone(found.Start, Offered(found.Start, found.End))
                : null;

            var isMine = string.Equals(label, mine, StringComparison.Ordinal);
            byLabel.TryGetValue(label, out var settled);

            voices.Add(new Voice(
                label,
                isMine,
                isMine ? 0 : ++number,
                said.Length,
                quoted,
                alone,
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

    /// <summary>
    /// The longest stretch one voice's own turns hold with nobody else talking over it, and the
    /// turn it is inside — the earliest such stretch when two are exactly as long.
    /// </summary>
    private static (Turn Turn, Duration Start, Duration End)? LongestSoloStretch(
        IReadOnlyList<Turn> mine, IReadOnlyList<Turn> others)
    {
        (Turn Turn, Duration Start, Duration End)? longest = null;

        foreach (var turn in mine)
        {
            foreach (var (start, end) in Alone(turn.Start, turn.End, others))
            {
                var length = end - start;

                if (longest is not { } current
                    || length > current.End - current.Start
                    || (length == current.End - current.Start && start < current.Start))
                {
                    longest = (turn, start, end);
                }
            }
        }

        return longest;
    }

    /// <summary>
    /// What is left of one stretch, <paramref name="start"/> to <paramref name="end"/>, once every
    /// turn of <paramref name="others"/> that overlaps it is cut out — on either channel, because a
    /// stretch somebody else talks over on the other track is still not one this voice is alone in.
    /// </summary>
    private static IEnumerable<(Duration Start, Duration End)> Alone(
        Duration start, Duration end, IReadOnlyList<Turn> others)
    {
        var pieces = new List<(Duration Start, Duration End)> { (start, end) };

        foreach (var other in others)
        {
            if (other.End <= start || other.Start >= end || pieces.Count == 0)
            {
                continue;
            }

            var next = new List<(Duration Start, Duration End)>();

            foreach (var piece in pieces)
            {
                if (other.End <= piece.Start || other.Start >= piece.End)
                {
                    next.Add(piece);
                    continue;
                }

                if (other.Start > piece.Start)
                {
                    next.Add((piece.Start, other.Start));
                }

                if (other.End < piece.End)
                {
                    next.Add((other.End, piece.End));
                }
            }

            pieces = next;
        }

        return pieces;
    }

    /// <summary>A stretch, capped at <see cref="LongestClip"/> from where it starts.</summary>
    private static Duration Offered(Duration start, Duration end)
    {
        var length = end - start;
        return start + (length > LongestClip ? LongestClip : length);
    }
}
