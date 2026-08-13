using System.Buffers.Binary;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a block of bytes turns out to be, which is the number a person reads off a meter and the
/// only evidence, while a meeting is running, that a source is hearing anything at all.
/// </summary>
public class LevelsTests
{
    private static readonly StreamFormat Float = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat Pcm = new(16_000, 1, 16, SampleEncoding.Pcm);

    [Fact]
    public void A_block_of_nothing_reads_as_silent()
    {
        var reading = Levels.Peak(new byte[4096], Float);

        reading.IsSilent.ShouldBeTrue();
        reading.Decibels.ShouldBe(float.NegativeInfinity);
        reading.ToString().ShouldBe("silent");
    }

    [Fact]
    public void A_full_scale_float_sample_reads_as_full_scale()
    {
        var reading = Levels.Peak(Floats(0, 0.25f, -1, 0.5f), Float);

        reading.Peak.ShouldBe(1);
        reading.IsSilent.ShouldBeFalse();
        reading.Decibels.ShouldBe(0);
    }

    [Fact]
    public void Half_of_full_scale_is_six_decibels_down()
    {
        var reading = Levels.Peak(Floats(0.5f, -0.5f), Float);

        reading.Decibels.ShouldBe(-6.02f, 0.01f);
    }

    /// <summary>
    /// A device is free to hand over anything a float holds. Since NaN wins every comparison it
    /// takes part in, one of them would otherwise stick to the meter for the rest of a recording.
    /// </summary>
    [Fact]
    public void A_sample_that_is_not_a_number_does_not_take_the_meter_with_it()
    {
        var reading = Levels.Peak(Floats(0.25f, float.NaN, float.PositiveInfinity), Float);

        reading.Peak.ShouldBe(0.25f);
    }

    /// <summary>
    /// Float samples are not bounded at one, and a source clipping is something to see rather
    /// than something to round away.
    /// </summary>
    [Fact]
    public void A_sample_past_full_scale_is_reported_past_full_scale()
    {
        var reading = Levels.Peak(Floats(1.5f), Float);

        reading.Peak.ShouldBe(1.5f);
        reading.Decibels.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The loudest sample a 16 bit device can send is the negative one, which reaches a step
    /// further than the positive side. Dividing by the positive side would put full scale past
    /// one, and every meter would then read a fraction of a decibel high.
    /// </summary>
    [Fact]
    public void The_loudest_a_sixteen_bit_device_can_be_reads_as_full_scale()
    {
        Levels.Peak(Shorts(short.MinValue), Pcm).Peak.ShouldBe(1);
        Levels.Peak(Shorts(short.MaxValue), Pcm).Peak.ShouldBe(1, 0.0001);
    }

    [Fact]
    public void Sixteen_bit_silence_is_silence()
    {
        Levels.Peak(Shorts(0, 0, 0), Pcm).IsSilent.ShouldBeTrue();
    }

    [Fact]
    public void Bytes_that_are_not_whole_samples_are_refused()
    {
        var stray = new byte[6];

        Should.Throw<AudioCaptureException>(() => Levels.Peak(stray, Float))
            .Message.ShouldContain("6 bytes");
    }

    /// <summary>
    /// The failure that has to be loud: a format nothing here decodes has to stop the capture,
    /// because the alternative is a meter reading silent through a meeting that was recorded fine.
    /// </summary>
    [Fact]
    public void A_width_this_build_cannot_read_is_refused_rather_than_metered()
    {
        var twentyFourBit = new StreamFormat(48_000, 2, 24, SampleEncoding.Pcm);

        Should.Throw<AudioCaptureException>(() => Levels.Peak(new byte[300], twentyFourBit))
            .Message.ShouldContain("24 bit");
    }

    /// <summary>
    /// The same refusal, asked before a device is opened rather than answered by its first block.
    /// A capture that started and died a second in is one somebody has already begun holding a
    /// meeting into.
    /// </summary>
    [Fact]
    public void A_format_that_could_never_be_metered_is_refused_before_anything_is_recorded()
    {
        Should.Throw<AudioCaptureException>(
            () => Levels.EnsureMeterable(new StreamFormat(48_000, 2, 24, SampleEncoding.Pcm)));

        Should.NotThrow(() => Levels.EnsureMeterable(Float));
        Should.NotThrow(() => Levels.EnsureMeterable(Pcm));
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
