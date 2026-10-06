using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Whether the program channel 0 follows is still running: its root's handle, held for as long as
/// channel 0 follows it, and then whether anything it started is still alive.
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
/// A signalled root is not yet a program that went. A launcher-style program starts the process
/// that plays and exits, and a meeting in it goes on: the tree is what is followed, so the program
/// has gone only when its root's handle is signalled <i>and</i> no running process has the root's
/// id in its parent chain. The list is asked only once the root is signalled, so a program that
/// is running costs a handle wait and never a snapshot, while a root that ended with children
/// still playing costs one on each look; and the root's id cannot be handed to
/// another process while the handle is held, so the chain is read against the id of the program
/// that was followed.
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

    /// <summary>How far up a parent chain is followed before it is taken for a loop.</summary>
    private const int Ancestors = 64;

    private readonly SafeProcessHandle? handle;
    private readonly bool goneFromTheStart;
    private readonly int root;
    private readonly Func<IReadOnlyList<AudioProcess>> running;
    private volatile bool treeEnded;

    private FollowedProgram(
        SafeProcessHandle? handle,
        bool goneFromTheStart,
        int root,
        Func<IReadOnlyList<AudioProcess>> running)
    {
        this.handle = handle;
        this.goneFromTheStart = goneFromTheStart;
        this.root = root;
        this.running = running;
    }

    /// <summary>
    /// Holds a handle on <paramref name="program"/> that says when it ends. Never throws for the
    /// operating system refusing the handle; see the remarks on the type for the two ways it does.
    /// </summary>
    /// <param name="program">The program channel 0 begins to follow.</param>
    /// <param name="running">
    /// What is running, asked once the root has ended. Nothing means
    /// <see cref="AudioProcesses.Running"/>; a test answers it.
    /// </param>
    public static FollowedProgram Watching(
        AudioProcess program,
        Func<IReadOnlyList<AudioProcess>>? running = null)
    {
        ArgumentNullException.ThrowIfNull(program);

        running ??= AudioProcesses.Running;

        var opened = OpenProcess(Synchronize, false, program.Id);
        if (!opened.IsInvalid)
        {
            return new FollowedProgram(opened, goneFromTheStart: false, program.Id, running);
        }

        var why = Marshal.GetLastWin32Error();
        opened.Dispose();
        return new FollowedProgram(null, goneFromTheStart: why == NothingRunsAsThat, program.Id, running);
    }

    /// <summary>
    /// Whether the program ended since it began to be followed: the root's handle is signalled and
    /// nothing it started is still running, or there was nothing to hold. A program that is
    /// running, a root that ended while its children go on, a wait or a list that failed and a
    /// handle already let go of are all false.
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

            if (treeEnded)
            {
                return true;
            }

            try
            {
                if (WaitForSingleObject(handle, 0) != Signalled)
                {
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                // Let go of between the field being read and the wait: a watch nobody asks about
                // any longer, and not a program that went.
                return false;
            }

            // The root ended. Whether the program did is whether anything is still standing under
            // it, and a list that cannot be had says nothing is known, as a refused handle does.
            try
            {
                treeEnded = !AnythingStandsUnderTheRoot(running());
            }
            catch (AudioCaptureException)
            {
                return false;
            }

            return treeEnded;
        }
    }

    /// <summary>Whether any process in <paramref name="processes"/> descends from the root.</summary>
    /// <remarks>
    /// Walked through the list and not through the immediate parent, because a launcher starts a
    /// helper and the helper starts the window. A chain that comes back round is a reused id and
    /// is not a descent, and one whose middle has itself ended is not followed: the snapshot no
    /// longer says whose child its last process was.
    /// </remarks>
    private bool AnythingStandsUnderTheRoot(IReadOnlyList<AudioProcess> processes)
    {
        var byId = processes.ToLookup(process => process.Id);

        bool Descends(AudioProcess process)
        {
            var above = process.StartedBy;

            for (var step = 0; step < Ancestors; step++)
            {
                if (above == root)
                {
                    return true;
                }

                var parent = byId[above].FirstOrDefault();
                if (parent is null || parent.StartedBy == above || above == process.Id)
                {
                    return false;
                }

                above = parent.StartedBy;
            }

            return false;
        }

        return processes.Any(process => process.Id != root && Descends(process));
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
