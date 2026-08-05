using System.Collections.Frozen;
using System.Text;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// Turns PascalCase into the snake_case the schema stores and the design documents use.
/// </summary>
/// <remarks>
/// A convention, so renaming an enum member or a property silently changes what is written to
/// disk. That is exactly what the tests in CorpusNamingTests pin down: they spell out every
/// stored name, so a rename shows up as a failing test rather than as an unreadable corpus.
/// </remarks>
public static class WireNames
{
    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0 && name[index - 1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>The stored form of an enum, one dictionary per type, built once.</summary>
public static class WireNames<TEnum>
    where TEnum : struct, Enum
{
    public static FrozenDictionary<TEnum, string> ToWire { get; } =
        Enum.GetValues<TEnum>().ToFrozenDictionary(value => value, value => WireNames.ToSnakeCase(value.ToString()));

    public static FrozenDictionary<string, TEnum> FromWire { get; } =
        ToWire.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Every stored value of this enum, quoted for a CHECK constraint.</summary>
    public static string AsSqlList() => string.Join(", ", ToWire.Values.Order(StringComparer.Ordinal).Select(name => $"'{name}'"));

    public static string Of(TEnum value) => ToWire[value];

    public static TEnum Parse(string name) => FromWire.TryGetValue(name, out var value)
        ? value
        : throw new InvalidOperationException($"'{name}' is not a stored {typeof(TEnum).Name}.");
}
