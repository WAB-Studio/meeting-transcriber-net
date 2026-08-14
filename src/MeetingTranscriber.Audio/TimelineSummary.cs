using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What the timeline turned out to be, once every packet is in. It reports what was done rather
/// than how well it went: whether the alignment is good is measured against known signals, and a
/// component grading itself is not evidence of anything.
/// </summary>
/// <param name="Length">How long the aligned recording is.</param>
/// <param name="Sources">One entry per channel, in channel order.</param>
public sealed record TimelineSummary(Duration Length, IReadOnlyList<SourceSummary> Sources)
{
    /// <summary>What <paramref name="channel"/> turned out to be.</summary>
    public SourceSummary On(AudioChannel channel) =>
        Sources.FirstOrDefault(source => source.Channel == channel)
        ?? throw new AudioContractException($"This timeline has no {channel} source.");
}

/// <summary>What one source turned out to be over the whole recording.</summary>
/// <param name="Channel">Which channel it fed.</param>
/// <param name="MeasuredRate">
/// The frames per second it really ran at against the machine's clock, which is its label until
/// the recording is long enough to say otherwise. The distance from the label is the drift.
/// </param>
/// <param name="Missing">How much of the recording the device never delivered and is silence.</param>
/// <param name="Waited">How long after the recording's origin this source's first frame was read.</param>
public sealed record SourceSummary(
    AudioChannel Channel,
    double MeasuredRate,
    Duration Missing,
    Duration Waited);
