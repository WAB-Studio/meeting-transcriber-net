using MeetingTranscriber.Presentation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The picker every screen that offers <em>one of these, none of them, or a new one</em> builds
/// out of, over a list of things the corpus holds.
/// </summary>
/// <remarks>
/// <para>
/// One construction and not one per list. The pills over the tree, the pill over a place for
/// somebody, and the pill over a meeting's voice are the same control asking the same three-part
/// question — one of these, none of them, or a new one — and the index arithmetic that turns an
/// answer back into an id is where that gets quietly wrong. It was written twice before this and
/// the same off-by-one had to be fixed in both, and a third screen was on its way to a third copy.
/// What each list offers past the rows themselves is the caller's, and is why this takes a list
/// rather than one callback: a pill over the tree offers two ways to name a new one at the top and
/// one below it, a row of people offers adding somebody, a voice offers naming somebody new, and
/// each of them offers correcting whatever already stands there. The arithmetic that reads an
/// answer back is still written once.
/// </para>
/// <para>
/// <c>SelectedIndex</c> is set before anything is subscribed, so the value this method writes
/// cannot come back as somebody having chosen it. The list is strings and never the rows
/// themselves: a <c>ComboBox</c> handed objects draws whatever they say about themselves, which is
/// a technical name on a screen that must not have one.
/// </para>
/// </remarks>
internal static class OneOfThese
{
    /// <param name="say">
    /// What a caller's own <c>In</c> reads a <see cref="UiText"/> as — handed in rather than closed
    /// over, because this is not a screen and carries no language of its own.
    /// </param>
    /// <param name="style">The pill's own style, which is the caller's own <c>Picker</c> lookup.</param>
    /// <param name="offered">What the corpus holds that may stand here, in the order it is offered.</param>
    /// <param name="standing">What stands here now, or nothing.</param>
    /// <param name="chose">Called with what was chosen, or with nothing for <em>Ninguno</em>.</param>
    /// <param name="alsoOffered">
    /// What the list offers past the things the corpus holds: naming one that is not there, and
    /// correcting the name of the one standing here. In the order they appear, which is the order
    /// the index arithmetic below reads them back in.
    /// </param>
    /// <param name="placeholder">
    /// What the pill says while nothing is chosen and nothing is typed over it. Not always
    /// <see cref="UiTexts.NoneOfThese"/>, which is always the first entry of the list regardless: a
    /// picker with nothing chosen yet reads better as <em>choose somebody</em> than as the answer
    /// that empties it.
    /// </param>
    /// <param name="nameWhenEmpty">
    /// What this pill is called, already read in the caller's language, while nothing stands in it.
    /// Already resolved rather than a <see cref="UiText"/>, because it is sometimes a heading and
    /// sometimes a voice's own handle, and only the caller knows which.
    /// </param>
    /// <param name="automationId">
    /// Where this pill stands, as the id an agent addresses it by. A caller's own coordinates and
    /// not worked out here, because it is the caller that holds them.
    /// </param>
    /// <param name="drawing">
    /// Whether the caller is building its own controls right now, so a picker being set to what it
    /// already says is not read as somebody having chosen something.
    /// </param>
    public static ComboBox Build(
        Func<UiText, string> say,
        Style style,
        IReadOnlyList<(Guid Id, string Name)> offered,
        Guid? standing,
        Action<Guid?> chose,
        IReadOnlyList<(UiText Words, Action Chose)> alsoOffered,
        UiText placeholder,
        string nameWhenEmpty,
        string automationId,
        Func<bool> drawing)
    {
        var picker = new ComboBox
        {
            Style = style,
            PlaceholderText = say(placeholder),
            ItemsSource = (string[])
            [
                say(UiTexts.NoneOfThese),
                .. offered.Select(one => one.Name),
                .. alsoOffered.Select(one => say(one.Words)),
            ],
        };

        // Nothing chosen when nothing stands here, and nothing chosen when what stands here is not
        // on the list — which is the answer that has to be spelt out. A position defaulting to zero
        // would put the pill on the first thing the list offers and read as an answer somebody
        // gave, which on a row of people is another person's name.
        var at = offered
            .Select((one, position) => (one.Id, At: position))
            .FirstOrDefault(found => found.Id == standing, (Id: Guid.Empty, At: -1));

        picker.SelectedIndex = at.At < 0 ? -1 : at.At + 1;

        // Where it stands, which is what an agent addresses it by, and never the words. An id is
        // unique by construction, is the same in both languages, and does not move when somebody
        // answers the pill — where the name is none of those three.
        AutomationProperties.SetAutomationId(picker, automationId);

        // What stands in it, and what it is called when nothing does. A glyph with no name is
        // nothing to a screen reader, and so is a pill: the name moves as the answer moves, because
        // it is read off the same `at` the selection is — so it can never say one thing while the
        // pill shows another.
        AutomationProperties.SetName(picker, at.At < 0 ? nameWhenEmpty : offered[at.At].Name);

        picker.SelectionChanged += (_, _) =>
        {
            if (drawing() || picker.SelectedIndex < 0)
            {
                return;
            }

            if (picker.SelectedIndex > offered.Count)
            {
                alsoOffered[picker.SelectedIndex - offered.Count - 1].Chose();
                return;
            }

            chose(picker.SelectedIndex == 0 ? null : offered[picker.SelectedIndex - 1].Id);
        };

        return picker;
    }
}
