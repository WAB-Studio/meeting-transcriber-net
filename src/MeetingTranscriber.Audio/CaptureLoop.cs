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
/// Given up on is a deadline and never a promise: a driver that answers a moment after it is one
/// whose loop carries on into the rest of its body. So the word never means the thread has stopped
/// being able to reach what it holds — it means the opposite, for as long as the process runs.
/// </para>
/// <para>
/// Bounded here rather than at each of the places that hold something the loop touches. There are
/// three of them and they let go in sequence, so a rule copied into each is two chances to write
/// the third one differently — and the one written differently is the one that closes a handle
/// under a live thread.
/// </para>
/// <para>
/// Two of the three moments a device can refuse to answer are here, because both are this thread:
/// it will not start, so the loop never says it is <see cref="Underway"/> and <see cref="Draining"/>
/// gives up on it; and it will not stop, so <see cref="Dispose"/> comes back with the loop still in
/// there. The third is letting go of the handles afterwards, which is not a loop and is
/// <see cref="DeviceRelease"/> — it shares this deadline and nothing else.
/// </para>
/// </remarks>
public sealed class CaptureLoop : IDisposable
{
    /// <summary>
    /// How long anything waits for a device before giving up on it — this loop and
    /// <see cref="DeviceRelease"/> alike. One number for every wait on the way into and out of a
    /// recording, so what "did not answer" means is the same sentence everywhere.
    /// </summary>
    /// <remarks>
    /// Per device and not per recording, and that is worth saying out loud: a session stopping two
    /// sources and the silence it played waits this three times at worst, once for whichever of
    /// draining or releasing each of them wedged on. Nobody waits it twice for one device.
    /// </remarks>
    public static readonly TimeSpan StopsWithin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What <see cref="Underway"/> is announced on and <see cref="Draining"/> waits on. A monitor
    /// rather than an event, and deliberately not a <see cref="System.Threading.Lock"/>, which has
    /// no wait and no pulse: a thread given up on may still reach <see cref="Underway"/> at any
    /// point afterwards, so the one thing this must not be is something with a handle to close.
    /// There is nothing here to dispose, which is what makes that safe rather than remembered.
    /// </summary>
    private readonly object gate = new();

    private readonly Thread thread;
    private volatile bool running = true;
    private volatile bool underway;

    private CaptureLoop(string name, Action<CaptureLoop> body) =>
        thread = new Thread(() => body(this)) { IsBackground = true, Name = name };

    /// <summary>
    /// Whether the loop has not been asked to stop. What its own body reads to decide whether to
    /// take another pass at the device.
    /// </summary>
    public bool Running => running;

    /// <summary>
    /// Whether this was given up on: it did not get underway, or did not come back, inside
    /// <see cref="StopsWithin"/> — so its thread is still running and still using everything it
    /// holds.
    /// </summary>
    public bool Abandoned { get; private set; }

    /// <summary>
    /// Starts <paramref name="drain"/> on a thread of its own and comes back once that loop says
    /// it is <see cref="Underway"/>, or gives up on it at <see cref="StopsWithin"/> and comes back
    /// <see cref="Abandoned"/>. The loop is handed itself so that its body can say so and can read
    /// <see cref="Running"/> — passed in rather than read off a field the caller assigns, because
    /// the thread is running before that assignment happens.
    /// </summary>
    /// <remarks>
    /// Waiting for the device to be running is the caller's whole reason to be here: a stream
    /// handed back before its device started is one a session would count as open, and a session
    /// opens both of its sources or neither. The wait is bounded for the same reason every other
    /// wait here is — starting a device is a call into a driver, and a driver that never returns
    /// from it would otherwise hang the application at the moment somebody pressed record, which
    /// is worse than hanging it on the way out.
    /// </remarks>
    /// <param name="name">What the thread is called in a debugger and a crash dump.</param>
    /// <param name="drain">
    /// The loop body, and it owes this one thing before anything else it does: it says
    /// <see cref="Underway"/> once the device is running, and says it on the way out of a device
    /// that refused too, since a refusal is an answer and there is nothing left to wait for. A body
    /// that reaches neither is a device that never answered — which is what abandoning it means, so
    /// a body that simply returns without ever saying so is read as wedged and is a defect in that
    /// body rather than a case handled here. Then it runs until it returns, and asking is all
    /// anyone can do. It owns its own failures: nothing is caught here, so a body that throws takes
    /// the process with it the way any unhandled exception on a thread does. That is deliberate — a
    /// loop is what a recording is being written by, and a swallow here would be this type
    /// deciding, for a case nobody has thought about, that the meeting goes on without the audio.
    /// </param>
    public static CaptureLoop Draining(string name, Action<CaptureLoop> drain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(drain);

        var loop = new CaptureLoop(name, drain);
        loop.thread.Start();
        loop.WaitToGetUnderway();
        return loop;
    }

    /// <summary>Asks the loop to stop, and comes back without waiting for it to.</summary>
    public void AskToStop() => running = false;

    /// <summary>
    /// Said by a loop body once its device is running, which is what <see cref="Draining"/> comes
    /// back on. Saying it twice is saying it once, and saying it after having been given up on is
    /// allowed and does nothing — the thread that would say it is exactly the thread nothing here
    /// can reach, so this may never be the thing that throws on it.
    /// </summary>
    public void Underway()
    {
        // Said before the gate is taken and not under it, which is what makes the deadline mean the
        // device rather than the lock. The one thread waiting reads this at its deadline while
        // holding the gate, so a body that announced a moment before that and is still queueing for
        // the lock would otherwise read as one that never announced at all — a device that did
        // start, given up on for having said so a moment too early to be heard.
        underway = true;

        lock (gate)
        {
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>
    /// Asks the loop to stop and waits <see cref="StopsWithin"/> for it. Coming back is the whole
    /// guarantee: either the thread is over, or <see cref="Abandoned"/> says it is still in there.
    /// </summary>
    public void Dispose()
    {
        running = false;

        // Nothing given up on is waited for again: every holder of something this touches calls
        // here on the way out, and each of them spending its own five seconds would turn one wedged
        // device into a shutdown measured in minutes. That covers a loop given up on at its start
        // as well as one given up on at its end — the thread is in the same place either way, and
        // the deadline for it has already been spent once. Asked again rather than skipped, though
        // — a thread that came back a moment after the deadline is one whose handles are free after
        // all, and whoever calls this next is the only thing that can find that out.
        Abandoned = !thread.Join(Abandoned ? TimeSpan.Zero : StopsWithin);
    }

    /// <summary>
    /// Waits for the body to say it is <see cref="Underway"/>, and gives up on it at the deadline.
    /// </summary>
    /// <remarks>
    /// One wait and no loop around it. What is being waited for only ever goes from unsaid to
    /// said, there is exactly one thread waiting on it, and the pulse happens under the same lock
    /// — so a wake with nothing said is the deadline having passed, which is the answer this
    /// wants rather than a case to wait through again. Read once more after the wait for the same
    /// reason it is written before the gate: what decides is what the body said, not whether it had
    /// reached the lock by the time this stopped waiting.
    /// </remarks>
    private void WaitToGetUnderway()
    {
        lock (gate)
        {
            if (!underway)
            {
                Monitor.Wait(gate, StopsWithin);
            }

            Abandoned = !underway;
        }
    }
}
