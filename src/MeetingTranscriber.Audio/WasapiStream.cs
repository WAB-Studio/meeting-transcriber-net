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
    private readonly byte[] block;
    private readonly int bytesPerFrame;
    private readonly int pollMs;
    private readonly ManualResetEventSlim started = new(initialState: false);
    private Action<CapturePacket>? captured;
    private Action<Exception?>? finished;
    private Exception? refused;
    private Thread? loop;
    private volatile bool running;

    private WasapiStream(AudioChannel channel, AudioClient client, WaveFormat format)
    {
        this.channel = channel;
        this.client = client;
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
    /// Opens <paramref name="endpoint"/> for capture onto <paramref name="channel"/>, in the format
    /// the machine is already mixing at.
    /// </summary>
    /// <param name="endpoint">The device to record, already opened.</param>
    /// <param name="channel">
    /// Which of the two channels its blocks feed, which is also the whole of what decides how the
    /// endpoint is opened: channel 0 records what it is playing and channel 1 what it hears. Passed
    /// as one thing rather than as a channel and a direction, because the two can only ever agree.
    /// </param>
    internal static WasapiStream On(MMDevice endpoint, AudioChannel channel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var direction = channel switch
        {
            AudioChannel.Loopback => AudioClientStreamFlags.Loopback,
            AudioChannel.Microphone => AudioClientStreamFlags.None,
            _ => throw new AudioContractException($"There is no way to capture '{channel}'."),
        };

        // Activated here and let go here if anything after it refuses. Windows hands the client
        // over before it will say whether it can record in this format or at all — another
        // application holding the device in exclusive mode is the ordinary way to hear no — and a
        // capture somebody retries after closing that application would otherwise leave one behind
        // every time it failed.
        var client = endpoint.AudioClient;
        try
        {
            var format = client.MixFormat;

            client.Initialize(
                AudioClientShareMode.Shared,
                direction,
                BufferedMs * TicksPerMillisecond,
                0,
                format,
                Guid.Empty);

            return new WasapiStream(channel, client, format);
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
        running = true;

        loop = new Thread(Run) { IsBackground = true, Name = $"{channel} capture" };
        loop.Start();
        started.Wait();

        if (refused is not null)
        {
            loop.Join();
            loop = null;
            running = false;

            // Thrown as it was caught, so what Windows said about this device is still a
            // COMException by the time the caller turns it into a sentence about that device.
            ExceptionDispatchInfo.Capture(refused).Throw();
        }
    }

    /// <summary>Asks the loop to stop, and comes back without waiting for it to.</summary>
    internal void Stop() => running = false;

    public void Dispose()
    {
        running = false;

        // Waited for on purpose: once this returns, nothing is still handing blocks to whoever
        // subscribed, so a file can be closed under it.
        loop?.Join();
        loop = null;

        client.Dispose();
        started.Dispose();
    }

    private void Run()
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
                while (running)
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
            running = false;
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
                captured?.Invoke(new CapturePacket(
                    channel,
                    position,
                    MonotonicInstant.FromTicks(instant),
                    block.AsMemory(0, length),
                    !flags.HasFlag(AudioClientBufferFlags.TimestampError)));
            }
        }
    }
}
