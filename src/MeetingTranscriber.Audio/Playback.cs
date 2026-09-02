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
/// can: there is no endpoint on one. What <em>is</em> provable is the arithmetic, and it is two
/// things — <see cref="Within"/>, where a seek somebody asked for actually lands, and
/// <see cref="BothSidesInBothEars"/>, what the two channels become before an endpoint hears them.
/// Both are public for that reason and no other: they are what a device is about to be asked to
/// do, worked out before there is a device. Everything else in this type is one call to one.
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
            through.Init(BothSidesInBothEars(wav.ToSampleProvider()));
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
    /// on a single earbud hears one side of the conversation. Folded half each, which is the
    /// average <see cref="Samples.ToMono"/> takes of the two-channel PCM16 this corpus stores, and
    /// for the same reason: whichever side spoke, it is heard.
    /// <para>
    /// Both weights are written here rather than left to the provider's own, and that is why this
    /// is not one line. The weights decide what every listener hears, and a package's default is
    /// not this repository's to state: the pin in <c>Directory.Packages.props</c> means a bump is a
    /// change somebody makes here, but nothing about that change would say a default underneath it
    /// had moved. Written down, the mix is this file's, and
    /// <c>PlaybackTests.The_two_sides_of_a_meeting_are_folded_half_each</c> is what goes red if
    /// either the line or the package moves it.
    /// </para>
    /// <para>
    /// Public, and taking what it folds rather than the file it came out of, for that test alone:
    /// everything else in <see cref="Of"/> is a call to an endpoint, and this is the one part of
    /// the path to one that is arithmetic. <see cref="Within"/> is public for the same reason.
    /// </para>
    /// <para>
    /// Only where there are exactly two. One track is what audio somebody brought in from outside
    /// becomes before it is a meeting's, and a fold over it would do nothing but stand between the
    /// file and the device; anything wider than two is not a shape this corpus stores, and the
    /// provider refuses it rather than guessing which pair of channels was the conversation.
    /// </para>
    /// </remarks>
    /// <param name="samples">The recording, as the samples it plays.</param>
    public static ISampleProvider BothSidesInBothEars(ISampleProvider samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.WaveFormat.Channels != CapturedAudio.ChannelCount)
        {
            return samples;
        }

        return new StereoToMonoSampleProvider(samples)
        {
            LeftVolume = 0.5f,
            RightVolume = 0.5f,
        };
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
