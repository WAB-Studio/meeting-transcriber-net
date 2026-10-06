using MeetingTranscriber.Presentation;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MeetingTranscriber.App;

/// <summary>
/// The one place a theme somebody chose is put on a window: its root content, which draws every
/// brush a screen names, and its title bar, which is the one part of a window the content's theme
/// does not reach.
/// </summary>
/// <remarks>
/// <para>
/// On the window's root content and never on <c>Application.RequestedTheme</c>, which throws once
/// the application is running and so could only ever be a choice made at launch. <see cref="AppTheme.System"/>
/// is <see cref="ElementTheme.Default"/> and <see cref="TitleBarTheme.UseDefaultAppMode"/>, which is
/// what leaves the window following a switch of Windows' own theme while it is open.
/// </para>
/// <para>
/// <b>What stands outside the root content is not assumed to follow it.</b> An open list, a tooltip
/// and a dialogue are hosted apart from the tree they were opened from; the screens that build one
/// hand it the root's <c>ActualTheme</c> themselves (<c>AddingSomebody</c>, the pill
/// <c>CorrectTheSelection</c> shows). High Contrast falls back to the <c>Default</c> dictionary and
/// is not specially handled here.
/// </para>
/// </remarks>
internal static class ShownInTheme
{
    /// <summary>Puts <paramref name="theme"/> on a window the application makes.</summary>
    internal static void Apply(Window window, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        Apply(window.Content, window.AppWindow, theme);
    }

    /// <summary>
    /// Puts <paramref name="theme"/> on a window found from something drawn on it, which is how a
    /// screen that is not given its window reaches it.
    /// </summary>
    internal static void Apply(UIElement? root, AppWindow appWindow, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(appWindow);

        if (root is FrameworkElement content)
        {
            content.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.System => ElementTheme.Default,
                _ => throw new ArgumentOutOfRangeException(nameof(theme)),
            };
        }

        appWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            AppTheme.System => TitleBarTheme.UseDefaultAppMode,
            _ => throw new ArgumentOutOfRangeException(nameof(theme)),
        };
    }
}
