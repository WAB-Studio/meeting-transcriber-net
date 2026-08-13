using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a device says it is handing over, in this application's terms. Shared mode gives an
/// extensible format rather than a plain one, so reading it is not a formality.
/// </summary>
public class StreamFormatTests
{
    [Fact]
    public void A_float_format_is_read_as_float_samples()
    {
        var format = StreamFormat.Of(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        format.SampleRate.ShouldBe(48_000);
        format.Channels.ShouldBe(2);
        format.BitsPerSample.ShouldBe(32);
        format.BytesPerSample.ShouldBe(4);
        format.Encoding.ShouldBe(SampleEncoding.IeeeFloat);
    }

    [Fact]
    public void An_integer_format_is_read_as_integer_samples()
    {
        var format = StreamFormat.Of(new WaveFormat(16_000, 16, 1));

        format.Encoding.ShouldBe(SampleEncoding.Pcm);
        format.BytesPerSample.ShouldBe(2);
        format.ToString().ShouldBe("16000 Hz, 1 ch, 16 bit pcm");
    }

    /// <summary>
    /// What WASAPI actually hands over in shared mode. Left as an extensible format it reads as
    /// neither integer nor float, and the meter would refuse every real capture.
    /// </summary>
    [Fact]
    public void The_extensible_format_a_device_really_gives_is_reduced_to_what_it_wraps()
    {
        var format = StreamFormat.Of(new WaveFormatExtensible(48_000, 32, 2));

        format.Encoding.ShouldBe(SampleEncoding.IeeeFloat);
        format.SampleRate.ShouldBe(48_000);
        format.Channels.ShouldBe(2);
    }

    [Fact]
    public void A_compressed_format_is_refused_rather_than_guessed_at()
    {
        Should.Throw<AudioCaptureException>(() => StreamFormat.Of(WaveFormat.CreateMuLawFormat(8_000, 1)))
            .Message.ShouldContain("MuLaw");
    }
}
