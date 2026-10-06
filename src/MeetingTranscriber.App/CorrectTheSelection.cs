using MeetingTranscriber.Domain.Knowledge;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;

using Windows.Foundation;

namespace MeetingTranscriber.App;

/// <summary>
/// The small <em>Corregir</em> press that stands at the end of words somebody selected, on a line
/// of a transcript or a voice's quotation.
/// </summary>
/// <remarks>
/// <para>
/// The application's own press and not Windows' context menu: the platform's menu is where the
/// owner could not find the act, and a menu item is also one more thing a pointer has to reach
/// after the selection it is about. Pressing it hands the selection to the screen, which asks the
/// window to open the corrections screen with the words already in its field.
/// </para>
/// <para>
/// A popup rather than an element in the line, because the line is one <see cref="TextBlock"/>
/// whose selection is the platform's to draw and a sibling element would push the next line down
/// every time somebody selected a word. It is built once for the words it stands over and shown at
/// the selection's end, in the root's own coordinates. It takes the words' own theme, since a popup
/// is hosted outside the content a window sets its theme on.
/// </para>
/// <para>
/// The words are kept at the moment they are selected and not read back at the press: they are
/// the ones the person saw selected, whatever the selection does by the time the click is
/// handled. What counts as a word at either end is <see cref="Spellings.TrimToWords"/>'s, in the
/// Domain beside the rule for what a word is, where a build agent can run it.
/// </para>
/// </remarks>
internal static class CorrectTheSelection
{
    /// <summary>The press that is on screen now, if any: only one selection is live at a time.</summary>
    private static Popup? _open;

    /// <summary>
    /// Takes the press down, whichever words it stands over. Called by a screen as it lets go of the
    /// room: the press lives in a popup outside the screen's own tree, so collapsing the screen does
    /// not take it with it, and one left up would offer to correct a meeting nobody is reading.
    /// </summary>
    public static void Dismiss()
    {
        if (_open is { } popup)
        {
            popup.IsOpen = false;
            _open = null;
        }
    }

    /// <summary>
    /// Shows <paramref name="label"/> as a press at the end of the words selected in
    /// <paramref name="words"/> for as long as the selection holds a word, and hands what was
    /// selected to <paramref name="chose"/> when it is pressed.
    /// </summary>
    /// <param name="words">The selectable words.</param>
    /// <param name="pill">The screen's own style for the press.</param>
    /// <param name="label">What the press says, in the language the screen is read in.</param>
    /// <param name="chose">Called with the selected words, trimmed.</param>
    public static void OfferOver(TextBlock words, Style pill, string label, Action<string> chose)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(pill);
        ArgumentNullException.ThrowIfNull(chose);

        string? held = null;
        Popup? popup = null;

        void Hide()
        {
            held = null;

            if (popup is not null)
            {
                popup.IsOpen = false;

                if (ReferenceEquals(_open, popup))
                {
                    _open = null;
                }
            }
        }

        var press = new Button
        {
            Style = pill,
            Content = label,

            // Pressing it must not take the focus off the words: the selection is the thing being
            // acted on, and losing it would hide this before its click was heard.
            AllowFocusOnInteraction = false,
        };

        press.Click += (_, _) =>
        {
            var chosen = held;
            Hide();

            if (chosen is not null)
            {
                chose(chosen);
            }
        };

        words.SelectionChanged += (_, _) =>
        {
            held = Spellings.TrimToWords(words.SelectedText);

            if (held is null || words.XamlRoot is null)
            {
                Hide();
                return;
            }

            popup ??= new Popup
            {
                ShouldConstrainToRootBounds = true,
                Child = press,
            };

            // Hosted outside the content a window sets its theme on, so it is told the theme the
            // words are drawn in rather than assumed to follow it.
            Dismiss();
            popup.XamlRoot = words.XamlRoot;
            press.RequestedTheme = words.ActualTheme;

            var end = words.SelectionEnd.GetCharacterRect(LogicalDirection.Forward);
            var at = words.TransformToVisual(null).TransformPoint(new Point(end.X, end.Y + end.Height));

            popup.HorizontalOffset = at.X;
            popup.VerticalOffset = at.Y + 2;
            popup.IsOpen = true;
            _open = popup;
        };

        // A line scrolled or recycled away is words nobody is looking at.
        words.EffectiveViewportChanged += (_, _) =>
        {
            if (held is not null)
            {
                Hide();
            }
        };
        words.Unloaded += (_, _) => Hide();
    }
}
