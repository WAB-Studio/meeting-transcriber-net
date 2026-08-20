using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One WASAPI stream, its loop driven here, handing over every block with the two numbers the
/// device reported for it: how many frames it had produced before that block's first frame, and
/// when it read that count on the machine's monotonic clock.
/// </summary>
/// <remarks>
/// <para>
/// This exists because NAudio's <c>WasapiCapture</c> throws both numbers away. It calls the
/// two-argument <c>IAudioCaptureClient::GetBuffer</c>, keeps the flags only to zero-fill a silent
/// block, and raises bytes and a count; its loop is private and not virtual, so there is nothing to
/// override. <c>AudioClient</c> and <c>AudioCaptureClient</c> are public, and the four-argument
/// overload is right there — so the loop is fifty lines here rather than a fork of NAudio.
/// </para>
/// <para>
/// Nothing here infers either number, and that is the whole point. A position counted from the
/// bytes that arrived is exact only while nothing is ever lost, and a counter read on this thread
/// says when the application looked rather than when the device did — the same number right up to
/// the moment a slow disk holds this loop, which is the moment it is needed.
/// </para>
/// <para>
/// Polled rather than event driven, which is what NAudio does too. The device is asked to buffer
/// <see cref="BufferedMs"/> and drained twice as often, and how late this loop is to look changes
/// nothing about where a block lands: the instants come from the device, not from the poll that
/// collected them.
/// </para>
/// </remarks>
internal sealed class WasapiStream : IDisposable
{
    /// <summary>How much of the meeting the device is asked to hold for this loop.</summary>
    private const int BufferedMs = 100;

    /// <summary>WASAPI's unit for a length of time: 100 nanoseconds.</summary>
    private const long TicksPerMillisecond = 10_000;

    private readonly AudioChannel channel;
    private readonly AudioClient client;

    /// <summary>The endpoint the client came from, or nothing when it came from a process.</summary>
    private readonly IDisposable? endpoint;

    /// <summary>
    /// Where each packet goes, or nothing when the device numbers its own frames — which is a
    /// microphone and nothing else, since the virtual device channel 0 comes off numbers none.
    /// </summary>
    private readonly FramePositions? positions;
    private readonly byte[] block;
    private readonly int bytesPerFrame;
    private readonly int pollMs;
    private Action<CapturePacket>? captured;
    private Action<Exception?>? finished;

    /// <summary>
    /// Why the device would not start, written on the loop thread and read on the caller's. Volatile
    /// because those two only meet through the gate when the loop reached it in time: at the deadline
    /// itself the caller stops waiting without ever synchronising with the loop, and an answer that
    /// arrived a moment too late is still an answer rather than a wedge.
    /// </summary>
    private volatile Exception? refused;

    private CaptureLoop? loop;
    private DeviceRelease? release;

    private WasapiStream(
        AudioChannel channel,
        AudioClient client,
        WaveFormat format,
        IDisposable? endpoint,
        FramePositions? positions)
    {
        this.channel = channel;
        this.client = client;
        this.endpoint = endpoint;
        this.positions = positions;
        WaveFormat = format;
        bytesPerFrame = format.Channels * format.BitsPerSample / 8;
        block = new byte[client.BufferSize * bytesPerFrame];

        // Half of what the device holds, so a poll that runs late still finds room rather than a
        // ring buffer that has already overwritten a stretch of the meeting.
        pollMs = Math.Max(1, client.BufferSize * 1000 / format.SampleRate / 2);
    }

    /// <summary>The format the device handed over, in NAudio's terms.</summary>
    internal WaveFormat WaveFormat { get; }

    /// <summary>
    /// Where this stream's packets are being placed when its device numbers none of them, or
    /// nothing when the device numbers its own. What a stream taking this one's place is handed,
    /// so that the channel goes on being laid out by one sequence rather than starting again.
    /// </summary>
    internal FramePositions? Positions => positions;

