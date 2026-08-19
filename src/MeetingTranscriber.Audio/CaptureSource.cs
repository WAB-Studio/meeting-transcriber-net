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
    private readonly SpoolWriter spool;
    private readonly SourceMeter meter = new();
    private readonly PacketTally tally;
    private readonly ManualResetEventSlim ended = new(initialState: false);

    /// <summary>
    /// What the device handed over, before the pause has had its say. The meter beside it is what
    /// the recording got, which is the right answer for a level somebody is watching and the wrong
    /// one for whether a program has ever played anything: a meeting paused over its first seconds
    /// would erase the program's audio and then be told the program was the wrong one.
    /// </summary>
    private readonly SourceMeter delivered = new();

    /// <summary>
    /// The streams this source used to be on, kept so that one given up on is asked again when this
    /// source is let go of. A stream that would not stop keeps everything it is still using, and
    /// answering late is what makes those handles releasable at all.
    /// </summary>
    private readonly List<WasapiStream> replaced = [];

    /// <summary>
    /// The recording's pause, shared with the other source rather than owned here. Both callbacks
    /// read the one flag, so pausing is a single write and cannot leave one channel recording the
    /// room while the other records silence — see <see cref="RecordingPause"/>.
    /// </summary>
    private readonly RecordingPause pause;

    /// <summary>
    /// The stream feeding this source now. Not the one it opened with: a channel following a
    /// program is moved to the whole machine's audio when somebody chooses it, and what makes that
    /// one recording rather than two is that everything around this field — the spool, the tally,
    /// the meter and the positions — carries straight on. See <see cref="MoveTo"/>.
    /// </summary>
    private WasapiStream stream;

    /// <summary>
    /// When it started, on the clock that does not move when the system's does. What the ten
    /// seconds a silent program is given are counted against — a wall clock corrected mid meeting
    /// would offer the whole machine at once or never.
    /// </summary>
    private long openedAt;

    private Exception? failure;
    private bool running;

    /// <summary>
    /// Whether this source never became part of a recording, so its file is nobody's. Written on
    /// the thread that gave up on it and read on the draining thread, which may be inside the
    /// device when that happens — volatile because those two never meet again if it is.
    /// </summary>
    private volatile bool discarded;

    private CaptureSource(
        AudioChannel channel,
        CaptureTarget listening,
        StreamFormat format,
        FileInfo file,
        WasapiStream stream,
        SpoolWriter spool,
        RecordingPause pause)
    {
        Channel = channel;
        Listening = listening;
        Format = format;
        File = file;
        this.stream = stream;
        this.spool = spool;
        this.pause = pause;
        tally = new PacketTally(format);
    }

    /// <summary>Which of the two channels this device feeds.</summary>
    public AudioChannel Channel { get; }

    /// <summary>
    /// What Windows has open for it: an endpoint, or one program's audio. What it is listening to
    /// now, which is what it opened with unless somebody moved it — the card beside the blocks is
    /// where what it opened with is kept.
    /// </summary>
    public CaptureTarget Listening { get; private set; }

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
    /// The loudest the device has handed over since it opened, whether or not the recording was
    /// paused for it. What says whether a program has ever played anything.
    /// </summary>
    public LevelReading Delivered => delivered.Loudest;

    /// <summary>How long it has been open, measured on a clock nothing can correct.</summary>
    public Duration OpenFor => Duration.FromTimeSpan(TimeProvider.System.GetElapsedTime(openedAt));

    /// <summary>
    /// Whether the stream is over. True before <see cref="Stop"/> means it ended on its own, and
    /// <see cref="Stop"/> is what says why.
    /// </summary>
    public bool HasEnded => ended.IsSet;

    /// <summary>The loudest block since this was last asked, which is what a meter shows.</summary>
    public LevelReading Level() => meter.Read();

    /// <summary>
    /// Moves this source onto <paramref name="destination"/> without ending it: the same spool,
    /// the same tally, the same meter, and packets laid out where the ones before them left off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recording goes on being one recording, which is the whole point — a meeting somebody is
    /// in the middle of is not something to stop and start again because the program they pointed
    /// at turned out to be the wrong one. What the spool then holds is one source's worth of audio
    /// whose first stretch came from somewhere else, and the folder says so beside the card rather
    /// than in it.
    /// </para>
    /// <para>
    /// Only a source whose device numbers no frames can be moved, which is exactly a channel
    /// following a program: it is already being placed by the machine's clock, so the stream taking
    /// over carries on from the same sequence and there is no seam to reconcile. One that numbers
    /// its own frames would arrive with a counter of its own that means nothing here, and that is a
    /// different thing entirely from this.
    /// </para>
    /// <para>
    /// <b>Nothing is given up until everything that can fail has happened.</b> The new device is
    /// obtained, initialised and really started while the old one is still the one being recorded —
    /// its blocks are dropped until this source says otherwise — and only then, with a live device
    /// in hand, is the channel handed over. Every way this can go wrong is on the near side of that
    /// one line, so a machine that refuses, a driver that does not answer and a folder that cannot
    /// be written all leave the recording exactly where it was, still on the program, still able to
    /// be asked again. Stopping the old device happens afterwards, where it can cost nothing.
    /// </para>
    /// </remarks>
    /// <param name="destination">The endpoint to record from here on.</param>
    /// <param name="sayingSo">
    /// Written down before a byte of the new device reaches this recording, and the last thing that
    /// may fail. It is this way round because of which lie is worse: a folder claiming a move that
    /// did not happen says the machine's own audio is in a file that only holds the program's, and
    /// somebody reads that as their notifications being there when they are not. The other way
    /// round is the same sentence with the truth in it, and it is the one this exists to prevent.
    /// </param>
    internal void MoveTo(CaptureTarget.Endpoint destination, Action sayingSo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sayingSo);

        var placedBy = stream.Positions
            ?? throw new AudioCaptureException(
                $"The {Channel} source is listening to '{Listening.Name}', which numbers its own "
                + "frames, so it is not something this moves: what places its audio is the device "
                + "that counted it.");

        var next = destination.Open(Channel, placedBy);
        try
        {
            var format = StreamFormat.Of(next.WaveFormat);
            if (format != Format)
            {
                // The spool declares one format in its header and every block in it is read back
                // that way, so a device handing over something else cannot be poured into it. Both
                // ways of opening channel 0 ask for what the audio engine is mixing at, so this is
                // the machine having changed what that is underneath a meeting.
                throw new AudioCaptureException(
                    $"'{destination.Name}' hands over {format} and the {Channel} recording is "
                    + $"{Format}, so it cannot take over: what is already recorded and what would "
                    + "follow it would be two different sources in one file.");
            }

            Underway(next);
            sayingSo();
        }
        catch (COMException refused)
        {
            // Windows' own refusal, said as a sentence about the device somebody chose — the same
            // wrapping opening a source for the first time does, and here it is what keeps a raw
            // COM failure from reaching a caller that is catching for a recording.
            DeviceRelease.LetGoOf(next);
            throw new AudioCaptureException(
                $"Windows would not record '{destination.Name}': {refused.Message}", refused);
        }
        catch
        {
            DeviceRelease.LetGoOf(next);
            throw;
        }

        var previous = stream;

        // Cleared before the handover rather than after it, and the race that leaves is the one
        // worth having: the old stream ending in the moment between these lines is reported as this
        // source having ended, which stops the recording and says so. The other order loses a new
        // stream that died on its first block, and a meeting recording nothing while everything
        // says it is fine is the failure this codebase is built against.
        failure = null;
        ended.Reset();
        Listening = destination;

        // The one line the move happens on. Every block the new device has handed over so far was
        // dropped, and from here they are the recording — the old one's are dropped by the same
        // rule read the other way, so the two never write into one spool.
        Volatile.Write(ref stream, next);

        // Afterwards, and nothing here throws: the channel is recording again, and a device that
        // will not let go is a handle held rather than a meeting lost. One that was given up on
        // keeps everything it is still using, and is asked again when this source is let go of.
        previous.Stop();
        previous.Stopped();
        previous.Dispose();
        replaced.Add(previous);
    }

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
        // Asked again, whatever they said last time. One of these was given up on and kept every
        // handle it was using; a loop that came back late is only noticed by whoever asks next,
        // and this is the last chance anybody does.
        foreach (var earlier in replaced)
        {
            DeviceRelease.LetGoOf(earlier);
        }

        try
        {
            stream.Dispose();
        }
        finally
        {
            // Every stream this source has ever been on, and not only the one it is on now. A
            // stream given up on is a live thread that may still be inside the callback that writes
            // each block, and the spool is what that callback writes to — so one this source was
            // moved off of holds the file open exactly as the current one would. Closing it under
            // that thread is the rule this whole file is written around, read one stream wider.
            if (!stream.Abandoned && !replaced.Exists(earlier => earlier.Abandoned))
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
    /// A source whose stream was given up on keeps both its handle and its file while that stream
    /// is still in the device, because a live thread is writing through them — a folder cleared
    /// here would be one two threads then wrote to. What it does not do is keep them for the life
    /// of the process: being given up on is a deadline and not a promise, so the draining thread
    /// carries on when the device finally answers, and taking the file is the last thing it does.
    /// A device that never answers at all keeps its file, and the recording that never started is
    /// then one more thing waiting to be decided about after a restart.
    /// </para>
    /// </remarks>
    internal void Discard()
    {
        // Said before anything is let go of, so that a device answering in the middle of this still
        // finds it said: after here the draining thread may be the only one that ever reads it.
        discarded = true;
        LetGo();

        if (stream.Abandoned)
        {
            return;
        }

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
    /// left behind — and a device that never answered at all comes back as a throw too, at the
    /// deadline, leaving behind only what a thread still inside that device is using.
    /// </summary>
    internal static CaptureSource Open(
        AudioChannel channel, CaptureTarget listening, FileInfo file, RecordingPause pause)
    {
        ArgumentNullException.ThrowIfNull(listening);
        ArgumentNullException.ThrowIfNull(file);

        WasapiStream? stream = null;
        SpoolWriter? spool = null;
        CaptureSource? source = null;
        var claimed = false;

        // Once there is a source, letting go is its own decision and not a second copy of it here:
        // it is the thing that knows whether a thread is still inside its stream, and a rule
        // written twice is one chance to write the second one differently.
        //
        // Before there is one, nothing that failed can be inside anything: a stream is handed back
        // only once its device answered, and the spool cannot be reached at all until Start wires
        // the callback that writes to it. So the stream first and then the file, in the order
        // Dispose uses, and every one of them whatever the one before it did.
        void LetGo()
        {
            if (source is not null)
            {
                source.Discard();
                return;
            }

            DeviceRelease.LetGoOf(stream);
            DeviceRelease.LetGoOf(spool);

            if (claimed)
            {
                BlockSpool.Erase(file);
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

            source = new CaptureSource(channel, listening, format, file, stream, spool, pause);
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
        Underway(stream);
        StartedAt = UtcTimestamp.From(TimeProvider.System.GetUtcNow());
        openedAt = TimeProvider.System.GetTimestamp();
        running = true;
    }

    /// <summary>
    /// Starts <paramref name="opening"/> draining, and takes what it hands over only while it is
    /// the stream this source is on.
    /// </summary>
    /// <remarks>
    /// That condition is the whole of how a channel moves from one device to another without ever
    /// having two of them writing into one spool, and without a moment where neither does. A stream
    /// starts before it is the source's — its blocks going nowhere, which is what proves the device
    /// is really running — and the one it replaces stops being the source's the instant the field
    /// changes, whatever it is still handing over on its way out.
    /// </remarks>
    private void Underway(WasapiStream opening)
    {
        opening.Start(
            packet =>
            {
                if (ReferenceEquals(Volatile.Read(ref stream), opening))
                {
                    Captured(packet);
                }
            },
            stopped =>
            {
                if (ReferenceEquals(Volatile.Read(ref stream), opening))
                {
                    Ended(stopped);
                }
            });
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

        // The pause decides what this block is worth to the recording, and it is asked once. There
        // is no branch here on purpose: the whole decision is one object, so a build that broke
        // pausing would have to delete this line rather than get a condition subtly wrong.
        var heard = pause.Reaching(packet);
        var level = Levels.Peak(heard.Samples.Span, Format);

        // The tally is fed the same block the spool is, silence and all, because what it counts is
        // where the recording got to and not what was in it. A pause is part of the meeting.
        tally.Add(heard);
        meter.Add(level);

        // The block as the device handed it over, which is the same block unless the meeting is
        // paused — and the pause is the only thing that ever substitutes one, so the reading is
        // taken twice only for the stretch where the two really are different.
        delivered.Add(ReferenceEquals(heard, packet) ? level : Levels.Peak(packet.Samples.Span, Format));

        spool.Write(heard);
    }

    private void Ended(Exception? stopped)
    {
        failure = stopped;
        ended.Set();

        // Only a source given up on and thrown away reaches the rest, and it reaches it once: a
        // stream that came back is one Discard and Dispose already had their turn at, on the thread
        // that asked. This is the other case — a device that was given up on and then answered — and
        // out here that thread is the only one that ever may close these, which is also what makes
        // the file removable at all, since Windows will not delete one a handle is still open on.
        if (!discarded || !stream.Abandoned)
        {
            return;
        }

        // Nothing here may throw. There is no boundary above this: it runs in the loop's own
        // finally, after the recording it belonged to has already failed, so an exception would end
        // the process over a folder nobody wanted. Letting go answers rather than throws, and so
        // does erasing.
        DeviceRelease.LetGoOf(spool);
        BlockSpool.Erase(File);
    }
}
