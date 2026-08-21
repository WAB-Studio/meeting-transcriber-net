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
/// an ask refused because another was in flight would refuse a caller nothing had gone wrong for.
/// The cost of that choice is the one window this cannot close: a caller already inside its own
/// five seconds is not stopped by what another learns, so each of them pays the deadline once —
/// and so does a caller arriving in the moment between one giving up and writing that down. Once
/// at all, and never once per look, is the whole of what is claimed.
/// </para>
/// <para>
/// Two callers do overlap and it is the two questions, not one question twice: the screen asks
/// what the machine plays through on its dispatcher while the watcher lists the microphones on its
/// own thread, and both are live for the whole of a meeting. So the deadline paid once is paid
/// once per question, which is also the whole of what the scoping above changes — nothing in the
/// product puts two asks of one question in flight at the same moment.
/// </para>
/// <para>
/// What a body that never comes back at all costs is that this application cannot ask that one
/// thing until it is restarted, and that is deliberate: it is what a device given up on already
/// costs here, and an ask whose thread is permanently inside a driver is one a fresh ask would
/// wedge in too. A machine that comes back late is forgiven at the next look, since what came back
/// is dropped before anything is refused.
/// </para>
/// </remarks>
public static class DeviceEnquiry
{
    /// <summary>
    /// Every ask this machine was given up on and has not come back from, oldest first. Guarded by
    /// its own lock, since two threads ask and either may be the one that gives up.
    /// </summary>
    /// <remarks>
    /// One entry per thread given up on, and not one slot per question, because the list is this at
    /// its simplest: adding, dropping what came back and asking whether any of them is still out
    /// there are one line each over a list, where a slot per question is the same three lines plus a
    /// rule for what happens when a second ask of one question is given up on while the first still
    /// has a thread inside the audio service. Nothing in the product puts two asks of one question
    /// in flight at once, so that rule would decide nothing anybody could reach — which is an
    /// argument for the list rather than for the rule, and never a claim that the case is handled.
    /// </remarks>
    private static readonly List<Outstanding> GivenUpOn = [];

    /// <summary>
    /// Asks <paramref name="question"/> of this machine on a thread of its own and comes back with
    /// what it answered — or at <see cref="CaptureLoop.StopsWithin"/>, saying nothing answered, and
    /// at once rather than at the deadline when the same question is already out there.
    /// </summary>
    /// <param name="question">
    /// Which of the things this application asks about. It is the identity the memory is kept
    /// against, so what makes two asks the same question is being the same one of these —
    /// <see cref="DeviceQuestion"/> says why that is an object and not the words on it.
    /// </param>
    /// <param name="ask">
    /// The whole of what touches the audio stack, run once. Everything it opens it also lets go of,
    /// on its own thread, because a thread given up on is still inside all of it.
    /// </param>
    public static T Answering<T>(DeviceQuestion question, Func<T> ask)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(ask);

        if (StillOutThere(question))
        {
            throw AudioDeviceWedgedException.NoAnswerAbout(question.Asked);
        }

        var mine = new Outstanding(question);

        try
        {
            return AudioAsk.Answering(
                question.Asked,
                () =>
                {
                    try
                    {
                        return ask();
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
                () => AudioDeviceWedgedException.NoAnswerAbout(question.Asked));
        }
        catch (AudioDeviceWedgedException)
        {
            // The deadline, or a body that threw this type itself — which nothing in this
            // application does today, and which is remembered rather than told apart because
            // remembering it costs nothing. A body that threw has come back, so its `finally` has
            // already set `Answered` before the throw reaches here: the entry goes in inert,
            // refuses nobody, and the next reader drops it on the way past. Telling the two apart
            // would be a second exception type carried for a case whose only effect is one list
            // entry that the next question deletes.
            Remember(mine);
            throw;
        }
    }

    /// <summary>
    /// Whether this machine is still being waited on for <paramref name="question"/> in particular.
    /// Everything that has come back is dropped on the way past, whichever question it was, since
    /// this is the only moment anything reads the memory at all.
    /// </summary>
    private static bool StillOutThere(DeviceQuestion question)
    {
        lock (GivenUpOn)
        {
            GivenUpOn.RemoveAll(outstanding => outstanding.Answered);
            return GivenUpOn.Exists(outstanding => outstanding.About == question);
        }
    }

    private static void Remember(Outstanding outstanding)
    {
        lock (GivenUpOn)
        {
            GivenUpOn.Add(outstanding);
        }
    }

    /// <summary>One ask given up on, and whether the machine ever came back from it.</summary>
    /// <remarks>
    /// The thread <see cref="AudioAsk"/> starts would answer the same thing and is not handed out
    /// for it: what has to be known here is whether the ask came back, and a mechanism that throws
    /// when it does not would have to give the thread back through the exception or an out
    /// parameter to say so.
    /// </remarks>
    private sealed class Outstanding(DeviceQuestion about)
    {
        /// <summary>
        /// Set by the thread that was asked, read by whoever asks next. Volatile rather than under
        /// the list's lock: the thread that sets it is inside the audio service and may never come
        /// back, so it is the one thing here that must take no lock.
        /// </summary>
        private volatile bool answered;

        internal DeviceQuestion About => about;

        internal bool Answered
        {
            get => answered;
            set => answered = value;
        }
    }
}
