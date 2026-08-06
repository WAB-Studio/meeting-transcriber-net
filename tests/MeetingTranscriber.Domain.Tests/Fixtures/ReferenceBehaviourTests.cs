using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Domain.Tests.Fixtures;

/// <summary>
/// The same rules against real responses, which is where the cases nobody would think to write
/// by hand are. Offline: it reads the committed fixtures and nothing else.
/// </summary>
public class ReferenceBehaviourTests
{
    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void A_real_meeting_becomes_turns_a_citation_can_land_on(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.ShouldNotBeEmpty();
        turns.Select(turn => turn.Ordinal).ShouldBe(Enumerable.Range(0, turns.Count));
        turns.ShouldAllBe(turn => turn.Text.Trim().Length > 0);
        turns.ShouldAllBe(turn => turn.End >= turn.Start);
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void Turns_run_forwards(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.Select(turn => turn.Start).ShouldBeInOrder();
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void Grouping_says_less_than_the_provider_did_and_loses_nothing(string name)
    {
        var segments = DeepgramFixtures.Segments(name);
        var turns = Turns.Group(segments);

        turns.Count.ShouldBeLessThan(segments.Count);
        Words(turns.Select(turn => turn.Text)).ShouldBe(Words(segments.Select(segment => segment.Text)));
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    public void Both_sides_of_a_call_get_their_own_turns(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.Select(turn => turn.Channel).Distinct().ShouldBe(
            [AudioChannel.Others, AudioChannel.Me],
            ignoreOrder: true);
    }

    /// <summary>
    /// Two consecutive turns are two turns for a reason: either somebody else is speaking, or the
    /// same speaker came back after a silence longer than the rule allows. A run of real speech is
    /// where it would show if merging keyed on the wrong thing.
    /// </summary>
    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void Nothing_that_could_have_been_one_turn_was_left_as_two(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.Zip(turns.Skip(1))
            .ShouldAllBe(pair => pair.First.Channel != pair.Second.Channel
                || pair.First.SpeakerLabel != pair.Second.SpeakerLabel
                || pair.Second.Start > pair.First.End + Turns.MaxSilence);
    }

    /// <summary>
    /// A meeting where one side never interrupts used to come back as a single turn with the
    /// whole recording inside it, and a citation against that checks nothing: every timestamp is
    /// its start and every quote belongs to it.
    /// </summary>
    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void No_turn_swallows_the_meeting_it_belongs_to(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));
        var meeting = turns[^1].End - turns[0].Start;

        turns.Count.ShouldBeGreaterThan(1);
        turns.ShouldAllBe(turn => (turn.End - turn.Start).Milliseconds * 2 < meeting.Milliseconds);
    }

    [Fact]
    public void A_channel_nobody_spoke_on_contributes_no_turns()
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(DeepgramFixtures.TwoChannelSilentMe));

        turns.ShouldAllBe(turn => turn.Channel == AudioChannel.Others);
    }

    [Fact]
    public void A_single_track_keeps_its_speakers_apart_without_a_channel_to_help()
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(DeepgramFixtures.SingleTrackDiarized));

        turns.ShouldAllBe(turn => turn.Channel == null);
        turns.Select(turn => turn.SpeakerLabel).Distinct().Count().ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// The one thing a provider's raw ordering is not: the conversation. It happens to be in
    /// order today, which is exactly why the sort has to stay — nothing announces the day it
    /// stops being.
    /// </summary>
    [Fact]
    public void Sorting_is_still_the_reason_a_transcript_reads_as_a_conversation()
    {
        var segments = DeepgramFixtures.Segments(DeepgramFixtures.TwoChannelShort);
        var shuffled = segments.OrderBy(segment => segment.Channel).ThenBy(segment => segment.Start);

        Turns.Group(shuffled).ShouldBe(Turns.Group(segments));
    }

    private static int Words(IEnumerable<string> texts) =>
        texts.Sum(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
}
