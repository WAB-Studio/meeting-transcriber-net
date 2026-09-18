namespace MeetingTranscriber.Presentation;

/// <summary>
/// The one line a screen is saying about what happened around it, kept as what it says rather than
/// as what it currently reads as.
/// </summary>
/// <remarks>
/// <para>
/// Five screens keep a <c>TextLine? _status</c>, a way of setting it, a way of clearing it and a
/// line of rendering, and they had already drifted: two of them carry a press's sentence across a
/// re-read and the rest do not. One engine, and the difference between the two is a named act
/// rather than a habit — <see cref="Says(UiText, object?[])"/> overwrites and
/// <see cref="KeepsWhatWasSaid"/> is the carry-over.
/// </para>
/// <para>
/// Here and not beside a window, for the reason <c>docs/layout.md</c> gives: this is what a screen
/// <em>says</em>, which is what this project holds, and it references nothing so a test can load it.
/// It holds no control and writes to none — a screen reads <see cref="In"/> and
/// <see cref="IsSaying"/> and puts them where its own XAML says they go.
/// </para>
/// </remarks>
public sealed class ScreenStatus
{
    private TextLine? _line;

    /// <summary>The line as it stands, or nothing.</summary>
    public TextLine? Line => _line;

    /// <summary>Whether there is anything to show.</summary>
    public bool IsSaying => _line is not null;

    /// <summary>Says something, over whatever was being said.</summary>
    public void Says(UiText text, params object?[] values) => _line = TextLine.Says(text, values);

    /// <summary>Says a line somebody else built, over whatever was being said.</summary>
    public void Says(TextLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _line = line;
    }

    /// <summary>Stops saying anything.</summary>
    public void Nothing() => _line = null;

    /// <summary>
    /// Puts <paramref name="said"/> back only where nothing is being said.
    /// </summary>
    /// <remarks>
    /// The rule a re-read needs: a read that failed put its own sentence here and that one wins,
    /// because it is about the list now on screen; a read that went through left this empty and
    /// whatever the last press said goes back. Clearing it instead would take "it is in the queue
    /// now" off the screen a second after the press that spends the money.
    /// </remarks>
    public void KeepsWhatWasSaid(TextLine? said) => _line ??= said;

    /// <summary>What it reads as in this language, or the empty string where nothing is said.</summary>
    public string In(UiLanguage language) => _line?.In(language) ?? string.Empty;
}
