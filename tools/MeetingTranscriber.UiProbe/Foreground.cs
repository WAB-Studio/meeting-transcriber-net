using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Puts the application's window in front of the desktop and keeps it there, for as long as the
/// probe has the application open.
/// </summary>
/// <remarks>
/// <para>
/// Before this, the probe left the window wherever the person had put it and photographed it by
/// asking the window to print itself. That worked for a window nobody covered and said nothing
/// true about one somebody had: a screen behind a browser was a screen the walk could not press
/// into, and a list or a tooltip the platform opens in a window of its own was never in the
/// picture at all. A probe that walks what is on the screen has to own the screen.
/// </para>
/// <para>
/// Topmost as well as foreground, because the foreground is a thing another application can take
/// back at any moment and topmost is the part that survives it: a toast or a person's own click
/// puts the window behind another for a moment, and the picture is a copy of the desktop.
/// </para>
/// <para>
/// Windows refuses <c>SetForegroundWindow</c> to a process that is not already in front, and
/// answers by returning false and doing nothing. So three ways are tried in order and each one is
/// read back before the next: the plain call, then attaching this thread's input to the thread of
/// the window that has the foreground — which is how a process borrows the right — and then UI
/// Automation's own <c>SetFocus</c>. When all three are refused the probe stops and names what has
/// the foreground; carrying on would photograph that window under this one's name.
/// </para>
/// <para>
/// A popup of the screen having the foreground is the screen having it. A list the application
/// opened is a window of its own with the screen for its owner, and taking the foreground back
/// from it dismisses it — which would close, on every verb after <c>key</c>, the very list the
/// walk is about to look at.
/// </para>
/// </remarks>
internal static class Foreground
{
    private static readonly TimeSpan ToTake = TimeSpan.FromMilliseconds(400);

    /// <summary>Brings <paramref name="screen"/> to the front and makes it topmost, or fails saying who has it.</summary>
    internal static void Hold(AppWindows windows, AutomationElement screen)
    {
        var handle = AppWindows.Handle(screen);
        if (handle == IntPtr.Zero)
        {
            throw new ProbeFailed("The window to bring forward has gone.");
        }

        if (Native.IsIconic(handle))
        {
            Native.ShowWindow(handle, Native.SwRestore);
        }

        // Without activating it: the order of the windows is changed and nothing takes the
        // foreground, so a list that is open is not dismissed by being asked about.
        Native.SetWindowPos(
            handle,
            Native.HwndTopmost,
            0,
            0,
            0,
            0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpShowWindow | Native.SwpNoActivate);

        if (Has(windows, screen, handle))
        {
            return;
        }

        if (Taken(windows, screen, handle, () => Native.SetForegroundWindow(handle))
            || Taken(windows, screen, handle, () => BorrowedFrom(Native.GetForegroundWindow(), handle))
            || Taken(windows, screen, handle, () => Focused(screen)))
        {
            return;
        }

        throw new ProbeFailed(
            $"Windows would not bring \"{ElementWords.Name(screen)}\" to the front: "
            + $"{Titled(Native.GetForegroundWindow())} has it. Anything the probe did now would be "
            + "done to that window's screen, so it stopped.");
    }

    private static bool Taken(AppWindows windows, AutomationElement screen, IntPtr handle, Func<bool> attempt)
    {
        _ = attempt();

        return Patience.Until(ToTake, () => Has(windows, screen, handle) ? string.Empty : null) is not null;
    }

    private static bool Has(AppWindows windows, AutomationElement screen, IntPtr handle)
    {
        var front = Native.GetForegroundWindow();

        return front == handle || windows.PopupsOf(screen).Contains(front);
    }

    private static bool BorrowedFrom(IntPtr holder, IntPtr handle)
    {
        var holding = Native.GetWindowThreadProcessId(holder, out _);
        var here = Native.GetCurrentThreadId();
        if (holding == 0 || holding == here)
        {
            return Native.SetForegroundWindow(handle);
        }

        Native.AttachThreadInput(here, holding, true);
        try
        {
            Native.BringWindowToTop(handle);

            return Native.SetForegroundWindow(handle);
        }
        finally
        {
            Native.AttachThreadInput(here, holding, false);
        }
    }

    private static bool Focused(AutomationElement screen)
    {
        try
        {
            screen.SetFocus();

            return true;
        }
        catch (Exception cannot)
            when (cannot is InvalidOperationException or ElementNotAvailableException)
        {
            return false;
        }
    }

    private static string Titled(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "nothing";
        }

        var element = Reading.Of(() => AutomationElement.FromHandle(window));
        var name = element is null ? string.Empty : ElementWords.Name(element);

        return name.Length > 0 ? $"\"{name}\"" : $"a window (0x{window:X8})";
    }
}
