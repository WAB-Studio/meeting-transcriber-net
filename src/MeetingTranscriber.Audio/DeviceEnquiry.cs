namespace MeetingTranscriber.Audio;

/// <summary>
/// Asking this machine what it can record from, on a thread that can be given up on — and not
/// asking it again while it has still not come back.
/// </summary>
/// <remarks>
/// <para>
/// Enumerating endpoints is synchronous COM like everything else here, and more of it than it
/// looks: the enumerator is opened, the default endpoint is asked for, and every endpoint's name
/// comes out of its driver's property store. With the audio service stuck, that is an application
/// which freezes while somebody is choosing a microphone — before any recording exists, so none of
/// the deadlines on the way into one is ever reached.
/// </para>
/// <para>
/// The deadline is <see cref="CaptureLoop.StopsWithin"/>, which is the number every wait on this
/// machine's audio shares, and the thread is <see cref="AudioAsk"/>'s. What differs from
/// <see cref="DeviceOpen"/> is what the deadline expiring costs. Nothing is held: the question
/// opened no device and reserved nothing, so the sentence says the machine did not answer and not
/// that anything is now this application's until it restarts.
/// </para>
/// <para>
/// What is here and nowhere else is that a machine which has not come back is not asked again. The
/// two callers that matter ask on a timer — a screen redrawing its meters every second, and the
/// watcher looking for a device that went away every two — so a deadline alone would leave both of
/// them waiting five seconds out of every six and starting one abandoned thread per go, which is
/// the freeze this exists to end, spelled with pauses in it. So the question that was given up on
/// is remembered, and while it is still out there the next one is refused at once with the same
/// sentence. A machine that comes back — the audio service restarts, and the abandoned thread
/// finishes what it was asked — is asked again on the next go, which is why what is remembered is
/// the question rather than a flag saying this application has given up on Windows for good.
/// </para>
/// </remarks>
public static class DeviceEnquiry
{
    /// <summary>
    /// The last question this machine was given up on, or nothing if it has never been. Read and
    /// written by every thread that asks, and its one field is what says whether it is still out
    /// there.
    /// </summary>
    private static Question? outstanding;

    /// <summary>
    /// Asks <paramref name="question"/> of this machine on a thread of its own and comes back with
    /// what it answered — or at <see cref="CaptureLoop.StopsWithin"/>, saying nothing answered, and
    /// at once rather than at the deadline when an earlier question is still out there.
    /// </summary>
    /// <param name="asked">
    /// What is being asked about, said the way a person would hear it and read as the end of a
    /// sentence about Windows not answering. Also what the thread is called in a debugger.
    /// </param>
    /// <param name="question">
    /// The whole of what touches the audio stack, run once. Everything it opens it also lets go of,
    /// on its own thread, because a thread given up on is still inside all of it.
    /// </param>
    public static T Answering<T>(string asked, Func<T> question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asked);
        ArgumentNullException.ThrowIfNull(question);

        var earlier = Volatile.Read(ref outstanding);
        if (earlier is not null && !earlier.Answered)
        {
            throw AudioDeviceWedgedException.NoAnswerAbout(earlier.Asked);
        }

        var mine = new Question(asked);

        try
        {
            return AudioAsk.Answering(
                asked,
                () =>
                {
                    try
                    {
                        return question();
                    }
                    finally
                    {
                        // Set by the abandoned thread as well as by one that answered in time, and
                        // that is the whole of how this machine stops being one nothing asks: the
                        // question is out there until the body it started comes back, however long
                        // after the deadline that is.
                        mine.Answered = true;
                    }
                },
                () => AudioDeviceWedgedException.NoAnswerAbout(asked));
        }
        catch (AudioDeviceWedgedException)
        {
            // Written after the deadline rather than before the ask, so a question that was
            // answered never leaves one behind — and written even though the body may have
            // finished a moment ago, since what the next caller reads is whether this one came
            // back and not whether it was given up on.
            Volatile.Write(ref outstanding, mine);
            throw;
        }
    }

    /// <summary>One question and whether the machine ever came back from it.</summary>
    private sealed class Question(string asked)
    {
        /// <summary>Set by the thread that was asked, read by whoever asks next.</summary>
        private volatile bool answered;

        internal string Asked => asked;

        internal bool Answered
        {
            get => answered;
            set => answered = value;
        }
    }
}
