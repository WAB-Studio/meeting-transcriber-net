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
    /// The stream feeding this source now, which is not always the one it opened with: a channel
    /// following a program is moved to the whole machine's audio when somebody chooses it, and a
    /// channel whose endpoint went away is moved onto whatever replaced it. What makes either one
    /// recording rather than two is that the spool and the meter carry straight on. Whether the
    /// frame counter does is what tells the two handovers apart, and <see cref="MoveTo"/> reads
    /// that off the stream rather than off the destination.
    /// </summary>
    private WasapiStream stream;

    /// <summary>
    /// When it started, on the clock that does not move when the system's does. What the ten
    /// seconds a silent program is given are counted against — a wall clock corrected mid meeting
    /// would offer the whole machine at once or never.
    /// </summary>
    private long openedAt;

    /// <summary>
    /// What the stream ended with, or nothing when it ended without one. Written on the draining
    /// thread the moment the device lets go of it, and read on the thread that stops this source —
    /// <see cref="Finish"/>, which is what turns it into the exception the caller hears — and
    /// cleared again on the thread that hands the source a different stream. Volatile because
    /// those threads share no lock over it: the one that writes it is inside the device and is
    /// gone by the time anybody asks.
    /// </summary>
    private volatile Exception? failure;

    /// <summary>
    /// The move this source is on, or nothing before it has ever been moved. Read on the draining
    /// thread of every stream this source has had and written on the thread that moves it, which
    /// is what makes a stream ending long after the channel left it tell the right move — and the
    /// right move, being the one that replaced it, is retired and says nothing.
    /// </summary>
    private Handover? moving;

    /// <summary>Backs <see cref="Listening"/>, which says there why it is not an auto-property.</summary>
    private CaptureTarget listening;

    private bool running;

    /// <summary>
    /// Whether this source never became part of a recording, so its file is nobody's. Written on
    /// the thread that gave up on it and read on the draining thread, which may be inside the
    /// device when that happens — volatile because those two never meet again if it is.
    /// </summary>
    private volatile bool discarded;

    /// <summary>
    /// Backs <see cref="EndedAt"/> — when the stream ended, in Unix milliseconds, and zero while it
    /// is still going. It says there why it is a number behind a volatile read and not the moment
    /// itself, and why it and not the gate is what a reader asks.
    /// </summary>
    private long endedAt;

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
        this.listening = listening;
        OpenedOn = listening;
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
    /// What Windows has open for it: an endpoint, one program's audio, or everything this machine
    /// plays. What it is listening to now, which is what it opened with unless somebody moved it —
    /// the card beside the blocks is where what it opened with is kept.
    /// </summary>
    /// <remarks>
    /// Read and written the way <see cref="stream"/> is, and for the same reason: this is the one
    /// thing about a source that changes while it is recording, and a screen showing what each
    /// channel is capturing reads it from a thread that has nothing to do with the one that moved
    /// it.
    /// </remarks>
    public CaptureTarget Listening
    {
        get => Volatile.Read(ref listening);
        private set => Volatile.Write(ref listening, value);
    }

    /// <summary>
    /// The format the device this source opened on handed over, which is what its spool's own
    /// header says it holds.
    /// </summary>
    /// <remarks>
    /// What this source opened at and never what it is on now, deliberately. A device that took
    /// this source over is free to hand over another format entirely, and the file says so where a
    /// file has to — in the record that marks the seam — rather than in a field that would then be
    /// the one thing on this object disagreeing with the header beside it. What a block is read at
    /// is the format of the stream that handed it over, settled where that stream is started.
    /// </remarks>
    public StreamFormat Format { get; }

    /// <summary>What this source opened on, whatever it is listening to now.</summary>
    /// <remarks>
    /// Kept beside <see cref="Listening"/> rather than read off the card in the folder, because
    /// what is watching a meeting run has the source and not the folder — and because the two
    /// differing is the whole of what a screen has to be able to say about a channel that moved.
    /// </remarks>
    public CaptureTarget OpenedOn { get; }

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

    /// <summary>
    /// When the stream ended, or nothing while it is still going. The instant and not a length,
    /// because what a screen says about a source that died is the time of day it was cut off at —
    /// somebody who steps back into a meeting reads that against the clock on the wall rather than
    /// against how long the recording has been running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the answer, and the gate beside it is not.</b> A reader takes the moment and asks
    /// nothing else: <see cref="HasEnded"/> is what stopping waits on, and the two are written on
    /// different threads, so a reader that took one and then the other would be reading two answers
    /// to one question. That is the same "a flag beside an instant" this record's
    /// <c>ChannelReading.StoppedAt</c> exists to collapse, and it has to be collapsed here too or
    /// the collapse upstairs is decoration.
    /// </para>
    /// <para>
    /// Milliseconds behind a <see cref="Volatile"/> read rather than the nullable struct it hands
    /// back, and that is not a micro-optimisation. <c>UtcTimestamp?</c> is a flag over a
    /// <c>DateTimeOffset</c> — more than a word — so writing one is several stores and reading one
    /// several loads, and a reader crossing a write sees a moment half of which is the one before
    /// it. What that puts on screen is a channel cut off in year one. A <c>long</c> is written and
    /// read whole, and zero is the value no meeting can be at, so it is the one that means still
    /// going.
    /// </para>
    /// <para>
    /// A wall clock and not the monotonic one <see cref="OpenFor"/> is measured on, which is the
    /// opposite choice to the one made ten lines up and for the opposite reason. That one is a
    /// length compared against a deadline, so a clock corrected mid meeting must not move it; this
    /// is a time of day somebody reads off a screen, and the correction is exactly what they want
    /// applied.
    /// </para>
    /// </remarks>
    public UtcTimestamp? EndedAt => Volatile.Read(ref endedAt) is var moment && moment != 0
        ? UtcTimestamp.FromUnixMilliseconds(moment)
        : null;

    // What the stream threw on its way out is deliberately not offered here. It was, once, so that
    // a screen could print it while the meeting ran — and what a person got was a COMException, or
    // this file's own sentence, or the filesystem's, all of it framework English on a screen every
    // word of which is owed in two languages. A channel that died is said in words the catalogue
    // owns; the machine's own text is put in the sentence Finish throws, where it is read by
    // whoever is diagnosing a meeting rather than by whoever is recording one.

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
    /// <b>There are two handovers and one thing decides which.</b> A source whose device numbers no
    /// frames of its own is being placed by the machine's clock, and so is the stream taking over:
    /// that one carries on from the same sequence at the same format, and there is no seam to
    /// reconcile — which is exactly a channel 0 moved onto the whole machine. A source whose device
    /// numbers its own frames is placed by that counter, and the counter of whatever replaces it
    /// starts again at its own zero — which is a microphone — so what it opens is a stretch of its
    /// own: its own format, its own anchor, and everything between the two devices counted as the
    /// audio that never arrived. Which of the two it is is read off the stream and never off the
    /// kind of thing the destination is, so a fourth way of opening a channel is placed by what it
    /// does rather than by a list somewhere that would have to remember it.
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
    /// <para>
    /// One thing is not on the near side of that line and cannot be put there: a replacement that
    /// started and then died before the channel reached it. Ordering the two writes differently
    /// only moves which end is lost, so it is a <see cref="Handover"/> that decides who reports.
    /// </para>
    /// </remarks>
    /// <param name="destination">What to record from here on.</param>
    /// <param name="sayingSo">
    /// Written down before a byte of the new device reaches this recording, and the last thing that
    /// may fail. It is this way round because of which lie is worse: a folder claiming a move that
    /// did not happen says the machine's own audio is in a file that only holds the program's, and
    /// somebody reads that as their notifications being there when they are not. The other way
    /// round is the same sentence with the truth in it, and it is the one this exists to prevent.
    /// </param>
    internal void MoveTo(CaptureTarget destination, Action sayingSo)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sayingSo);

        if (destination.Channel != Channel)
        {
            throw new AudioCaptureException(
                $"'{destination.Name}' feeds the {destination.Channel} channel and this source is "
                + $"the {Channel} one. What a channel is listening to may change; which channel it "
                + "feeds is what it is.");
        }

        var placedBy = stream.Positions;

        // Wrapped where it is made rather than in the catch below, because the catch below never
        // saw it: opening is what activates the COM object, so a machine that refuses hands back a
        // raw COMException before there is anything to release. Reaching a caller that way it was
        // an exception nothing on the thread that follows a device catches, which ends the process
        // and the meeting with it.
        WasapiStream next;
        try
        {
            next = destination.Open(placedBy);
        }
        catch (COMException refused)
        {
            throw new AudioCaptureException(
                $"Windows would not record '{destination.Name}': {refused.Message}", refused);
        }

        // What both threads say what they did to, so that a replacement dying between being
        // started and being handed the channel is reported by whichever of them is second. The one
        // it replaces is retired here rather than when it finished, because a stream goes on
        // ending after the channel has left it and only a live move may report anything.
        var handover = new Handover(Ended);
        Interlocked.Exchange(ref moving, handover)?.Retire();

        try
        {
            var taking = StreamFormat.Of(next.WaveFormat);

            // Asked before the channel hands over rather than answered by the first block of the
            // new device: a width nothing here can read is knowable now, and a channel that moved
            // onto one would meter and spool nothing until somebody stopped the meeting.
            Levels.EnsureMeterable(taking);

            if (placedBy is not null && taking != Format)
            {
                // A stream carrying a sequence on is one stretch with the one before it, so the two
                // are read back at one format and a device handing over another one cannot take
                // over. Both ways of opening channel 0 ask for what the audio engine is mixing at,
                // so the only way here is the machine having changed what that is underneath a
                // meeting. A device that numbers its own frames is not this case at all: what it
                // opens is a stretch, which is allowed to be anything the device hands over.
                throw new AudioCaptureException(
                    $"'{destination.Name}' hands over {taking} and the {Channel} recording is "
                    + $"{Format}, so it cannot carry on placing it: what is already recorded and "
                    + "what would follow it would be two different sources in one stretch.");
            }

            Underway(next, placedBy is null ? taking : null, handover);
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
        // says it is fine is the failure this codebase is built against. What this clearing would
        // itself erase is `handover`'s, and is put back on top of it below.
        failure = null;

        // Cleared with the gate, and it does not matter which of the two goes first: nothing reads
        // the pair. What asks whether this source died reads the moment alone, and what waits for
        // it to stop reads the gate alone — which is the whole reason the moment is not stored
        // behind the gate. See EndedAt.
        Volatile.Write(ref endedAt, 0);
        ended.Reset();
        Listening = destination;

        // The one line the move happens on. Every block the new device has handed over so far was
        // dropped, and from here they are the recording — the old one's are dropped by the same
        // rule read the other way, so the two never write into one spool.
        Volatile.Write(ref stream, next);

        try
        {
            // Immediately after that line and before the old device is touched: from here the
            // channel is on the new stream, so a replacement that died while the two lines above
            // were running is this source having ended, and this is the arrival that says so.
            handover.TookOver();
        }
        finally
        {
            // The old device is let go of whatever saying that cost, which is what the `finally`
            // is for: this is the only reference to it, so a throw on the line above would leave a
            // live device draining into nothing with nobody able to ask it again — and it would
            // throw out of a move the folder has already recorded as made.
            //
            // Nothing here throws: the channel is recording again, and a device that will not let
            // go is a handle held rather than a meeting lost. One that was given up on keeps
            // everything it is still using, and is asked again when this source is let go of.
            previous.Stop();
            previous.Stopped();
            previous.Dispose();
            replaced.Add(previous);
        }
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
    /// Opens <paramref name="listening"/> onto the channel it feeds and starts recording it.
    /// Anything the machine refuses comes back as a throw, with nothing left open and no file
    /// left behind — and a device that never answered at all comes back as a throw too, at the
    /// deadline, leaving behind only what a thread still inside that device is using.
    /// </summary>
    internal static CaptureSource Open(CaptureTarget listening, FileInfo file, RecordingPause pause)
    {
        ArgumentNullException.ThrowIfNull(listening);
        ArgumentNullException.ThrowIfNull(file);

        // The target's and never a second answer beside it: what feeds which channel is fixed by
        // what the target is, and a channel passed in here could disagree with the stream's own.
        var channel = listening.Channel;

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
            stream = listening.Open(carryingOn: null);
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
    /// <param name="starting">The stream to drain.</param>
    /// <param name="seam">
    /// The format to declare on the first block of this stream that becomes the recording, when
    /// this stream is a device taking the source over, and nothing when it is not.
    /// </param>
    /// <param name="handover">
    /// The move this stream is being started for, when it is one taking the source over, and
    /// nothing when this stream is the one the source opened with. It is what an end arriving
    /// before the channel hands over is said to instead of being dropped — which is only ever this
    /// stream's case, since the stream being replaced is one the channel has already left.
    /// </param>
    private void Underway(WasapiStream starting, StreamFormat? seam = null, Handover? handover = null)
    {
        // Settled once, here, and carried into the callback. Every block this stream hands over is
        // read at the format this stream really opened at — a field read on the capture thread could
        // be the other device's for the instant a channel takes to hand over, and one block decoded
        // at the wrong width is noise nothing downstream could trace back.
        var arriving = StreamFormat.Of(starting.WaveFormat);

        // The seam belongs to this stream for exactly the reason the format above does, and it was
        // a field on the source until an adversarial pass found what that costs: between the moment
        // it is set and the moment the channel hands over, the stream still being recorded is the
        // old one, so the old device's own callback could take it — and one of its blocks would go
        // into the spool behind a record declaring the next device's format. Held here, only the
        // stream it belongs to can take it, and it is taken once.
        var opening = seam;

        starting.Start(
            packet =>
            {
                // Placed inside the condition and never before it. A stream that is not this
                // source's is one whose blocks go nowhere, and the sequence a channel is laid out
                // by advances every time it is asked — so asking for a block that is about to be
                // dropped would push the rest of the meeting along by its length.
                if (ReferenceEquals(Volatile.Read(ref stream), starting))
                {
                    Captured(starting.Place(packet), arriving, ref opening);
                }
            },
            stopped =>
            {
                // While this stream is the one blocks are read from, its end is this source's and
                // is said straight out. Otherwise it belongs to the move that brought this stream
                // in, which knows whether it is still the source's move and whether the channel
                // reached this stream — and reports, or does not, on those two answers. The stream
                // the source opened with has no move behind it, so its end there is nobody's.
                if (ReferenceEquals(Volatile.Read(ref stream), starting))
                {
                    Ended(stopped);
                    return;
                }

                handover?.Ended(stopped);
            });
    }

    /// <summary>
    /// One block, as the device reported it. Everything about where it belongs is on the packet
    /// and nothing here infers any of it — see <see cref="WasapiStream"/> for why that matters and
    /// <see cref="PacketTally"/> for what the positions then add up to.
    /// </summary>
    /// <param name="packet">The block, as this stream's device handed it over.</param>
    /// <param name="arriving">The format this stream's device hands over.</param>
    /// <param name="opening">
    /// This stream's seam, taken exactly once by the first block of it that really becomes the
    /// recording. Everything downstream reads it as the seam it is: the spool writes the new format
    /// down, the tally starts counting again, and the timeline closes the stretch before it and
    /// opens one here.
    /// </param>
    private void Captured(CapturePacket packet, StreamFormat arriving, ref StreamFormat? opening)
    {
        if (packet.Samples.Length <= 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref opening, null) is { } stretch)
        {
            packet = packet with { Opening = stretch };
        }

        // The pause decides what this block is worth to the recording, and it is asked once. There
        // is no branch here on purpose: the whole decision is one object, so a build that broke
        // pausing would have to delete this line rather than get a condition subtly wrong.
        var heard = pause.Reaching(packet);
        var level = Levels.Peak(heard.Samples.Span, arriving);

        // The tally is fed the same block the spool is, silence and all, because what it counts is
        // where the recording got to and not what was in it. A pause is part of the meeting.
        tally.Add(heard);
        meter.Add(level);

        // The block as the device handed it over, which is the same block unless the meeting is
        // paused — and the pause is the only thing that ever substitutes one, so the reading is
        // taken twice only for the stretch where the two really are different.
        delivered.Add(ReferenceEquals(heard, packet) ? level : Levels.Peak(packet.Samples.Span, arriving));

        spool.Write(heard);
    }

    private void Ended(Exception? stopped)
    {
        failure = stopped;
        Volatile.Write(
            ref endedAt, TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds());
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
