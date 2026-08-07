using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Knowledge;

/// <summary>
/// The one assignment the system may make without asking, and the line it stops at.
/// </summary>
public class SpeakersTests
{
    /// <summary>
    /// One voice on the microphone is the user, because there is nobody else it could be. Asking
    /// would be asking a question with one answer.
    /// </summary>
    [Fact]
    public void One_speaker_on_the_microphone_is_the_user_and_nothing_is_asked()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            [Said(AudioChannel.Microphone, 0), Said(AudioChannel.Loopback, 0)]);

        resolution.Mine.ShouldBe("ch1:speaker_0");
        resolution.NeedingAPerson.ShouldBe(["ch0:speaker_0"]);
    }

    /// <summary>
    /// Two people in one room share the microphone. Which of them is the user is the thing the
    /// recording cannot know, so both wait: picking one writes somebody's name onto what the other
    /// said, and a citation carries that further than the guess.
    /// </summary>
    [Fact]
    public void Two_speakers_on_the_microphone_settle_nobody_and_both_wait_for_a_person()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            [Said(AudioChannel.Microphone, 0), Said(AudioChannel.Microphone, 1), Said(AudioChannel.Loopback, 0)]);

        resolution.Mine.ShouldBeNull();
        resolution.NeedsAPerson.ShouldBeTrue();
        resolution.NeedingAPerson.ShouldBe(["ch0:speaker_0", "ch1:speaker_0", "ch1:speaker_1"]);
    }

    /// <summary>
    /// A microphone that caught nobody settles nobody, and leaves nothing to ask about either. It
    /// was transcribed and charged for all the same.
    /// </summary>
    [Fact]
    public void A_microphone_nobody_spoke_into_settles_nobody_and_asks_nothing_about_it()
    {
        var resolution = Speakers.Resolve(SourceProfile.Multichannel, [Said(AudioChannel.Loopback, 0)]);

        resolution.Mine.ShouldBeNull();
        resolution.NeedingAPerson.ShouldBe(["ch0:speaker_0"]);
    }

    /// <summary>
    /// A single track has no microphone channel, so it settles nobody however few speakers it has.
    /// The one voice on an imported recording is not the user by default.
    /// </summary>
    [Fact]
    public void A_single_track_settles_nobody_even_when_only_one_person_speaks()
    {
        var resolution = Speakers.Resolve(SourceProfile.Diarize, [Said(null, 0)]);

        resolution.Mine.ShouldBeNull();
        resolution.NeedingAPerson.ShouldBe(["speaker_0"]);
    }

    /// <summary>
    /// Saying nothing is not being heard: an empty stretch of speech neither becomes a speaker to
    /// resolve nor stops the microphone settling the one person who did speak.
    /// </summary>
    [Fact]
    public void A_speaker_who_said_nothing_is_not_somebody_to_resolve()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            [Said(AudioChannel.Microphone, 0), Said(AudioChannel.Microphone, 1, "   ")]);

        resolution.Mine.ShouldBe("ch1:speaker_0");
        resolution.NeedingAPerson.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_speaker_heard_all_meeting_is_still_one_speaker()
    {
        var resolution = Speakers.Resolve(
            SourceProfile.Multichannel,
            [Said(AudioChannel.Microphone, 0), Said(AudioChannel.Microphone, 0), Said(AudioChannel.Microphone, 0)]);

        resolution.Mine.ShouldBe("ch1:speaker_0");
        resolution.NeedingAPerson.ShouldBeEmpty();
    }

    [Fact]
    public void A_profile_the_domain_does_not_have_reaches_no_answer()
    {
        const SourceProfile unknown = (SourceProfile)99;

        Should.Throw<AudioContractException>(() => Speakers.Resolve(unknown, [Said(null, 0)]));
    }

    private static SpeechSegment Said(AudioChannel? channel, int speaker, string text = "Lento.") => new(
        Duration.Zero,
        Duration.FromMilliseconds(1_000),
        channel,
        SpeakerLabels.For(channel, speaker),
        text);
}
