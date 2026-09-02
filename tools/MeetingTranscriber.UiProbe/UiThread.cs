using System.Windows.Threading;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The one thread every window is touched on, for as long as the process lives.
/// </summary>
/// <remarks>
/// <para>
/// The command line never needed one of these on purpose: it runs a script on the thread it
/// started on and exits, so there is one thread by accident. A server has no such thread — a call
/// arrives on whichever thread pool thread was free, the next one arrives on a different one, and
/// both of them are in the multi-threaded apartment. UI Automation is COM, and an
/// <c>AutomationElement</c> found on one apartment and used from another is marshalled behind your
/// back or is not valid at all. So the thread is made once, explicitly, and both hosts are given
/// it — the command line included, because a finding that is reproduced under a different
/// apartment model than the one it was found in is not the same experiment.
/// </para>
/// <para>
/// Two things it buys, and they are the two it is claimed for. STA is what makes the elements this
/// tool holds between calls — the window a <c>wait</c> named, an element a search found — belong
/// to somewhere that still exists on the next one. And running every call here serialises the
/// session: each one runs to completion before the next starts, so two verbs cannot be half way
/// through the same window at once and nothing in <see cref="McpHost"/> needs a lock.
/// </para>
/// <para>
/// The pump is <see cref="Dispatcher"/>'s and is what an STA owes COM, not something measured
/// here. It costs nothing — <c>UseWPF</c> was already on for the image encoder — so it is the
/// cheapest way to be a well-formed apartment rather than a bet that nothing ever calls in. What
/// it is not is load-bearing: this tool subscribes to no automation event, so the only traffic is
/// outgoing, and COM pumps an STA for the duration of an outgoing call by itself. Which is also
/// why <see cref="Patience"/> sleeping between polls rather than pumping is not the failure this
/// paragraph is about — nothing is queued behind those sleeps.
/// </para>
/// <para>
/// That was argued about a hundred milliseconds at a time, and a script's <c>sleep</c> may ask for
/// twenty minutes. It is slept in slices for another reason — a crashed application is worth
/// hearing about while it is still news — and the slices bound this as well, so the longest this
/// thread goes without returning is one of them rather than the whole hold. What is not claimed is
/// that anything is pumped between them: a broadcast arriving from another process waits on a
/// slice exactly as it already waited on a poll, and no run has met one.
/// </para>
/// </remarks>
internal sealed class UiThread : IDisposable
{
    /// <summary>
    /// What a wedged windows thread is given before the process stops waiting for it. Long enough
    /// for the polite close and the insisting inside it; short of forever, which is what an
    /// unbounded wait would be worth here.
    /// </summary>
    internal static readonly TimeSpan ToStop = TimeSpan.FromSeconds(45);

    private readonly Dispatcher _dispatcher;

    private readonly Thread _thread;

    private UiThread(Dispatcher dispatcher, Thread thread)
    {
        _dispatcher = dispatcher;
        _thread = thread;
    }

    internal static UiThread Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>();

        // A background thread, and that is the opposite of the first draft. A foreground thread
        // was chosen so that closing the application could not be skipped on the way out — but a
        // foreground thread that wedges also stops the process exiting, and a process that will
        // not exit is a process still holding the job handle, so the leash never fires either.
        // The application would be left open by the very thing meant to guarantee it was not.
        // Waiting for the close, with a bound, is what makes it deliberate; being foreground only
        // made it unbounded.
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            Name = "ui-probe windows",
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return new UiThread(ready.Task.GetAwaiter().GetResult(), thread);
    }

    /// <summary>
    /// Runs <paramref name="work"/> there and answers with what it said. Awaited rather than
    /// blocked on, because a <c>wait</c> can sit on a screen for fifteen seconds and that is a
    /// thread pool thread held for fifteen seconds if the caller blocks.
    /// </summary>
    internal Task<T> RunAsync<T>(Func<T> work) => _dispatcher.InvokeAsync(work).Task;

    /// <summary>
    /// For a caller with nothing else to do until it is done — and never for longer than
    /// <see cref="ToStop"/>, because the caller is usually the one shutting the process down.
    /// False when the budget ran out, which is a fact the caller has to say out loud.
    /// </summary>
    internal bool RunWithin(TimeSpan budget, Action work) =>
        _dispatcher.InvokeAsync(work).Task.Wait(budget);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(ToStop);
    }
}
