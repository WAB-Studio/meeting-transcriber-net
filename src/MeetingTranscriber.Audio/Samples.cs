using System.Buffers.Binary;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The one place that knows how to turn a device's bytes into numbers, and therefore the one
/// place that decides which formats this build can read at all.
/// </summary>
/// <remarks>
/// A meter wants the loudest of every channel a device interleaves and the timeline wants one
/// value per frame, so the two loops are genuinely different — but both stop at the same set of
/// formats, and a build that could meter a width it could not resample would report a healthy
/// level right up to the moment the recording was finished.
/// </remarks>
public static class Samples
{
    /// <summary>Reads one sample out of its bytes, full scale at one.</summary>
    internal delegate float Reader(ReadOnlySpan<byte> sample);

    /// <summary>How many frames of <paramref name="format"/> a block of that many bytes holds.</summary>
    public static int FramesIn(int bytes, StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var frame = format.BytesPerSample * format.Channels;
        if (frame <= 0 || (bytes % frame) != 0)
        {
            throw new AudioCaptureException($"A block of {bytes} bytes is not whole frames of {format}.");
        }

        return bytes / frame;
    }

    /// <summary>
    /// Writes one sample per frame of <paramref name="block"/> into <paramref name="mono"/>,
    /// averaging whatever channels the device interleaved into each frame.
    /// </summary>
    /// <remarks>
    /// Averaged rather than taking the first channel: a device that puts the speech on one side of
    /// a stereo pair would otherwise be recorded as silence, and which side that is belongs to the
    /// hardware rather than to anything this application can ask.
    /// </remarks>
    public static void ToMono(ReadOnlySpan<byte> block, StreamFormat format, Span<float> mono)
    {
        ArgumentNullException.ThrowIfNull(format);

        var frames = FramesIn(block.Length, format);
        if (mono.Length < frames)
        {
            throw new AudioCaptureException(
                $"{frames} frames do not fit in room for {mono.Length}.");
        }

        var read = ReaderFor(format);
        var width = format.BytesPerSample;
        var channels = format.Channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var offset = frame * width * channels;
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = read(block.Slice(offset + (channel * width), width));

                // A device is free to hand over anything a float holds, and one NaN would take
                // the frame — and, once resampled, the frames around it — with it.
                sum += float.IsFinite(sample) ? sample : 0f;
            }

            mono[frame] = sum / channels;
        }
    }

    /// <summary>
    /// Writes <paramref name="mono"/> out in the interchange format's width. Full scale is the
    /// positive side, so the quietest sample a device can send survives the trip rather than
    /// wrapping round to the loudest one.
    /// </summary>
    public static void ToPcm16(ReadOnlySpan<float> mono, Span<short> pcm)
    {
        if (pcm.Length < mono.Length)
        {
            throw new AudioCaptureException($"{mono.Length} samples do not fit in room for {pcm.Length}.");
        }

        for (var index = 0; index < mono.Length; index++)
        {
            var sample = mono[index];
            pcm[index] = float.IsFinite(sample)
                ? (short)Math.Clamp(MathF.Round(sample * short.MaxValue), short.MinValue, short.MaxValue)
                : (short)0;
        }
    }

    /// <summary>
    /// How a sample of <paramref name="format"/> is read, or a refusal for a format this build
    /// cannot read at all.
    /// </summary>
    internal static Reader ReaderFor(StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return (format.Encoding, format.BitsPerSample) switch
        {
            (SampleEncoding.IeeeFloat, 32) => sample => BinaryPrimitives.ReadSingleLittleEndian(sample),

            // Full scale is the negative side, which reaches one step further than the positive
            // one, so the loudest sample a device can send reads as 1.0 rather than as 1.00003.
            (SampleEncoding.Pcm, 16) => sample =>
                BinaryPrimitives.ReadInt16LittleEndian(sample) / -(float)short.MinValue,

            _ => throw new AudioCaptureException($"This build cannot read {format}."),
        };
    }
}
