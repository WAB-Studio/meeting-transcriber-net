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
/// <para>
/// "For as long as this object is alive" is held to by <see cref="Dispose"/> for every ending this
/// tool can see, and by <see cref="Leash"/> for the one it cannot — being killed rather than asked.
/// </para>
/// </remarks>
internal sealed class LaunchedApp : IDisposable
{
    private const uint SmtoAbortIfHung = 0x0002;

    internal static readonly TimeSpan ToOpenAWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ToShutDown = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ToPublishItsModules = TimeSpan.FromSeconds(10);

    private readonly Process _process;

    private readonly Leash? _leash;

    private LaunchedApp(Process process, string appUserModelId, string runningFrom)
    {
        _process = process;
        AppUserModelId = appUserModelId;
        RunningFrom = runningFrom;
        Windows = new AppWindows(process.Id);

        // Before anything else is done with it. Every ending this tool can see is Dispose's, and
        // this is the one it cannot see: a kill leaves a window nobody is driving.
        _leash = Leash.OnEverythingThatEnds(process, out var refused);
        Unleashed = refused;
    }

    internal string AppUserModelId { get; }

    internal AppWindows Windows { get; }

    internal int ProcessId => _process.Id;

    /// <summary>
    /// Why this application will outlive a killed probe, or null when it will not. Carried rather
    /// than printed: the host says it where whoever started the application is looking.
    /// </summary>
    internal string? Unleashed { get; }

    /// <summary>
    /// The application is gone — somebody closed the window, or it crashed. Asked before anything
    /// is read off it, because every other reader here throws on a dead process rather than saying
    /// so, and "the probe broke" plus a stack trace is a poor way to report the most ordinary
    /// event there is.
    /// </summary>
    internal bool HasGone
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (Exception unreadable) when (unreadable is InvalidOperationException or Win32Exception)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// The file Windows actually started, which is not necessarily the one that was last built:
    /// what runs is whatever layout the package registration points at.
    /// </summary>
    /// <remarks>
    /// Read once, at the launch, and kept. It cannot change while the process lives, and the two
    /// moments it cannot be read are both moments something asks for it: a process too young to
    /// have published its module list, and a process that has exited. Asking again later turned a
    /// closed window into "will not say what it is running", which is a true sentence about the
    /// wrong thing.
    /// </remarks>
    internal string RunningFrom { get; }

    internal static LaunchedApp Start(string aumid)
    {
        var manager = (Native.IApplicationActivationManager)Activator.CreateInstance(
            typeof(Native.ApplicationActivationManager))!;

        var asked = DateTime.Now;
        var outcome = manager.ActivateApplication(aumid, arguments: null, options: 0, out var id);
        if (outcome != 0)
        {
            throw new ProbeFailed(
                $"Windows would not start {aumid} (0x{outcome:X8}), which is the identity in this "
                + "checkout's build output. Either nothing is registered under it, or what is "
                + "registered under it is a layout that has since been deleted. Register this "
                + "checkout's build output — see docs/ui-probe.md.");
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

        try
        {
            return new LaunchedApp(process, aumid, ImageOf(process));
        }
        catch
        {
            // The application is already running by now, and nothing owns it until the line above
            // returns. Anything thrown between those two facts would leave a window open with no
            // Dispose and no leash behind it.
            Abandon(process);
            throw;
        }
    }

    private static void Abandon(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception beyondUs) when (beyondUs is InvalidOperationException or COMException or Win32Exception)
        {
            Console.Error.WriteLine($"The application (process {process.Id}) would not close.");
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>Blocks until there is something on the screen to read.</summary>
    internal void OpenAWindow() => Windows.WaitForAWindow(ToOpenAWindow);

    /// <summary>
    /// Ends the application without asking it, which is the one ending it cannot prepare for.
    /// </summary>
    /// <remarks>
    /// The opposite of <see cref="Dispose"/>, and deliberately not a variation on it: what that
    /// method is careful to do first — send every window a close, wait for whatever was being
    /// written to finish — is exactly what must not happen here. A meeting whose save is halfway
    /// through and a recording nobody stopped are states the corpus only reaches after somebody's
    /// machine died, so probing what a later start finds has to arrive at them the same way. The
    /// process tree and not the process, because what Windows activated is the application and
    /// anything it started is part of the same death.
    /// <para>
    /// The death is waited for and checked, which is the difference between this verb and a
    /// wish. Everything a probe concludes from a kill is a sentence about a process that was
    /// gone — so a kill that has not landed inside the budget has to say so and stop, rather than
    /// answer as though it had: <see cref="Dispose"/> would then find a live application, take the
    /// polite path over it, and let the application finish the very save this was meant to
    /// interrupt. That is a run that reports a crash and recorded a clean shutdown.
    /// </para>
    /// <para>
    /// What is waited for is the process Windows handed back and not the tree: <c>WaitForExit</c>
    /// takes one process, and a descendant is signalled by the kill and never checked. The
    /// application starts nothing that writes the corpus today, so the sentence a probe draws from
    /// this holds — but it holds on that and not on the wait, and a child that wrote would need
    /// waiting for by name.
    /// </para>
    /// </remarks>
    internal void Kill()
    {
        if (HasGone)
        {
            throw new ProbeFailed(
                "The application is not running any more, so there is nothing left to kill.");
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }

        // The same three as Abandon and Insist, and for the same reason: the process can go in the
        // gap after HasGone, and a child of the tree can refuse. Neither is the probe breaking.
        catch (Exception beyondUs)
            when (beyondUs is InvalidOperationException or COMException or Win32Exception)
        {
            if (!HasGone)
            {
                throw new ProbeFailed(
                    $"The application (process {_process.Id}) would not be killed: {beyondUs.Message}");
            }
        }

        if (!_process.WaitForExit(ToShutDown))
        {
            throw new ProbeFailed(
                $"The application (process {_process.Id}) was killed and was still running "
                + $"{ToShutDown.TotalSeconds:0} seconds later, so nothing after this is about a "
                + "process that died.");
        }
    }

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
            var handles = Windows.All().Select(AppWindows.Handle).Where(one => one != IntPtr.Zero).ToList();
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

            // Nothing asked means nothing to wait for. An application refused before it opened a
            // window — a stale build, the wrong checkout — has no close to finish, and waiting the
            // full budget out before killing it anyway put ten seconds on every refusal.
            if (handles.Count == 0 || !_process.WaitForExit(ToShutDown))
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
            // After the insisting and never before it: closing the job handle is itself a kill,
            // and the polite close is what lets the application finish whatever it was writing.
            _leash?.Dispose();
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

    /// <summary>
    /// What the process is running, waited for. A process activated a moment ago has not always
    /// published its module list yet, and the read fails rather than blocking — which showed up as
    /// a launch that failed one time in some tens rather than as anything reproducible.
    /// </summary>
    private static string ImageOf(Process process)
    {
        var image = Patience.Until(ToPublishItsModules, () =>
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch (Exception notYet) when (notYet is InvalidOperationException or Win32Exception)
            {
                return null;
            }
        });

        return image ?? throw new ProbeFailed(
            $"Process {process.Id} would not say what it is running within "
            + $"{ToPublishItsModules.TotalSeconds:0} seconds. The application started and stopped, "
            + "which is a crash on launch.");
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
