using System.Runtime.ExceptionServices;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One question put to this machine's audio, on a thread that can be given up on.
/// </summary>
/// <remarks>
/// <para>
/// Everything this application asks the audio stack is synchronous COM, and a driver or the audio
/// service itself can stay inside any of those calls for as long as it likes. Asked on the caller's
/// thread that is a frozen application; asked here it is a deadline, after which the question is
/// abandoned rather than answered.
/// </para>
/// <para>
/// What a body given up on is still holding stays held, which is what abandoning anything here
/// means: the thread is inside the audio stack, so anything it obtained a moment later is something
/// nothing out here ever learns about and nothing may close.
/// </para>
/// <para>
/// The mechanism and no policy: what the deadline expiring means, and whether the question should
/// have been asked at all, are the two callers' — <see cref="DeviceOpen"/>, where a device that
/// never answered is held until this application restarts, and <see cref="DeviceEnquiry"/>, where
/// nothing is held and the answer is simply not coming. They agree on the thread, on the deadline
/// and on nothing else, and neither may spell this dance out again: it is short and every line of
/// it is load-bearing.
/// </para>
/// </remarks>
internal static class AudioAsk
{
    /// <summary>
    /// Runs <paramref name="ask"/> on a thread of its own and comes back with what it answered, or
    /// gives up on it at <see cref="CaptureLoop.StopsWithin"/> and throws what
    /// <paramref name="gaveUp"/> builds.
    /// </summary>
    /// <param name="named">
    /// What was asked, said the way a person would hear it. Also what the thread is called in a
    /// debugger and a crash dump.
    /// </param>
    /// <param name="ask">
    /// The question, run once. It owns its own failures: what it throws is thrown again here, as it
    /// was thrown, so what Windows said is still Windows' answer by the time somebody reads it.
    /// </param>
    /// <param name="gaveUp">What the caller says a question nothing answered has cost.</param>
    internal static T Answering<T>(string named, Func<T> ask, Func<AudioDeviceWedgedException> gaveUp)
    {
        // What the answer is announced on and what this waits on. A monitor rather than an event,
        // for the reason CaptureLoop's gate is one: a thread given up on may answer at any point
        // afterwards, so the one thing this must not be is something with a handle to close.
        var gate = new object();
        var answered = false;
        T? asked = default;
        Exception? refused = null;

        var thread = new Thread(() =>
        {
            try
            {
                asked = ask();
            }
            catch (Exception no)
            {
                refused = no;
            }

            // Written before the gate is taken and not under it, which is what makes the deadline
            // mean the machine rather than the lock: the one thread waiting reads this at its
            // deadline while holding the gate, so a question answered a moment earlier and still
            // queueing for the lock would otherwise read as one that was never answered at all.
            Volatile.Write(ref answered, true);

            lock (gate)
            {
                Monitor.PulseAll(gate);
            }
        })
        {
            IsBackground = true,
            Name = named,
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
                throw gaveUp();
            }
        }

        if (refused is not null)
        {
            ExceptionDispatchInfo.Capture(refused).Throw();
        }

        // Never the default: nothing sets the gate without having set one of these two first, and
        // the read above is what makes both visible on this thread.
        return asked!;
    }
}
