using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One device being recorded onto one channel: the stream, the spool it lands in and how loud it
/// has been. Nothing here knows there is another one.
/// </summary>
/// <remarks>
/// What it writes is the device's own format, unconverted and unaligned with anything else. That
/// is the point of it — the two sources arriving as they really are is what the timeline then has
/// to reconcile, and a resample done here would hide the very thing being measured.
/// </remarks>
public sealed class CaptureSource : IDisposable
{
    private readonly WasapiStream stream;
    private readonly SpoolWriter spool;
    private readonly SourceMeter meter = new();
    private readonly PacketTally tally;
    private readonly ManualResetEventSlim ended = new(initialState: false);
    private Exception? failure;
    private bool running;

    private CaptureSource(
        AudioChannel channel,
        CaptureTarget listening,
        StreamFormat format,
        FileInfo file,
        WasapiStream stream,
        SpoolWriter spool)
    {
        Channel = channel;
        Listening = listening;
        Format = format;
        File = file;
        this.stream = stream;
        this.spool = spool;
        tally = new PacketTally(format);
    }

    /// <summary>Which of the two channels this device feeds.</summary>
    public AudioChannel Channel { get; }

    /// <summary>What Windows opened for it: an endpoint, or one program's audio.</summary>
    public CaptureTarget Listening { get; }

    /// <summary>The format that device handed over.</summary>
    public StreamFormat Format { get; }

    /// <summary>The spool its blocks are being written to.</summary>
    public FileInfo File { get; }

    /// <summary>When its stream opened.</summary>
    public UtcTimestamp StartedAt { get; private set; }

    /// <summary>
    /// What this source's packets add up to: the stretch of the meeting they cover, how much of it
    /// never arrived, and how the device's own clock behaved while they did.
    /// </summary>
    public PacketTally Packets => tally;

    /// <summary>How many bytes of samples have been spooled, not counting what frames them.</summary>
    public long Bytes => spool.Bytes;

    /// <summary>The loudest this source has been since it opened.</summary>
    public LevelReading Loudest => meter.Loudest;

    /// <summary>
    /// Whether the stream is over. True before <see cref="Stop"/> means it ended on its own, and
    /// <see cref="Stop"/> is what says why.
    /// </summary>
    public bool HasEnded => ended.IsSet;

    /// <summary>The loudest block since this was last asked, which is what a meter shows.</summary>
    public LevelReading Level() => meter.Read();

