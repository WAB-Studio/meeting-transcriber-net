using System.Runtime.InteropServices;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The Win32 and COM entry points this tool cannot reach any other way: activating a packaged
/// application and getting its process back, asking which window is in front, and copying the
/// pixels under one.
/// </summary>
internal static class Native
{
    /// <summary>
    /// <c>PW_RENDERFULLCONTENT</c>: print what the window composes rather than only what it
    /// paints into its own device context.
    /// </summary>
    internal const uint PW_RENDERFULLCONTENT = 2;

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
}
