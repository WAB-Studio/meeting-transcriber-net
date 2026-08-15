namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a sample of the interchange format is worth, and where the line between a marker and the
/// filter thinking about one falls. Two sinks read the same recordings — <see cref="Collected"/>
/// keeps them and <see cref="Onsets"/> watches them go past — and a threshold each held its own
/// copy of is one that can be moved in one of them.
/// </summary>
internal static class Loudness
{
    /// <summary>
    /// Half of full scale. A resampled edge takes a millisecond or two to get there and rings
    /// around it afterwards, so anything that crosses half was a burst rather than the ringing.
    /// </summary>
    internal const float Loud = 0.5f;

    /// <summary>One stored sample as a fraction of full scale.</summary>
    internal static float Of(short sample) => sample / -(float)short.MinValue;

    /// <summary>Whether that sample is one of a marker rather than of the quiet around it.</summary>
    internal static bool IsLoud(short sample) => MathF.Abs(Of(sample)) > Loud;
}
