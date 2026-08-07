using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Tests.Audio;

public class AudioChannelTests
{
    [Fact]
    public void The_loopback_is_channel_zero_and_the_microphone_is_channel_one()
    {
        // The numbers are the contract Deepgram answers with, not an implementation detail.
        ((int)AudioChannel.Loopback).ShouldBe(0);
        ((int)AudioChannel.Microphone).ShouldBe(1);

        CapturedAudio.IndexOf(AudioChannel.Loopback).ShouldBe(0);
        CapturedAudio.IndexOf(AudioChannel.Microphone).ShouldBe(1);
    }

    [Fact]
    public void Interleaving_puts_the_loopback_first_in_every_frame()
    {
        short[] loopback = [10, 20, 30];
        short[] microphone = [-10, -20, -30];

        CapturedAudio.Interleave(loopback, microphone).ShouldBe([10, -10, 20, -20, 30, -30]);
    }

    [Fact]
    public void Deinterleaving_gives_each_source_back_untouched()
    {
        short[] loopback = [10, 20, 30];
        short[] microphone = [-10, -20, -30];

        var interleaved = CapturedAudio.Interleave(loopback, microphone);

        CapturedAudio.Deinterleave(interleaved, AudioChannel.Loopback).ShouldBe(loopback);
        CapturedAudio.Deinterleave(interleaved, AudioChannel.Microphone).ShouldBe(microphone);
    }

    [Fact]
    public void Deinterleaving_reads_the_channel_asked_for_and_not_the_other_one()
    {
        short[] interleaved = [10, -10, 20, -20];

        CapturedAudio.Deinterleave(interleaved, AudioChannel.Loopback).ShouldBe([10, 20]);
        CapturedAudio.Deinterleave(interleaved, AudioChannel.Microphone).ShouldBe([-10, -20]);
    }

    [Fact]
    public void A_buffer_that_is_not_whole_frames_is_rejected()
    {
        short[] interleaved = [10, -10, 20];

        Should.Throw<AudioContractException>(
            () => CapturedAudio.Deinterleave(interleaved, AudioChannel.Loopback));
    }

    [Fact]
    public void Two_sources_of_different_length_cannot_be_interleaved()
    {
        short[] loopback = [10, 20, 30];
        short[] microphone = [-10, -20];

        Should.Throw<AudioContractException>(() => CapturedAudio.Interleave(loopback, microphone));
    }

    [Fact]
    public void What_the_application_captures_is_stereo_at_sixteen_kilohertz()
    {
        CapturedAudio.ChannelCount.ShouldBe(2);
        CapturedAudio.SampleRate.ShouldBe(16_000);
        CapturedAudio.BitsPerSample.ShouldBe(16);
    }
}
