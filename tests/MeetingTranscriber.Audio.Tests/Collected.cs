using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Keeps everything the timeline wrote, so a test can ask where in the recording something landed.
/// </summary>
internal sealed class Collected : IAlignedAudio
{
    private readonly List<short> interleaved = [];

    /// <summary>How many stereo frames were written.</summary>
    internal int Frames => interleaved.Count / CapturedAudio.ChannelCount;

    public void Write(ReadOnlySpan<short> frames) => interleaved.AddRange(frames);

    /// <summary>The sample of <paramref name="channel"/> at <paramref name="frame"/>, full scale at one.</summary>
    internal float At(AudioChannel channel, int frame) =>
        interleaved[(frame * CapturedAudio.ChannelCount) + CapturedAudio.IndexOf(channel)] / -(float)short.MinValue;

    /// <summary>The loudest <paramref name="channel"/> gets between two seconds of the recording.</summary>
    internal float Loudest(AudioChannel channel, double fromSeconds, double toSeconds)
    {
        var peak = 0f;
        for (var frame = Frame(fromSeconds); frame < Math.Min(Frame(toSeconds), Frames); frame++)
        {
            peak = MathF.Max(peak, MathF.Abs(At(channel, frame)));
        }

        return peak;
    }

    /// <summary>
    /// Where <paramref name="channel"/> first gets loud after <paramref name="fromSeconds"/>, in
    /// seconds of the recording, or null if it never does.
    /// </summary>
    /// <remarks>
    /// Half of full scale as the line: a resampled edge takes a millisecond or two to get there
    /// and rings around it afterwards, and anything that crosses half was a burst rather than the
    /// filter thinking about one.
    /// </remarks>
    internal double? OnsetAfter(AudioChannel channel, double fromSeconds)
    {
        for (var frame = Frame(fromSeconds); frame < Frames; frame++)
        {
            if (MathF.Abs(At(channel, frame)) > 0.5f)
            {
                return frame / (double)CapturedAudio.SampleRate;
            }
        }

        return null;
    }

    /// <summary>
    /// The first frame where this recording and <paramref name="other"/> hold different samples, or
    /// null if they hold the same ones throughout. Reported as a frame rather than asserted sample
    /// by sample because two recordings that differ are millions of samples of difference, and the
    /// one number worth reading is where they parted.
    /// </summary>
    internal int? FirstDifferenceFrom(Collected other)
    {
        ArgumentNullException.ThrowIfNull(other);

        for (var frame = 0; frame < Math.Min(Frames, other.Frames); frame++)
        {
            for (var channel = 0; channel < CapturedAudio.ChannelCount; channel++)
            {
                var at = (frame * CapturedAudio.ChannelCount) + channel;
                if (interleaved[at] != other.interleaved[at])
                {
                    return frame;
                }
            }
        }

        return Frames == other.Frames ? null : Math.Min(Frames, other.Frames);
    }

    private static int Frame(double seconds) => Math.Max(0, (int)(seconds * CapturedAudio.SampleRate));
}
