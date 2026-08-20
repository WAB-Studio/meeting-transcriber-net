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
/// the recording is long enough to say otherwise. The distance from the label is the drift — and
/// there is none to read when <paramref name="CounterGivenUp"/> is set, because the counter a rate
/// is measured from is the one that was given up on.
/// </param>
/// <param name="Missing">How much of the recording the device never delivered and is silence.</param>
/// <param name="Waited">How long after the recording's origin this source's first frame was read.</param>
/// <param name="CounterGivenUp">
/// Whether this device numbered its frames in something other than the frames it handed over, so
/// its own counter was given up on and its audio placed by the instants beside it. The recording is
/// the meeting either way; what is gone is the drift measurement, and a rate that reads as measured
/// while being the label is the one thing this exists to stop.
/// </param>
/// <param name="Stretches">
/// How many devices fed this channel over the recording. One for almost every recording; more when
/// a device was unplugged mid meeting, or Windows moved the channel to another one.
/// </param>
/// <remarks>
/// More than one stretch is what says a channel came to name more than one device, which is the
/// difference between the missing above being a device that dropped audio and a device that was
/// replaced. Which devices they were, and when each took over, is the recording's own folder — this
/// says what the audio turned out to be and never what anything was called.
/// </remarks>
public sealed record SourceSummary(
    AudioChannel Channel,
    double MeasuredRate,
    Duration Missing,
    Duration Waited,
    bool CounterGivenUp,
    int Stretches);
