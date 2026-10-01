using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Whether the program channel 0 follows is still running, read off a handle held on it for as
/// long as channel 0 follows it.
/// </summary>
/// <remarks>
/// <para>
/// A handle and not a level, and not a list. A program that has fallen silent after playing is
/// every meeting between two sentences, so a quiet channel would offer the whole machine during a
/// pause in conversation; and asking <see cref="AudioProcesses.Running"/> every second is a
/// snapshot that cannot tell the program from whatever Windows gave its id to next. A handle
/// opened on the process pins the id for as long as it is held, and is signalled the moment the
/// process ends whatever else still holds it.
/// </para>
/// <para>
/// Two refusals are answered without throwing. Nothing running as that id when the handle is
/// opened is a program that is <i>gone from the start</i>. Windows refusing the access — an
/// elevated or protected program — is a program that is <i>not known to have gone</i>, and that is
/// never said: it is exactly what following that program did before this existed.
/// </para>
/// <para>
/// The window between the list a person picked from and this handle opening is the same window the
/// capture's own activation has; a program that ended inside it is gone from the start.
/// </para>
/// </remarks>
public sealed class FollowedProgram : IDisposable
{
    private const uint Synchronize = 0x00100000;

    /// <summary>ERROR_INVALID_PARAMETER: what <c>OpenProcess</c> answers for an id nothing runs as.</summary>
    private const int NothingRunsAsThat = 87;

    private const uint Signalled = 0x00000000;

    private readonly SafeProcessHandle? handle;
    private readonly bool goneFromTheStart;

    private FollowedProgram(SafeProcessHandle? handle, bool goneFromTheStart)
    {
        this.handle = handle;
        this.goneFromTheStart = goneFromTheStart;
    }

    /// <summary>
    /// Holds a handle on <paramref name="program"/> that says when it ends. Never throws for the
    /// operating system refusing the handle; see the remarks on the type for the two ways it does.
    /// </summary>
    /// <param name="program">The program channel 0 begins to follow.</param>
    public static FollowedProgram Watching(AudioProcess program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var opened = OpenProcess(Synchronize, false, program.Id);
        if (!opened.IsInvalid)
        {
            return new FollowedProgram(opened, goneFromTheStart: false);
        }

        var why = Marshal.GetLastWin32Error();
        opened.Dispose();
        return new FollowedProgram(null, goneFromTheStart: why == NothingRunsAsThat);
    }

    /// <summary>
    /// Whether the program ended since it began to be followed: the handle is signalled, or there
    /// was nothing to hold. A program that is running, a wait that failed and a handle already let
    /// go of are all false.
    /// </summary>
    /// <remarks>
    /// Read without any gate, once a second, from the screen's thread. The handle is a
    /// <see cref="SafeHandle"/> so that the marshaller holds a reference on it for the length of
    /// the wait: a <see cref="Dispose"/> landing meanwhile defers closing until the wait returns,
    /// and a raw handle closed mid-call could be handed by Windows to another object whose
    /// signalled state would read as the program having gone.
    /// </remarks>
    public bool HasGone
    {
        get
        {
            if (goneFromTheStart)
            {
                return true;
            }

            if (handle is null)
            {
                return false;
            }

            try
            {
                return WaitForSingleObject(handle, 0) == Signalled;
            }
            catch (ObjectDisposedException)
            {
                // Let go of between the field being read and the wait: a watch nobody asks about
                // any longer, and not a program that went.
                return false;
            }
        }
    }

    /// <summary>Lets go of the handle. Saying it twice is saying it once, and it never throws.</summary>
    public void Dispose() => handle?.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeHandle handle, uint milliseconds);
}
