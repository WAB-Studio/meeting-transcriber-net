using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The application, running, for as long as this object is alive.
/// </summary>
/// <remarks>
/// <para>
/// Opened through the shell's activation manager rather than by starting the executable in the
/// package layout. Two reasons, and both are the difference between probing this application and
/// probing something that looks like it: a process started directly has no package identity, so it
/// reads a different corpus location and cannot see its own resources; and activation is the only
/// call that hands back the process id, which is what every window here is found by.
/// </para>
/// <para>
/// <see cref="Dispose"/> ends a process tree, so the one thing this has to be sure of is that the
/// process is its own. Measured on 2026-08-26: this application is not single instance, and
/// activating it while a copy is already open starts a second process rather than handing back
/// the first — so the check below never fires today. It is here because it is the precondition
/// the closing depends on, and an application that later decides two recorders is one too many
/// would otherwise make this tool kill the person's, silently, in whichever build did it.
/// </para>
/// </remarks>
internal sealed class LaunchedApp : IDisposable
{
    private const uint SmtoAbortIfHung = 0x0002;

    internal static readonly TimeSpan ToOpenAWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ToShutDown = TimeSpan.FromSeconds(10);

    private readonly Process _process;

    private LaunchedApp(Process process, string appUserModelId)
    {
        _process = process;
        AppUserModelId = appUserModelId;
        Windows = new AppWindows(process.Id);
    }

    internal string AppUserModelId { get; }

    internal AppWindows Windows { get; }

    internal int ProcessId => _process.Id;

    /// <summary>
    /// The file Windows actually started, which is not necessarily the one that was last built:
    /// what runs is whatever layout the package registration points at.
    /// </summary>
    internal string RunningFrom =>
        _process.MainModule?.FileName
        ?? throw new ProbeFailed($"Process {_process.Id} will not say what it is running.");

    internal static LaunchedApp Start(string manifestPath)
    {
        var aumid = Aumid.OfTheApplicationIn(manifestPath);

        var manager = (Native.IApplicationActivationManager)Activator.CreateInstance(
            typeof(Native.ApplicationActivationManager))!;

        var asked = DateTime.Now;
        var outcome = manager.ActivateApplication(aumid, arguments: null, options: 0, out var id);
        if (outcome != 0)
        {
            throw new ProbeFailed(
                $"Windows would not start {aumid} (0x{outcome:X8}). Either the package is not "
                + "registered from the build output — see docs/ui-probe.md — or the manifest this "
                + "id was derived from is not the manifest the registered package was built with.");
        }

        var process = Of(id, aumid);
        var since = Started(process);
        if (since < asked)
        {
            process.Dispose();
            throw new ProbeFailed(
                $"Activation handed back process {id}, which has been running since "
                + $"{since:yyyy-MM-dd HH:mm:ss} — before this probe asked for anything. It only "
                + "drives, and only closes, an application it started itself. Close that one and "
                + "run again.");
        }

        return new LaunchedApp(process, aumid);
    }

    /// <summary>Blocks until there is something on the screen to read.</summary>
    internal void OpenAWindow() => Windows.WaitForAWindow(ToOpenAWindow);

    /// <summary>
    /// Asks every window to close, the way pressing the cross does, and only then insists. The
    /// polite half is what lets the application finish whatever it was writing; the insistent half
    /// is what makes sure the next run starts an application rather than finding this one.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_process.HasExited)
            {
                return;
            }

            // Every handle read before the first message is sent. Closing one window of this
            // application closes the rest, so reading them one at a time reads dead elements —
            // which used to throw out of here and leave the process running, the one thing this
            // method exists to prevent.
            var handles = Windows.All().Select(AppWindows.Handle).Where(one => one != IntPtr.Zero);
            foreach (var handle in handles)
            {
                Native.SendMessageTimeout(
                    handle,
                    Native.WM_CLOSE,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    SmtoAbortIfHung,
                    (uint)ToShutDown.TotalMilliseconds,
                    out _);
            }

            if (!_process.WaitForExit(ToShutDown))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(ToShutDown);
            }
        }
        catch (Exception ending) when (ending is not (OutOfMemoryException or StackOverflowException))
        {
            // Nothing thrown on the way out may stop the insisting, and nothing thrown here may
            // replace the failure that brought us here — that message is the one worth reading.
            Insist();
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void Insist()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception beyondUs) when (beyondUs is InvalidOperationException or COMException or Win32Exception)
        {
            Console.Error.WriteLine($"The application (process {_process.Id}) would not close.");
        }
    }

    private static Process Of(uint id, string aumid)
    {
        try
        {
            return Process.GetProcessById((int)id);
        }
        catch (ArgumentException)
        {
            throw new ProbeFailed(
                $"{aumid} was activated as process {id} and had gone before it could be read. "
                + "The application started and stopped, which is a crash on launch.");
        }
    }

    private static DateTime Started(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception unreadable) when (unreadable is InvalidOperationException or Win32Exception)
        {
            // Unreadable is not "started before we asked": refusing here would refuse every run.
            return DateTime.MaxValue;
        }
    }
}
