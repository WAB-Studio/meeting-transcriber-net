using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Knowledge;

/// <summary>
/// The rules the Python system proved out, as rules. What they look like against real responses
/// is in <see cref="Fixtures.ReferenceBehaviourTests"/>; docs/reference-behaviour.md says where
/// each one came from and which of them were changed on purpose.
/// </summary>
public class TurnsTests
{
    [Fact]
    public void A_response_grouped_by_channel_comes_back_as_a_conversation()
    {
        // What a multichannel response looks like before anything sorts it: one whole side of
        // the call, then the other. Rendered in that order it reads as two monologues.
        var turns = Turns.Group([
            Segment(0.5, 2.0, AudioChannel.Loopback, "speaker_0", "morning, can you hear me?"),
            Segment(9.2, 11.0, AudioChannel.Loopback, "speaker_1", "different person here"),
            Segment(3.1, 4.0, AudioChannel.Microphone, "speaker_0", "loud and clear"),
            Segment(12.0, 13.0, AudioChannel.Microphone, "speaker_0", "agreed"),
        ]);

        turns.Select(turn => turn.Text).ShouldBe([
            "morning, can you hear me?",
            "loud and clear",
            "different person here",
            "agreed",
        ]);
    }

    [Fact]
    public void Turns_are_numbered_from_the_start_of_the_meeting()
    {
        var turns = Turns.Group([
            Segment(5.0, 6.0, AudioChannel.Microphone, "speaker_0", "second"),
            Segment(1.0, 2.0, AudioChannel.Loopback, "speaker_0", "first"),
        ]);

        turns.Select(turn => turn.Ordinal).ShouldBe([0, 1]);
        turns[0].Text.ShouldBe("first");
    }

    [Fact]
    public void Consecutive_speech_from_one_speaker_is_one_turn()
    {
        var turns = Turns.Group([
            Segment(0.0, 0.9, AudioChannel.Microphone, "speaker_0", "First."),
            Segment(1.0, 1.9, AudioChannel.Microphone, "speaker_0", "Second."),
            Segment(2.0, 2.9, AudioChannel.Loopback, "speaker_0", "Third."),
        ]);

        turns.Count.ShouldBe(2);
        turns[0].Text.ShouldBe("First. Second.");
        turns[0].Start.ShouldBe(Duration.Zero);
        turns[0].End.ShouldBe(Duration.FromMilliseconds(1_900));
    }

