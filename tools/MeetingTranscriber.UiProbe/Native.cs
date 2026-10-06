using System.Runtime.InteropServices;
using System.Text;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The Win32 and COM entry points this tool cannot reach any other way: activating a packaged
/// application and getting its process back, bringing its window in front and keeping it there,
/// copying the pixels of the desktop under it, and putting a keystroke or a pointer movement into
/// the input queue.
/// </summary>
internal static class Native
{
    /// <summary><c>INPUT_MOUSE</c>.</summary>
    internal const uint InputMouse = 0;

    /// <summary><c>MOUSEEVENTF_MOVE</c>.</summary>
    internal const uint MouseMove = 0x0001;

    /// <summary><c>MOUSEEVENTF_LEFTDOWN</c>.</summary>
    internal const uint MouseLeftDown = 0x0002;

    /// <summary><c>MOUSEEVENTF_LEFTUP</c>.</summary>
    internal const uint MouseLeftUp = 0x0004;

    /// <summary><c>MOUSEEVENTF_VIRTUALDESK</c>: the absolute position is over every monitor.</summary>
    internal const uint MouseVirtualDesk = 0x4000;

    /// <summary><c>MOUSEEVENTF_ABSOLUTE</c>: the position is a place and not a movement.</summary>
    internal const uint MouseAbsolute = 0x8000;

    /// <summary><c>GW_OWNER</c>.</summary>
    internal const uint GwOwner = 4;

    /// <summary><c>SW_RESTORE</c>.</summary>
    internal const int SwRestore = 9;

    /// <summary><c>SWP_NOSIZE</c>.</summary>
    internal const uint SwpNoSize = 0x0001;

    /// <summary><c>SWP_NOMOVE</c>.</summary>
    internal const uint SwpNoMove = 0x0002;

    /// <summary><c>SWP_NOZORDER</c>.</summary>
    internal const uint SwpNoZOrder = 0x0004;

    /// <summary><c>SWP_NOACTIVATE</c>.</summary>
    internal const uint SwpNoActivate = 0x0010;

    /// <summary><c>SWP_SHOWWINDOW</c>.</summary>
    internal const uint SwpShowWindow = 0x0040;

    /// <summary><c>HWND_TOPMOST</c>.</summary>
    internal static readonly IntPtr HwndTopmost = new(-1);

    /// <summary><c>SM_XVIRTUALSCREEN</c>.</summary>
    internal const int SmXVirtualScreen = 76;

    /// <summary><c>SM_YVIRTUALSCREEN</c>.</summary>
    internal const int SmYVirtualScreen = 77;

    /// <summary><c>SM_CXVIRTUALSCREEN</c>.</summary>
    internal const int SmCxVirtualScreen = 78;

    /// <summary><c>SM_CYVIRTUALSCREEN</c>.</summary>
    internal const int SmCyVirtualScreen = 79;

    /// <summary><c>SRCCOPY</c>.</summary>
    internal const uint SrcCopy = 0x00CC0020;

    /// <summary>
    /// <c>CAPTUREBLT</c>: include the layered windows — a tooltip, a flyout, a list a platform
    /// control opens in a window of its own — which a plain copy of the desktop leaves out.
    /// </summary>
    internal const uint CaptureBlt = 0x40000000;

    /// <summary><c>CURSOR_SHOWING</c>.</summary>
    internal const uint CursorShowing = 0x0001;

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

    /// <summary><c>CURSORINFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorInfo
    {
        internal int cbSize;
        internal uint flags;
        internal IntPtr hCursor;
        internal Point ptScreenPos;
    }

    /// <summary>
    /// <c>MOUSEINPUT</c>: what the pointer verbs send, and the largest arm of <c>INPUT</c>'s union.
    /// </summary>
    /// <remarks>
    /// It is 32 bytes against <c>KEYBDINPUT</c>'s 24 on x64 and therefore what fixes the size of the
    /// whole structure. <c>SendInput</c> is passed that size and rejects any other by doing
    /// nothing, so leaving the arm out would give a probe whose key verb silently pressed no key,
    /// which is the exact failure that verb exists not to have. <c>HARDWAREINPUT</c> is the third
    /// arm and is deliberately absent: it is eight bytes, so it changes no size, and a declaration
    /// that measures nothing and is never assigned is a line somebody has to work out the purpose
    /// of.
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

    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(
        uint attach, uint to, [MarshalAs(UnmanagedType.Bool)] bool attached);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(
        IntPtr to, int x, int y, int width, int height, IntPtr from, int fromX, int fromY, uint operation);

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

    /// <summary><c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</summary>
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

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
