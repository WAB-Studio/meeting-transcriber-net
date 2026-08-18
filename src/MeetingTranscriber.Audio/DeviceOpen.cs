using System.Runtime.ExceptionServices;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Getting hold of one device, on a thread that can be given up on.
/// </summary>
/// <remarks>
/// <para>
/// Opening an endpoint, asking it for its client and telling that client what to record are all
/// synchronous calls into the driver, and a driver can wedge on being opened exactly as it can wedge
/// on being drained or let go of. Done on the caller's thread, that is an application which freezes
/// at the moment somebody pressed record — the worst of the three, because nothing has been
/// recorded yet and the person is still sitting in front of it.
/// </para>
/// <para>
/// So the deadline is <see cref="CaptureLoop.StopsWithin"/> and not a second number, and one device
/// costs one of them: everything it takes to get from a name to a stream runs inside a single ask,
/// so a driver that wedges anywhere along the way is given up on once rather than once per call.
/// </para>
/// <para>
/// A type of its own rather than another way into <see cref="CaptureLoop"/> or
/// <see cref="DeviceRelease"/>, for the reason those two are already separate: they agree on the
/// deadline and on nothing else. A draining loop reads whether it was asked to stop and announces
/// when its device got going; a release has nothing to hand back and swallows what it catches. This
/// one hands back what the device gave — or throws what the device said, as the device said it,
/// because the sentence a person acts on is Windows' own.
/// </para>
/// </remarks>
public static class DeviceOpen
{
    /// <summary>
    /// Asks <paramref name="open"/> for a device on a thread of its own and comes back with what it
    /// answered, or gives up on it at <see cref="CaptureLoop.StopsWithin"/> and throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a body given up on is still holding stays held, which is what abandoning anything here
    /// means: the thread is inside the driver, so a handle it obtained a moment later is one nothing
    /// out here ever learns about and nothing may close. The device is this application's until it
    /// is restarted, and that cost is named in the sentence thrown rather than hidden.
    /// </para>
    /// <para>
    /// A body that fails part-way lets go of what it had before it throws, and it does that on this
    /// same thread — which is the whole reason the ask covers the cleanup and not only the opening.
    /// A handle released by anybody else would be released from under the one thread still inside
    /// it, and a release bounded separately would be a second deadline over one device.
    /// </para>
    /// </remarks>
    /// <param name="device">
    /// What did not answer, said the way a person would hear it. Also what the thread is called in a
    /// debugger and a crash dump.
    /// </param>
    /// <param name="open">
    /// Everything it takes to get hold of the device, run once. It owns its own failures: what it
    /// throws is thrown again here, as it was thrown, so what Windows said about this device is
    /// still Windows' answer by the time somebody reads it.
    /// </param>
    public static T Answering<T>(string device, Func<T> open)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentNullException.ThrowIfNull(open);

        // What the answer is announced on and what this waits on. A monitor rather than an event,
        // for the reason CaptureLoop's gate is one: a thread given up on may answer at any point
        // afterwards, so the one thing this must not be is something with a handle to close.
        var gate = new object();
        var answered = false;
        T? opened = default;
        Exception? refused = null;

        var thread = new Thread(() =>
        {
            try
            {
                opened = open();
            }
            catch (Exception no)
            {
                refused = no;
            }

            // Written before the gate is taken and not under it, which is what makes the deadline
            // mean the device rather than the lock: the one thread waiting reads this at its
            // deadline while holding the gate, so a device that answered a moment earlier and is
            // still queueing for the lock would otherwise read as one that never answered at all.
            Volatile.Write(ref answered, true);

            lock (gate)
            {
                Monitor.PulseAll(gate);
            }
        })
        {
            IsBackground = true,
            Name = device,
        };

        thread.Start();

        lock (gate)
        {
            // One wait and no loop around it, for the reason CaptureLoop gives: what is waited for
            // only ever goes from unsaid to said, there is exactly one thread waiting, and the pulse
            // happens under this lock — so a wake with nothing said is the deadline having passed.
            // Read once more afterwards for the same reason it is written before the gate.
            if (!Volatile.Read(ref answered))
            {
                Monitor.Wait(gate, CaptureLoop.StopsWithin);
            }

            if (!Volatile.Read(ref answered))
            {
                throw AudioDeviceWedgedException.NoAnswerFrom(device);
            }
        }

        if (refused is not null)
        {
            ExceptionDispatchInfo.Capture(refused).Throw();
        }

        // Never the default: nothing sets the gate without having set one of these two first, and
        // the read above is what makes both visible on this thread.
        return opened!;
    }
}
