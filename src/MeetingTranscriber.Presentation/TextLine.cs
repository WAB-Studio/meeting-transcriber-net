namespace MeetingTranscriber.Presentation;

/// <summary>
/// A line a screen produced while it was running, kept as what it says rather than as what it
/// currently reads as.
/// </summary>
/// <remarks>
/// Text a screen was born with is re-read by asking the catalogue again. Text it produced —
/// a result, a status, a report — is not: the words were already chosen, and a screen holding
/// them as a string is precisely the stretch that stays in the previous language when somebody
/// switches. Holding the line instead of its rendering is what makes the switch total.
/// </remarks>
public sealed class TextLine
{
    private readonly string _data;
    private readonly UiText? _text;
    private readonly object?[] _values;

    private TextLine(UiText? text, object?[] values, string data)
    {
        _text = text;
        _values = values;
        _data = data;
    }

    /// <summary>Something the application says, with the values it leaves room for.</summary>
    public static TextLine Says(UiText text, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextLine(text, values ?? [], string.Empty);
    }

    /// <summary>
    /// A line that is data and not a sentence — a path, an environment variable, a blank — and so
    /// reads the same in every language.
    /// </summary>
    public static TextLine Data(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new TextLine(null, [], data);
    }

    /// <summary>
    /// What the line reads as in this language. A value that is itself a text is read in the same
    /// language as the line carrying it, so a "yes" inside a Spanish sentence does not come out in
    /// English.
    /// </summary>
    public string In(UiLanguage language) => _text is null
        ? _data
        : _text.In(language, _values.Select(value => value is UiText nested ? nested.In(language) : value).ToArray());
}
