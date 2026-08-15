using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Where each channel got loud, and nothing else. It is <see cref="Collected"/>'s counterpart for a
/// recording too long to keep: two hours of the interchange format is a quarter of a gigabyte per
/// channel, and what the drift measurement asks of it is where a few hundred markers landed.
/// </summary>
/// <remarks>
/// An onset is a rising edge — the first frame over half of full scale after the channel has been
/// under it for <see cref="QuietFrames"/>. The line and the reason for it are
/// <see cref="Collected.OnsetAfter"/>'s: a resampled edge takes a millisecond or two to get there
/// and rings around it afterwards, so anything quieter was the filter thinking about a burst rather
/// than a burst.
/// </remarks>
internal sealed class Onsets : IAlignedAudio
{
    /// <summary>How long a channel has to have been quiet before getting loud is a new marker.</summary>
    /// <remarks>
    /// Longer than the ring around one edge and far shorter than the gap between two markers, so a
    /// burst is one onset however the resampler rounded its shoulders.
    /// </remarks>
    private const int QuietFrames = CapturedAudio.SampleRate / 10;

    private readonly List<double>[] found =
        [.. Enumerable.Range(0, CapturedAudio.ChannelCount).Select(_ => new List<double>())];

    private readonly int[] quiet = new int[CapturedAudio.ChannelCount];

    private long frames;

    internal Onsets()
    {
        Array.Fill(quiet, QuietFrames);
    }

    /// <summary>How many stereo frames were written.</summary>
    internal long Frames => frames;

    /// <summary>Every second of the recording <paramref name="channel"/> got loud at, in order.</summary>
    internal IReadOnlyList<double> On(AudioChannel channel) => found[CapturedAudio.IndexOf(channel)];

    public void Write(ReadOnlySpan<short> interleaved)
    {
        for (var frame = 0; frame < interleaved.Length / CapturedAudio.ChannelCount; frame++)
        {
            for (var channel = 0; channel < CapturedAudio.ChannelCount; channel++)
            {
                var sample = interleaved[(frame * CapturedAudio.ChannelCount) + channel]
                    / -(float)short.MinValue;

                if (MathF.Abs(sample) > 0.5f)
                {
                    if (quiet[channel] >= QuietFrames)
                    {
                        found[channel].Add((frames + frame) / (double)CapturedAudio.SampleRate);
                    }

                    quiet[channel] = 0;
                }
                else
                {
                    quiet[channel]++;
                }
            }
        }

        frames += interleaved.Length / CapturedAudio.ChannelCount;
    }
}