    /// <summary>
    /// Asks the stream to stop, and comes back without waiting for it to. Separate from
    /// <see cref="Finish"/> so that a session can ask both of its sources before waiting on
    /// either: waiting on one first leaves the other recording for however long that took, which
    /// is a difference between the two files invented by the order they were stopped in.
    /// </summary>
    internal void AskToStop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        stream.Stop();
    }

    /// <summary>
    /// Waits for the stream to be over and hands the last blocks on. A stream that had already
    /// ended by itself throws here, carrying the reason it ended, and so does one that will not end
    /// at all — which is the only thing anybody gets to do about that one.
    /// </summary>
    internal void Finish()
    {
        // One wait, and it is the thread's: a source is over exactly when the loop draining it comes
        // back, and joining it is also what makes everything that loop wrote visible here. Waiting
        // on the gate first and then on the thread would spend the deadline twice over one wedged
        // device, and a session stopping two of them would spend it four times — so "five seconds"
        // would name none of the times anybody actually waits.
        if (!stream.Stopped())
        {
            // Nothing is flushed and nothing is closed on the way out of here: the stream is still
            // inside the device and is still the thread that would write the next block. So this
            // says what is left of the source instead of doing anything to it. The count is the one
            // number that means something — every block already written went to the operating
            // system as it was written, so what it names is a recording that is really there and
            // really readable, and not a file somebody has to be told to go looking for.
            throw new AudioDeviceWedgedException(
                $"The {Channel} stream on '{Listening.Name}' did not stop within "
                + $"{CaptureLoop.StopsWithin.TotalSeconds:0} seconds. Its {Bytes} bytes of audio "
                + $"are in '{File.Name}' and stay there, and nothing it is still using is taken "
                + "away from it while it is in there.");
        }

        spool.Flush();

        if (failure is not null)
        {
            throw new AudioCaptureException(
                $"The {Channel} stream on '{Listening.Name}' ended by itself: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Lets go of everything this source holds, in an order and with a guarantee: the stream first,
    /// because it is what would otherwise still be handing blocks over, and every one of them
    /// whatever the one before it did. A device that will not close is exactly the case where a
    /// spool left open would refuse the next attempt at the same folder — so the first failure is
    /// what the caller hears, and the rest still happen.
    /// </summary>
    /// <remarks>
    /// Unless the stream was given up on, and then none of it happens. Both of these are things the
    /// draining thread uses and it is still running: the spool is what it writes each block to, and
    /// the gate is what it sets on its way out — and setting a gate that has been disposed throws
    /// on that thread, where nothing is catching, which ends the process and the meeting with it.
    /// So the source keeps its handle and says so through <see cref="Finish"/>. Closing a spool
    /// finishes nothing and completes nothing anyway: every block was already whole when it was
    /// written, which is why a capture that was killed rather than stopped still leaves a recording
    /// worth everything that reached the disk, and why one left open leaves the same.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            stream.Dispose();
        }
        finally
        {
            if (!stream.Abandoned)
            {
                try
                {
                    spool.Dispose();
                }
                finally
                {
                    ended.Dispose();
                }
            }

            running = false;
        }
    }

    /// <summary>
    /// Closes a source that never became part of a recording and takes its file with it. What it
    /// leaves behind would otherwise be a file with nothing in it, standing exactly where the
    /// next attempt wants to write — so a capture that failed once would go on failing for a
    /// reason that is no longer the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here throws. It runs while a session is already failing, and what the caller has
    /// to hear is why that happened, not that a handle would not close on the way out.
    /// </para>
    /// <para>
    /// A source whose stream was given up on keeps its file, because the handle is open and a live
    /// thread is writing through it. So the next attempt at that folder is refused by the blocks
    /// still standing in it — which is the truth: this process really is still recording into that
    /// file, and a folder it cleared would be one two threads then wrote to.
    /// </para>
    /// </remarks>
    internal void Discard()
    {
        LetGo();
        BlockSpool.Erase(File);
    }

    /// <summary>
    /// Closes everything this source holds and keeps its file. Like <see cref="Discard"/> it does
    /// not throw: it is how a session lets go of every source it has, and one handle refusing to
    /// close is not a reason to leave the next source recording.
    /// </summary>
    internal void LetGo()
    {
        try
        {
            Dispose();
        }
        catch (Exception letGo) when (letGo is IOException or UnauthorizedAccessException or COMException)
        {
            // Swallowed on purpose, and only here: see the summary. What a source has to say
            // about how it ended is said by Finish, which the session calls before this.
        }
    }

    /// <summary>
    /// Opens <paramref name="listening"/> onto <paramref name="channel"/> and starts recording it.
    /// Anything the machine refuses comes back as a throw, with nothing left open and no file
    /// left behind.
    /// </summary>
    internal static CaptureSource Open(AudioChannel channel, CaptureTarget listening, FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(listening);
        ArgumentNullException.ThrowIfNull(file);

        WasapiStream? stream = null;
        SpoolWriter? spool = null;
        var claimed = false;

        // The stream first and then the file, which is the same order and the same rule as Dispose:
        // the stream is what decides whether anything else here may be touched at all. A device
        // that would not start leaves a thread inside it, and being given up on is a deadline and
        // not a promise — one that answers a moment late runs the rest of its body, draining into
        // the spool through the callback it was handed. So a spool closed and a file erased here
        // would be closed and erased under a live writer, and the one attempt that failed would
        // take the next one down with it. What is kept instead is a handle and a file this process
        // really is still writing to, which is why the next recording into that folder is refused.
        void LetGo()
        {
            try
            {
                stream?.Dispose();
            }
            finally
            {
                if (stream is not { Abandoned: true })
                {
                    try
                    {
                        spool?.Dispose();
                    }
                    finally
                    {
                        if (claimed)
                        {
                            BlockSpool.Erase(file);
                        }
                    }
                }
            }
        }

        try
        {
            stream = listening.Open(channel);
            var format = StreamFormat.Of(stream.WaveFormat);

            // Asked before the device is started rather than answered by its first block: a width
            // nothing here can read is knowable now, and a capture that dies a second in is one
            // somebody has already started holding a meeting into.
            Levels.EnsureMeterable(format);

            spool = SpoolWriter.Create(file, channel, format);
            claimed = true;

            var source = new CaptureSource(channel, listening, format, file, stream, spool);
            source.Start();
            return source;
        }
        catch (COMException refused)
        {
            LetGo();
            throw new AudioCaptureException(
                $"Windows would not record '{listening.Name}': {refused.Message}", refused);
        }
        catch (AudioDeviceWedgedException wedged)
        {
            // Said again here because this is the only level that knows what a person called the
            // thing that did not answer: the stream knows a channel, and a channel is not what
            // somebody chose in a list. What letting go does and does not close on this path is
            // LetGo's own rule, and a stream given up on keeps every one of them.
            LetGo();
            throw new AudioDeviceWedgedException(
                $"'{listening.Name}' did not start the {channel} recording and did not refuse it "
                + $"either, so nothing was recorded. {wedged.Message}",
                wedged);
        }
        catch
        {
            LetGo();
            throw;
        }
    }

    private void Start()
    {
        // The device was already initialised on this thread, so one Windows will not hand over
        // threw before here rather than on a capture loop nobody is watching.
        stream.Start(Captured, Ended);
        StartedAt = UtcTimestamp.From(TimeProvider.System.GetUtcNow());
        running = true;
    }

    /// <summary>
    /// One block, as the device reported it. Everything about where it belongs is on the packet
    /// and nothing here infers any of it — see <see cref="WasapiStream"/> for why that matters and
    /// <see cref="PacketTally"/> for what the positions then add up to.
    /// </summary>
    private void Captured(CapturePacket packet)
    {
        if (packet.Samples.Length <= 0)
        {
            return;
        }

        tally.Add(packet);
        meter.Add(Levels.Peak(packet.Samples.Span, Format));
        spool.Write(packet);
    }

    private void Ended(Exception? stopped)
    {
        failure = stopped;
        ended.Set();
    }
}
