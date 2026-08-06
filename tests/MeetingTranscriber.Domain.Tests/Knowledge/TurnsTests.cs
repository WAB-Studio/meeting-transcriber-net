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
            Segment(0.5, 2.0, AudioChannel.Others, "speaker_0", "morning, can you hear me?"),
            Segment(9.2, 11.0, AudioChannel.Others, "speaker_1", "different person here"),
            Segment(3.1, 4.0, AudioChannel.Me, "speaker_0", "loud and clear"),
            Segment(12.0, 13.0, AudioChannel.Me, "speaker_0", "agreed"),
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
            Segment(5.0, 6.0, AudioChannel.Me, "speaker_0", "second"),
            Segment(1.0, 2.0, AudioChannel.Others, "speaker_0", "first"),
        ]);

        turns.Select(turn => turn.Ordinal).ShouldBe([0, 1]);
        turns[0].Text.ShouldBe("first");
    }

    [Fact]
    public void Consecutive_speech_from_one_speaker_is_one_turn()
    {
        var turns = Turns.Group([
            Segment(0.0, 0.9, AudioChannel.Me, "speaker_0", "First."),
            Segment(1.0, 1.9, AudioChannel.Me, "speaker_0", "Second."),
            Segment(2.0, 2.9, AudioChannel.Others, "speaker_0", "Third."),
        ]);

        turns.Count.ShouldBe(2);
        turns[0].Text.ShouldBe("First. Second.");
        turns[0].Start.ShouldBe(Duration.Zero);
        turns[0].End.ShouldBe(Duration.FromMilliseconds(1_900));
    }

    [Fact]
    public void The_two_sides_of_a_call_are_never_welded_into_one_turn()
    {
        // Both channels label their first speaker 0, because a provider numbers speakers within
        // a channel. Merging on the label alone would make this one turn saying both halves.
        var turns = Turns.Group([
            Segment(0.0, 1.0, AudioChannel.Others, "speaker_0", "are you there?"),
            Segment(1.0, 2.0, AudioChannel.Me, "speaker_0", "yes"),
        ]);

        turns.Count.ShouldBe(2);
        turns[0].Channel.ShouldBe(AudioChannel.Others);
        turns[1].Channel.ShouldBe(AudioChannel.Me);
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
            Segment(0.0, 1.0, AudioChannel.Me, "speaker_0", "   "),
            Segment(1.0, 2.0, AudioChannel.Me, "speaker_0", string.Empty),
            Segment(2.0, 3.0, AudioChannel.Me, "speaker_0", " something "),
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
            Segment(0.0, 1.0, AudioChannel.Others, "speaker_0", "a"),
            Segment(1.0, 2.0, AudioChannel.Me, "speaker_0", "b"),
            Segment(2.0, 3.0, AudioChannel.Others, "speaker_1", "c"),
        };

        Turns.Group(segments).ShouldBe(Turns.Group(segments));
    }

    private static SpeechSegment Segment(
        double start,
        double end,
        AudioChannel? channel,
        string label,
        string text) =>
        new(Duration.FromSeconds(start), Duration.FromSeconds(end), channel, label, text);
}
