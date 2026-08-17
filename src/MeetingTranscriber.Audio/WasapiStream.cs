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

    /// <summary>Where each packet goes, or nothing when the device numbers its own frames.</summary>
    private readonly FramePositions? positions;
    private readonly byte[] block;
    private readonly int bytesPerFrame;
    private readonly int pollMs;
    private readonly ManualResetEventSlim started = new(initialState: false);
    private Action<CapturePacket>? captured;
    private Action<Exception?>? finished;
    private Exception? refused;
    private CaptureLoop? loop;

    private WasapiStream(
        AudioChannel channel,
        AudioClient client,
        WaveFormat format,
        IDisposable? endpoint,
        bool numbersFrames)
    {
        this.channel = channel;
        this.client = client;
        this.endpoint = endpoint;
        positions = numbersFrames ? null : new FramePositions(format.SampleRate);
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
    /// Whether the draining loop was given up on, so this stream is still inside the device and
    /// everything it holds is still being used by a thread nothing can stop. What a caller reads
    /// before letting go of anything of its own that the loop hands blocks to.
    /// </summary>
    internal bool Abandoned => loop is { Abandoned: true };

    /// <summary>
    /// Opens <paramref name="device"/> for capture onto <paramref name="channel"/>, in the format
    /// the machine is already mixing at.
    /// </summary>
    /// <param name="device">The endpoint to record.</param>
    /// <param name="channel">
    /// Which of the two channels its blocks feed, which is also the whole of what decides how the
    /// endpoint is opened: channel 0 records what it is playing and channel 1 what it hears. Passed
    /// as one thing rather than as a channel and a direction, because the two can only ever agree.
    /// </param>
    internal static WasapiStream On(AudioDevice device, AudioChannel channel)
    {
        ArgumentNullException.ThrowIfNull(device);

        var direction = channel switch
        {
            AudioChannel.Loopback => AudioClientStreamFlags.Loopback,
            AudioChannel.Microphone => AudioClientStreamFlags.None,
            _ => throw new AudioContractException($"There is no way to capture '{channel}'."),
        };

        // Opened here and let go here if anything after it refuses. Windows hands the endpoint and
        // its client over before it will say whether it can record in this format or at all —
        // another application holding the device in exclusive mode is the ordinary way to hear no —
        // and a capture somebody retries after closing that application would otherwise leave one
        // behind every time it failed.
        var endpoint = AudioDevices.Open(device);
        try
        {
            var client = endpoint.AudioClient;
            return Ready(channel, client, client.MixFormat, direction, endpoint, numbersFrames: true);
        }
        catch
        {
            endpoint.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens what <paramref name="process"/> and the processes it started are playing, onto channel
    /// 0. There is no endpoint behind it, so the format is the one the audio engine is mixing at
    /// rather than one a device named: the virtual client will not say what it mixes at, and asking
    /// for what the engine already produces is what keeps channel 0 the same file either way.
    /// </summary>
    internal static WasapiStream Following(AudioProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        // Asked for before anything is activated, and that order is the point: it is the one step
        // here that can fail without a client to let go of, and a client obtained first would be
        // left open by a machine that had just lost its playback endpoint.
        var format = AudioDevices.EngineFormat();

        // Set even though the format asked for is the one the engine mixes at, so that a machine
        // mixing at something this cannot ask for gets a conversion instead of a refusal.
        const AudioClientStreamFlags converting =
            AudioClientStreamFlags.Loopback
            | AudioClientStreamFlags.AutoConvertPcm
            | AudioClientStreamFlags.SrcDefaultQuality;

        // It numbers nothing: every packet comes back at frame zero, measured on this machine over
        // ten seconds of a program playing a tone. See FramePositions for what stands in.
        return Ready(
            AudioChannel.Loopback,
            ProcessLoopback.For(process),
            format,
            converting,
            endpoint: null,
            numbersFrames: false);
    }

    /// <summary>
    /// Initialises a client somebody else obtained and wraps it, letting the client go if Windows
    /// refuses the format or the mode. Where the client came from is the only thing the two ways in
    /// disagree about; from here on there is one stream.
    /// </summary>
    private static WasapiStream Ready(
        AudioChannel channel,
        AudioClient client,
        WaveFormat format,
        AudioClientStreamFlags how,
        IDisposable? endpoint,
        bool numbersFrames)
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

            return new WasapiStream(channel, client, format, endpoint, numbersFrames);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the device and the loop that drains it. <paramref name="onPacket"/> runs on that
    /// loop, and the packet's samples live in a buffer the next block overwrites — so whoever takes
    /// one writes what it needs before returning.
    /// </summary>
    /// <remarks>
    /// Comes back once the device is running, and throws here if it would not start. The loop is on
    /// its own thread and a refusal reached over there would leave this returning a stream that is
    /// already over: a session opens both of its sources or neither, and it cannot hold to that if
    /// the second source is handed back before the first one has said whether it started.
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
        started.Wait();

        if (refused is not null)
        {
            // The loop returned before it set this, so there is nothing left to wait for and the
            // stream never became one anybody was handed.
            loop.Dispose();
            loop = null;

            // Thrown as it was caught, so what Windows said about this device is still a
            // COMException by the time the caller turns it into a sentence about that device.
            ExceptionDispatchInfo.Capture(refused).Throw();
        }
    }

    /// <summary>Asks the loop to stop, and comes back without waiting for it to.</summary>
    internal void Stop() => loop?.AskToStop();

    /// <summary>
    /// Waits for the loop, bounded, and then lets go of what the loop was using — in that order,
    /// because the order is the guarantee: once this returns without <see cref="Abandoned"/>,
    /// nothing is still handing blocks to whoever subscribed, so a file can be closed under it.
    /// </summary>
    /// <remarks>
    /// A loop that did not come back keeps every one of these. The client is what it is blocked
    /// inside, the endpoint is underneath that client, and the gate is what it would touch on its
    /// way out if it ever took one — so releasing a COM object here would not be tidying up after a
    /// dead thread, it would be pulling the floor out from under a live one. What that costs is one
    /// device held until the process ends, and the recording is on disk either way.
    /// </remarks>
    public void Dispose()
    {
        loop?.Dispose();

        if (Abandoned)
        {
            return;
        }

        loop = null;
        client.Dispose();
        endpoint?.Dispose();
        started.Dispose();
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
            // waiting for it, and not to whoever was going to be told the stream was over.
            refused = no;
            started.Set();
            return;
        }

        started.Set();

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
                var vouched = !flags.HasFlag(AudioClientBufferFlags.TimestampError);

                captured?.Invoke(new CapturePacket(
                    channel,
                    positions?.For(at, vouched, frames) ?? position,
                    at,
                    block.AsMemory(0, length),
                    vouched));
            }
        }
    }
}
