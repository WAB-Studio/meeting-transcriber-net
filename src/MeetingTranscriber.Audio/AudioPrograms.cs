using System.Runtime.InteropServices;
using System.Text;

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingTranscriber.Audio;

/// <summary>A visible, titled top-level window and the process that owns it.</summary>
/// <param name="Process">The owning process id.</param>
/// <param name="Title">What the window's title bar says.</param>
public sealed record ProgramWindow(int Process, string Title);

/// <summary>One entry in the list of programs channel 0 can be pointed at.</summary>
/// <param name="Process">
/// The root of the application's own same-name tree. This is what is followed, so the tree rule
/// the capture is built on is untouched by how the list is drawn.
/// </param>
/// <param name="Title">
/// The first window title of the tree in z-order, or nothing at all when the application has no
/// window. It is for a person to tell two instances apart and is never what a choice is compared
/// on: a browser's title changes with its tab.
/// </param>
public sealed record OfferedProgram(AudioProcess Process, string Title);

/// <summary>
/// The programs a person is offered for channel 0: one per application, the way OBS lists them.
/// </summary>
/// <remarks>
/// <para>
/// A machine runs hundreds of processes and a browser alone is twenty of them, so the list is not
/// the processes. It is every application that has a visible titled window or holds an audio
/// session on an active playback endpoint, each reduced to the root of its own same-name tree —
/// the rule <see cref="AudioProcesses.Choose"/> already follows a name with — and a session whose
/// owner stands under a windowed application is that application: a web view playing for Teams is
/// Teams. Windows only would leave out a meeting rig with none; sessions only would leave out Zoom
/// before the meeting starts.
/// </para>
/// <para>
/// Split like <see cref="AudioProcesses"/>: <see cref="OnePerProgram"/> is the rule and asks the
/// machine nothing, <see cref="Offered"/> is the machine and answers to the rule. This
/// application and the shell are never offered and never what something folds into. The shell is
/// found by the process of <c>GetShellWindow()</c> and never by the name <c>explorer</c>; it owns
/// visible windows and parents everything started from Start or the taskbar, so a player in the
/// tray folded into it would be left out with it and offered nowhere.
/// </para>
/// <para>
/// The command line's <c>capture --follow</c> is not this list: it still matches a name or an id.
/// </para>
/// </remarks>
public static class AudioPrograms
{
    private const int OwnerWindow = 4;
    private const int ExtendedStyle = -20;
    private const long ToolWindow = 0x00000080;
    private const int Cloaked = 14;

    /// <summary>
    /// The process every Store application's frame window belongs to. It makes no sound: the
    /// application's own process holds the audio session, and offering the frame would be
    /// offering a program that captures nothing. The application is still offered by its session.
    /// </summary>
    private const string StoreFrame = "ApplicationFrameHost";

    private delegate bool WindowVisit(IntPtr window, IntPtr state);

    /// <summary>
    /// Which programs to offer, out of what is running, which windows there are, who plays and who
    /// is asking. Windowed applications come first, in the z-order of their first window, then those
    /// that only play; the screen orders them for a person (<c>RecorderScreen.ProgramsOnOffer</c>).
    /// </summary>
    /// <param name="running">Every process, with its parent.</param>
    /// <param name="windows">Visible titled windows, topmost first.</param>
    /// <param name="playing">The ids of the processes holding an audio session.</param>
    /// <param name="self">This application's process id.</param>
    /// <param name="shell">The process id of the shell's window.</param>
    public static IReadOnlyList<OfferedProgram> OnePerProgram(
        IReadOnlyList<AudioProcess> running,
        IReadOnlyList<ProgramWindow> windows,
        IReadOnlySet<int> playing,
        int self,
        int shell)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(playing);

        var byId = running.ToLookup(process => process.Id);

        bool NeverOffered(AudioProcess process) => process.Id == self || process.Id == shell;

        // The windowed applications, keyed by their root and kept in z-order: the first window of
        // a tree is the first one met.
        var windowed = new Dictionary<int, OfferedProgram>();

