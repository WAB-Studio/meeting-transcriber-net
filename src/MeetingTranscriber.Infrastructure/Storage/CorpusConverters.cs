using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// An instant keeps the UTC text form the domain round trips through, and a timestamp nobody
/// assigned never reaches a column: every date field is required with a setter, so forgetting one
/// would otherwise land in the corpus as year one and read back as a date like any other.
/// </summary>
public sealed class UtcTimestampConverter : ValueConverter<UtcTimestamp, string>
{
    public UtcTimestampConverter()
        : base(value => value.ToStorage(), text => UtcTimestamp.Parse(text))
    {
    }
}

/// <summary>A length stays whole milliseconds, in a column the naming pass suffixes with _ms.</summary>
public sealed class DurationConverter : ValueConverter<Duration, long>
{
    public DurationConverter()
        : base(value => value.Milliseconds, milliseconds => Duration.FromMilliseconds(milliseconds))
    {
    }
}

/// <summary>
/// A channel is stored as the number the contract fixes — 0 the meeting, 1 the user — because
/// that number is also what the provider answers with.
/// </summary>
public sealed class AudioChannelConverter : ValueConverter<AudioChannel, int>
{
    public AudioChannelConverter()
        : base(value => (int)value, index => (AudioChannel)index)
    {
    }
}

/// <summary>Every other enum is stored under its snake_case name.</summary>
public sealed class EnumWireConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public EnumWireConverter()
        : base(value => WireNames<TEnum>.Of(value), name => WireNames<TEnum>.Parse(name))
    {
    }
}
