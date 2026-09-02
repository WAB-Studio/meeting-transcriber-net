namespace MeetingTranscriber.Recording;

/// <summary>
/// Where a level sits on a meter: linear in dBFS from −60 at the left to 0 at the right, which is
/// the scale <c>docs/design.md</c> §The scale fixes and everything the meter draws follows from.
/// </summary>
/// <remarks>
/// <para>
/// One place, because two things need the same answer and would otherwise each hold their own
/// floor. How full the bar is comes off a reading, and where the numbers under it go — and where
/// the hot zone starts — is drawn by the component; a meter whose bar and whose scale disagreed
/// about what −12 means would put the boundary somewhere other than where the colour changes,
/// which is the one thing the scale exists to make readable.
/// </para>
/// <para>
/// Beside <see cref="ChannelReading"/>, which is the other half that reads it, and deliberately not
/// in <c>Domain/Audio</c>. That folder holds the invariants the rest of the system assumes, where
/// breaking one corrupts meetings already recorded — and this breaks nothing: moving the floor
/// redraws a bar and leaves every recording exactly as it was. It is a rule about how a level is
/// drawn, so it lives with the readings a meter is drawn from.
/// </para>
/// </remarks>
public static class MeterScale
{
    /// <summary>
    /// The quietest the scale draws. Decibels, and not silence: speech arrives around a twentieth
    /// of full scale, so a bar drawn from the true floor sits near nothing for a meeting that is
    /// recording perfectly well and says nothing to anybody.
    /// </summary>
    public const float Quietest = -60f;

    /// <summary>
    /// The loudest, which is full scale. A source past it is full rather than more than full — a
    /// reading that clipped is something to see and not something to draw off the end of the bar.
    /// </summary>
    public const float Loudest = 0f;

    /// <summary>
    /// Where the hot zone starts. It is the one number on the scale that is a judgement rather than
    /// a round figure, and it is the same number the segments above it change colour at.
    /// </summary>
    public const float HotFrom = -12f;

    /// <summary>
    /// The numbers written under the bar, left to right. Closed and in order: the scale is what
    /// says whether −16 is close to clipping or nowhere near it, so which marks are on it is a
    /// decision on <c>docs/design.md</c> and not something a screen picks.
    /// </summary>
    public static IReadOnlyList<float> Marks { get; } = [-60f, -40f, -20f, -12f, 0f];

    /// <summary>
    /// Where <paramref name="decibels"/> falls, from nothing at the left to one at the right.
    /// Clamped, so a level past full scale is the right-hand end rather than past it, and one below
    /// the floor is the left-hand end rather than off it.
    /// </summary>
    /// <remarks>
    /// Negative infinity — which is what a stretch where no sample moved reads as — clamps to the
    /// left-hand end like any other level below the floor, rather than coming back as a number that
    /// is not one. Whether a silent channel draws a level at all is the meter's decision and not
    /// this one's.
    /// </remarks>
    public static double Along(float decibels) =>
        Math.Clamp((decibels - Quietest) / (Loudest - Quietest), 0, 1);
}
