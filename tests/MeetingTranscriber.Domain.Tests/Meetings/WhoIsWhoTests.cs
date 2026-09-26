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