        foreach (var window in windows)
        {
            if (byId[window.Process].FirstOrDefault() is not { } owner
                || NeverOffered(owner)
                || owner.Name.Equals(StoreFrame, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = AudioProcesses.RootOf(owner, byId);
            if (!NeverOffered(root))
            {
                windowed.TryAdd(root.Id, new OfferedProgram(root, window.Title));
            }
        }

        var alone = new Dictionary<int, OfferedProgram>();
        var windowedRoots = windowed.Keys.ToHashSet();

        foreach (var id in playing)
        {
            if (byId[id].FirstOrDefault() is not { } player || NeverOffered(player))
            {
                continue;
            }

            var root = AudioProcesses.RootOf(player, byId);
            if (NeverOffered(root) || FoldsInto(player, byId, windowedRoots, NeverOffered))
            {
                continue;
            }

            alone.TryAdd(root.Id, new OfferedProgram(root, string.Empty));
        }

        return
        [
            .. windowed.Values,
            .. alone.Values
                .OrderBy(program => program.Process.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(program => program.Process.Id),
        ];
    }

    /// <summary>
    /// Every application worth offering right now, asked of the machine.
    /// </summary>
    /// <remarks>
    /// The playback endpoints' sessions are bounded by <see cref="DeviceEnquiry"/> like the
    /// questions about devices, because walking them is the audio service's and can sit in a stuck
    /// one for as long as it likes. A process that ends while this reads is a refusal from the
    /// capture, not something to guard here.
    /// </remarks>
    /// <exception cref="AudioCaptureException">
    /// Windows would not say what runs or plays, within the deadline or at all.
    /// </exception>
    public static IReadOnlyList<OfferedProgram> Offered()
    {
        var running = AudioProcesses.Running();
        var windows = VisibleWindows();
        var playing = DeviceEnquiry.Answering(DeviceQuestion.ProgramsPlaying, ProcessesPlaying);

        GetWindowThreadProcessId(GetShellWindow(), out var shell);

        return OnePerProgram(running, windows, playing, Environment.ProcessId, (int)shell);
    }

    /// <summary>
    /// Whether <paramref name="player"/>, or something above it, belongs to a windowed application:
    /// walking up, the first step that stands in the tree of one. The walk stops at this
    /// application and at the shell, which nothing folds into and nothing is folded past.
    /// </summary>
    private static bool FoldsInto(
        AudioProcess player,
        ILookup<int, AudioProcess> byId,
        HashSet<int> windowedRoots,
        Func<AudioProcess, bool> neverOffered)
    {
        foreach (var step in new[] { player }.Concat(AudioProcesses.Ancestry(player, byId)))
        {
            if (neverOffered(step))
            {
                return false;
            }

            if (windowedRoots.Contains(AudioProcesses.RootOf(step, byId).Id))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<int> ProcessesPlaying()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var ids = new HashSet<int>();

            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (endpoint)
                {
                    var sessions = endpoint.AudioSessionManager.Sessions;

                    for (var index = 0; index < sessions.Count; index++)
                    {
                        using var session = sessions[index];

                        try
                        {
                            // Process 0 is the system's own sounds, and an expired session is one
                            // whose program has ended.
                            if (session.GetProcessID != 0
                                && session.State != AudioSessionState.AudioSessionStateExpired)
                            {
                                ids.Add((int)session.GetProcessID);
                            }
                        }
                        catch (COMException)
                        {
                            // A session torn down between being listed and being read is one
                            // program closing, not a reason to offer nothing.
                        }
                    }
                }
            }

            return ids;
        }
        catch (COMException refused)
        {
            throw new AudioCaptureException(
                $"Windows would not answer about what is playing: {refused.Message}", refused);
        }
    }

    /// <summary>
    /// The windows a person would call open: visible, titled, with no owner (a dialogue belongs to
    /// the window that raised it), not a tool window and not cloaked (a window on another virtual
    /// desktop and a suspended UWP shell are both visible and neither is there). Topmost first,
    /// which is the order <c>EnumWindows</c> walks.
    /// </summary>
    private static List<ProgramWindow> VisibleWindows()
    {
        var found = new List<ProgramWindow>();

        EnumWindows(
            (window, _) =>
            {
                if (!IsWindowVisible(window)
                    || GetWindow(window, OwnerWindow) != IntPtr.Zero
                    || (GetWindowLongPtr(window, ExtendedStyle).ToInt64() & ToolWindow) != 0
                    || (DwmGetWindowAttribute(window, Cloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                {
                    return true;
                }

                var length = GetWindowTextLength(window);
                if (length == 0)
                {
                    return true;
                }

                var title = new StringBuilder(length + 1);
                if (GetWindowText(window, title, title.Capacity) == 0)
                {
                    return true;
                }

                GetWindowThreadProcessId(window, out var process);
                found.Add(new ProgramWindow((int)process, title.ToString()));
                return true;
            },
            IntPtr.Zero);

        return found;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowVisit visit, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int relation);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
