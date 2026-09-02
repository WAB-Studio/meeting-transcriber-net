using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// Which of the meeting's fixed sections a thing the AI left belongs to.
/// </summary>
/// <remarks>
/// Closed, and it is the same three tables the corpus already has: <c>decisions</c>,
/// <c>action_items</c>, <c>open_questions</c>. The model does not get to invent a fourth — a
/// corpus whose sections vary by meeting is one that cannot answer "every decision in August",
/// which is what having them at all is for. The summary is not here because it is about the whole
/// meeting and anchors nowhere; it is <see cref="WhatTheAiLeft.Abstract"/>.
/// </remarks>
public enum LeftKind
{
    /// <summary>Something the meeting settled.</summary>
    Decision = 1,

    /// <summary>Something the meeting left for somebody to do.</summary>
    Action = 2,

    /// <summary>Something the meeting raised and did not settle.</summary>
    Question = 3,
}

/// <summary>
/// One thing the AI left, and where in the meeting it was said.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="At"/> is not nullable and that is the whole point of this type. Every row the corpus
/// will hand back has a citation — validation refuses an extraction whose citation does not land
/// on a real turn — so a thing with nowhere to go back to is not a thing that can be built, rather
/// than one a screen has to remember to leave the play button off.
/// </para>
/// <para>
/// It carries the turn's position as well as its offset, because the two answer different
/// questions: the offset is where the player goes, and the position is what the transcript is
/// unfolded around. Neither is derivable from the other without reading the meeting.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the three sections it belongs to.</param>
/// <param name="Says">The sentence itself, as the extraction proposed it.</param>
/// <param name="At">Where in the meeting it was said, from the meeting's start.</param>
/// <param name="TurnOrdinal">The position of the turn it was said in.</param>
/// <param name="Quoted">What was actually said there, as the citation recorded it.</param>
/// <param name="SpeakerLabel">The label of whoever said it, never a person's name.</param>
public sealed record LeftThing(
    LeftKind Kind,
    string Says,
    Duration At,
    int TurnOrdinal,
    string Quoted,
    string SpeakerLabel);

/// <summary>
/// Who produced what this meeting has, and when. Null on either half means nobody has.
/// </summary>
/// <param name="Transcriber">The provider that transcribed it, and its model where it named one.</param>
/// <param name="TranscribedAt">When that transcription came back.</param>
/// <param name="Summariser">Whatever wrote the summary, and its model where it named one.</param>
/// <param name="SummarisedAt">When that summary was accepted.</param>
public sealed record WhoWroteThis(
    string? Transcriber,
    UtcTimestamp? TranscribedAt,
    string? Summariser,
    UtcTimestamp? SummarisedAt)
{
    /// <summary>Nobody has written anything about this meeting yet.</summary>
    public static WhoWroteThis Nobody { get; } = new(null, null, null, null);
}

/// <summary>
/// Everything one extraction left of a meeting: the abstract, and the three anchored lists as one
/// ordered run.
/// </summary>
/// <remarks>
/// <para>
/// One list rather than three, because the order is a fact about the meeting and not about the
/// sections: what the player marks along the track is every one of these, and a screen holding
/// three lists would have to interleave them again to draw that. <see cref="Of"/> is how a section
/// gets its own, and it keeps the order it was put in.
/// </para>
/// <para>
/// It holds no <c>ExtractionRunId</c>. Which run these came out of is the reader's question, and
/// once it has answered it every row here is from that one run — two runs' worth on one screen
/// would show the same decision twice, said slightly differently, with nothing to tell a reader
/// which is the current one.
/// </para>
/// </remarks>
/// <param name="Abstract">What the meeting was about, in the summary's own words, or none.</param>
/// <param name="Things">Every anchored thing, earliest in the meeting first.</param>
/// <param name="Wrote">Who produced the transcription and the summary, and when.</param>
public sealed record WhatTheAiLeft(
    string? Abstract,
    IReadOnlyList<LeftThing> Things,
    WhoWroteThis Wrote)
{
    /// <summary>A meeting nothing has been made of yet.</summary>
    public static WhatTheAiLeft Nothing { get; } = new(null, [], WhoWroteThis.Nobody);

    /// <summary>
    /// Puts things in the order they were said, which is the order a meeting is read in.
    /// </summary>
    /// <remarks>
    /// Stable, and the tie-break is deliberate rather than incidental: two things cited to the
    /// same turn come out in a fixed order every time the screen is drawn, so a list does not
    /// shuffle under somebody between one look and the next. The section decides first and the
    /// turn's position second, because a decision and the question it left open are frequently
    /// the same sentence to the millisecond — and the sentence itself decides last, so that two
    /// decisions cited to one turn have an order too. Sorting on three keys and leaving a fourth
    /// tie to whatever order three separate queries came back in is a promise this would keep
    /// almost always, which is the kind that is noticed once and never reproduced.
    /// </remarks>
    public static IReadOnlyList<LeftThing> InTheOrderTheyWereSaid(IEnumerable<LeftThing> things)
    {
        ArgumentNullException.ThrowIfNull(things);

        return [.. things
            .OrderBy(thing => thing.At.Milliseconds)
            .ThenBy(thing => thing.Kind)
            .ThenBy(thing => thing.TurnOrdinal)
            .ThenBy(thing => thing.Says, StringComparer.Ordinal)];
    }

    /// <summary>One section's things, in the order the whole run is in.</summary>
    public IReadOnlyList<LeftThing> Of(LeftKind kind) =>
        [.. Things.Where(thing => thing.Kind == kind)];

    /// <summary>
    /// Where each thing sits along the meeting, which is what the player marks.
    /// </summary>
    /// <remarks>
    /// The offsets and not the things: the track has room for a mark and nothing else, and a
    /// player that knew what each mark meant would be a second copy of the sections above it.
    /// </remarks>
    public IReadOnlyList<Duration> MarkedAlongTheMeeting =>
        [.. Things.Select(thing => thing.At)];
}
