namespace MeetingTranscriber.Recording;

/// <summary>
/// How far a meter's shown level falls while nothing louder arrives: the rule
/// <c>docs/design.md</c> §Its ballistics states, which is a rate and not a duration.
/// </summary>
/// <remarks>
/// <para>
/// Rising is not in here because it is not a rule: a reading louder than what is shown is shown at
/// once, and <see cref="Fall"/> returns it unchanged for that reason. Easing the rise would
/// under-report a transient, and a peak the meter smoothed away is a clip nobody was told about.
/// </para>
/// <para>
/// A rate, so a bar that has dropped 5 dB between two frames falls the same speed as one that has
/// dropped 40; and a pure function of the three numbers, so it is the one thing about how the bar
/// moves that a build agent can run. What calls it — a frame of the window's composition — is the
/// screen's.
/// </para>
/// </remarks>
public static class MeterBallistics
{
    /// <summary>How far the shown level falls over <see cref="FallMilliseconds"/>.</summary>
    public const double FallDecibels = 20;

    /// <summary>The time in which the shown level falls <see cref="FallDecibels"/>.</summary>
    public const double FallMilliseconds = 1500;

    /// <summary>
    /// The level to show after <paramref name="elapsedMilliseconds"/> have passed: the reading when
    /// it is the louder, otherwise what was shown less the fall since, and never under the floor
    /// the scale draws.
    /// </summary>
    /// <param name="shownDecibels">What the bar showed at the last frame.</param>
    /// <param name="readDecibels">What the channel reads now.</param>
    /// <param name="elapsedMilliseconds">Time since that frame.</param>
    public static double Fall(double shownDecibels, double readDecibels, double elapsedMilliseconds)
    {
        var fallen = shownDecibels - (FallDecibels * Math.Max(0, elapsedMilliseconds) / FallMilliseconds);

        return Math.Max(Math.Max(readDecibels, fallen), MeterScale.Quietest);
    }
}
