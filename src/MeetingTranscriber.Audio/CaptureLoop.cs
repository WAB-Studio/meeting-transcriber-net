namespace MeetingTranscriber.Audio;

/// <summary>
/// The thread draining one source, and the single decision about what happens when it does not
/// come back.
/// </summary>
/// <remarks>
/// <para>
/// A draining loop ends two ways that were designed for: it was asked to, or the device under it
/// threw. There is a third, and it is the one this type exists for — it does not end at all. A
/// driver wedged inside WASAPI, a disk that stops answering, a USB microphone pulled out halfway
/// through the meeting: the thread is somewhere in Windows and nothing brings it back. .NET has no
/// way to kill it, so the only question left open is what everything else does about it, and an
/// unbounded wait answers it by hanging the application on the one thread that will never arrive.
/// </para>
/// <para>
/// So: <b>waiting for a loop is bounded, and one that did not come back is given up on instead.</b>
/// Abandoned means exactly one thing, and every holder of anything the loop touches reads it the
/// same way — the loop is still running, so nothing it uses may be closed, freed or deleted. That
/// leaves a thread and the handles under it held until the process ends, which is the price of not
/// closing a file another thread is in the middle of writing to. The alternative is not a tidier
/// shutdown; it is a torn block, or a COM object released under a thread still calling into it.
/// </para>
/// <para>
/// Bounded here rather than at each of the places that hold something the loop touches. There are
/// three of them and they let go in sequence, so a rule copied into each is two chances to write
/// the third one differently — and the one written differently is the one that closes a handle
/// under a live thread.
/// </para>
/// </remarks>
public sealed class CaptureLoop : IDisposable
{
    /// <summary>
    /// How long anything waits for a loop before giving up on it. One number for every wait on the
    /// way out of a recording, so what "did not stop" means is the same sentence everywhere.
    /// </summary>
    public static readonly TimeSpan StopsWithin = TimeSpan.FromSeconds(5);

    private readonly Thread thread;
    private volatile bool running = true;

    private CaptureLoop(string name, Action<CaptureLoop> drain) =>
        thread = new Thread(() => drain(this)) { IsBackground = true, Name = name };

    /// <summary>
    /// Whether the loop has not been asked to stop. What its own body reads to decide whether to
    /// take another pass at the device.
    /// </summary>
    public bool Running => running;

    /// <summary>
    /// Whether this loop was given up on: it did not come back inside <see cref="StopsWithin"/>,
    /// so it is still running and still using everything it holds.
    /// </summary>
    public bool Abandoned { get; private set; }

    /// <summary>
    /// Starts <paramref name="drain"/> on a thread of its own and comes back. The loop is handed
    /// itself so that its body can read <see cref="Running"/> — passed in rather than read off a
    /// field the caller assigns, because the thread is running before that assignment happens.
    /// </summary>
    /// <param name="name">What the thread is called in a debugger and a crash dump.</param>
    /// <param name="drain">
    /// The loop body. It runs until it returns, and asking is all anyone can do. It owns its own
    /// failures: nothing is caught here, so a body that throws takes the process with it the way any
    /// unhandled exception on a thread does. That is deliberate — a loop is what a recording is
    /// being written by, and a swallow here would be this type deciding, for a case nobody has
    /// thought about, that the meeting goes on without the audio.
    /// </param>
    public static CaptureLoop Draining(string name, Action<CaptureLoop> drain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(drain);

        var loop = new CaptureLoop(name, drain);
        loop.thread.Start();
        return loop;
    }

    /// <summary>Asks the loop to stop, and comes back without waiting for it to.</summary>
    public void AskToStop() => running = false;

    /// <summary>
    /// Asks the loop to stop and waits <see cref="StopsWithin"/> for it. Coming back is the whole
    /// guarantee: either the loop is over, or <see cref="Abandoned"/> says it is still in there.
    /// </summary>
    public void Dispose()
    {
        running = false;

        // Nothing given up on is waited for again: every holder of something this touches calls
        // here on the way out, and each of them spending its own five seconds would turn one wedged
        // device into a shutdown measured in minutes. Asked again rather than skipped, though —
        // a loop that came back a moment after the deadline is one whose handles are free after
        // all, and whoever calls this next is the only thing that can find that out.
        Abandoned = !thread.Join(Abandoned ? TimeSpan.Zero : StopsWithin);
    }
}
