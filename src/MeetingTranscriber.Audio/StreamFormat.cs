using System.Globalization;

using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The shape of the samples a device is handing over: what the machine decided, not what this
/// application would have asked for.
/// </summary>
/// <remarks>
/// A capture takes the device's own format rather than negotiating one, so what lands in the
/// spike's files is exactly what WASAPI produced. Two sources agreeing on nothing — different
/// rates, different widths, one mono and one stereo — is the normal case and the reason the
/// timeline has resampling to do.
/// </remarks>
public sealed record StreamFormat(int SampleRate, int Channels, int BitsPerSample, SampleEncoding Encoding)
{
    /// <summary>How many bytes one sample of one channel occupies.</summary>
    public int BytesPerSample => BitsPerSample / 8;

    /// <summary>
    /// What NAudio says the device gave, in this application's terms. An extensible format is
    /// reduced to the plain one it wraps, and anything that is neither integer nor float samples
    /// stops here — metering it would otherwise report a number that means nothing.
    /// </summary>
    public static StreamFormat Of(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var plain = format is WaveFormatExtensible extensible ? Plain(extensible) : format;

        var encoding = plain.Encoding switch
        {
            WaveFormatEncoding.Pcm => SampleEncoding.Pcm,
            WaveFormatEncoding.IeeeFloat => SampleEncoding.IeeeFloat,
            _ => throw new AudioCaptureException(
                $"A device handed over {plain.Encoding} samples, which this build cannot read."),
        };

        return new StreamFormat(plain.SampleRate, plain.Channels, plain.BitsPerSample, encoding);
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{SampleRate} Hz, {Channels} ch, {BitsPerSample} bit {(Encoding is SampleEncoding.IeeeFloat ? "float" : "pcm")}");

    private static WaveFormat Plain(WaveFormatExtensible extensible)
    {
        try
        {
            return extensible.ToStandardWaveFormat();
        }
        catch (InvalidOperationException unknown)
        {
            throw new AudioCaptureException(
                $"A device handed over a format this build cannot read: {extensible}", unknown);
        }
    }
}
