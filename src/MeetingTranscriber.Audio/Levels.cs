namespace MeetingTranscriber.Audio;

/// <summary>
/// How loud a block of captured bytes was. The one place a device's bytes become a number a
/// person reads, so a format nothing here can decode is refused rather than metered as silence.
/// </summary>
/// <remarks>
/// Which formats those are is <see cref="Samples"/>'s to say, not this file's. A build that could
/// meter a width it could not resample would show a healthy level for a whole meeting and fail
/// once it was over.
/// </remarks>
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

        var read = Samples.ReaderFor(format);
        var peak = 0f;
        for (var offset = 0; offset + width <= block.Length; offset += width)
        {
            var sample = read(block.Slice(offset, width));

            // A device is free to hand over anything a float holds, and a NaN wins every
            // comparison it takes part in — a meter stuck on NaN for the rest of a two hour
            // recording is a worse answer than skipping the sample that poisoned it.
            if (float.IsFinite(sample))
            {
                peak = MathF.Max(peak, MathF.Abs(sample));
            }
        }

        return new LevelReading(peak);
    }

    /// <summary>
    /// The loudest sample of one channel of <paramref name="block"/>, where the other channels
    /// interleaved with it are a different source rather than the same one in stereo.
    /// </summary>
    /// <remarks>
    /// The meter a person watches while a meeting runs wants the whole of what one device handed
    /// over, which is what <see cref="Peak(ReadOnlySpan{byte}, StreamFormat)"/> answers. A finished
    /// recording is the other case: its two channels are the two sources, and one number over both
    /// of them would call a recording that lost the microphone entirely a healthy one.
    /// </remarks>
    public static LevelReading Peak(ReadOnlySpan<byte> block, StreamFormat format, int channel)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (channel < 0 || channel >= format.Channels)
        {
            throw new AudioCaptureException($"{format} has no channel {channel} to meter.");
        }

        Samples.FramesIn(block.Length, format);

        var read = Samples.ReaderFor(format);
        var width = format.BytesPerSample;
        var frame = width * format.Channels;
        var peak = 0f;
        for (var offset = channel * width; offset + width <= block.Length; offset += frame)
        {
            var sample = read(block.Slice(offset, width));

            // Skipped rather than taken, for the reason the reading across every channel skips it.
            if (float.IsFinite(sample))
            {
                peak = MathF.Max(peak, MathF.Abs(sample));
            }
        }

        return new LevelReading(peak);
    }

    /// <summary>
    /// Refuses a format no block of which could be metered, and does it by metering no bytes at
    /// all — so what is checked before a device is opened is the same rule, and not a second copy
    /// of it that can fall out of step.
    /// </summary>
    /// <remarks>
    /// Worth checking early because the alternative is a capture that opens, starts and dies on
    /// its first block, which is a recording somebody has already begun holding.
    /// </remarks>
    public static void EnsureMeterable(StreamFormat format) => Peak([], format);
}
