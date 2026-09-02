using System.Diagnostics;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Ties the application's life to this process's, so that it cannot outlive the thing driving it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LaunchedApp.Dispose"/> is the tidy way out and it covers every ending this tool can
/// see: a script that finished, a script that failed, a verb that threw. It cannot cover the
/// ending where this process is not asked — a kill, a client that goes away, a session cut off
/// mid-turn — and that ending stopped being hypothetical the moment an application started staying
/// open across turns. What it leaves behind is a window nobody is driving, and the next run then
/// refuses to start because activation hands back a process older than it is.
/// </para>
/// <para>
/// A Windows job object with <c>KILL_ON_JOB_CLOSE</c> is the only thing that answers that, because
/// the kernel is what does the killing: when the last handle to the job closes — and process exit
/// closes every handle, however the process ended — everything in it is terminated. No shutdown
/// hook is involved, so there is no ending it can be skipped for.
/// </para>
/// <para>
/// It is a backstop and not the way out. The polite close still happens first, because it is what
/// lets the application finish whatever it was writing; this is what happens when nobody got to be
/// polite. A script that asks for a <c>kill</c> is not that case and does not come through here —
/// it is deliberately impolite and says so, where this is the ending nobody chose. When Windows
/// refuses to make one the tool goes on, because an application that might be
/// left behind is worth less than no probe at all — but the refusal is handed back rather than
/// printed, because the host that most needs to hear it is the one whose reader never sees a
/// console.
/// </para>
/// </remarks>
internal sealed class Leash : IDisposable
{
    private IntPtr _job;

    private Leash(IntPtr job) => _job = job;

    /// <summary>
    /// The application dies with us, or null and <paramref name="refused"/> set to a sentence
    /// saying it will not. Never thrown out of: this is insurance on the way in, and the thing it
    /// insures is worth having without it.
    /// </summary>
    internal static Leash? OnEverythingThatEnds(Process application, out string? refused)
    {
        refused = null;

        var job = Native.CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return Refused("Windows would not make a job object", out refused);
        }

        var limits = default(Native.JobExtendedLimits);
        limits.BasicLimitInformation.LimitFlags = Native.JobLimitKillOnJobClose;

        if (!Native.SetInformationJobObject(
                job,
                Native.JobObjectExtendedLimitInformation,
                ref limits,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.JobExtendedLimits>())
            || !Native.AssignProcessToJobObject(job, application.Handle))
        {
            Native.CloseHandle(job);

            return Refused($"Windows would not put process {application.Id} in a job object", out refused);
        }

        return new Leash(job);
    }

    /// <summary>
    /// Closing the handle is what kills whatever is still in the job, so this runs after the
    /// polite close and not instead of it.
    /// </summary>
    public void Dispose()
    {
        if (_job != IntPtr.Zero)
        {
            Native.CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }

    private static Leash? Refused(string what, out string? refused)
    {
        refused =
            $"{what}, so if this process is killed rather than asked to stop, the application it "
            + "started will be left open. Close it by hand if that happens.";

        return null;
    }
}
