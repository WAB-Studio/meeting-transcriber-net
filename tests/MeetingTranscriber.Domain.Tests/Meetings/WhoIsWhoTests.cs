using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// Who spoke in one meeting, built out of its turns and whatever has already been settled: the
/// microphone's own voice first, every other voice numbered by when it first spoke, and what each
/// is quoted by.
/// </summary>
public class WhoIsWhoTests
{
    private static readonly Guid Somebody = Guid.NewGuid();

    [Fact]
    public void The_microphone_that_caught_one_voice_is_its_own_and_comes_first()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0),
            Turn(1, AudioChannel.Microphone, 0, start: 1_000),
        };

        var voices = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices;

        voices.Select(voice => voice.Label).ShouldBe(["ch1:speaker_0", "ch0:speaker_0"]);
        voices[0].IsTheMicrophonesOwn.ShouldBeTrue();
        voices[0].Number.ShouldBe(0);
        voices[1].IsTheMicrophonesOwn.ShouldBeFalse();
        voices[1].Number.ShouldBe(1);
    }

    [Fact]
    public void Every_other_voice_is_numbered_in_the_order_it_first_spoke()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Microphone, 0, start: 0),
            Turn(1, AudioChannel.Loopback, 1, start: 1_000),
            Turn(2, AudioChannel.Loopback, 0, start: 2_000),
        };

        var voices = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices;

        // Labelled by ordinal — speaker_0 sorts before speaker_1 — but heard second, so a numbering
        // by label alone would put it first.
        voices.Select(voice => voice.Label).ShouldBe(["ch1:speaker_0", "ch0:speaker_1", "ch0:speaker_0"]);
        voices[1].Number.ShouldBe(1);
        voices[2].Number.ShouldBe(2);
    }

    [Fact]
    public void A_microphone_that_caught_two_voices_has_no_voice_of_its_own()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Microphone, 0, start: 0),
            Turn(1, AudioChannel.Microphone, 1, start: 1_000),
        };

        var voices = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices;

        voices.ShouldAllBe(voice => !voice.IsTheMicrophonesOwn);
        voices.Select(voice => voice.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void A_single_track_numbers_every_voice()
    {
        var turns = new[]
        {
            Turn(0, null, 0, start: 0),
            Turn(1, null, 1, start: 1_000),
        };

        var voices = WhoIsWho.Of(SourceProfile.Diarize, turns, []).Voices;

        voices.ShouldAllBe(voice => !voice.IsTheMicrophonesOwn);
        voices.Select(voice => voice.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void A_voice_is_quoted_by_its_longest_turn()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 500),
            Turn(1, AudioChannel.Loopback, 0, start: 5_000, length: 3_000),
            Turn(2, AudioChannel.Loopback, 0, start: 9_000, length: 1_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.Quoted.Ordinal.ShouldBe(1);
    }

    [Fact]
    public void A_voice_the_recording_settled_says_so_and_one_a_person_named_does_not()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Microphone, 0, start: 0),
            Turn(1, AudioChannel.Loopback, 0, start: 1_000),
        };

        var assigned = new[]
        {
            new AssignedVoice("ch1:speaker_0", Somebody, "Ada", SpeakerAssignmentSource.Channel),
            new AssignedVoice("ch0:speaker_0", Somebody, "Jo", SpeakerAssignmentSource.Person),
        };

        var voices = WhoIsWho.Of(SourceProfile.Multichannel, turns, assigned).Voices;

        voices.Single(voice => voice.Label == "ch1:speaker_0").SettledByTheRecording.ShouldBeTrue();
        voices.Single(voice => voice.Label == "ch0:speaker_0").SettledByTheRecording.ShouldBeFalse();
    }

    [Fact]
    public void An_assignment_no_turn_carries_is_not_a_voice()
    {
        var turns = new[] { Turn(0, AudioChannel.Loopback, 0, start: 0) };
        var assigned = new[]
        {
            new AssignedVoice("ch0:speaker_7", Somebody, "Renata", SpeakerAssignmentSource.Person),
        };

        var voices = WhoIsWho.Of(SourceProfile.Multichannel, turns, assigned).Voices;

        voices.Select(voice => voice.Label).ShouldBe(["ch0:speaker_0"]);
    }

    [Fact]
    public void A_voice_is_offered_the_longest_stretch_it_spoke_alone_in()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 2_000),
            Turn(1, AudioChannel.Loopback, 0, start: 10_000, length: 5_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.Alone.ShouldNotBeNull();
        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(10_000));
        voice.Alone.To.ShouldBe(Duration.FromMilliseconds(15_000));
    }

    [Fact]
    public void A_stretch_somebody_talked_over_is_cut_where_they_did()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 10_000),
            Turn(1, AudioChannel.Loopback, 1, start: 6_000, length: 1_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices
            .Single(candidate => candidate.Label == SpeakerLabels.For(AudioChannel.Loopback, 0));

        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(0));
        voice.Alone.To.ShouldBe(Duration.FromMilliseconds(6_000));
    }

    [Fact]
    public void Talking_over_from_the_other_channel_counts()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 10_000),
            Turn(1, AudioChannel.Microphone, 0, start: 6_000, length: 1_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices
            .Single(candidate => candidate.Label == SpeakerLabels.For(AudioChannel.Loopback, 0));

        voice.Alone!.To.ShouldBe(Duration.FromMilliseconds(6_000));
    }

    [Fact]
    public void A_voice_that_never_spoke_alone_is_offered_nothing()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 5_000),
            Turn(1, AudioChannel.Loopback, 1, start: 0, length: 5_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices
            .Single(candidate => candidate.Label == SpeakerLabels.For(AudioChannel.Loopback, 0));

        voice.Alone.ShouldBeNull();
    }

    [Fact]
    public void A_long_stretch_is_offered_as_its_first_fifteen_seconds()
    {
        var turns = new[] { Turn(0, AudioChannel.Loopback, 0, start: 0, length: 20_000) };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(0));
        voice.Alone.To.ShouldBe(WhoIsWho.LongestClip);
    }

    [Fact]
    public void A_voice_is_quoted_by_the_turn_its_stretch_is_in()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 8_000),
            Turn(1, AudioChannel.Loopback, 1, start: 1_000, length: 6_000),
            Turn(2, AudioChannel.Loopback, 0, start: 20_000, length: 3_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices
            .Single(candidate => candidate.Label == SpeakerLabels.For(AudioChannel.Loopback, 0));

        voice.Quoted.Ordinal.ShouldBe(2);
        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(20_000));
    }

    [Fact]
    public void A_voice_whose_longest_stretch_is_short_brings_two_more()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 2_000),
            Turn(1, AudioChannel.Loopback, 0, start: 10_000, length: 5_000),
            Turn(2, AudioChannel.Loopback, 0, start: 20_000, length: 1_000),
            Turn(3, AudioChannel.Loopback, 0, start: 30_000, length: 3_000),
            Turn(4, AudioChannel.Loopback, 0, start: 40_000, length: 4_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(10_000));
        voice.OtherStretches.Select(stretch => (stretch.From.Milliseconds, stretch.To.Milliseconds))
            .ShouldBe([(30_000L, 33_000L), (40_000L, 44_000L)]);
        (voice.OtherStretches.Count + 1).ShouldBeLessThanOrEqualTo(WhoIsWho.MostClipsOfOneVoice);
    }

    [Fact]
    public void A_voice_heard_alone_for_a_whole_clip_brings_no_more()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 20_000),
            Turn(1, AudioChannel.Loopback, 0, start: 30_000, length: 3_000),
            Turn(2, AudioChannel.Loopback, 0, start: 40_000, length: 4_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.OtherStretches.ShouldBeEmpty();
    }

    [Fact]
    public void The_other_stretches_never_include_the_first_one_the_voice_spoke_alone_in()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 2_000),
            Turn(1, AudioChannel.Loopback, 0, start: 10_000, length: 5_000),
            Turn(2, AudioChannel.Loopback, 0, start: 20_000, length: 3_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices.Single();

        voice.OtherStretches.Select(stretch => stretch.From.Milliseconds).ShouldBe([20_000L]);
    }

    [Fact]
    public void The_other_stretches_come_from_turns_of_their_own_in_meeting_order()
    {
        var turns = new[]
        {
            Turn(0, AudioChannel.Loopback, 0, start: 0, length: 10_000),
            Turn(1, AudioChannel.Loopback, 1, start: 2_000, length: 1_000),
            Turn(2, AudioChannel.Loopback, 0, start: 20_000, length: 6_000),
            Turn(3, AudioChannel.Loopback, 1, start: 22_000, length: 1_000),
            Turn(4, AudioChannel.Loopback, 0, start: 30_000, length: 1_000),
            Turn(5, AudioChannel.Loopback, 0, start: 40_000, length: 2_000),
        };

        var voice = WhoIsWho.Of(SourceProfile.Multichannel, turns, []).Voices
            .Single(candidate => candidate.Label == SpeakerLabels.For(AudioChannel.Loopback, 0));

        voice.Alone!.From.ShouldBe(Duration.FromMilliseconds(3_000));
        voice.OtherStretches.Select(stretch => (stretch.From.Milliseconds, stretch.To.Milliseconds))
            .ShouldBe([(23_000L, 26_000L), (40_000L, 42_000L)]);
    }

    [Fact]
    public void A_null_argument_throws()
    {
        Should.Throw<ArgumentNullException>(() => WhoIsWho.Of(SourceProfile.Multichannel, null!, []));
        Should.Throw<ArgumentNullException>(() => WhoIsWho.Of(SourceProfile.Multichannel, [], null!));
    }

    private static Turn Turn(int ordinal, AudioChannel? channel, int speaker, long start, long length = 1_000) =>
        new(
            ordinal,
            Duration.FromMilliseconds(start),
            Duration.FromMilliseconds(start + length),
            channel,
            SpeakerLabels.For(channel, speaker),
            $"turno {ordinal}");
}
