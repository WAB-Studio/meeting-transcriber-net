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
    private readonly string? insteadOfRepeating;
    private bool valuesRead;

    private Arguments(string? insteadOfRepeating) => this.insteadOfRepeating = insteadOfRepeating;

    /// <summary>What was typed that is not a flag, in the order it was typed.</summary>
    public IReadOnlyList<string> Values => values;

    /// <summary>
    /// Reads a command line, where <paramref name="insteadOfRepeating"/> is what every refusal says
    /// in place of quoting what it was handed.
    /// </summary>
    /// <param name="arguments">What was typed after the command name.</param>
    /// <param name="insteadOfRepeating">
    /// The sentence a command whose line could be carrying a secret supplies, and nothing for every
    /// other command. Quoting the offending text is what makes a misspelled line readable, and it is
    /// also what would put a key somebody typed on the screen and into whatever is capturing this
    /// program's error stream.
    /// </param>
    /// <remarks>
    /// A property of the whole line rather than of one reader, and that is the point of it. The leak
    /// is not a flag with a value after it, it is any message that repeats a token. A guard on one
    /// reader closes the spellings somebody thought of — <c>--set &lt;key&gt;</c> and
    /// <c>&lt;key&gt;</c> — and leaves <c>--set=&lt;key&gt;</c>, which this grammar reads as an
    /// option <em>named</em> <c>--set=&lt;key&gt;</c> carrying no value, coming back out of
    /// <see cref="EnsureNothingLeftOver"/> with the key in it. Every refusal below asks this one
    /// question instead, so the spellings nobody thought of are covered, and so is the next refusal
    /// somebody writes.
    /// </remarks>
    public static Arguments Parse(IEnumerable<string> arguments, string? insteadOfRepeating = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var parsed = new Arguments(insteadOfRepeating);
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
                throw parsed.Refusing($"'{token}' was given twice.");
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

    /// <summary>Whether an option was on the line at all, whatever it carried.</summary>
    /// <remarks>
    /// The question a command asks about a flag it will not accept where it is, and it is different
    /// from every other reader here: <see cref="Optional"/> and <see cref="Required"/> ask what a
    /// flag carries and refuse one with nothing after it, <see cref="Flag"/> refuses one with
    /// something after it. Both answer about the value, so both turn <em>you may not give this
    /// here</em> into a sentence about what should have followed it. This one marks the flag read
    /// like the rest, so a command that has refused it by name does not then meet
    /// <see cref="EnsureNothingLeftOver"/> saying the command takes no such thing.
    /// </remarks>
    public bool WasGiven(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        read.Add(name);

        return options.ContainsKey(name);
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

        return value is null
            ? true
            : throw Refusing($"{name} takes no value, and got '{value}'.");
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
            : throw Refusing($"{name} takes a whole number above zero, and got '{text}'.");
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
            : throw Refusing(
                $"{what} is one thing, and {values.Count} were given: {string.Join(", ", values)}.");
    }

    /// <summary>Refuses a command that carries something no command of that name reads.</summary>
    public void EnsureNothingLeftOver()
    {
        var unread = options.Keys.Where(name => !read.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        if (unread.Length > 0)
        {
            throw Refusing($"This command takes no {string.Join(", ", unread)}.");
        }

        if (!valuesRead && values.Count > 0)
        {
            throw Refusing(
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
            : throw Refusing(
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
            throw Refusing($"{name} is {string.Join(" or ", known)}, and got '{text}'.");
        }
    }

    /// <summary>
    /// A refusal that would repeat something somebody typed, or the command's own sentence in its
    /// place when this line could be carrying a secret.
    /// </summary>
    /// <remarks>
    /// Every refusal in this type that repeats a token somebody typed goes through here, with one
    /// exception it cannot: <see cref="Meeting"/> is <see langword="static"/>, so it has no line to
    /// ask about and quotes what it got unconditionally. A command that both declares a sentence
    /// here and reads a meeting id would leak through it, and there is none — making
    /// <see cref="Meeting"/> an instance method is what to do on the day there is.
    /// The refusals that name only a flag or a noun this program declares —
    /// <see cref="Optional"/>'s, <see cref="Required"/>'s, <see cref="Language"/>'s and
    /// <see cref="Only"/>'s missing-value branch — do not go through here and do not need to.
    /// </remarks>
    private UsageException Refusing(string quotingWhatItGot) =>
        new(insteadOfRepeating ?? quotingWhatItGot);

    /// <summary>A meeting id as the corpus and every report spell one.</summary>
    public static Guid Meeting(string text) =>
        Guid.TryParse(text, out var meeting)
            ? meeting
            : throw new UsageException($"'{text}' is not a meeting id.");
}
