using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Tests.Audio;

public class AudioChannelTests
{
    [Fact]
    public void The_meeting_is_channel_zero_and_the_user_is_channel_one()
    {
        // The numbers are the contract Deepgram answers with, not an implementation detail.
        ((int)AudioChannel.Others).ShouldBe(0);
        ((int)AudioChannel.Me).ShouldBe(1);

        CapturedAudio.IndexOf(AudioChannel.Others).ShouldBe(0);
        CapturedAudio.IndexOf(AudioChannel.Me).ShouldBe(1);
    }

    [Fact]
    public void Interleaving_puts_the_meeting_first_in_every_frame()
    {
        short[] others = [10, 20, 30];
        short[] me = [-10, -20, -30];

        CapturedAudio.Interleave(others, me).ShouldBe([10, -10, 20, -20, 30, -30]);
    }

    [Fact]
    public void Deinterleaving_gives_each_source_back_untouched()
    {
        short[] others = [10, 20, 30];
        short[] me = [-10, -20, -30];

        var interleaved = CapturedAudio.Interleave(others, me);

        CapturedAudio.Deinterleave(interleaved, AudioChannel.Others).ShouldBe(others);
        CapturedAudio.Deinterleave(interleaved, AudioChannel.Me).ShouldBe(me);
    }

    [Fact]
    public void Deinterleaving_reads_the_channel_asked_for_and_not_the_other_one()
    {
        short[] interleaved = [10, -10, 20, -20];

        CapturedAudio.Deinterleave(interleaved, AudioChannel.Others).ShouldBe([10, 20]);
        CapturedAudio.Deinterleave(interleaved, AudioChannel.Me).ShouldBe([-10, -20]);
    }

    [Fact]
    public void A_buffer_that_is_not_whole_frames_is_rejected()
    {
        short[] interleaved = [10, -10, 20];

        Should.Throw<AudioContractException>(
            () => CapturedAudio.Deinterleave(interleaved, AudioChannel.Others));
    }

    [Fact]
    public void Two_sources_of_different_length_cannot_be_interleaved()
    {
        short[] others = [10, 20, 30];
        short[] me = [-10, -20];

        Should.Throw<AudioContractException>(() => CapturedAudio.Interleave(others, me));
    }

    [Fact]
    public void What_the_application_captures_is_stereo_at_sixteen_kilohertz()
    {
        CapturedAudio.ChannelCount.ShouldBe(2);
        CapturedAudio.SampleRate.ShouldBe(16_000);
        CapturedAudio.BitsPerSample.ShouldBe(16);
    }
}
