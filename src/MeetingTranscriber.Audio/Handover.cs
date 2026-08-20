namespace MeetingTranscriber.Audio;

/// <summary>
/// One move of a channel from one device to another, and the single decision about the replacement
/// ending inside it: whichever of the two threads gets there second is the one that reports it.
/// </summary>
/// <remarks>
/// <para>
/// A replacement is started before the channel is handed over to it — that is what proves the
/// device is really running before anything is given up. So there is a window where the stream
/// about to become the recording is already draining and is not yet the one blocks are read from,
/// and a stream that dies inside it has nobody listening: the thread that hands over is still in
/// the middle of doing so, and the guard that keeps two devices out of one spool drops the end
/// because the field it reads still names the old stream.
/// </para>
/// <para>
/// What that costs is the failure this codebase is built against. The end is lost, the handover
/// resets the source to not-ended, and the channel is left on a stream nothing will ever hear from
/// again — silent, with every reading green, for the rest of the meeting. On the microphone it is
/// worse than silence: the thread that follows a device Windows took away looks for a source that
/// <em>ended</em>, so a channel whose replacement died in the window is one that stops being
/// followed, and the bound on following a device that keeps dying is never even asked.
/// </para>
/// <para>
/// The two threads are ordered against each other here rather than by the order of two writes,
/// because there is no order of two writes that works: the capture thread reads which stream is
/// the source's and the handing-over thread writes it, and either one may be first. So both say
/// what they did, one lock decides which of them was second, and that one reports.
/// </para>
/// <para>
/// <b>One of these is one move, and it stops meaning anything when the next move starts.</b> A
/// stream goes on ending after the channel has left it — the move that replaced it stops it — and
/// that end is a device closing on the way out of something that worked, not a recording that
/// ended. It reaches this object all the same, because what holds it is the stream's own callback
/// and that callback outlives the move that made it. So a move retires the one before it, and a
/// retired one reports nothing. Nothing legitimate is lost by that: while a stream is still the
/// one blocks are read from, its end is reported without ever coming here.
/// </para>
/// <para>
/// Public, and for the reason <see cref="CaptureLoop"/> is: what it decides cannot be reached on a
/// machine with no devices, and an ordering that only a real replacement dying inside a window of
/// a few milliseconds would exercise is one nothing would ever prove. What it takes is no device
/// and no stream — two threads saying what they did — so every way round is driven directly.
/// </para>
/// </remarks>
public sealed class Handover
{
    /// <summary>
    /// What makes the arrivals a sequence. Held for the length of a few field writes and nothing
    /// else: reporting happens outside it, so a capture thread is never held behind whatever the
    /// report does.
    /// </summary>
    private readonly Lock gate = new();

    private readonly Action<Exception?> report;

    private Exception? failure;
    private bool ended;
    private bool tookOver;
    private bool retired;

    /// <summary>
    /// Takes what says the source has ended. Taken once, here, rather than at each of the two
    /// arrivals: reported at most once is then a property of this object and not of two call sites
    /// happening to pass the same thing.
    /// </summary>
    /// <param name="report">What says the source has ended.</param>
    public Handover(Action<Exception?> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        this.report = report;
    }

    /// <summary>
    /// Said by the stream taking over when it ends before the channel has read a block from it.
    /// Reports when the channel already took over, and otherwise leaves it for <see cref="TookOver"/>.
    /// </summary>
    /// <param name="stopped">Why the stream ended, or nothing when it was asked to.</param>
    public void Ended(Exception? stopped)
    {
        bool now;
        lock (gate)
        {
            // A loop ends once, so a second end is a stream that answered late and not a second
            // thing to report — and a retired move's stream ending is the device the channel left
            // closing behind it, which is not this source ending at all.
            if (ended || retired)
            {
                return;
            }

            ended = true;
            failure = stopped;
            now = tookOver;
        }

        if (now)
        {
            report(stopped);
        }
    }

    /// <summary>
    /// Said by the thread that hands the channel over, once the stream taking over really is the
    /// one blocks are read from. Reports the end it finds already waiting, if there is one.
    /// </summary>
    public void TookOver()
    {
        bool now;
        Exception? why;
        lock (gate)
        {
            if (tookOver)
            {
                return;
            }

            tookOver = true;
            now = ended;
            why = failure;
        }

        if (now)
        {
            report(why);
        }
    }

    /// <summary>
    /// Said by the move that replaces this one. Everything this stream does from here belongs to a
    /// channel that has already left it, so nothing is reported afterwards.
    /// </summary>
    public void Retire()
    {
        lock (gate)
        {
            retired = true;
        }
    }
}
