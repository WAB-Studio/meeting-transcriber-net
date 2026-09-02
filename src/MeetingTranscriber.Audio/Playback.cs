using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingTranscriber.Audio;

/// <summary>
/// A recording being played back through whatever this machine plays through.
/// </summary>
/// <remarks>
/// <para>
/// It is here and not beside a window on purpose, and the reasoning is the one that put capture
/// here: an endpoint, a format and a stream are the same kind of thing whichever way the bytes are
/// going, and a second place in this repository that knows what WASAPI is would be a second place
/// to fix when one of them is wrong. <c>docs/layout.md</c>'s rule about
/// <c>MeetingTranscriber.Presentation</c> is about what a person <em>reads</em> and about UI that
/// has to be provable; this is neither. What the screen decides about a player — whether it is
/// offered at all, where the marks along it go — is <c>MeetingScreen</c>'s and lives where a build
/// agent can run it.
/// </para>
/// <para>
/// Nothing here can be run by a build agent, for the same reason nothing that opens a microphone
/// can: there is no endpoint on one. What <em>is</em> provable is the arithmetic, and it is
/// <see cref="Within"/> — where a seek somebody asked for actually lands. Everything else in this
/// type is one call to a device.
/// </para>
/// <para>
/// It plays a file and knows nothing else about the meeting it came from. That is the whole of
/// what makes hearing a recording free: there is no stage, no job, no transcription and no price
/// in reach of this type, so there is nothing here that could come to depend on one.
/// </para>
/// </remarks>
public sealed class Playback : IDisposable
{
    private readonly WaveFileReader _wav;
    private readonly WasapiOut _out;

    private Playback(WaveFileReader wav, WasapiOut through)
    {
        _wav = wav;
        _out = through;
        _out.PlaybackStopped += (_, stopped) => WhatStoppedIt ??= stopped.Exception;
    }

    /// <summary>
    /// What went wrong on the thread pushing the audio, when something did.
    /// </summary>
    /// <remarks>
    /// The endpoint reads out of the file on a thread of its own, so a device pulled out mid
    /// meeting, or anything else that ends the playback, surfaces there and nowhere a caller is
    /// standing. Held rather than raised as an event: whoever is drawing the player is already
    /// reading this object several times a second to move the track, and a second mechanism for
    /// the one thing it has to notice would be a second thing to remember to hook up.
    /// </remarks>
    public Exception? WhatStoppedIt { get; private set; }

    /// <summary>How long the recording is.</summary>
    public Duration Length => Duration.FromTimeSpan(_wav.TotalTime);

    /// <summary>How far into it the playback has got.</summary>
    public Duration At => Duration.FromTimeSpan(_wav.CurrentTime);

    /// <summary>Whether sound is coming out of it right now.</summary>
    public bool IsPlaying => _out.PlaybackState == PlaybackState.Playing;

    /// <summary>Whether there is nothing left of the recording to play.</summary>
    public bool HasReachedTheEnd => _wav.Position >= _wav.Length;

    /// <summary>
    /// Opens a recording for playing, without starting it.
    /// </summary>
    /// <remarks>
    /// The file is opened before the device, and that ordering is what makes the two failures tell
    /// each other apart: a meeting whose audio is not there or is not a WAV fails naming the file,
    /// where a machine with nothing to play through fails naming the machine. Reversed, a corpus
    /// with a missing recording would report a sound problem.
    /// </remarks>
    /// <exception cref="AudioCaptureException">
    /// The file is not there, is not a WAV, or this machine would not open an endpoint to play it
    /// through.
    /// </exception>
    public static Playback Of(FileInfo recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var wav = AudioFiles.Open(recording);

        try
        {
            var through = new WasapiOut();
            through.Init(BothSidesInBothEars(wav));
            return new Playback(wav, through);
        }
        catch (Exception refused)
        {
            // Everything, and not a list of types. What is behind this call is COM and then a
            // driver, and there is no enumerable set of what a driver raises — a list would be a
            // guess nothing here has ever run, and the type it left out would come out of a click
            // handler as a crash rather than as a line on the screen.
            wav.Dispose();
            throw new AudioCaptureException(
                $"This machine would not play '{recording.FullName}': {refused.Message}");
        }
    }

    /// <summary>
    /// What actually goes to the endpoint: a recording's two channels folded into one.
    /// </summary>
    /// <remarks>
    /// A meeting's two channels are two sources and not a stereo image — channel 0 is what the
    /// machine played and channel 1 is the microphone — so handing the file straight to an
    /// endpoint puts everybody else in one ear and the user in the other, and somebody listening
    /// on a single earbud hears one side of the conversation. Folded by averaging, which is what
    /// <see cref="Samples.ToMono"/> already does everywhere else in this project and for the same
    /// reason: whichever side spoke, it is heard.
    /// <para>
    /// Only where there are two. Audio somebody brought in from outside enters as one track, and a
    /// fold over one channel is a fold that does nothing but stand between the file and the device.
    /// </para>
    /// </remarks>
    private static ISampleProvider BothSidesInBothEars(WaveFileReader wav)
    {
        var samples = wav.ToSampleProvider();

        return samples.WaveFormat.Channels == CapturedAudio.ChannelCount
            ? new StereoToMonoSampleProvider(samples)
            : samples;
    }

    /// <summary>
    /// Where a seek to <paramref name="wanted"/> actually lands in a recording
    /// <paramref name="length"/> long.
    /// </summary>
    /// <remarks>
    /// A track is a strip of pixels and a press lands on one of them, so the far end is reachable
    /// by ordinary use rather than by a caller getting it wrong: dragging to it asks for an offset
    /// the file has no frame at. Clamped rather than refused for that reason — somebody dragging
    /// to the end means the end.
    /// <para>
    /// One end and not two, because a <see cref="Duration"/> refuses to be negative where it is
    /// made. There is no seek before the start to clamp: asking for one is not a value this can be
    /// handed, and a branch here would be one nothing could ever reach.
    /// </para>
    /// </remarks>
    public static Duration Within(Duration wanted, Duration length) =>
        wanted > length ? length : wanted;

    /// <summary>
    /// Starts it, or carries on from wherever it was paused.
    /// </summary>
    /// <remarks>
    /// A recording that ran to its end starts again from the beginning. Left where it was, the
    /// endpoint would be handed a stream with nothing in it and stop immediately: a play button
    /// that flickers and makes no sound, on the one path every meeting somebody listens to the end
    /// of reaches.
    /// </remarks>
    public void Play()
    {
        if (HasReachedTheEnd)
        {
            _wav.Position = 0;
        }

        _out.Play();
    }

    /// <summary>Stops the sound and leaves it where it is.</summary>
    public void Pause() => _out.Pause();

    /// <summary>
    /// Moves to a point in the recording, whether or not it is playing.
    /// </summary>
    /// <remarks>
    /// Whether or not, because that is what a citation is: pressing one on a meeting nobody has
    /// started playing has to put the player at that moment and leave it there, and pressing one
    /// while it plays has to carry on from the new place.
    /// </remarks>
    public void Seek(Duration to) => _wav.CurrentTime = Within(to, Length).ToTimeSpan();

    /// <summary>
    /// Lets go of the endpoint and the file.
    /// </summary>
    /// <remarks>
    /// The device first. It reads out of the file on a thread of its own, so disposing the reader
    /// under a player still running is a read off a closed stream — which surfaces as a crash on
    /// the audio thread rather than as anything a screen could report.
    /// </remarks>
    public void Dispose()
    {
        _out.Dispose();
        _wav.Dispose();
    }
}
