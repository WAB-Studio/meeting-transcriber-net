using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// One stretch of speech exactly as a transcription provider handed it back, before anything
/// has been decided about it.
/// </summary>
/// <remarks>
/// A provider splits a sentence across these freely — "Con", "Creo", "Eso todavía no lo tengo
/// hecho" — so one of them on its own is usually a fragment. <see cref="Turns"/> is what turns
/// them into something a person can read and a claim can cite.
/// </remarks>
public readonly record struct SpeechSegment(
    Duration Start,
    Duration End,
    AudioChannel? Channel,
    string SpeakerLabel,
    string Text);

/// <summary>
/// What the projection stores as a row of <c>utterances</c>: one contiguous stretch of speech
/// from one speaker, at a fixed position on the meeting timeline.
/// </summary>
public sealed record Turn(
    int Ordinal,
    Duration Start,
    Duration End,
    AudioChannel? Channel,
    string SpeakerLabel,
    string Text);

/// <summary>
/// How the turns of a meeting are built from what the provider returned.
/// </summary>
/// <remarks>
/// <para>
/// This is the rule the citation contract rests on. A claim anchors on a meeting and a turn's
/// position in it, so what counts as a turn decides what those positions are, and projecting the
/// same response twice has to produce the same ones. See docs/reference-behaviour.md.
/// </para>
/// <para>
/// Pure, and deliberately says nothing about how a speaker is labelled or how a channel becomes
/// a person's name: those are applied when rendering, and evidence stays comparable to the raw
/// response because they are not applied here.
/// </para>
/// </remarks>
public static class Turns
{
    /// <summary>
    /// Groups what the provider returned into the turns of one meeting, in the order they were
    /// spoken.
    /// </summary>
    public static IReadOnlyList<Turn> Group(IEnumerable<SpeechSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var turns = new List<Turn>();
        var ordered = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            // A multichannel response comes back grouped by channel, so the raw order can be one
            // whole side of the call followed by the other. Sorting is what rebuilds the
            // conversation, and it is stable, so segments sharing an instant keep their order.
            .OrderBy(segment => segment.Start);

        foreach (var segment in ordered)
        {
            var text = segment.Text.Trim();
            if (turns is [.., var previous] && Continues(previous, segment))
            {
                turns[^1] = previous with
                {
                    End = previous.End > segment.End ? previous.End : segment.End,
                    Text = $"{previous.Text} {text}",
                };
                continue;
            }

            turns.Add(new Turn(
                turns.Count,
                segment.Start,
                segment.End,
                segment.Channel,
                segment.SpeakerLabel,
                text));
        }

        return turns;
    }

    /// <summary>
    /// Whether this segment is more of the turn before it. The channel is half of the answer:
    /// a provider numbers speakers within a channel, so speaker 0 of the meeting and speaker 0
    /// of the microphone are two people, and merging on the label alone would weld the two sides
    /// of a call into one turn.
    /// </summary>
    private static bool Continues(Turn turn, SpeechSegment segment) =>
        turn.Channel == segment.Channel
        && string.Equals(turn.SpeakerLabel, segment.SpeakerLabel, StringComparison.Ordinal);
}
