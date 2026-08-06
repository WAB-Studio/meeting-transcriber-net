using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Tests.Audio;

public class SourceProfileTests
{
    [Fact]
    public void Multichannel_is_two_channels_and_diarize_is_one()
    {
        SourceProfile.Multichannel.ChannelCount().ShouldBe(2);
        SourceProfile.Diarize.ChannelCount().ShouldBe(1);
    }

    [Theory]
    [InlineData(SourceProfile.Multichannel, 2)]
    [InlineData(SourceProfile.Diarize, 1)]
    public void A_profile_accepts_the_channel_count_it_promises(SourceProfile profile, int channelCount)
    {
        Should.NotThrow(() => profile.EnsureChannelCount(channelCount));
    }

    [Theory]
    [InlineData(SourceProfile.Multichannel, 1)]
    [InlineData(SourceProfile.Multichannel, 3)]
    [InlineData(SourceProfile.Diarize, 2)]
    [InlineData(SourceProfile.Diarize, 0)]
    public void A_profile_rejects_a_channel_count_that_is_not_its_own(SourceProfile profile, int channelCount)
    {
        Should.Throw<AudioContractException>(() => profile.EnsureChannelCount(channelCount));
    }

    /// <summary>
    /// The microphone and nothing else. Channel 0 is the whole meeting, so a multichannel capture
    /// knows who one of its two channels belongs to and nothing about who is on the other.
    /// </summary>
    [Fact]
    public void The_only_speaker_the_contract_names_is_the_one_on_the_microphone()
    {
        SourceProfile.Multichannel.DeterministicSpeakerChannel().ShouldBe(AudioChannel.Me);
        SourceProfile.Diarize.DeterministicSpeakerChannel().ShouldBeNull();
    }

    /// <summary>
    /// Both profiles diarize. Turning it off for multichannel would leave the channel carrying
    /// the room with one label for everybody on it.
    /// </summary>
    [Theory]
    [InlineData(SourceProfile.Multichannel)]
    [InlineData(SourceProfile.Diarize)]
    public void Every_profile_asks_the_provider_to_tell_speakers_apart(SourceProfile profile)
    {
        profile.RequestsDiarization().ShouldBeTrue();
    }

    [Fact]
    public void A_profile_the_domain_does_not_have_reaches_neither_answer()
    {
        const SourceProfile unknown = (SourceProfile)99;

        Should.Throw<AudioContractException>(() => unknown.DeterministicSpeakerChannel());
        Should.Throw<AudioContractException>(() => unknown.RequestsDiarization());
    }

    [Theory]
    [InlineData(SourceProfile.Multichannel, "multichannel")]
    [InlineData(SourceProfile.Diarize, "diarize")]
    public void Profiles_round_trip_through_the_name_they_are_stored_under(SourceProfile profile, string wireName)
    {
        profile.ToWireName().ShouldBe(wireName);
        SourceProfiles.FromWireName(wireName).ShouldBe(profile);
    }

    [Fact]
    public void An_unknown_stored_name_is_not_guessed_at()
    {
        Should.Throw<AudioContractException>(() => SourceProfiles.FromWireName("Multichannel"));
    }

    [Fact]
    public void Audio_captured_by_the_application_matches_the_multichannel_profile()
    {
        CapturedAudio.Profile.ShouldBe(SourceProfile.Multichannel);
        Should.NotThrow(() => CapturedAudio.Profile.EnsureChannelCount(CapturedAudio.ChannelCount));
    }
}
