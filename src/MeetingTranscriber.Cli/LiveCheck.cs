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

    /// <summary>What a response is called, as a name ends.</summary>
    private const string ResponseExtension = ".json";

    /// <summary>
    /// How a run stamps every response it writes.
    /// </summary>
    /// <remarks>
    /// Compact because <c>UtcTimestamp.ToString</c> writes colons, which is not a file name Windows
    /// will take. It is read back as well as written — see <see cref="AnswersAlready"/> — so it is
    /// spelled once here rather than at each end.
    /// </remarks>
    private const string RunStamp = "yyyyMMdd'T'HHmmss'Z'";

    private LiveCheck(
        IReadOnlyList<LiveAudio> audio, IReadOnlyList<FileInfo> answered, int ceilingMinutes)
    {
        Audio = audio;
        Answered = answered;
        CeilingMinutes = ceilingMinutes;
    }

    /// <summary>What this run would send, in the order it would send it.</summary>
    public IReadOnlyList<LiveAudio> Audio { get; }

    /// <summary>
    /// What this run leaves out because the folder it writes into already holds its response, in
    /// name order.
    /// </summary>
    /// <remarks>
    /// Said on its own line by <see cref="Say"/> and never only subtracted. Files go in name order
    /// and the first failure ends the run, so an eight-file run that died on the sixth is re-run to
    /// finish it — and a run that quietly sent fewer files than the folder holds would be worse
    /// than one that re-bought them, because nobody could tell the two apart from the report.
    /// </remarks>
    public IReadOnlyList<FileInfo> Answered { get; }

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
    /// What is in <paramref name="audio"/> and is not already answered in <paramref name="into"/>,
    /// refused now rather than after somebody has paid for the files before the bad one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every file this would send is read through <see cref="AudioFiles.Read"/>, which counts the
    /// frames rather than believing the header — so a file that is not a WAV at all is refused by
    /// the engine's own sentence, and a length here is a length there really is.
    /// </para>
    /// <para>
    /// <b><paramref name="into"/> is read here and not only written to later, and that is what
    /// makes a run resumable.</b> The ceiling is spent against the audio that will actually be
    /// sent, so the minutes said, the minutes checked against the ceiling and the minutes typed
    /// back are one number — an eight-file run that died on the sixth used to be re-offered at the
    /// same total and re-buy five files. A file already answered is not read at all: it is not
    /// going to be sent, and a full read of it would be minutes of disk for a number nobody uses.
    /// </para>
    /// <para>
    /// <b>What is in <paramref name="into"/> is a ledger nothing here locks.</b> Two runs pointed
    /// at one folder would both see nothing answered, both pass the ceiling and both buy every
    /// file, and the run stamps would keep the files from colliding, which is exactly what would
    /// hide it. What makes that a ledger one run at a time is <see cref="SendingMark"/>, and
    /// <b>this does not take it and does not require it</b>: the claim is a whole run's and a run
    /// is <see cref="DeepgramCommands"/>'s, which takes it before it reaches here. So what this
    /// answers is what the folder says, and whether anything may act on that answer is settled one
    /// layer up. A second caller that read this and then sent, holding nothing, would be back where
    /// the hazard was — which is a reason for there to go on being one place that sends, and not a
    /// property this method has.
    /// </para>
    /// <para>
    /// <b>What the claim does not cover is two folders holding one audio folder's responses.</b>
    /// The question this asks is what <em>this</em> folder already answers, and a run pointed
    /// somewhere else is a ledger of its own with nothing in it — so two runs over one
    /// <c>--audio</c> and two different <c>--out</c>s both buy everything and neither is told. That
    /// is somebody choosing two folders rather than a claim failing to hold, and nothing here can
    /// see it, but it is the hazard that is left and it is said here rather than left to be found.
    /// </para>
    /// </remarks>
    /// <exception cref="CommandException">
    /// Either folder is not there, <paramref name="audio"/> holds no <c>.wav</c>, or one of the
    /// files this would send is not something this application transcribes.
    /// </exception>
    public static LiveCheck Of(DirectoryInfo audio, DirectoryInfo into, int ceilingMinutes)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(into);

        audio.Refresh();
        if (!audio.Exists)
        {
            throw new CommandException($"There is no folder '{audio.FullName}' to take audio from.");
        }

        // Both folders, now this owns both. The command refuses a missing `--out` first and in its
        // own words, before the frame count that costs minutes; what this stops is the caller that
        // does not, meeting a `DirectoryNotFoundException` where every other way out of here is a
        // sentence.
        into.Refresh();
        if (!into.Exists)
        {
            throw new CommandException(
                $"There is no folder '{into.FullName}' for the responses to land in.");
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

        var responses = into.EnumerateFiles("*" + ResponseExtension).Select(file => file.Name).ToArray();
        var sending = new List<LiveAudio>();
        var answered = new List<FileInfo>();

        foreach (var file in files)
        {
            if (AnswersAlready(responses, Path.GetFileNameWithoutExtension(file.Name)))
            {
                answered.Add(file);
                continue;
            }

            sending.Add(Sent(file));
        }

        return new LiveCheck(sending, answered, ceilingMinutes);
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

        // Beside the ones that will be sent and never instead of them, because the difference
        // between a folder of eight and a run of three is the whole of what somebody is agreeing
        // to, and a run that silently sent fewer files than the folder held would read exactly like
        // one that had nothing left to do.
        foreach (var already in Answered)
        {
            Report.Line(
                output,
                "already",
                $"{already.Name} — the folder responses land in already holds one for it, so it is "
                + "not sent again.");
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

    /// <summary>
    /// How a run stamps every response it writes, so that files straddling a second still sort and
    /// group together.
    /// </summary>
    public static string StampOf(UtcTimestamp when) =>
        when.Value.ToString(RunStamp, CultureInfo.InvariantCulture);

    /// <summary>
    /// What the response to <paramref name="sent"/> is called in a run stamped
    /// <paramref name="stamp"/>.
    /// </summary>
    /// <remarks>
    /// Written by <see cref="DeepgramCommands"/> and read back by <see cref="Of"/>, which is why it
    /// is here and not a literal at each end: two spellings of one name would mean a resumed run
    /// re-buying every file the moment either changed.
    /// </remarks>
    public static string ResponseNamed(FileInfo sent, string stamp)
    {
        ArgumentNullException.ThrowIfNull(sent);

        return $"{Path.GetFileNameWithoutExtension(sent.Name)}-{stamp}{ResponseExtension}";
    }

    /// <summary>
    /// Whether one of <paramref name="responses"/> is a response some run already wrote for
    /// <paramref name="stem"/>.
    /// </summary>
    /// <remarks>
    /// The stamp is read back and not only the prefix. A folder may hold <c>a.wav</c> and
    /// <c>a-b.wav</c> at once, and <c>a-b</c>'s response begins <c>a-</c>; a rule that stopped at
    /// the prefix would leave <c>a.wav</c> out of a run that had never sent it, which is the one
    /// way this could cost somebody a file rather than save them one.
    /// <para>
    /// Case is ignored, because Windows decides case for itself and the two ends of this rule are a
    /// name this program wrote and a name the file system handed back.
    /// </para>
    /// </remarks>
    private static bool AnswersAlready(IReadOnlyList<string> responses, string stem)
    {
        var prefix = stem + "-";

        return responses.Any(name =>
            name.Length > prefix.Length + ResponseExtension.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && DateTime.TryParseExact(
                name[prefix.Length..^ResponseExtension.Length],
                RunStamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _));
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
