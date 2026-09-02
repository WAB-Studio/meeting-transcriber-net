using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Presentation;

/// <summary>
/// One thing the application says, in every language it is written in. There is no constructor
/// that takes one language, so a text that exists in Spanish and not in English is not something
/// this codebase can express — which is the whole of what keeps the two in step.
/// </summary>
public sealed partial record UiText
{
    /// <param name="spanish">What it says in Spanish.</param>
    /// <param name="english">What it says in English.</param>
    public UiText(string spanish, string english)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spanish);
        ArgumentException.ThrowIfNullOrWhiteSpace(english);

        // A `{0}` in one language and not in the other is a text that reads fine until the day
        // somebody switches language, and then either loses a value or throws. Both versions
        // carry the same placeholders or neither version is usable.
        var inSpanish = Placeholders(spanish);
        var inEnglish = Placeholders(english);
        if (!inSpanish.SetEquals(inEnglish))
        {
            throw new ArgumentException(
                $"The two versions of this text take different values: Spanish uses "
                + $"{Spelled(inSpanish)} and English uses {Spelled(inEnglish)}.",
                nameof(english));
        }

        // The regex above says which values a version asks for; it does not say the version is a
        // format string at all. `"Valor: {0"` asks for none by that reading, matches an English
        // version that also asks for none, and throws the first time the text is read. So both
        // are put through the formatter here, with as many values as they claim to want: a
        // translation with a brace in the wrong place costs a failure where the text was written
        // rather than on the screen it was written for.
        Readable(spanish, inSpanish, nameof(spanish));
        Readable(english, inEnglish, nameof(english));

        Spanish = spanish;
        English = english;
        Values = Room(inSpanish);
    }

    /// <summary>What it says in Spanish.</summary>
    public string Spanish { get; }

    /// <summary>What it says in English.</summary>
    public string English { get; }

    /// <summary>
    /// How many values it has to be handed, which is one past the highest placeholder rather than
    /// the count of them: `{0}` twice is one value, and a text using only `{1}` still takes two.
    /// </summary>
    /// <remarks>
    /// Counted over one version because the constructor above has already refused a pair whose two
    /// versions ask for different values, and read off the same number <see cref="Readable"/> feeds
    /// the formatter — so this is what the text was proved readable with, not a second reading of
    /// it. What it is for is the caller that has to say how many values it will have: a screen
    /// pairing a text with a source of values can be held to the two agreeing before somebody
    /// reaches the screen, instead of `string.Format` throwing inside a draw.
    /// </remarks>
    public int Values { get; }

    /// <summary>What it says in the language being read in.</summary>
    public string In(UiLanguage language) => language switch
    {
        UiLanguage.Spanish => Spanish,
        UiLanguage.English => English,
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    /// <summary>
    /// What it says in the language being read in, with the values it leaves room for put in.
    /// The values are written the way that language writes them.
    /// </summary>
    public string In(UiLanguage language, params object?[] values) =>
        string.Format(UiLanguages.Culture(language), In(language), values ?? []);

    /// <summary>
    /// Throws unless the text is something the formatter can actually read.
    /// </summary>
    private static void Readable(string text, HashSet<int> placeholders, string parameter)
    {
        var values = new object?[Room(placeholders)];

        try
        {
            string.Format(CultureInfo.InvariantCulture, text, values);
        }
        catch (FormatException malformed)
        {
            throw new ArgumentException(
                $"This version of the text is not something that can be read: {malformed.Message}",
                parameter,
                malformed);
        }
    }

    /// <summary>How many values a set of placeholder indices asks the formatter for.</summary>
    private static int Room(HashSet<int> placeholders) =>
        placeholders.Count == 0 ? 0 : placeholders.Max() + 1;

    private static string Spelled(HashSet<int> placeholders) => placeholders.Count == 0
        ? "none"
        : string.Join(", ", placeholders.Order().Select(index => $"{{{index}}}"));

    /// <summary>The indices of the `{n}` placeholders a version of the text leaves room for.</summary>
    private static HashSet<int> Placeholders(string text) => PlaceholderPattern()
        .Matches(text)
        .Where(match => match.Groups["index"].Success)
        .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
        .ToHashSet();

    // `{{` is an escaped brace and holds no value, so it is consumed before a placeholder can be
    // read out of it.
    [GeneratedRegex(@"\{\{|\}\}|\{(?<index>\d+)(?<format>[^}]*)\}")]
    private static partial Regex PlaceholderPattern();
}
