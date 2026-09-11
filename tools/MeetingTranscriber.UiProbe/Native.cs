using System.Runtime.InteropServices;
using System.Text;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The Win32 and COM entry points this tool cannot reach any other way: activating a packaged
/// application and getting its process back, asking which window is in front, copying the pixels
/// under one, and putting a keystroke into the input queue.
/// </summary>
internal static class Native
{
    /// <summary>
    /// <c>PW_RENDERFULLCONTENT</c>: print what the window composes rather than only what it
    /// paints into its own device context.
    /// </summary>
    internal const uint PW_RENDERFULLCONTENT = 2;

    /// <summary><c>INPUT_KEYBOARD</c>.</summary>
    internal const uint InputKeyboard = 1;

    /// <summary><c>KEYEVENTF_KEYUP</c>: the release rather than the press.</summary>
    internal const uint KeyUp = 0x0002;

    internal const int BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;
    internal const uint WM_CLOSE = 0x0010;

    /// <summary><c>JobObjectExtendedLimitInformation</c>.</summary>
    internal const int JobObjectExtendedLimitInformation = 9;

    /// <summary>
    /// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: when the last handle to the job goes, so does
    /// everything in it.
    /// </summary>
    internal const uint JobLimitKillOnJobClose = 0x2000;

    /// <summary>
    /// <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>. Without it Windows lies to this process
    /// about where the window is and how big, and the picture comes out cropped on any display
    /// that is not at 100%.
    /// </summary>
    internal static readonly IntPtr PerMonitorAwareV2 = (IntPtr)(-4);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    /// <summary><c>KEYBDINPUT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort wVk;
        internal ushort wScan;
        internal uint dwFlags;
        internal uint time;
        internal IntPtr dwExtraInfo;
    }

    /// <summary>
    /// <c>MOUSEINPUT</c>, declared and never used.
    /// </summary>
    /// <remarks>
    /// It is here because it is the largest arm of <c>INPUT</c>'s union — 32 bytes against
    /// <c>KEYBDINPUT</c>'s 24 on x64 — and therefore what fixes the size of the whole structure.
    /// <c>SendInput</c> is passed that size and rejects any other by doing nothing, so leaving the
    /// arm out would give a probe whose key verb silently pressed no key, which is the exact failure
    /// that verb exists not to have. <c>HARDWAREINPUT</c> is the third arm and is deliberately
    /// absent: it is eight bytes, so it changes no size, and a declaration that measures nothing and
    /// is never assigned is a line somebody has to work out the purpose of.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int dx;
        internal int dy;
        internal uint mouseData;
        internal uint dwFlags;
        internal uint time;
        internal IntPtr dwExtraInfo;
    }

    /// <summary>The union inside <c>INPUT</c>.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal MouseInput Mouse;

        [FieldOffset(0)]
        internal KeyboardInput Keyboard;
    }

    /// <summary><c>INPUT</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Union;
    }

    /// <summary>
    /// What <see cref="SendInput"/> is told one <see cref="Input"/> measures. Asked of the runtime
    /// rather than written down: the number differs between 32- and 64-bit, and a wrong one is
    /// refused by doing nothing.
    /// </summary>
    internal static readonly int InputSize = Marshal.SizeOf<Input>();

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint biSize;
        internal int biWidth;
        internal int biHeight;
        internal ushort biPlanes;
        internal ushort biBitCount;
        internal uint biCompression;
        internal uint biSizeImage;
        internal int biXPelsPerMeter;
        internal int biYPelsPerMeter;
        internal uint biClrUsed;
        internal uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    /// <summary>
    /// Puts events into the same queue a keyboard does, which is the only way to raise a real key
    /// event.
    /// </summary>
    /// <remarks>
    /// UI Automation has no pattern for a keystroke: <c>ValuePattern.SetValue</c> writes a field
    /// without a key ever going down, so a control that commits on Enter never hears one. Posting
    /// <c>WM_KEYDOWN</c> at the window is the other candidate and is not the same thing — XAML
    /// routes keyboard input off the queue, so a posted message reaches the window procedure and no
    /// handler on the screen. What comes back is how many events were accepted, and zero is the
    /// answer when a window of higher integrity is in front.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint milliseconds,
        out IntPtr result);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(
        IntPtr deviceContext,
        IntPtr bitmap,
        uint firstLine,
        uint lines,
        byte[]? pixels,
        ref BitmapInfoHeader header,
        uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr deviceContext);

    /// <summary>
    /// The shell's own launcher for a packaged application. `Process.Start` cannot open one — the
    /// executable in the package layout is not what a person double-clicks, and starting it
    /// directly gives a process with no package identity, which is the one thing this application
    /// needs to have. This is the call that hands back the process id, which is the whole reason
    /// it is here rather than `explorer.exe shell:AppsFolder\...`.
    /// </summary>
    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    /// <summary>
    /// <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c>. Every field is here and only one is set, because
    /// the structure is passed by size and a short one is rejected — see <see cref="Leash"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobBasicLimits
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    /// <summary><c>IO_COUNTERS</c>: read, never written, and part of the size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    /// <summary><c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct JobExtendedLimits
    {
        internal JobBasicLimits BasicLimitInformation;
        internal IoCounters IoInfo;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateJobObject(IntPtr security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        ref JobExtendedLimits information,
        uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// The image a process is running, asked of the kernel rather than of the process's loaded
    /// module list. <c>length</c> goes in as the room in <c>name</c> and comes out as what was
    /// written.
    /// </summary>
    /// <remarks>
    /// The module list is what <c>Process.MainModule</c> reads, and a process Windows has only just
    /// created has one entry in it: <c>ntdll.dll</c>, before the loader has mapped the executable.
    /// So the module list answers the question wrongly for a while and never says it is not ready.
    /// This one has no such window — the path is a property of the process from the moment it
    /// exists — which is why the probe asks it instead. <c>LaunchedApp.Start</c> is the reason it
    /// matters: it reads that path within milliseconds of an activation.
    /// </remarks>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr process, uint flags, StringBuilder name, ref uint length);
}
