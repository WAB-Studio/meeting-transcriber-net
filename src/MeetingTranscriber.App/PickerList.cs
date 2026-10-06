using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MeetingTranscriber.App;

/// <summary>
/// Puts a picker's open list directly under its pill, set from <c>Olivo.xaml</c>'s
/// <c>DropDown</c> style and never from a screen.
/// </summary>
/// <remarks>
/// A <c>ComboBox</c> opens its list in a popup of its own window, placed by the platform for a
/// carousel: over the entry that is chosen, and with nothing bounding it, as wide as the monitor.
/// Neither <c>ShouldConstrainToRootBounds</c> nor <c>DesiredPlacement</c> on the template's popup
/// changed that, which a photograph of the whole desktop showed. What does hold is moving the
/// popup after the platform has opened it: its offsets are put where the pill's own bottom edge is
/// and its width is bounded by the window, queued at low priority because the platform sets its
/// own offsets while it opens and a write made during <c>DropDownOpened</c> is overwritten by them.
/// </remarks>
public static class PickerList
{
    /// <summary>Whether the picker's list is placed under its pill.</summary>
    public static readonly DependencyProperty IsUnderItsPillProperty = DependencyProperty.RegisterAttached(
        "IsUnderItsPill",
        typeof(bool),
        typeof(PickerList),
        new PropertyMetadata(false, OnChanged));

    /// <summary>Whether <paramref name="element"/> places its open list under its pill.</summary>
    public static bool GetIsUnderItsPill(DependencyObject element) =>
        (bool)element.GetValue(IsUnderItsPillProperty);

    /// <summary>Makes <paramref name="element"/> place its open list under its pill.</summary>
    public static void SetIsUnderItsPill(DependencyObject element, bool value) =>
        element.SetValue(IsUnderItsPillProperty, value);

    /// <summary>
    /// What a list is given when the pill is narrower: measuring the entries answers zero, because
    /// they stretch to whatever they are given, so a width has to be chosen. A program is listed
    /// with its window's title, which is the longest thing any picker holds.
    /// </summary>
    private const double WideEnoughForAProgramsName = 560;

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ComboBox picker)
        {
            return;
        }

        picker.DropDownOpened -= Opened;

        if ((bool)e.NewValue)
        {
            picker.DropDownOpened += Opened;
        }
    }

    private static void Opened(object? sender, object e)
    {
        var picker = (ComboBox)sender!;

        picker.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Place(picker));
    }

    private static void Place(ComboBox picker)
    {
        if (!picker.IsDropDownOpen
            || Part(picker, "Popup") is not Popup popup
            || Part(picker, "Background") is not FrameworkElement pill
            || popup.Child is not FrameworkElement list
            || picker.XamlRoot?.Content is not FrameworkElement root)
        {
            return;
        }

        // No wider than the window leaves from the pill's left edge: the popup's own window is as
        // wide as the monitor and a child left to stretch fills it.
        var left = pill.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        var room = Math.Max(pill.ActualWidth, root.ActualWidth - left - 8);

        list.Width = Math.Min(room, Math.Max(pill.ActualWidth, WideEnoughForAProgramsName));

        popup.HorizontalOffset = 0;
        popup.VerticalOffset = pill.ActualHeight + 2;
    }

    /// <summary>
    /// A part of the template, found by name. <c>GetTemplateChild</c> is the platform's own way and
    /// is protected, so this walks what the template made.
    /// </summary>
    private static DependencyObject? Part(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement { Name: var found } && found == name)
            {
                return child;
            }

            if (Part(child, name) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
