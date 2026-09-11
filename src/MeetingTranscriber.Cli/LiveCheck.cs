using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Cli;

/// <summary>
/// One live run against the real provider: which files it would send, how much audio that is,
/// whether the ceiling allows it, and whether somebody at a keyboard said yes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here can reach the network, and that is the shape rather than an accident.</b> This
/// type holds every decision a run makes and owns no client; what spends is private to
/// <see cref="DeepgramCommands"/> and runs after this has said yes. So a test drives the whole of
/// the deciding — every refusal, the ceiling arithmetic, the confirmation — without a socket
/// existing anywhere in the process.
/// </para>
/// <para>
/// The keyboard is a parameter rather than the console, for the reason <see cref="WholeMachine"/>'s
/// is: this is the whole of the consent, and a gate nothing in CI can stand over is a claim resting
/// on somebody having read it. Nothing here asks whether input is redirected either — that is a
/// fact about the prompt, and it lives in the keyboard <see cref="DeepgramCommands"/> binds, so
/// that a suite driving this with a keyboard of its own is not answering a question about the
/// machine it happens to be running on.
/// </para>
/// </remarks>
public sealed class LiveCheck
{
    /// <summary>
    /// What is said before the confirmation is asked for, every run, whether or not it goes ahead.
    /// The card asks for a run to use a test account; this product deliberately keeps one answer to
    /// <em>which key does this machine use</em>, so what it can do is say where the money lands
    /// before anybody agrees to spend it.
    /// </summary>
    private const string WhereTheSpendLands =
        "the spend lands on whatever Deepgram account this machine's key belongs to. Point that "
        + "key at a test project first if that is not what you want charged: "
        + "'meeting-transcriber key --set'.";

    private LiveCheck(IReadOnlyList<LiveAudio> audio, int ceilingMinutes)
    {
        Audio = audio;
        CeilingMinutes = ceilingMinutes;
    }

    /// <summary>What this run would send, in the order it would send it.</summary>
    public IReadOnlyList<LiveAudio> Audio { get; }

    /// <summary>How much audio that is, all of it together.</summary>
    public Duration Total => Audio.Aggregate(Duration.Zero, (sum, sent) => sum + sent.Length);

    /// <summary>
    /// <see cref="Total"/> in whole minutes, rounded up.
    /// </summary>
    /// <remarks>
    /// The one number the report, the ceiling and the confirmation all talk about, so that nobody
    /// has to work out whether eight minutes and twenty-three seconds is eight minutes or nine. Up
    /// rather than to the nearest, because that is the direction that cannot understate a bill.
    /// </remarks>
    public int Minutes => (int)Math.Ceiling(Total.Milliseconds / 60_000.0);

    /// <summary>How much audio one run of this command was given leave to send.</summary>
    public int CeilingMinutes { get; }

    /// <summary>Whether this run is inside the ceiling it was given.</summary>
    public bool UnderTheCeiling => Minutes <= CeilingMinutes;

    /// <summary>
    /// What is in <paramref name="audio"/>, refused now rather than after somebody has paid for the
    /// files before the bad one.
    /// </summary>
    /// <remarks>
    /// Every file is read through <see cref="AudioFiles.Read"/>, which counts the frames rather
    /// than believing the header — so a file that is not a WAV at all is refused by the engine's
    /// own sentence, and a length here is a length there really is.
    /// </remarks>
    /// <exception cref="CommandException">
    /// There is no such folder, it holds no <c>.wav</c>, or one of the files in it is not something
    /// this application transcribes.
    /// </exception>
    public static LiveCheck Of(DirectoryInfo audio, int ceilingMinutes)
    {
        ArgumentNullException.ThrowIfNull(audio);

        audio.Refresh();
        if (!audio.Exists)
        {
            throw new CommandException($"There is no folder '{audio.FullName}' to take audio from.");
        }

        var files = audio
            .EnumerateFiles("*.wav")
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new CommandException(
                $"'{audio.FullName}' holds no .wav file, so there is nothing to send.");
        }

        return new LiveCheck([.. files.Select(Sent)], ceilingMinutes);
    }

    /// <summary>What this run is, said before anything is asked and whether or not it goes ahead.</summary>
    /// <remarks>
    /// Every file is named with the profile it would be sent under, because that is the one thing
    /// on this screen nobody typed. What the audio is gets decided from the file's own channel
    /// count — see <see cref="LiveAudio"/> — and a stereo export from somewhere else is two
    /// channels the same way a recording of this application is, so somebody pointing
    /// <c>--audio</c> at the wrong folder finds out here rather than out of a report saying a
    /// microphone channel carried nobody.
    /// </remarks>
    public void Say(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (var sent in Audio)
        {
            Report.Line(
                output,
                "will send",
                $"{sent.File.Name} ({Report.Offset(sent.Length)}, {sent.Profile.ToWireName()})");
        }

        Report.Line(output, "files", $"{Audio.Count}");
        Report.Line(output, "audio", $"{Report.Offset(Total)} ({Minutes} minute(s))");
        Report.Line(output, "ceiling", $"{CeilingMinutes} minute(s)");
        Report.Line(output, "account", WhereTheSpendLands);
    }

    /// <summary>
    /// Asks once, and answers whether somebody agreed to this run.
    /// </summary>
    /// <remarks>
    /// The number typed back, and not <c>y</c>. What somebody is agreeing to is a quantity, so
    /// agreeing to it means saying it — a stray keystroke on a prompt that was waiting for one
    /// cannot then spend their money, and somebody who read the wrong line types the wrong number.
    /// </remarks>
    /// <param name="output">Where the question goes.</param>
    /// <param name="typed">What somebody typed, or nothing at all when nobody is there.</param>
    public bool Confirmed(TextWriter output, Func<string?> typed)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(typed);

        Report.Line(
            output,
            "confirm",
            $"type {Minutes} and press Enter to send {Minutes} minute(s) of audio to Deepgram. "
            + "Anything else sends nothing.");

        return typed()?.Trim() == Minutes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One file, refused by name where it is not something this application transcribes.</summary>
    private static LiveAudio Sent(FileInfo file)
    {
        var read = AudioFiles.Read(file);

        var profile = read.Format.Channels switch
        {
            2 => SourceProfile.Multichannel,
            AudioFiles.OneTrack => SourceProfile.Diarize,
            var channels => throw new CommandException(
                $"'{file.Name}' has {channels} channels. This application transcribes two — the "
                + "loopback and the microphone — or a single track, and nothing else."),
        };

        return read.Length > Duration.Zero
            ? new LiveAudio(file, profile, read.Length)
            : throw new CommandException(
                $"'{file.Name}' holds no audio at all. Deepgram would refuse it after the files "
                + "before it had already been paid for.");
    }
}
