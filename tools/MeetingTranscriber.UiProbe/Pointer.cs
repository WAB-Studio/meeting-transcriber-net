using System.Runtime.InteropServices;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The person's own mouse, moved and pressed through the input queue, and the cursor it is showing
/// read back by name.
/// </summary>
/// <remarks>
/// <para>
/// UI Automation has no hover, no cursor and no drag, and the three things this exists to prove —
/// that a press shows the hand, that a bar drags the window and a slider its value, and that a
/// line of a transcript can be selected — are all things a real pointer does and an element's
/// patterns only describe. So the pointer is real: absolute moves over the whole virtual desktop,
/// in physical pixels, normalised to the 0..65535 the call wants. The process is already per-monitor
/// aware, which is what makes the two the same pixels.
/// </para>
/// <para>
/// It takes the mouse from whoever is holding it, and says so in <c>docs/ui-probe.md</c> and in
/// the server's description: <c>hover</c>, <c>drag</c> and <c>select</c> are the verbs that cannot
/// be run while a person is using the same machine.
/// </para>
/// </remarks>
internal static class Pointer
{
    /// <summary>The cursors a probe tells apart, by the system's own name for them.</summary>
    private static readonly (string Name, int Id)[] Named =
    [
        ("arrow", 32512),
        ("ibeam", 32513),
        ("wait", 32514),
        ("cross", 32515),
        ("hand", 32649),
        ("sizeall", 32646),
        ("sizenwse", 32642),
        ("sizenesw", 32643),
        ("sizewe", 32644),
        ("sizens", 32645),
        ("no", 32648),
        ("appstarting", 32650),
    ];

    /// <summary>Puts the pointer at a physical pixel of the virtual desktop.</summary>
    internal static void MoveTo(int x, int y) =>
        Send(Native.MouseMove | Native.MouseAbsolute | Native.MouseVirtualDesk, x, y);

    /// <summary>Presses the left button where the pointer is.</summary>
    internal static void Press() => Send(Native.MouseLeftDown, 0, 0);

    /// <summary>Releases the left button where the pointer is.</summary>
    internal static void Release() => Send(Native.MouseLeftUp, 0, 0);

    /// <summary>
    /// Where the cursor is, and what it is showing there: one of the system's names,
    /// <c>another (0x…)</c> for a handle that is not one of them, or <c>hidden</c>.
    /// </summary>
    internal static (string Name, int X, int Y) CursorAt()
    {
        var info = new Native.CursorInfo { cbSize = Marshal.SizeOf<Native.CursorInfo>() };
        if (!Native.GetCursorInfo(ref info))
        {
            throw new ProbeFailed(
                $"Windows would not say which cursor is showing ({Marshal.GetLastWin32Error()}).");
        }

        if ((info.flags & Native.CursorShowing) == 0)
        {
            return ("hidden", info.ptScreenPos.X, info.ptScreenPos.Y);
        }

        foreach (var (name, id) in Named)
        {
            if (Native.LoadCursor(IntPtr.Zero, new IntPtr(id)) == info.hCursor)
            {
                return (name, info.ptScreenPos.X, info.ptScreenPos.Y);
            }
        }

        return ($"another (0x{info.hCursor:X})", info.ptScreenPos.X, info.ptScreenPos.Y);
    }

    private static void Send(uint flags, int x, int y)
    {
        var left = Native.GetSystemMetrics(Native.SmXVirtualScreen);
        var top = Native.GetSystemMetrics(Native.SmYVirtualScreen);
        var width = Native.GetSystemMetrics(Native.SmCxVirtualScreen);
        var height = Native.GetSystemMetrics(Native.SmCyVirtualScreen);

        var absolute = (flags & Native.MouseAbsolute) != 0;
        Native.Input[] one =
        [
            new()
            {
                Type = Native.InputMouse,
                Union = new Native.InputUnion
                {
                    Mouse = new Native.MouseInput
                    {
                        dx = absolute ? Normalised(x - left, width) : 0,
                        dy = absolute ? Normalised(y - top, height) : 0,
                        dwFlags = flags,
                    },
                },
            },
        ];

        if (Native.SendInput(1, one, Native.InputSize) != 1)
        {
            throw new ProbeFailed(
                $"Windows would not take the pointer ({Marshal.GetLastWin32Error()}). Something has "
                + "the input queue: a UAC prompt, a locked workstation, or an application running "
                + "higher than this one.");
        }
    }

    /// <summary>
    /// A pixel as the 0..65535 an absolute move is written in, rounded to the middle of the pixel
    /// so that it reads back as the same one.
    /// </summary>
    private static int Normalised(int pixel, int span) =>
        (int)((((long)pixel * 2) + 1) * 65536 / (span * 2L));
}
