using System.Globalization;

namespace MeetingTranscriber.Presentation;

/// <summary>
/// The only place that decides which language the application reads in. Everything else is handed
/// the answer.
/// </summary>
public static class UiLanguages
{
    /// <summary>
    /// What the application opens in when Windows is set to a language it is not written in.
    /// Something has to be picked — the application exists in two — and English is the one more
    /// people who speak neither can read.
    /// </summary>
    public const UiLanguage WhenWindowsSpeaksNeither = UiLanguage.English;

    /// <summary>
    /// The language to read in: the one somebody chose, and failing that the first of Windows'
    /// preferred languages this application is written in.
    /// </summary>
    /// <param name="chosen">What somebody picked, or <c>null</c> if nobody has.</param>
    /// <param name="windowsLanguages">
    /// Windows' preferred languages, most wanted first, as BCP-47 tags. The region is ignored:
    /// the application is written in Spanish and English, not in `es-AR` and `en-GB`.
    /// </param>
    public static UiLanguage Resolve(UiLanguage? chosen, IEnumerable<string> windowsLanguages)
    {
        ArgumentNullException.ThrowIfNull(windowsLanguages);

        if (chosen is not null)
        {
            return chosen.Value;
        }

        foreach (var tag in windowsLanguages)
        {
            if (Parse(tag) is { } spoken)
            {
                return spoken;
            }
        }

        return WhenWindowsSpeaksNeither;
    }

    /// <summary>The BCP-47 primary subtag a language is stored and recognised as.</summary>
    public static string Tag(UiLanguage language) => language switch
    {
        UiLanguage.Spanish => "es",
        UiLanguage.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    /// <summary>
    /// The language a BCP-47 tag names, or <c>null</c> when it names one this application is not
    /// written in. Only the primary subtag is read, so `es-419` and `es` are the same answer.
    /// </summary>
    public static UiLanguage? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var parts = tag.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var primary = parts[0];

        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            if (string.Equals(primary, Tag(language), StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return null;
    }

    /// <summary>
    /// A language's name in itself — "Español", "English". It is a text like any other and lives
    /// in the catalogue like any other; what is particular about these two is that their versions
    /// are equal, which is what makes a picker readable to somebody who cannot read the language
    /// the application happens to be in.
    /// </summary>
    public static UiText Endonym(UiLanguage language) => language switch
    {
        UiLanguage.Spanish => UiTexts.SpanishName,
        UiLanguage.English => UiTexts.EnglishName,
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    /// <summary>
    /// The culture a text is formatted with. A number or a date inside a Spanish sentence is
    /// written the way Spanish writes it, so the language decides both the words and their shape.
    /// </summary>
    public static CultureInfo Culture(UiLanguage language) => CultureInfo.GetCultureInfo(Tag(language));
}
