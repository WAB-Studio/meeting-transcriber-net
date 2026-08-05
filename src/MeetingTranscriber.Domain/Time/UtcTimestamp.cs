using System.Globalization;

namespace MeetingTranscriber.Domain.Time;

/// <summary>
/// An instant, always UTC and always to the millisecond. Local and unspecified times are
/// converted or rejected here so no other layer has to wonder what a stored date meant.
/// </summary>
public readonly record struct UtcTimestamp : IComparable<UtcTimestamp>
{
    /// <summary>The round trippable text form used in SQLite, manifests and artifacts.</summary>
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private UtcTimestamp(DateTimeOffset value) => Value = Normalize(value);

    /// <summary>The instant, with a zero offset and no sub millisecond remainder.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>Milliseconds since the Unix epoch.</summary>
    public long UnixMilliseconds => Value.ToUnixTimeMilliseconds();

    /// <summary>Takes the instant an offset already identifies, and moves it to UTC.</summary>
    public static UtcTimestamp From(DateTimeOffset value) => new(value);

    /// <summary>
    /// Converts a UTC or local <see cref="DateTime"/>. An unspecified kind is rejected: it
    /// names no instant, and guessing one is how a recording ends up hours off.
    /// </summary>
    public static UtcTimestamp From(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc or DateTimeKind.Local => new UtcTimestamp(new DateTimeOffset(value)),
        _ => throw new ArgumentException(
            "A DateTime with an unspecified kind names no instant; give it a kind first.",
            nameof(value)),
    };

    public static UtcTimestamp FromUnixMilliseconds(long milliseconds) =>
        new(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));

    /// <summary>Reads back a timestamp written by <see cref="ToString"/>.</summary>
    public static UtcTimestamp Parse(string text)
    {
        if (!DateTimeOffset.TryParseExact(
                text,
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new ArgumentException($"'{text}' is not a timestamp in the '{Format}' format.", nameof(text));
        }

        return new UtcTimestamp(value);
    }

    public int CompareTo(UtcTimestamp other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>How long after <paramref name="earlier"/> the left instant happened.</summary>
    public static Duration operator -(UtcTimestamp later, UtcTimestamp earlier) =>
        Duration.FromMilliseconds(later.UnixMilliseconds - earlier.UnixMilliseconds);

    public static UtcTimestamp operator +(UtcTimestamp origin, Duration offset) =>
        FromUnixMilliseconds(origin.UnixMilliseconds + offset.Milliseconds);

    public static bool operator <(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) < 0;

    public static bool operator >(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) > 0;

    public static bool operator <=(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) <= 0;

    public static bool operator >=(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) >= 0;

    private static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
    }
}
