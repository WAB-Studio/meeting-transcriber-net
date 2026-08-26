using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The windows one running application has, and which of them is the screen right now.
/// </summary>
/// <remarks>
/// <para>
/// Every instruction asks this and none of them works it out for itself. The application opens a
/// second window rather than swapping a panel — the meetings are their own window — so "the
/// screen" is a question with a real answer that changes, and two instructions disagreeing about
/// it would press a button on one screen and photograph another.
/// </para>
/// <para>
/// The answer never comes from what the desktop has in front. It was written that way first and
/// it was wrong: this tool cannot raise a window and does not try, so the window in front is
/// decided by the person's last click and by whichever <c>Activate()</c> the application happened
/// to call — which means <c>press MeetingsButton</c> followed by <c>see meetings</c> would
/// photograph whichever window won a race, and write it under the name the script asked for. An
/// artifact of the wrong screen under the right name is worse than no artifact, because it is
/// believed. So the screen is what the script said it is, and before a script has said, the only
/// window there is.
/// </para>
/// </remarks>
internal sealed class AppWindows(int processId)
{
    private IntPtr _named;

    /// <summary>
    /// Says which window the rest of the script is about. <see cref="Verb.Wait"/> is the only
    /// caller: waiting for something is how a script says it has arrived somewhere, so the window
    /// that had it is the screen from then on.
    /// </summary>
    internal void TheScreenIs(AutomationElement window) => _named = Handle(window);

    /// <summary>
    /// Every top-level window the process has. <c>Window</c> and not merely "a child of the
    /// desktop with our process id": a WinUI host puts helper windows out there too, and each one
    /// would otherwise count towards the application having more than one screen.
    /// </summary>
    internal IReadOnlyList<AutomationElement> All()
    {
        var mine = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

        var found = AutomationElement.RootElement.FindAll(TreeScope.Children, mine);

        var windows = new List<AutomationElement>(found.Count);
        for (var index = 0; index < found.Count; index++)
        {
            windows.Add(found[index]);
        }

        return windows;
    }

    /// <summary>
    /// The window the script last named, while it is still open; failing that, the only one the
    /// application has. Anything else stops, because pressing a button on a screen nobody meant is
    /// worse than stopping.
    /// </summary>
    internal AutomationElement Active()
    {
        var windows = All();
        if (windows.Count == 0)
        {
            throw new ProbeFailed($"The application (process {processId}) has no window open.");
        }

        var named = windows.FirstOrDefault(window => Handle(window) == _named);
        if (named is not null)
        {
            return named;
        }

        // It was closed. What a script named once should not go on deciding anything.
        _named = IntPtr.Zero;

        if (windows.Count == 1)
        {
            return windows[0];
        }

        throw new ProbeFailed(
            $"The application has {windows.Count} windows open — {Names(windows)} — and the script "
            + "has not said which one it is on. Say so with `wait <something on it>`.");
    }

    /// <summary>
    /// Blocks until the application has a window with something on it, which is not the same
    /// moment as the process starting: a packaged application is running for a while before its
    /// first window exists, and the window exists for a while before its content does.
    /// </summary>
    internal AutomationElement WaitForAWindow(TimeSpan budget)
    {
        var found = Patience.Until(budget, () => All()
            .FirstOrDefault(window =>
                Reading.Of(() => window.FindFirst(TreeScope.Children, Condition.TrueCondition))
                is not null));

        return found ?? throw new ProbeFailed(
            $"The application started as process {processId} but no window of it had anything on "
            + $"it within {budget.TotalSeconds:0} seconds.");
    }

    internal static string Names(IEnumerable<AutomationElement> windows) =>
        string.Join(", ", windows.Select(window => $"\"{ElementWords.Name(window)}\""));

    /// <summary>
    /// Zero when the element has gone, which is never a window a script can be on. Guarded because
    /// closing one window of an application closes the others, and the loop that closes them all
    /// reads this after the first one has already taken the rest with it.
    /// </summary>
    internal static IntPtr Handle(AutomationElement window)
    {
        try
        {
            return new IntPtr(window.Current.NativeWindowHandle);
        }
        catch (ElementNotAvailableException)
        {
            return IntPtr.Zero;
        }
    }
}
