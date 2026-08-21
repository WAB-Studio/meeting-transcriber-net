namespace MeetingTranscriber.Audio;

/// <summary>
/// Asking this machine what it can record from, on a thread that can be given up on — and not
/// asking it the same thing again while it has still not come back.
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
/// <see cref="DeviceOpen"/> is what the deadline expiring costs. No device is held: an abandoned
/// body is still inside the enumerator and the property stores it opened, and it lets go of those
/// if it ever comes back, but nothing was reserved and nothing else is denied — so the sentence
/// says the machine did not answer, and not that a device is now this application's until it
/// restarts.
/// </para>
/// <para>
/// What is here and nowhere else is that a question the machine has not come back from is not put
/// to it again. Both callers look on a timer: a screen redrawing its meters asks what the machine
/// plays through once a second, and the watcher lists the microphones every two for as long as a
/// channel's device is gone. A deadline alone would leave either of them waiting five seconds out
/// of every six and starting one abandoned thread per go, which is the freeze this exists to end,
/// spelled with pauses in it. So a question given up on is remembered until its body comes back,
/// and while it is out there it is refused at once.
/// </para>
/// <para>
/// That question and no other, which is the whole of what the memory is for and the whole of what
/// it may cost. A caller that looks on a timer asks one thing, so refusing what is already out
/// there is what turns its every look into one deadline; a different question is a different
/// caller, with its own deadline to pay once. Refusing that one too would tie the two callers this
/// application has together, and the tie runs the wrong way — the screen's once-a-second look at
/// what the machine plays through says whether a room is hearing the other side twice, while the
/// watcher's list of the microphones is how a meeting follows a microphone somebody unplugged
/// while it was recording. One memory over both means the cosmetic one wedging stops the recovery
/// for as long as its body is out, which for a body that never comes back is the rest of the
/// meeting. A stuck audio service is a fair guess that the next question sticks too, and that
/// guess is worth a deadline paid once per question rather than a meeting that stops following its
/// microphone on the evidence of a meter.
/// </para>
/// <para>
/// Remembered on the deadline and never at the start, which is what keeps a healthy machine free:
/// two callers really do ask the same thing at once — the window listing the microphones as it
/// opens, the watcher listing them on its own thread — and a question refused because another was
/// in flight would be a device change missed on a machine with nothing wrong with it. The cost of
/// that choice is the one window this cannot close: callers already inside their first five
/// seconds are not stopped by what the first of them learns, so each of them pays the deadline
/// once. Once at all, and never once per look, is the whole of what is claimed.
/// </para>
/// <para>
/// All of them and not the last one, for as many as are out there asking the same thing. A machine
/// that came back from one of those while another is still out is not a machine answering that
/// question: the second thread is inside the same call, and admitting a look on that evidence puts
/// the deadline back into every poll — which is the freeze again, in the shape that is hardest to
/// see. What it costs is that a body which never comes back at all leaves this application unable
/// to ask that one thing until it is restarted, and that is deliberate: it is what a device given
/// up on already costs here, and a question whose thread is permanently inside a driver is one a
/// fresh ask would wedge in too.
/// </para>
/// </remarks>
public static class DeviceEnquiry
{
    /// <summary>
    /// Every question this machine was given up on and has not come back from, oldest first.
    /// Guarded by its own lock, since two threads ask and either may be the one that gives up.
    /// </summary>
    /// <remarks>
    /// A list and not one slot per question, because what a caller has to read is whether anything
    /// asking its own question is still out there, which one slot cannot say: a second one given up
    /// on would either forget the first or be forgotten by it, and each of those admits a look while
    /// a thread is still inside the audio service. It holds one entry per thread that asks, since
    /// nothing asking the same thing is admitted while it has any, and it empties itself as those
    /// threads come back.
    /// </remarks>
    private static readonly List<Question> GivenUpOn = [];

    /// <summary>
    /// Asks <paramref name="question"/> of this machine on a thread of its own and comes back with
    /// what it answered — or at <see cref="CaptureLoop.StopsWithin"/>, saying nothing answered, and
    /// at once rather than at the deadline when the same question is already out there.
    /// </summary>
    /// <param name="asked">
    /// What is being asked about, said the way a person would hear it and read as the end of a
    /// sentence about Windows not answering. Also what the thread is called in a debugger, and what
    /// says two asks are the same question: the memory is scoped to it, so two callers spelling one
    /// question two ways would each pay their own deadline, and two spelling different questions the
    /// same way would refuse each other. Both are why the words live on <see cref="AudioDevices"/>,
    /// one per question, and are never composed at a call site.
    /// </param>
    /// <param name="question">
    /// The whole of what touches the audio stack, run once. Everything it opens it also lets go of,
    /// on its own thread, because a thread given up on is still inside all of it.
    /// </param>
    public static T Answering<T>(string asked, Func<T> question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asked);
        ArgumentNullException.ThrowIfNull(question);

        if (StillOutThere(asked))
        {
            throw AudioDeviceWedgedException.NoAnswerAbout(asked);
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
                        // that is the whole of how this question stops being one nothing asks: it
                        // is out there until the body it started comes back, however long after
                        // the deadline that is.
                        mine.Answered = true;
                    }
                },
                () => AudioDeviceWedgedException.NoAnswerAbout(asked));
        }
        catch (AudioDeviceWedgedException)
        {
            // The deadline, or a body that threw this type itself — which nothing in this
            // application does, and which costs nothing if something starts to: it comes with its
            // body already come back, so the next caller reads it as answered and drops it.
            Remember(mine);
            throw;
        }
    }

    /// <summary>
    /// Whether this machine is still being waited on for <paramref name="asked"/> in particular.
    /// Every question that has come back is dropped on the way past, whichever one it was, since
    /// this is the only moment anything reads the memory at all.
    /// </summary>
    private static bool StillOutThere(string asked)
    {
        lock (GivenUpOn)
        {
            GivenUpOn.RemoveAll(question => question.Answered);
            return GivenUpOn.Exists(question => question.Is(asked));
        }
    }

    private static void Remember(Question question)
    {
        lock (GivenUpOn)
        {
            GivenUpOn.Add(question);
        }
    }

    /// <summary>One question and whether the machine ever came back from it.</summary>
    /// <remarks>
    /// The thread <see cref="AudioAsk"/> starts would answer the same question and is not handed
    /// out for it: what has to be known here is whether the question came back, and a mechanism
    /// that throws when it does not would have to give the thread back through the exception or an
    /// out parameter to say so.
    /// </remarks>
    private sealed class Question(string asked)
    {
        /// <summary>
        /// Set by the thread that was asked, read by whoever asks next. Volatile rather than under
        /// the list's lock: the thread that sets it is inside the audio service and may never come
        /// back, so it is the one thing here that must take no lock.
        /// </summary>
        private volatile bool answered;

        internal bool Answered
        {
            get => answered;
            set => answered = value;
        }

        /// <summary>Whether this is the question <paramref name="other"/> names.</summary>
        /// <remarks>
        /// Ordinal, because these are not words being compared for a person: they are two call
        /// sites naming one question, and a culture that decided they matched would be a caller
        /// refused over somebody's locale.
        /// </remarks>
        internal bool Is(string other) => string.Equals(asked, other, StringComparison.Ordinal);
    }
}
