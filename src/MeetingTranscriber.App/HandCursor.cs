using System.Reflection;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace MeetingTranscriber.App;

/// <summary>
/// Shows the hand over what can be pressed, set from <c>Olivo.xaml</c>'s styles and never from a
/// screen.
/// </summary>
/// <remarks>
/// WinUI 3 keeps an element's cursor behind <c>UIElement.ProtectedCursor</c>, which only a
/// subclass may set, and there is no property for it in XAML. The attached property here sets it
/// through reflection, once resolved, so a style can say "this shows the hand" the way it says
/// every other thing about a control. The application is not published trimmed, so the member is
/// still there to find; where it is not, the platform's own cursor stands and nothing throws.
/// <para>
/// This is a cursor and not a reaction to one: <c>docs/design.md</c> §What never moves is about
/// what a control draws when the pointer is over it, and no control here draws anything.
/// </para>
/// </remarks>
public static class HandCursor
{
    /// <summary>Whether the element shows the hand over itself.</summary>
    public static readonly DependencyProperty IsShownProperty = DependencyProperty.RegisterAttached(
        "IsShown",
        typeof(bool),
        typeof(HandCursor),
        new PropertyMetadata(false, OnIsShownChanged));

    private static readonly PropertyInfo? Cursor =
        typeof(UIElement).GetProperty(
            "ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    /// <summary>One hand for every element, since the drawer makes its buttons again on each refresh.</summary>
    private static readonly InputSystemCursor Hand = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    /// <summary>Whether <paramref name="element"/> shows the hand.</summary>
    public static bool GetIsShown(DependencyObject element) =>
        (bool)element.GetValue(IsShownProperty);

    /// <summary>Makes <paramref name="element"/> show the hand, or the platform's cursor again.</summary>
    public static void SetIsShown(DependencyObject element, bool value) =>
        element.SetValue(IsShownProperty, value);

    private static void OnIsShownChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement shown || Cursor is not { CanWrite: true } cursor)
        {
            return;
        }

        cursor.SetValue(
            shown,
            (bool)e.NewValue ? Hand : null);
    }
}
