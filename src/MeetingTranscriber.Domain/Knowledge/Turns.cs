using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// One stretch of speech exactly as a transcription provider handed it back, before anything
/// has been decided about it.
/// </summary>
/// <remarks>
/// <para>
/// A provider splits a sentence across these freely — "Con", "Creo", "Eso todavía no lo tengo
/// hecho" — so one of them on its own is usually a fragment. <see cref="Turns"/> is what turns
/// them into something a person can read and a claim can cite.
/// </para>
/// <para>
/// <c>Confidence</c> is what the provider reported about this one stretch, carried through
/// unchanged. Null when the response did not say.
/// </para>
/// </remarks>
public readonly record struct SpeechSegment(
    Duration Start,
    Duration End,
    AudioChannel? Channel,
    string SpeakerLabel,
    string Text,
    double? Confidence = null);

/// <summary>
/// What the projection stores as a row of <c>utterances</c>: one contiguous stretch of speech
/// from one speaker, at a fixed position on the meeting timeline.
/// </summary>
/// <param name="Confidence">
/// How much of this turn's text the provider stood behind, as the mean of what it reported about
/// the parts, weighted by how long each one lasted. Null when the response said nothing about any
/// of them.
/// </param>
public sealed record Turn(
    int Ordinal,
    Duration Start,
    Duration End,
    AudioChannel? Channel,
    string SpeakerLabel,
    string Text,
    double? Confidence = null);

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
    /// The longest silence a turn is allowed to carry inside it. Speech from the same speaker
    /// resuming after more than this starts a turn of its own.
    /// </summary>
    /// <remarks>
    /// Measured over the committed fixtures: pauses inside somebody's own sentence sit at 0.7 s
    /// median and 1.3 s at the third quartile, and two seconds is where the top decile begins. It
    /// is not a cosmetic setting — without it a meeting where one side never interrupts merges
    /// into a single turn, and both halves of the citation contract stop checking anything: every
    /// timestamp is the start of that turn and every quote belongs to it.
    /// </remarks>
    public static Duration MaxSilence => Duration.FromMilliseconds(2_000);

    /// <summary>
    /// Groups what the provider returned into the turns of one meeting, in the order they were
    /// spoken.
    /// </summary>
    public static IReadOnlyList<Turn> Group(IEnumerable<SpeechSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var turns = new List<Turn>();
        var certainty = new List<Certainty>();
        var ordered = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            // A multichannel response comes back grouped by channel, so the raw order can be one
            // whole side of the call followed by the other. Sorting is what rebuilds the
            // conversation.
            //
            // The tiebreakers are the contract, not tidiness: an ordinal is what every citation
            // is anchored on, so two segments that start in the same millisecond have to be
            // numbered the same way however they were handed over — by the list of turns, or
            // channel by channel. A stable sort only preserves the order they arrived in, which
            // is the caller's, not the meeting's.
            .OrderBy(segment => segment.Start)
            .ThenBy(segment => segment.Channel)
            .ThenBy(segment => segment.SpeakerLabel, StringComparer.Ordinal)
            .ThenBy(segment => segment.End)
            .ThenBy(segment => segment.Text, StringComparer.Ordinal);

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
                certainty[^1] = certainty[^1].And(segment);
                continue;
            }

            turns.Add(new Turn(
                turns.Count,
                segment.Start,
                segment.End,
                segment.Channel,
                segment.SpeakerLabel,
                text));
            certainty.Add(default(Certainty).And(segment));
        }

        // Settled once the parts are all in, because a weighted mean is not something the running
        // total can be read as halfway through.
        for (var ordinal = 0; ordinal < turns.Count; ordinal++)
        {
            turns[ordinal] = turns[ordinal] with { Confidence = certainty[ordinal].Mean };
        }

        return turns;
    }

    /// <summary>
    /// How sure the provider was about a turn, accumulated over the parts it merged.
    /// </summary>
    /// <remarks>
    /// Weighted by how long each part lasted, so a doubtful half-second does not drag down a turn
    /// that is otherwise a minute of clear speech, while a doubtful minute does. The alternatives
    /// were the lowest of the parts, which makes every long turn look untrustworthy because one bad
    /// part is enough, and nothing at all, which leaves the column to whoever writes a screen.
    /// A part the response said nothing about is left out of the mean rather than counted as zero,
    /// which would be a claim the response never made.
    /// </remarks>
    private readonly record struct Certainty(double Weighted, long Milliseconds, double Total, int Parts)
    {
        /// <summary>
        /// Null when nothing was reported about any part. Unweighted when every part is
        /// instantaneous — a zero length turn is degenerate, and dividing by its length is worse
        /// than averaging what there is.
        /// </summary>
        public double? Mean => Parts is 0
            ? null
            : Milliseconds > 0 ? Weighted / Milliseconds : Total / Parts;

        public Certainty And(SpeechSegment segment)
        {
            if (segment.Confidence is not { } confidence)
            {
                return this;
            }

            // Overlapping speech can start before the turn so far has ended, so this is the part's
            // own length and never its distance from where the turn had reached.
            var length = segment.End.Milliseconds - segment.Start.Milliseconds;
            return new Certainty(
                Weighted + (confidence * length),
                Milliseconds + length,
                Total + confidence,
                Parts + 1);
        }
    }

    /// <summary>
    /// Whether this segment is more of the turn before it. The channel is part of the answer:
    /// a provider numbers speakers within a channel, so speaker 0 of the meeting and speaker 0
    /// of the microphone are two people, and merging on the label alone would weld the two sides
    /// of a call into one turn. The silence between them is the rest of it.
    /// </summary>
    /// <remarks>
    /// Compared against the turn's end rather than as a subtraction, because overlapping speech
    /// starts before the turn so far has ended and a length is never negative.
    /// </remarks>
    private static bool Continues(Turn turn, SpeechSegment segment) =>
        turn.Channel == segment.Channel
        && string.Equals(turn.SpeakerLabel, segment.SpeakerLabel, StringComparison.Ordinal)
        && segment.Start <= turn.End + MaxSilence;
}
