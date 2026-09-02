namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Starts this application, does what it is told to its windows, says what they said, and ends it
/// — by closing it, or by killing it when what is being probed is what a crash leaves.
/// </summary>
/// <remarks>
/// <para>
/// It exists because everything else that checks a screen reads the screen's source. Source says
/// what a screen was written to do; only a running window says what it does — that it opened at
/// all, that the button is alive, that the second screen is reached by pressing the first.
/// </para>
/// <para>
/// Two hosts over one <see cref="Session"/>, doing two jobs. <see cref="CommandLine"/> runs a whole
/// script in one process, which is what makes a finding repeatable and pasteable. <see
/// cref="McpHost"/> holds the application open and answers every call with what the screen became,
/// which is what building a screen needs. Neither replaces the other.
/// </para>
/// <para>
/// Only the entry point differs, and this file is what makes that true rather than nearly true.
/// The two process-wide facts a window depends on — real pixels, and which thread and apartment it
/// is touched on — are settled here, once, before either host exists. Left to the hosts they would
/// have been settled twice and differently, and a script pasted out of a server session into a
/// pull request would have reproduced a finding under conditions the finding was not made in.
/// </para>
/// <para>
/// Both need a desktop somebody is logged into, so this is run by hand and never by a build.
/// Nothing under <c>src/</c> or <c>tests/</c> knows it exists. <c>docs/ui-probe.md</c> is how to
/// use it.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Process-wide, and Windows takes it once: a host that asked a second time would be
        // refused on a call that had already succeeded. Without it Windows reports this process a
        // made-up desktop, and every picture comes out cropped on a display that is not at 100%.
        if (!Native.SetProcessDpiAwarenessContext(Native.PerMonitorAwareV2))
        {
            Console.Error.WriteLine(
                "Windows would not let this process see real pixels, so every picture it took "
                + "would be the wrong size on any display that is not at 100%.");

            return CommandLine.BadProbe;
        }

        using var windows = UiThread.Start();

        return args is ["--mcp"]
            ? await McpHost.RunAsync(windows)
            : await windows.RunAsync(() => CommandLine.Run(args));
    }
}
