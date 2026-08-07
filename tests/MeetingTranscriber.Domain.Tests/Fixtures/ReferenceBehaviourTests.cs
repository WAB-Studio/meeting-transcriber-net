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
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
    public void A_real_meeting_becomes_turns_a_citation_can_land_on(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.ShouldNotBeEmpty();
        turns.Select(turn => turn.Ordinal).ShouldBe(Enumerable.Range(0, turns.Count));
        turns.ShouldAllBe(turn => turn.Text.Trim().Length > 0);
        turns.ShouldAllBe(turn => turn.End >= turn.Start);
    }

    [Theory]
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
    public void Turns_run_forwards(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.Select(turn => turn.Start).ShouldBeInOrder();
    }

    [Theory]
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
    public void Grouping_says_less_than_the_provider_did_and_loses_nothing(string name)
    {
        var segments = DeepgramFixtures.Segments(name);
        var turns = Turns.Group(segments);

        turns.Count.ShouldBeLessThan(segments.Count);
        Words(turns.Select(turn => turn.Text)).ShouldBe(Words(segments.Select(segment => segment.Text)));
    }

    [Theory]
    [MemberData(nameof(DeepgramFixtures.EachWithBothSidesHeard), MemberType = typeof(DeepgramFixtures))]
    public void Both_sides_of_a_call_get_their_own_turns(string name)
    {
        var turns = Turns.Group(DeepgramFixtures.Segments(name));

        turns.Select(turn => turn.Channel).Distinct().ShouldBe(
            [AudioChannel.Loopback, AudioChannel.Microphone],
            ignoreOrder: true);
    }

    /// <summary>
    /// Two consecutive turns are two turns for a reason: either somebody else is speaking, or the
    /// same speaker came back after a silence longer than the rule allows. A run of real speech is
    /// where it would show if merging keyed on the wrong thing.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
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
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
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

        turns.ShouldAllBe(turn => turn.Channel == AudioChannel.Loopback);
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

    /// <summary>
    /// The rule, against every real response: the microphone settles the user exactly when it
    /// caught one speaker, and everybody else waits. Stated as the biconditional so it keeps
    /// checking something whichever way a new fixture falls.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeepgramFixtures.Each), MemberType = typeof(DeepgramFixtures))]
    public void Only_a_microphone_that_caught_one_speaker_settles_who_it_was(string name)
    {
        var segments = DeepgramFixtures.Segments(name);
        var microphone = DeepgramFixtures.ProfileOf(name).MicrophoneChannel();

        var resolution = Speakers.Resolve(DeepgramFixtures.ProfileOf(name), segments);
        var onMicrophone = segments
            .Where(segment => segment.Channel == microphone && microphone is not null)
            .Select(segment => segment.SpeakerLabel)
            .Distinct()
            .ToArray();

        (resolution.Mine is not null).ShouldBe(onMicrophone.Length == 1);
        segments.Select(segment => segment.SpeakerLabel).Distinct()
            .ShouldBe([.. resolution.NeedingAPerson, .. resolution.Mine is null ? Array.Empty<string>() : [resolution.Mine]], ignoreOrder: true);
    }

    /// <summary>
    /// Why the rule is not "channel 1 is the user". This is a real meeting in which two people sat
    /// at one microphone, and the old contract would have signed both sets of words with one name.
    /// </summary>
    [Fact]
    public void Two_people_at_one_microphone_is_a_meeting_that_actually_happened()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            DeepgramFixtures.Segments(DeepgramFixtures.TwoChannelLong));

        resolution.Mine.ShouldBeNull();
        resolution.NeedingAPerson.ShouldContain("ch1:speaker_0");
        resolution.NeedingAPerson.ShouldContain("ch1:speaker_1");
    }

    /// <summary>
    /// The other side of the same rule, and the case the set had no real response for: one person
    /// at their desk on a call, which is how most meetings are recorded. Until this fixture the
    /// biconditional above only ever checked that nothing was settled, so the one assignment the
    /// system makes without asking was proved by hand written segments alone.
    /// </summary>
    [Fact]
    public void One_voice_on_the_microphone_settles_the_user_and_leaves_the_meeting_to_a_person()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            DeepgramFixtures.Segments(DeepgramFixtures.TwoChannelOneVoiceMe));

        resolution.Mine.ShouldBe("ch1:speaker_0");

        // Three people on the other end of the call, and the recording says nothing about which
        // of them is which. Settling the microphone does not settle them.
        resolution.NeedingAPerson.ShouldBe(["ch0:speaker_0", "ch0:speaker_1", "ch0:speaker_2"]);
    }

    private static int Words(IEnumerable<string> texts) =>
        texts.Sum(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
}