    /// <summary>
    /// Says where <paramref name="packet"/> belongs on the channel, for a device that numbers no
    /// frames of its own; a device that numbers them is already right and comes back untouched.
    /// </summary>
    /// <remarks>
    /// Asked by whoever is keeping the packet and never by the loop that drained it, because the
    /// sequence advances: a channel being moved has two streams handing blocks over for a moment
    /// and only one of them is the recording, so placing a block before that was decided would push
    /// the channel past audio no file holds. See <see cref="FramePositions.For"/>, which is where
    /// that costs the rest of the meeting.
    /// </remarks>
    internal CapturePacket Place(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return positions is null
            ? packet
            : packet with
            {
                DevicePosition = positions.For(
                    packet.CapturedAt, packet.TimingIsSound, packet.Samples.Length / bytesPerFrame),
            };
    }

    /// <summary>
    /// Whether the draining loop was given up on, so this stream is still inside the device and
    /// everything it holds is still being used by a thread nothing can stop. What a caller reads
    /// before letting go of anything of its own that the loop hands blocks to.
    /// </summary>
    internal bool Abandoned => loop is { Abandoned: true };

    /// <summary>
    /// Opens <paramref name="device"/> for capture onto <paramref name="channel"/>, in the format
    /// the machine is already mixing at.
    /// </summary>
    /// <param name="device">The endpoint to record, which is a microphone.</param>
    /// <param name="channel">
    /// Which channel its blocks are labelled with. Carried rather than decided on: an endpoint is
    /// captured one way only, and which channel it feeds is the session's to settle — see
    /// <see cref="CaptureTarget.Endpoint"/>, which is the only thing that builds one of these.
    /// </param>
    internal static WasapiStream On(AudioDevice device, AudioChannel channel)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Every line of it behind one deadline, because every line of it is a synchronous call into
        // the same driver: opening the endpoint, asking it for its client, reading what the machine
        // mixes at, and telling the client what to record. One ask rather than four, so a device
        // that wedges anywhere along the way is given up on once — and so the letting go that a
        // failure part-way needs happens on the one thread that is holding anything.
        return DeviceOpen.Answering($"{channel} device", () =>
        {
            // Opened here and let go here if anything after it refuses. Windows hands the endpoint
            // and its client over before it will say whether it can record in this format or at all
            // — another application holding the device in exclusive mode is the ordinary way to
            // hear no — and a capture somebody retries after closing that application would
            // otherwise leave one behind every time it failed.
            var endpoint = AudioDevices.Open(device);
            AudioClient? client = null;
            WaveFormat mixing;

            try
            {
                client = endpoint.AudioClient;
                mixing = client.MixFormat;
            }
            catch
            {
                // The client as well as the endpoint, and it is not covered by the endpoint: asking
                // for one activates a COM object NAudio keeps no reference to, so an MMDevice let go
                // of takes nothing with it. A machine that will not say what it is mixing at is the
                // ordinary way to reach this, and one client per attempt is what missing it costs.
                DeviceRelease.LetGoOf(client);
                DeviceRelease.LetGoOf(endpoint);
                throw;
            }

            // Handed over rather than wrapped in a catch of its own, and that is the whole reason
            // these two lines are not one: from here the client is what a wedge would be inside and
            // the endpoint is underneath it, so both are let go of together and in that order.
            //
            // Captured and never played back, and nothing places its packets: an endpoint numbers
            // its own frames. Channel 0 is no endpoint, so neither of those is a case here.
            return Ready(
                channel, client!, mixing, AudioClientStreamFlags.None, endpoint, positions: null);
        });
    }

    /// <summary>
    /// Opens what <paramref name="process"/> and the processes it started are playing, onto
    /// channel 0.
    /// </summary>
    /// <remarks>
    /// It takes no positions to carry on from, and that is the shape of what can happen: a
    /// recording is moved onto the whole machine and never onto a program, so a program's stream is
    /// always a channel's first.
    /// </remarks>
    /// <param name="process">The program to follow.</param>
    internal static WasapiStream Following(AudioProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return Loopback("program", () => ProcessLoopback.Following(process), placedBy: null);
    }

    /// <summary>
    /// Opens everything this machine is playing, wherever it comes out, onto channel 0.
    /// </summary>
    /// <remarks>
    /// The same activation as <see cref="Following"/> under the other mode, so a recording moved
    /// from a program to the machine changes which processes are being listened to and nothing
    /// else: the same format, the same absent frame numbering, the same file.
    /// </remarks>
    /// <param name="placedBy">
    /// The positions this stream carries on from, when it is taking a program's place on a channel
    /// that is already being recorded, or nothing when it is the channel's first stream.
    /// </param>
    internal static WasapiStream TheWholeMachine(FramePositions? placedBy = null) =>
        Loopback("machine", ProcessLoopback.EverythingThisMachinePlays, placedBy);

    /// <summary>
    /// Opens channel 0, which is a process loopback either way. There is no endpoint behind it, so
    /// the format is the one the audio engine is mixing at rather than one a device named: the
    /// virtual client will not say what it mixes at, and asking for what the engine already
    /// produces is what keeps channel 0 the same file however it was opened.
    /// </summary>
    /// <param name="named">What this way in is called in the name of the thread that opens it.</param>
    /// <param name="activate">What produces the client, which is the whole of what the two differ by.</param>
    /// <param name="placedBy">The positions to carry on from, or nothing to start a sequence.</param>
    private static WasapiStream Loopback(
        string named, Func<AudioClient> activate, FramePositions? placedBy)
    {
        // Set even though the format asked for is the one the engine mixes at, so that a machine
        // mixing at something this cannot ask for gets a conversion instead of a refusal.
        const AudioClientStreamFlags converting =
            AudioClientStreamFlags.Loopback
            | AudioClientStreamFlags.AutoConvertPcm
            | AudioClientStreamFlags.SrcDefaultQuality;

        // Behind the deadline for the reason the endpoint way in is, and it is not a lesser case
        // because there is no device named: asking what the engine mixes at opens the playback
        // endpoint and asks its client, which is the same driver and the same way of not answering.
        return DeviceOpen.Answering($"{AudioChannel.Loopback} {named}", () =>
        {
            // Asked for before anything is activated, and that order is the point: it is the one
            // step here that can fail without a client to let go of, and a client obtained first
            // would be left open by a machine that had just lost its playback endpoint.
            var format = AudioDevices.EngineFormat();

            // The virtual device numbers nothing: every packet comes back at frame zero, measured
            // on this machine over ten seconds of a program playing a tone. See FramePositions for
            // what stands in, and CaptureSource.MoveTo for why one is ever handed in.
            return Ready(
                AudioChannel.Loopback,
                activate(),
                format,
                converting,
                endpoint: null,
                placedBy ?? new FramePositions(format.SampleRate));
        });
    }

    /// <summary>
    /// Initialises a client somebody else obtained and wraps it, letting that client go — and the
    /// endpoint under it, where there is one — if Windows refuses the format or the mode. Where the
    /// client came from is the only thing the two ways in disagree about; from here on there is one
    /// stream, and one thread that lets its handles go.
    /// </summary>
    /// <remarks>
    /// Runs inside the ask both ways in are made through, so the thread it lets go on is the thread
    /// that obtained everything — which is what makes the release below correct rather than
    /// hopeful, and what puts it under the one deadline this device gets.
    /// </remarks>
    private static WasapiStream Ready(
        AudioChannel channel,
        AudioClient client,
        WaveFormat format,
        AudioClientStreamFlags how,
        IDisposable? endpoint,
        FramePositions? positions)
    {
        try
        {
            client.Initialize(
                AudioClientShareMode.Shared,
                how,
                BufferedMs * TicksPerMillisecond,
                0,
                format,
                Guid.Empty);

            return new WasapiStream(channel, client, format, endpoint, positions);
        }
        catch
        {
            // Initialising is where a device most often says no, and a driver that says no and then
            // will not be let go of is the same wedge as any other — which is why the deadline this
            // runs inside covers the letting go and not only the asking.
            //
            // In the order Dispose uses and for its reason: the client is what a wedge would be
            // inside and the endpoint is underneath it. A client that refuses to close has answered,
            // so its endpoint is still let go of after it; a client that wedges never reaches the
            // line below, which is the same rule read the other way.
            DeviceRelease.LetGoOf(client);
            DeviceRelease.LetGoOf(endpoint);

            throw;
        }
    }

    /// <summary>
    /// Starts the device and the loop that drains it. <paramref name="onPacket"/> runs on that
    /// loop, and the packet's samples live in a buffer the next block overwrites — so whoever takes
    /// one writes what it needs before returning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comes back once the device is running, and throws here if it would not start. The loop is on
    /// its own thread and a refusal reached over there would leave this returning a stream that is
    /// already over: a session opens both of its sources or neither, and it cannot hold to that if
    /// the second source is handed back before the first one has said whether it started.
    /// </para>
    /// <para>
    /// A device that neither starts nor refuses is the third way out, and it is the loop's own
    /// deadline that bounds it: starting the device happens on that thread, so a driver that never
    /// returns from it is a loop that never gets underway, which is the one thing
    /// <see cref="CaptureLoop"/> already decides what to do about.
    /// </para>
    /// </remarks>
    /// <param name="onPacket">Takes each block the device hands over.</param>
    /// <param name="onEnd">Takes the reason the stream ended, or null if it was asked to.</param>
    internal void Start(Action<CapturePacket> onPacket, Action<Exception?> onEnd)
    {
        ArgumentNullException.ThrowIfNull(onPacket);
        ArgumentNullException.ThrowIfNull(onEnd);

        captured = onPacket;
        finished = onEnd;

        loop = CaptureLoop.Draining($"{channel} capture", Run);

        // The answer first, whichever side of the deadline it landed on. A device that refused has
        // answered and its loop is already returning, so reading the refusal before the deadline is
        // what keeps a machine that said no from being reported as one that said nothing — and a
        // refusal is what a person is told about and offered something to do, where a wedge is not.
        if (refused is not null)
        {
            // Waited for rather than assumed. The body sets that and returns, so the thread is over
            // or a step from it, and only a loop that really came back may have its client let go
            // of afterwards.
            loop.Dispose();

            if (!loop.Abandoned)
            {
                loop = null;
            }

            // Thrown as it was caught, so what Windows said about this device is still a
            // COMException by the time the caller turns it into a sentence about that device.
            ExceptionDispatchInfo.Capture(refused).Throw();
        }

        if (loop.Abandoned)
        {
            // Kept rather than let go of, and that is the whole of what can be done here: the loop
            // is inside the call that starts the device, so the client it is starting and the
            // endpoint under that client are what a live thread is holding. Nothing is released and
            // the loop is not dropped, so whoever disposes this reads that it was given up on and
            // leaves it alone too.
            throw AudioDeviceWedgedException.NoAnswerFrom($"{channel} device");
        }
    }

    /// <summary>Asks the loop to stop, and comes back without waiting for it to.</summary>
    internal void Stop() => loop?.AskToStop();

    /// <summary>
    /// Asks the loop to stop and waits the one deadline for it, letting go of nothing. True when it
    /// came back, and then everything the loop wrote is visible here — joining a thread is what
    /// makes it so, which is why nothing else has to be waited on to read why it ended.
    /// </summary>
    /// <remarks>
    /// The whole of the waiting a source does on the way out, so that a device is given up on once
    /// rather than once per holder. <see cref="Dispose"/> after this costs nothing either way.
    /// </remarks>
    internal bool Stopped()
    {
        loop?.Dispose();
        return !Abandoned;
    }

    /// <summary>
    /// Waits for the loop, bounded, and then lets go of what the loop was using — in that order,
    /// because the order is the guarantee: once this returns without <see cref="Abandoned"/>,
    /// nothing is still handing blocks to whoever subscribed, so a file can be closed under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A loop that did not come back keeps every one of these. The client is what it is blocked
    /// inside and the endpoint is underneath that client — so releasing a COM object here would not
    /// be tidying up after a dead thread, it would be pulling the floor out from under a live one.
    /// What that costs is one device held until the process ends, and the recording is on disk
    /// either way.
    /// </para>
    /// <para>
    /// Letting go is itself bounded, because a driver that came back from draining can still wedge
    /// on being released: the two calls below are synchronous COM into that same driver, and an
    /// application that will not close after a meeting is the same failure one line further down.
    /// So they run somewhere they can be given up on, and being given up on there means what it
    /// always means — the handles stay held, and the device is this application's until it is
    /// restarted.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        loop?.Dispose();

        // The loop is kept rather than dropped, so that a second call still knows whether it was
        // given up on — and so that a loop which came back late is noticed and its handles freed
        // by whoever calls next, instead of being held to the end of the process over a deadline
        // that was missed by a moment.
        if (Abandoned)
        {
            return;
        }

        // Built once and waited on again, which is what makes a second call cost nothing and a
        // release that came back late still count. A release wedged in there deliberately does not
        // make this stream Abandoned: that word is read by whoever else holds something the
        // draining loop was writing through, and this thread is inside nothing but the two handles
        // on these lines, which are this type's own and nobody else's to close.
        release ??= DeviceRelease.Of($"{channel} release", () =>
        {
            DeviceRelease.LetGoOf(client);
            DeviceRelease.LetGoOf(endpoint);
        });

        release.Dispose();
    }

    private void Run(CaptureLoop loop)
    {
        AudioCaptureClient capture;
        try
        {
            capture = client.AudioCaptureClient;
            client.Start();
        }
        catch (Exception no)
        {
            // Nothing started, so nothing has ended: this goes back to Start, which is still
            // waiting for it, and not to whoever was going to be told the stream was over. Said to
            // be underway on the way out of a refusal as much as on the way into a device that
            // started — a refusal is an answer, and there is nothing left here to wait for.
            refused = no;
            loop.Underway();

            // Unless nobody is waiting any more. A device that refuses after it has been given up
            // on has no caller left to throw at, and this thread coming back is the only news
            // anybody gets — so the callback is told, because whoever gave up is holding something
            // it kept open for exactly as long as this thread might still have written to it.
            if (loop.Abandoned)
            {
                finished?.Invoke(no);
            }

            return;
        }

        loop.Underway();

        Exception? failure = null;
        try
        {
            try
            {
                while (loop.Running)
                {
                    Thread.Sleep(pollMs);
                    Drain(capture);
                }

                // What the device produced while it was being asked to stop is still the meeting.
                Drain(capture);
            }
            finally
            {
                client.Stop();
            }
        }
        catch (Exception broke)
        {
            failure = broke;
        }
        finally
        {
            // Asked here as well as by whoever stopped it, so that a loop which ended on its own
            // reads as over rather than as one still meaning to take another pass.
            loop.AskToStop();
            finished?.Invoke(failure);
        }
    }

    /// <summary>Takes everything the device has ready, one packet at a time.</summary>
    /// <remarks>
    /// The buffer goes back to WASAPI before whoever is listening sees the packet, and that order is
    /// load bearing. A packet still held is a packet the engine cannot reuse, and Windows says so:
    /// hold one past a processing period and the device starts dropping audio. Handing it back first
    /// means a slow disk under the listener costs latency and not a hole in the meeting — which
    /// would be a hole invented by the thing measuring holes.
    /// </remarks>
    private void Drain(AudioCaptureClient capture)
    {
        while (capture.GetNextPacketSize() > 0)
        {
            var buffer = capture.GetBuffer(out var frames, out var flags, out var position, out var instant);
            var length = frames * bytesPerFrame;

            try
            {
                if (frames <= 0)
                {
                    continue;
                }

                // A silent block's buffer holds whatever was last in it, and WASAPI is free to hand
                // over no buffer at all for one. Copying it would record the stretch nobody spoke
                // through as noise, which is worse than recording it as the silence it was.
                //
                // Not dead code, and no test reaches it: neither endpoint on the machine this was
                // written on ever sets the flag — both hand over real zeros for silence, loopback
                // included, measured over five seconds each — so what holds this branch is the
                // documented meaning of AUDCLNT_BUFFERFLAGS_SILENT and the driver that does set it.
                if (flags.HasFlag(AudioClientBufferFlags.Silent))
                {
                    Array.Clear(block, 0, length);
                }
                else
                {
                    Marshal.Copy(buffer, block, 0, length);
                }
            }
            finally
            {
                capture.ReleaseBuffer(frames);
            }

            if (frames > 0)
            {
                var at = MonotonicInstant.FromTicks(instant);

                // The device's own number, unplaced. Where it belongs on the channel is Place's,
                // and it is asked by whoever keeps the packet — see there for why not here.
                captured?.Invoke(new CapturePacket(
                    channel,
                    position,
                    at,
                    block.AsMemory(0, length),
                    !flags.HasFlag(AudioClientBufferFlags.TimestampError)));
            }
        }
    }
}
