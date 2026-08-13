using System.Globalization;

namespace MeetingTranscriber.Audio;

/// <summary>
/// How loud a stretch of one source was: the furthest any sample of it got from silence, as a
/// fraction of full scale.
/// </summary>
/// <remarks>
/// Peak rather than an average, because what a person needs from a meter while a meeting is
/// running is whether anything arrived at all. An average over a block hides a sentence spoken
/// once; a peak of zero means no sample moved, which is the only thing that can honestly be
/// called silent.
/// </remarks>
public readonly record struct LevelReading(float Peak)
{
    /// <summary>Nothing arrived: every sample of the stretch sat at zero.</summary>
    public bool IsSilent => Peak <= 0;

    /// <summary>The peak in dBFS, which is negative infinity when there was nothing.</summary>
    public float Decibels => IsSilent ? float.NegativeInfinity : 20 * MathF.Log10(Peak);

    public override string ToString() => IsSilent
        ? "silent"
        : string.Create(CultureInfo.InvariantCulture, $"{Decibels:0.0} dBFS");
}
