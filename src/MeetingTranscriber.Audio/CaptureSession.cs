using MeetingTranscriber.Domain.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The two sources of one recording, open at the same time: what the machine is playing on
/// channel 0 and what the chosen microphone hears on channel 1.
/// </summary>
/// <remarks>
/// <para>
/// Both or neither. Half a meeting is not a smaller meeting — it is a recording that looks
/// complete and has lost one side of the conversation — so a session that cannot open its second
/// source closes the first one and says so.
/// </para>
/// <para>
/// Channel 0 is either everything the machine plays or one program and what it started, and that
/// is the only thing the two shapes of recording disagree about. A program that cannot be followed
/// is not a failed recording: the meeting still happened, so channel 0 falls back to the whole
/// machine and the session says it did, which is a warning about notifications and other
/// applications being in the file rather than a reason to record nothing.
/// </para>
/// </remarks>
public sealed class CaptureSession : IDisposable
{
    private readonly CaptureSource[] sources;
    private readonly SilentPlayback? silence;

    private CaptureSession(CaptureSource[] sources, SilentPlayback? silence, string? fellBack)
    {
        this.sources = sources;
        this.silence = silence;
        FellBack = fellBack;
    }

    /// <summary>Both sources, in channel order.</summary>
    public IReadOnlyList<CaptureSource> Sources => sources;

    /// <summary>
    /// Why channel 0 is not following the program it was asked to, or nothing when it is — or when
    /// no program was named.
    /// </summary>
    public string? FellBack { get; }

    /// <summary>How channel 0 was obtained, read off what it actually opened.</summary>
    public CaptureMode Mode => On(AudioChannel.Loopback).Listening is CaptureTarget.Program
        ? CaptureMode.ProcessLoopback
        : CaptureMode.FullLoopback;

    /// <summary>
    /// Opens both streams into <paramref name="folder"/>, one spool each, and starts recording.
    /// </summary>
    /// <param name="folder">Where the two spools go. Made if it is not there.</param>
    /// <param name="playback">The endpoint channel 0 listens to, and falls back to.</param>
    /// <param name="microphone">The device channel 1 listens to.</param>
    /// <param name="follow">
    /// The program channel 0 should follow, or nothing to record the whole machine.
    /// </param>
    public static CaptureSession Start(
        DirectoryInfo folder,
        AudioDevice playback,
        AudioDevice microphone,
        AudioProcess? follow = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(microphone);

        folder.Create();

        SilentPlayback? silence = null;
        var opened = new List<CaptureSource>();
        string? fellBack = null;

        CaptureSource Open(AudioChannel channel, CaptureTarget target)
        {
            // Only ever for the whole machine's loopback, and before it opens, so the endpoint is
            // already handing packets over by the time anything is listening for them. A program's
            // audio needs none of it: silence played by this process is not that program's.
            if (channel == AudioChannel.Loopback && target is CaptureTarget.Endpoint)
            {
                silence ??= SilentPlayback.On(playback);
            }

            return CaptureSource.Open(channel, target, BlockSpool.FileFor(folder, channel));
        }

        try
        {
            foreach (var (channel, target) in InChannelOrder(Others(follow, playback), microphone))
            {
                try
                {
                    opened.Add(Open(channel, target));
                }
                catch (AudioCaptureException cannot) when (target is CaptureTarget.Program)
                {
                    fellBack = cannot.Message;
                    opened.Add(Open(channel, new CaptureTarget.Endpoint(playback)));
                }
            }
        }
        catch
        {
            foreach (var source in opened)
            {
                source.Discard();
            }

            silence?.Dispose();
            throw;
        }

        return new CaptureSession([.. opened], silence, fellBack);
    }

    /// <summary>The source feeding <paramref name="channel"/>.</summary>
    public CaptureSource On(AudioChannel channel) =>
        Array.Find(sources, source => source.Channel == channel)
        ?? throw new AudioContractException($"This capture has no {channel} source.");

    /// <summary>
    /// Stops both streams and finishes both spools. Every source is asked to stop before either is
    /// waited on, and every one of them is stopped even when another has something to say about
    /// how it ended — the other one's file is a recording somebody wants either way.
    /// </summary>
    public void Stop()
    {
        var failures = new List<Exception>();

        // Both asked, then both waited on. The other way round leaves the second source recording
        // for however long the first one took to let go, which is a difference between the two
        // files that nothing in the meeting put there.
        foreach (var source in sources)
        {
            Collect(source.AskToStop, failures);
        }

        foreach (var source in sources)
        {
            Collect(source.Finish, failures);
        }

        if (failures.Count == 1)
        {
            throw failures[0] as AudioCaptureException
                ?? new AudioCaptureException(failures[0].Message, failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AudioCaptureException(
                string.Join(" ", failures.Select(failure => failure.Message)), failures[0]);
        }
    }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            // Every source, whatever the one before it did with its last block: this is where the
            // handles are let go, and Stop is where a failure is reported.
            source.LetGo();
        }

        silence?.Dispose();
    }

    /// <summary>
    /// Runs one step of stopping and keeps what it threw, so that the step after it still runs.
    /// Broad on purpose: a source left recording because another one failed is the outcome this
    /// exists to prevent, and nothing is lost — what was caught is what gets thrown.
    /// </summary>
    private static void Collect(Action step, List<Exception> failures)
    {
        try
        {
            step();
        }
        catch (Exception failed)
        {
            failures.Add(failed);
        }
    }

    /// <summary>What channel 0 was asked to listen to, before anything says whether it can.</summary>
    private static CaptureTarget Others(AudioProcess? follow, AudioDevice playback) =>
        follow is null ? new CaptureTarget.Endpoint(playback) : new CaptureTarget.Program(follow);

    /// <summary>
    /// What feeds which channel, ordered by the contract rather than by the order these two are
    /// written in, so channel 0 is opened first because it is channel 0.
    /// </summary>
    private static IEnumerable<(AudioChannel Channel, CaptureTarget Target)> InChannelOrder(
        CaptureTarget others,
        AudioDevice microphone) =>
        new[]
        {
            (Channel: AudioChannel.Microphone, Target: (CaptureTarget)new CaptureTarget.Endpoint(microphone)),
            (Channel: AudioChannel.Loopback, Target: others),
        }.OrderBy(source => CapturedAudio.IndexOf(source.Channel));
}
