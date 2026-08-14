namespace MeetingTranscriber.Audio;

/// <summary>
/// A point on the machine's monotonic clock, in the 100 nanosecond ticks WASAPI reports next to
/// every packet it hands over.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a <see cref="Domain.Time.UtcTimestamp"/>. It is the one thing the two sources
/// have in common — each has its own device counting its own frames, and this is the only clock
/// both are read against — so it has to be the clock that does not jump when the system time is
/// corrected halfway through a meeting.
/// </para>
/// <para>
/// It also keeps sub millisecond detail on purpose. What separates the two devices over two hours
/// is measured in parts per million, and a domain millisecond is four orders of magnitude coarser
/// than the thing being measured: rounding each packet as it arrived would bury the drift under
/// the rounding meant to tidy it.
/// </para>
/// </remarks>
public readonly record struct MonotonicInstant : IComparable<MonotonicInstant>
{
    /// <summary>Ticks in one second. WASAPI reports the performance counter in 100 ns units.</summary>
    public const long TicksPerSecond = 10_000_000;

    private MonotonicInstant(long ticks) => Ticks = ticks;

    /// <summary>The counter itself. Only differences between two of these mean anything.</summary>
    public long Ticks { get; }

    /// <summary>The instant a performance counter reading of <paramref name="ticks"/> names.</summary>
    public static MonotonicInstant FromTicks(long ticks) => new(ticks);

    /// <summary>
    /// The instant <paramref name="milliseconds"/> after the counter's own zero, which is what a
    /// fabricated packet is written in terms of.
    /// </summary>
    public static MonotonicInstant FromMilliseconds(double milliseconds)
    {
        if (!double.IsFinite(milliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds), milliseconds, "An instant is a number of milliseconds.");
        }

        return new MonotonicInstant((long)Math.Round(milliseconds * (TicksPerSecond / 1000)));
    }

    /// <summary>
    /// How far this is after <paramref name="earlier"/>, in ticks. Signed, because a source whose
    /// stream opened second is behind the timeline's origin and that is not an error.
    /// </summary>
    public long Since(MonotonicInstant earlier) => Ticks - earlier.Ticks;

    public int CompareTo(MonotonicInstant other) => Ticks.CompareTo(other.Ticks);

    public override string ToString() => $"{Ticks / (double)TicksPerSecond:0.000} s";

    public static bool operator <(MonotonicInstant left, MonotonicInstant right) => left.Ticks < right.Ticks;

    public static bool operator >(MonotonicInstant left, MonotonicInstant right) => left.Ticks > right.Ticks;

    public static bool operator <=(MonotonicInstant left, MonotonicInstant right) => left.Ticks <= right.Ticks;

    public static bool operator >=(MonotonicInstant left, MonotonicInstant right) => left.Ticks >= right.Ticks;
}