    [Fact]
    public void A_silence_longer_than_the_rule_allows_starts_a_new_turn()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, AudioChannel.Loopback, "speaker_0", "so that is settled"),
            Segment(1.0 + 2.001, 4.0, AudioChannel.Loopback, "speaker_0", "and another thing"),
        ]);

        turns.Count.ShouldBe(2);
        turns[1].Start.ShouldBe(Duration.FromMilliseconds(3_001));
    }

    [Fact]
    public void A_pause_the_rule_allows_stays_inside_one_turn()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, AudioChannel.Loopback, "speaker_0", "so that is settled"),
            Segment(1.0 + 2.0, 4.0, AudioChannel.Loopback, "speaker_0", "and another thing"),
        ]);

        turns.Count.ShouldBe(1);
        turns[0].Text.ShouldBe("so that is settled and another thing");
    }

    /// <summary>
    /// The case the rule exists for: one person talking for an hour with nobody interrupting.
    /// Without a threshold that is a single turn, and a citation against it checks nothing —
    /// every timestamp is its start and every quote is inside it.
    /// </summary>
    [Fact]
    public void A_monologue_with_silences_in_it_is_not_one_turn()
    {
        var turns = Turns.Group(Enumerable.Range(0, 20)
            .Select(index => Segment(index * 100.0, (index * 100.0) + 1.0, AudioChannel.Loopback, "speaker_0", "a")));

        turns.Count.ShouldBe(20);
    }

    /// <summary>
    /// An ordinal is the identity every citation hangs off, with a foreign key behind it. Two
    /// segments starting in the same millisecond must not be numbered by whoever handed them
    /// over: reading the same response by the list of turns or channel by channel would then
    /// point a stored claim at a different turn, and nothing would fail.
    /// </summary>
    [Fact]
    public void Segments_that_tie_on_the_instant_are_numbered_the_same_either_way()
    {
        var byChannel = new[]
        {
            Segment(4.0, 5.0, AudioChannel.Loopback, "speaker_1", "third"),
            Segment(1.0, 2.0, AudioChannel.Loopback, "speaker_0", "the meeting side"),
            Segment(1.0, 3.0, AudioChannel.Microphone, "speaker_0", "the microphone side"),
        };

        Turns.Group(byChannel).ShouldBe(Turns.Group(byChannel.AsEnumerable().Reverse()));
        Turns.Group(byChannel).Select(turn => turn.Text)
            .ShouldBe(["the meeting side", "the microphone side", "third"]);
    }

    [Fact]
    public void The_two_sides_of_a_call_are_never_welded_into_one_turn()
    {
        // Both channels label their first speaker 0, because a provider numbers speakers within
        // a channel. Merging on the label alone would make this one turn saying both halves.
        var turns = Turns.Group([
            Segment(0.0, 1.0, AudioChannel.Loopback, "speaker_0", "are you there?"),
            Segment(1.0, 2.0, AudioChannel.Microphone, "speaker_0", "yes"),
        ]);

        turns.Count.ShouldBe(2);
        turns[0].Channel.ShouldBe(AudioChannel.Loopback);
        turns[1].Channel.ShouldBe(AudioChannel.Microphone);
    }

    [Fact]
    public void Two_speakers_of_one_track_stay_two_turns()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, null, "speaker_0", "first"),
            Segment(1.0, 2.0, null, "speaker_1", "second"),
            Segment(2.0, 3.0, null, "speaker_1", "still second"),
        ]);

        turns.Select(turn => turn.SpeakerLabel).ShouldBe(["speaker_0", "speaker_1"]);
        turns[1].Text.ShouldBe("second still second");
    }

    [Fact]
    public void A_speaker_who_comes_back_gets_a_turn_of_their_own()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, null, "speaker_0", "a"),
            Segment(1.0, 2.0, null, "speaker_1", "b"),
            Segment(2.0, 3.0, null, "speaker_0", "c"),
        ]);

        turns.Select(turn => turn.Text).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Speech_with_nothing_in_it_is_not_a_turn()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, AudioChannel.Microphone, "speaker_0", "   "),
            Segment(1.0, 2.0, AudioChannel.Microphone, "speaker_0", string.Empty),
            Segment(2.0, 3.0, AudioChannel.Microphone, "speaker_0", " something "),
        ]);

        turns.Count.ShouldBe(1);
        turns[0].Text.ShouldBe("something");
        // The turn starts where somebody spoke, not where the provider returned nothing.
        turns[0].Start.ShouldBe(Duration.FromMilliseconds(2_000));
    }

    [Fact]
    public void A_meeting_nobody_spoke_in_has_no_turns()
    {
        Turns.Group([]).ShouldBeEmpty();
    }

    [Fact]
    public void A_turn_never_ends_before_it_started()
    {
        // Segments that overlap: the second is contained in the first. Taking the last end would
        // move the turn's end backwards, and a span the renderer cannot draw.
        var turns = Turns.Group([
            Segment(0.0, 9.0, null, "speaker_0", "a long one"),
            Segment(1.0, 2.0, null, "speaker_0", "inside it"),
        ]);

        turns.Count.ShouldBe(1);
        turns[0].End.ShouldBe(Duration.FromMilliseconds(9_000));
    }

    [Fact]
    public void Grouping_the_same_response_again_puts_every_turn_back_where_it_was()
    {
        // What the citation contract needs: a rebuild deletes the turns and projects them again,
        // and a claim anchored on a position has to land on the same turn afterwards.
        var segments = new[]
        {
            Segment(0.0, 1.0, AudioChannel.Loopback, "speaker_0", "a"),
            Segment(1.0, 2.0, AudioChannel.Microphone, "speaker_0", "b"),
            Segment(2.0, 3.0, AudioChannel.Loopback, "speaker_1", "c"),
        };

        Turns.Group(segments).ShouldBe(Turns.Group(segments));
    }

    /// <summary>
    /// A turn is as certain as its text, weighted by how long each part of it lasted. The lowest of
    /// the parts was the alternative, and it makes every long turn look untrustworthy because one
    /// bad part is enough — and turns get longer the less somebody is interrupted.
    /// </summary>
    [Fact]
    public void A_turns_confidence_is_the_mean_of_its_parts_weighted_by_their_length()
    {
        var turns = Turns.Group([
            Segment(0.0, 8.0, null, "speaker_0", "eight clear seconds", 0.98),
            Segment(8.0, 10.0, null, "speaker_0", "two doubtful ones", 0.61),
        ]);

        // (0.98 * 8000 + 0.61 * 2000) / 10000, and not 0.61 and not 0.795.
        turns.ShouldHaveSingleItem().Confidence!.Value.ShouldBe(0.906, tolerance: 1e-9);
    }

    /// <summary>
    /// A part the response said nothing about is left out of the mean. Counting it as zero would be
    /// a claim the response never made, and it would drag the turn down for saying nothing.
    /// </summary>
    [Fact]
    public void A_part_the_response_said_nothing_about_is_not_a_zero()
    {
        var turns = Turns.Group([
            Segment(0.0, 1.0, null, "speaker_0", "measured", 0.9),
            Segment(1.0, 2.0, null, "speaker_0", "not measured"),
        ]);

        turns.ShouldHaveSingleItem().Confidence!.Value.ShouldBe(0.9, tolerance: 1e-9);
    }

    [Fact]
    public void A_turn_the_response_said_nothing_about_carries_no_confidence()
    {
        Turns.Group([Segment(0.0, 1.0, null, "speaker_0", "nothing reported")])
            .ShouldHaveSingleItem().Confidence.ShouldBeNull();
    }

    /// <summary>
    /// A turn with no length has no weights to divide by, and averaging what there is beats
    /// dividing by zero over a case a provider is free to hand back.
    /// </summary>
    [Fact]
    public void A_turn_that_lasts_no_time_still_answers()
    {
        var turns = Turns.Group([
            Segment(1.0, 1.0, null, "speaker_0", "one"),
            Segment(1.0, 1.0, null, "speaker_0", "two"),
        ]);

        turns.ShouldHaveSingleItem().Confidence.ShouldBeNull();

        Turns.Group([
            Segment(1.0, 1.0, null, "speaker_0", "one", 0.4),
            Segment(1.0, 1.0, null, "speaker_0", "two", 0.6),
        ]).ShouldHaveSingleItem().Confidence!.Value.ShouldBe(0.5, tolerance: 1e-9);
    }

    private static SpeechSegment Segment(
        double start,
        double end,
        AudioChannel? channel,
        string label,
        string text,
        double? confidence = null) =>
        new(Duration.FromSeconds(start), Duration.FromSeconds(end), channel, label, text, confidence);
}
