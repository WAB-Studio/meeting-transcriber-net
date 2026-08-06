namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// The internal interchange format, and the only place a channel number is read. Everything
/// else names the channel it wants, so inverting the contract breaks here and nowhere else.
/// </summary>
public static class CapturedAudio
{
    /// <summary>Channels the application always captures.</summary>
    public const int ChannelCount = 2;

    /// <summary>Sample rate of the interchange format, in hertz.</summary>
    public const int SampleRate = 16_000;

    /// <summary>Bits per sample of the interchange format: linear PCM, no compression.</summary>
    public const int BitsPerSample = 16;

    /// <summary>The profile of anything the application captures itself.</summary>
    public const SourceProfile Profile = SourceProfile.Multichannel;

    /// <summary>
    /// Position of <paramref name="channel"/> inside an interleaved frame. It is the value of
    /// the enum member and nothing else, so the contract lives in one declaration.
    /// </summary>
    public static int IndexOf(AudioChannel channel)
    {
        if (channel is not (AudioChannel.Others or AudioChannel.Me))
        {
            throw new AudioContractException($"Unknown audio channel '{channel}'.");
        }

        return (int)channel;
    }

    /// <summary>
    /// The channel at <paramref name="index"/>, which is both its position inside an interleaved
    /// frame and the channel number a provider reports back about it. The inverse of
    /// <see cref="IndexOf"/>, and here for the same reason: a number becomes a position in one
    /// place or the contract has two copies to keep in step.
    /// </summary>
    public static AudioChannel ChannelAt(int index)
    {
        var channel = (AudioChannel)index;
        if (channel is not (AudioChannel.Others or AudioChannel.Me))
        {
            throw new AudioContractException($"There is no audio channel at index {index}.");
        }

        return channel;
    }

    /// <summary>Lays two equally long sources out as interleaved stereo frames.</summary>
    public static short[] Interleave(ReadOnlySpan<short> others, ReadOnlySpan<short> me)
    {
        if (others.Length != me.Length)
        {
            throw new AudioContractException(
                $"Both sources need the same number of samples, got {others.Length} and {me.Length}.");
        }

        var othersIndex = IndexOf(AudioChannel.Others);
        var meIndex = IndexOf(AudioChannel.Me);
        var interleaved = new short[others.Length * ChannelCount];
        for (var frame = 0; frame < others.Length; frame++)
        {
            var start = frame * ChannelCount;
            interleaved[start + othersIndex] = others[frame];
            interleaved[start + meIndex] = me[frame];
        }

        return interleaved;
    }

    /// <summary>Copies the samples of one channel out of interleaved stereo.</summary>
    public static short[] Deinterleave(ReadOnlySpan<short> interleaved, AudioChannel channel)
    {
        if ((interleaved.Length % ChannelCount) != 0)
        {
            throw new AudioContractException(
                $"Interleaved stereo needs whole frames, got {interleaved.Length} samples.");
        }

        var index = IndexOf(channel);
        var samples = new short[interleaved.Length / ChannelCount];
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = interleaved[(frame * ChannelCount) + index];
        }

        return samples;
    }
}
