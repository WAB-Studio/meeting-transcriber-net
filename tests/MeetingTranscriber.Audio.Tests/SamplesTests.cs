using System.Buffers.Binary;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A device's bytes read as numbers. The meter and the timeline want different things out of the
/// same block, and this is what they both go through.
/// </summary>
public class SamplesTests
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat MonoPcm = new(16_000, 1, 16, SampleEncoding.Pcm);

    [Fact]
    public void A_stereo_frame_becomes_the_average_of_its_channels()
    {
        var mono = new float[2];

        Samples.ToMono(Floats(1f, 0f, 0.5f, 0.5f), StereoFloat, mono);

        mono[0].ShouldBe(0.5f);
        mono[1].ShouldBe(0.5f);
    }

    /// <summary>
    /// Averaged rather than taking the first channel: which side of a stereo pair a device puts
    /// the speech on is the hardware's business, and reading only channel 0 would record a
    /// hard-panned source as a meeting nobody spoke in.
    /// </summary>
    [Fact]
    public void A_source_on_one_side_of_a_stereo_pair_is_not_read_as_silence()
    {
        var mono = new float[1];

        Samples.ToMono(Floats(0f, 1f), StereoFloat, mono);

        mono[0].ShouldBe(0.5f);
    }

    [Fact]
    public void Sixteen_bit_full_scale_reads_as_full_scale()
    {
        var mono = new float[2];

        Samples.ToMono(Shorts(short.MinValue, 0), MonoPcm, mono);

        mono[0].ShouldBe(-1f);
        mono[1].ShouldBe(0f);
    }

    /// <summary>
    /// A device is free to hand over anything a float holds, and one NaN would take the frames
    /// around it too once the resampler had smeared it across them.
    /// </summary>
    [Fact]
    public void A_sample_that_is_not_a_number_does_not_poison_the_frames_after_it()
    {
        var mono = new float[2];

        Samples.ToMono(Floats(float.NaN, float.NaN, 0.5f, 0.5f), StereoFloat, mono);

        mono[0].ShouldBe(0f);
        mono[1].ShouldBe(0.5f);
    }

    [Fact]
    public void Bytes_that_are_not_whole_frames_are_refused()
    {
        Should.Throw<AudioCaptureException>(() => Samples.FramesIn(6, StereoFloat))
            .Message.ShouldContain("6 bytes");
    }

    /// <summary>
    /// The one that matters: a build that could meter a width it could not resample would show a
    /// healthy level for a whole meeting and fail once somebody stopped it.
    /// </summary>
    [Fact]
    public void A_width_this_build_cannot_read_is_refused_by_the_meter_and_by_the_timeline_alike()
    {
        var twentyFourBit = new StreamFormat(48_000, 2, 24, SampleEncoding.Pcm);

        Should.Throw<AudioCaptureException>(() => Samples.ToMono(new byte[300], twentyFourBit, new float[50]));
        Should.Throw<AudioCaptureException>(() => Levels.Peak(new byte[300], twentyFourBit));
    }

    [Fact]
    public void Full_scale_survives_the_trip_to_the_interchange_format()
    {
        var pcm = new short[4];

        Samples.ToPcm16([1f, -1f, 0f, float.NaN], pcm);

        pcm[0].ShouldBe(short.MaxValue);
        pcm[1].ShouldBe((short)-short.MaxValue);
        pcm[2].ShouldBe((short)0);
        pcm[3].ShouldBe((short)0);
    }

    /// <summary>A float device is free to clip, and what comes out is the loudest a short holds.</summary>
    [Fact]
    public void A_sample_past_full_scale_is_held_at_full_scale_rather_than_wrapping()
    {
        var pcm = new short[2];

        Samples.ToPcm16([1.5f, -1.5f], pcm);

        pcm[0].ShouldBe(short.MaxValue);
        pcm[1].ShouldBe(short.MinValue);
    }

    private static byte[] Floats(params float[] samples)
    {
        var block = new byte[samples.Length * 4];
        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(index * 4, 4), samples[index]);
        }

        return block;
    }

    private static byte[] Shorts(params short[] samples)
    {
        var block = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(index * 2, 2), samples[index]);
        }

        return block;
    }
}
