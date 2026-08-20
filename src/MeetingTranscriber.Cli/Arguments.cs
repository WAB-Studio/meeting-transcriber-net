using System.Globalization;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// A command line that says nothing this program can act on, phrased for the person who typed it.
/// </summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// What was typed after the command name, and the one place a flag becomes a value the rest of
/// the program can use.
/// </summary>
/// <remarks>
/// <para>
/// Reading an option marks it read, and <see cref="EnsureNothingLeftOver"/> refuses whatever no
/// command asked for. That is what makes a misspelled flag stop the command instead of being
/// silently ignored — <c>--verify-content</c> on a check that then reports a corpus as sound
/// without having verified anything is the failure this exists to prevent — and it costs no
/// declaration list that could fall out of step with what the command actually reads.
/// </para>
/// <para>
/// A value follows its flag and never uses <c>=</c>; anything that is not a flag or the value of
/// one is positional. The grammar is small on purpose: this is a diagnostic tool driven by a
/// person at a prompt and by a test, and neither is served by a parser with opinions.
/// </para>
/// </remarks>
public sealed class Arguments
{
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    private readonly HashSet<string> read = new(StringComparer.Ordinal);
    private readonly List<string> values = [];
    private bool valuesRead;

    private Arguments()
    {
    }

    /// <summary>What was typed that is not a flag, in the order it was typed.</summary>
    public IReadOnlyList<string> Values => values;

    public static Arguments Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var parsed = new Arguments();
        var tokens = arguments.ToArray();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                parsed.values.Add(token);
                continue;
            }

            if (parsed.options.ContainsKey(token))
            {
                throw new UsageException($"'{token}' was given twice.");
            }

            var next = index + 1 < tokens.Length ? tokens[index + 1] : null;
            var value = next is not null && !next.StartsWith("--", StringComparison.Ordinal) ? next : null;
            parsed.options.Add(token, value);

            if (value is not null)
            {
                index++;
            }
        }

        return parsed;
    }

    /// <summary>The value of an option that has to be there.</summary>
    public string Required(string name) =>
        Optional(name) ?? throw new UsageException($"{name} is needed, and it takes a value.");

    /// <summary>The value of an option, or null when it was not given.</summary>
    public string? Optional(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        read.Add(name);

        if (!options.TryGetValue(name, out var value))
        {
            return null;
        }

        return value ?? throw new UsageException($"{name} takes a value.");
    }

    /// <summary>Whether an option with no value of its own was given.</summary>
    public bool Flag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        read.Add(name);

        if (!options.TryGetValue(name, out var value))
        {
            return false;
        }

        return value is null ? true : throw new UsageException($"{name} takes no value, and got '{value}'.");
    }

    /// <summary>A whole number an option carries, or <paramref name="fallback"/> without it.</summary>
    public int Number(string name, int fallback)
    {
        if (Optional(name) is not { } text)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new UsageException($"{name} takes a whole number above zero, and got '{text}'.");
    }

    /// <summary>The one thing this command is about, named so the refusal can say what is missing.</summary>
    public string Only(string what)
    {
        valuesRead = true;

        if (values.Count == 0)
        {
            throw new UsageException($"{what} is needed.");
        }

        return values.Count == 1
            ? values[0]
            : throw new UsageException(
                $"{what} is one thing, and {values.Count} were given: {string.Join(", ", values)}.");
    }

    /// <summary>Refuses a command that carries something no command of that name reads.</summary>
    public void EnsureNothingLeftOver()
    {
        var unread = options.Keys.Where(name => !read.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        if (unread.Length > 0)
        {
            throw new UsageException($"This command takes no {string.Join(", ", unread)}.");
        }

        if (!valuesRead && values.Count > 0)
        {
            throw new UsageException(
                $"This command takes no arguments of its own, and got {string.Join(", ", values)}.");
        }
    }

    /// <summary>
    /// An instant as a person writes one. Wider than <see cref="UtcTimestamp.Parse"/>, which reads
    /// back what the corpus stored: nobody types milliseconds, and a date with no zone at a prompt
    /// on this machine means the zone the machine is in.
    /// </summary>
    public UtcTimestamp Instant(string name)
    {
        var text = Required(name);
        return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var value)
            ? UtcTimestamp.From(value)
            : throw new UsageException(
                $"{name} takes an instant, and got '{text}'. '2026-03-04T14:00:00Z' or "
                + "'2026-03-04 14:00' both read.");
    }

    /// <summary>
    /// What a meeting is expected to be spoken in, or <paramref name="fallback"/> when nobody
    /// said.
    /// </summary>
    /// <remarks>
    /// Its own reader rather than <see cref="Optional"/>, because a blank one is a command line
    /// that does not say what it looks like it says. Every other option that carries a value
    /// refuses a bad one here, where the refusal can name the flag; read as a plain string it
    /// reached the domain instead, and came back out at the prompt as an argument name and a stack
    /// trace.
    /// </remarks>
    public string Language(string name, string fallback)
    {
        if (Optional(name) is not { } text)
        {
            return fallback;
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw new UsageException($"{name} takes what the meeting is spoken in, and got nothing.")
            : text;
    }

    /// <summary>A source profile under the name it is stored and requested under.</summary>
    public SourceProfile Profile(string name)
    {
        var text = Required(name);
        try
        {
            return SourceProfiles.FromWireName(text);
        }
        catch (AudioContractException)
        {
            var known = Enum.GetValues<SourceProfile>().Select(profile => profile.ToWireName());
            throw new UsageException($"{name} is {string.Join(" or ", known)}, and got '{text}'.");
        }
    }

    /// <summary>A meeting id as the corpus and every report spell one.</summary>
    public static Guid Meeting(string text) =>
        Guid.TryParse(text, out var meeting)
            ? meeting
            : throw new UsageException($"'{text}' is not a meeting id.");
}
