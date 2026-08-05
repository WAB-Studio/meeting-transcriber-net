namespace MeetingTranscriber.Domain.Time;

/// <summary>
/// A length of time in whole milliseconds, the only unit the domain stores. It is also
/// how an offset into a timeline is expressed: the distance from that timeline's origin.
/// </summary>
public readonly record struct Duration : IComparable<Duration>
{
    private Duration(long milliseconds) => Milliseconds = milliseconds;

    /// <summary>No time at all, and the origin of any timeline.</summary>
    public static Duration Zero => default;

    /// <summary>The length, in whole milliseconds. Never negative.</summary>
    public long Milliseconds { get; }

    public static Duration FromMilliseconds(long milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds), milliseconds, "A duration is never negative.");
        }

        return new Duration(milliseconds);
    }

    /// <summary>
    /// Truncates towards zero. Sub millisecond precision is not part of the contract, and
    /// dropping it here keeps that decision at one visible boundary.
    /// </summary>
    public static Duration FromTimeSpan(TimeSpan value) => FromMilliseconds((long)value.TotalMilliseconds);

    public TimeSpan ToTimeSpan() => TimeSpan.FromMilliseconds(Milliseconds);

    public int CompareTo(Duration other) => Milliseconds.CompareTo(other.Milliseconds);

    public override string ToString() => $"{Milliseconds} ms";

    public static Duration operator +(Duration left, Duration right) =>
        FromMilliseconds(left.Milliseconds + right.Milliseconds);

    public static Duration operator -(Duration left, Duration right) =>
        FromMilliseconds(left.Milliseconds - right.Milliseconds);

    public static bool operator <(Duration left, Duration right) => left.CompareTo(right) < 0;

    public static bool operator >(Duration left, Duration right) => left.CompareTo(right) > 0;

    public static bool operator <=(Duration left, Duration right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Duration left, Duration right) => left.CompareTo(right) >= 0;
}
