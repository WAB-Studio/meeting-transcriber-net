using System.Buffers.Binary;

namespace MeetingTranscriber.Audio;

/// <summary>
/// How loud a block of captured bytes was. The one place a device's bytes become a number a
/// person reads, so a format nothing here can decode is refused rather than metered as silence.
/// </summary>
public static class Levels
{
    /// <summary>
    /// The loudest sample in <paramref name="block"/>, across every channel it interleaves. A
    /// float format may report past full scale, and that is left as it came: a source clipping is
    /// something to see, not to round away.
    /// </summary>
    public static LevelReading Peak(ReadOnlySpan<byte> block, StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var width = format.BytesPerSample;
        if (width <= 0 || (block.Length % width) != 0)
        {
            throw new AudioCaptureException(
                $"A block of {block.Length} bytes is not whole samples of {format}.");
        }

        return new LevelReading((format.Encoding, format.BitsPerSample) switch
        {
            (SampleEncoding.IeeeFloat, 32) => LoudestFloat(block),
            (SampleEncoding.Pcm, 16) => LoudestPcm16(block),
            _ => throw new AudioCaptureException($"This build cannot meter {format}."),
        });
    }

    private static float LoudestFloat(ReadOnlySpan<byte> block)
    {
        var peak = 0f;
        for (var offset = 0; offset + 4 <= block.Length; offset += 4)
        {
            var sample = BinaryPrimitives.ReadSingleLittleEndian(block.Slice(offset, 4));

            // A device is free to hand over anything a float holds, and a NaN wins every
            // comparison it takes part in — a meter stuck on NaN for the rest of a two hour
            // recording is a worse answer than skipping the sample that poisoned it.
            if (float.IsFinite(sample))
            {
                peak = MathF.Max(peak, MathF.Abs(sample));
            }
        }

        return peak;
    }

    private static float LoudestPcm16(ReadOnlySpan<byte> block)
    {
        var peak = 0;
        for (var offset = 0; offset + 2 <= block.Length; offset += 2)
        {
            // Widened before it is made absolute: short.MinValue has no positive twin, and
            // Math.Abs over a short of it throws.
            int sample = BinaryPrimitives.ReadInt16LittleEndian(block.Slice(offset, 2));
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // Full scale is the negative side, which reaches one step further than the positive one,
        // so the loudest sample a device can send reads as 1.0 rather than as 1.00003.
        return peak / -(float)short.MinValue;
    }
}
