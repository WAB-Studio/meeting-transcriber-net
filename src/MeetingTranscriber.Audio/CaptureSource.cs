using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One device being recorded onto one channel: the stream, the file it lands in and how loud it
/// has been. Nothing here knows there is another one.
/// </summary>
/// <remarks>
/// What it writes is the device's own format, unconverted and unaligned with anything else. That
/// is the point of it — the two sources arriving as they really are is what the timeline then has
/// to reconcile, and a resample done here would hide the very thing being measured.
/// </remarks>
public sealed class CaptureSource : IDisposable
{
    private static readonly TimeSpan StopsWithin = TimeSpan.FromSeconds(5);

    private readonly MMDevice endpoint;
    private readonly WasapiStream stream;
    private readonly WaveFileWriter writer;
    private readonly SourceMeter meter = new();
    private readonly PacketTally tally;
    private readonly ManualResetEventSlim ended = new(initialState: false);
    private long bytes;
    private Exception? failure;
    private bool running;

    private CaptureSource(
        AudioChannel channel,
        AudioDevice device,
        StreamFormat format,
        FileInfo file,
        MMDevice endpoint,
        WasapiStream stream,
        WaveFileWriter writer)
    {
        Channel = channel;
        Device = device;
        Format = format;
        File = file;
        this.endpoint = endpoint;
        this.stream = stream;
        this.writer = writer;
        tally = new PacketTally(format);
    }

    /// <summary>Which of the two channels this device feeds.</summary>
    public AudioChannel Channel { get; }

    /// <summary>The device Windows opened for it.</summary>
    public AudioDevice Device { get; }

    /// <summary>The format that device handed over.</summary>
    public StreamFormat Format { get; }

    /// <summary>Where its samples are being written.</summary>
    public FileInfo File { get; }

    /// <summary>When its stream opened.</summary>
    public UtcTimestamp StartedAt { get; private set; }

    /// <summary>
    /// What this source's packets add up to: the stretch of the meeting they cover, how much of it
    /// never arrived, and how the device's own clock behaved while they did.
    /// </summary>
    public PacketTally Packets => tally;

    /// <summary>How many bytes have been written to <see cref="File"/>.</summary>
    public long Bytes => Interlocked.Read(ref bytes);

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
    /// Waits for the stream to be over and finishes the file. A stream that had already ended by
    /// itself throws here, carrying the reason it ended.
    /// </summary>
    internal void Finish()
    {
        if (!ended.Wait(StopsWithin))
        {
            throw new AudioCaptureException(
                $"The {Channel} stream did not stop within {StopsWithin.TotalSeconds:0} seconds.");
        }

        writer.Flush();

        if (failure is not null)
        {
            throw new AudioCaptureException(
                $"The {Channel} stream on '{Device.Name}' ended by itself: {failure.Message}", failure);
        }
    }

    public void Dispose()
    {
        // First, and on purpose: this stops the stream and waits for its loop, so once it returns
        // nothing is still handing blocks to the writer.
        stream.Dispose();

        // What patches the WAV header with the length actually written, which is why a capture
        // that was killed still leaves a file something can play.
        writer.Dispose();
        endpoint.Dispose();
        ended.Dispose();
        running = false;
    }

    /// <summary>
    /// Closes a source that never became part of a recording and takes its file with it. What it
    /// leaves behind would otherwise be a file with nothing in it, standing exactly where the
    /// next attempt wants to write — so a capture that failed once would go on failing for a
    /// reason that is no longer the reason.
    /// </summary>
    /// <remarks>
    /// Nothing here throws. It runs while a session is already failing, and what the caller has
    /// to hear is why that happened, not that a handle would not close on the way out.
    /// </remarks>
    internal void Discard()
    {
        LetGo();
        Erase(File);
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
    /// Opens <paramref name="device"/> onto <paramref name="channel"/> and starts recording it.
    /// Anything the machine refuses comes back as a throw, with nothing left open and no file
    /// left behind.
    /// </summary>
    internal static CaptureSource Open(AudioChannel channel, AudioDevice device, FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(file);

        var endpoint = AudioDevices.Open(device);
        WasapiStream? stream = null;
        WaveFileWriter? writer = null;
        var claimed = false;

        void LetGo()
        {
            writer?.Dispose();
            stream?.Dispose();
            endpoint.Dispose();

            if (claimed)
            {
                Erase(file);
            }
        }

        try
        {
            stream = WasapiStream.On(endpoint, channel);
            var format = StreamFormat.Of(stream.WaveFormat);

            // Asked before the device is started rather than answered by its first block: a width
            // nothing here can read is knowable now, and a capture that dies a second in is one
            // somebody has already started holding a meeting into.
            Levels.EnsureMeterable(format);

            var bytes = Claim(file);
            claimed = true;
            writer = new WaveFileWriter(bytes, stream.WaveFormat);

            var source = new CaptureSource(channel, device, format, file, endpoint, stream, writer);
            source.Start();
            return source;
        }
        catch (COMException refused)
        {
            LetGo();
            throw new AudioCaptureException(
                $"Windows would not record '{device.Name}': {refused.Message}", refused);
        }
        catch
        {
            LetGo();
            throw;
        }
    }

    /// <summary>
    /// Takes the path for this recording, or says it is taken. The file system answers rather
    /// than a question asked a moment earlier: NAudio opens a path by truncating whatever is
    /// there, so a check and then a create is a window in which the other capture's WAV is the
    /// thing being truncated.
    /// </summary>
    private static FileStream Claim(FileInfo file)
    {
        try
        {
            return new FileStream(file.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException taken) when (System.IO.File.Exists(file.FullName))
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' is already there, and a recording is not written over another one.", taken);
        }
    }

    /// <summary>
    /// Removes the file of an attempt that never became a recording. A file that will not delete
    /// is not worth replacing the reason the attempt failed with, so it does not throw either.
    /// </summary>
    private static void Erase(FileInfo file)
    {
        try
        {
            file.Refresh();
            if (file.Exists)
            {
                file.Delete();
            }
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see the summary.
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
        writer.Write(packet.Samples.Span);
        Interlocked.Add(ref bytes, packet.Samples.Length);
    }

    private void Ended(Exception? stopped)
    {
        failure = stopped;
        ended.Set();
    }
}
