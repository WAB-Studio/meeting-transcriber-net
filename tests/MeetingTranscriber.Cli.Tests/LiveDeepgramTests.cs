using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// <c>deepgram-live</c>, which is the one command that spends money — proved without spending any.
/// </summary>
/// <remarks>
/// <para>
/// Two ways in, and which one a test takes is a rule rather than a preference.
/// <see cref="CommandLine.Of"/> drives the table's own binding, which is the console and this
/// machine's key — so it is used only where the command refuses before
/// <see cref="LiveCheck.Confirmed"/> is reached, and every one of those refusals is in argument
/// parsing or in <see cref="LiveCheck.Of"/>. Everything past that point drives the four-argument
/// <see cref="DeepgramCommands.Live(Arguments, TextWriter, Func{string}, Sending)"/> with a
/// keyboard and a <see cref="Sending"/> of its own.
/// </para>
/// <para>
/// That second overload is what makes the card's promise a property of the code and not of this
/// file: a test can type the number back, and what it confirms is a <see cref="Sending"/> it wrote,
/// which reads no key and holds no client. The one implementation that does either is private to
/// <see cref="DeepgramCommands"/>. So no test in this suite can reach a console — none can hang on
/// one — and none can spend.
/// </para>
/// <para>
/// The half that only a real call would otherwise exercise — what a response has to hold — is
/// <see cref="LiveInvariants"/>'s, and it is proved in <c>MeetingTranscriber.Processing.Tests</c>
/// against <c>tests/fixtures/deepgram/</c>. It is not here because the rule is not here:
/// <c>docs/layout.md</c> says this project holds no rule of its own, and what this file is about is
/// the argument reading, the ceiling, the confirmation and the exit code.
/// </para>
/// </remarks>
public sealed class LiveDeepgramTests : IDisposable
{
    /// <summary>
    /// A hundred frames a second. What is under test is a length and the arithmetic over it, and a
    /// realistic rate would make a four-minute file eight megabytes for nothing.
    /// </summary>
    private const int Rate = 100;

    private readonly DirectoryInfo audio = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    private readonly DirectoryInfo into = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public LiveDeepgramTests()
    {
        audio.Create();
        into.Create();
    }

    public void Dispose()
    {
        Erase(audio);
        Erase(into);
    }

    [Fact]
    public void A_run_with_no_ceiling_is_refused()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);

        var run = CommandLine.Of(
            "deepgram-live", "--audio", audio.FullName, "--out", into.FullName);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--ceiling-minutes");
    }

    /// <summary>
    /// The ceiling is over the run and not over a file, which is the difference between one call
    /// somebody agreed to and eight of them.
    /// </summary>
    [Fact]
    public void More_audio_than_the_ceiling_is_refused_before_anything_is_sent()
    {
        Wav("one.wav", seconds: 240, 0.4f, 0.2f);
        Wav("two.wav", seconds: 240, 0.4f, 0.2f);

        var run = Line("--ceiling-minutes", "5");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("8");
        run.Error.ShouldContain("5");
        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// Refused by name, before the run, rather than by the provider after the files before it have
    /// been paid for. The profile is the file's own channel count and never a flag.
    /// </summary>
    [Fact]
    public void A_file_this_application_cannot_transcribe_is_refused_naming_it()
    {
        Wav("array.wav", seconds: 10, 0.4f, 0.3f, 0.2f, 0.1f, 0.05f, 0.02f);
        Wav("meeting.wav", seconds: 10, 0.4f, 0.2f);

        var run = Line("--ceiling-minutes", "5");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("array.wav");
        run.Error.ShouldContain("6");
        into.EnumerateFiles().ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_with_no_audio_in_it_is_refused()
    {
        var run = Line("--ceiling-minutes", "5");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain(audio.FullName);
    }

    /// <summary>
    /// Every file is looked at and not only the total, because a run of eight good minutes and one
    /// empty file is a run that pays for the eight and is then refused.
    /// </summary>
    [Fact]
    public void A_wav_with_no_audio_in_it_is_refused_naming_it()
    {
        ForeignWav.Steady(new FileInfo(Path.Combine(audio.FullName, "empty.wav")), Rate, 0, 0.2f, 0.1f);
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);

        var run = Line("--ceiling-minutes", "5");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("empty.wav");
        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// The card's "the automatic suite can never trigger it", proved: a keyboard nobody is at sends
    /// nothing, and sending nothing because nobody agreed is the command working.
    /// </summary>
    [Fact]
    public void Nobody_at_the_keyboard_sends_nothing_and_is_not_a_failure()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);
        using var output = new StringWriter();
        var asked = 0;

        var code = DeepgramCommands.Live(
            Typed("--ceiling-minutes", "5"),
            output,
            () => null,
            (_, _, _, _) => ++asked);

        code.ShouldBe(Cli.Ok);
        asked.ShouldBe(0);
        output.ToString().ShouldContain("not sent");
        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// And the other half of the same rule: typing the number back is what sends, so the gate is
    /// the confirmation and nothing else.
    /// </summary>
    /// <remarks>
    /// It is safe to run because what a confirmed run reaches is the <see cref="Sending"/> handed
    /// in, and this one holds no client and reads no key. That is the whole reason the parameter
    /// exists: with the keyboard alone, this test would be a charge on somebody's account.
    /// </remarks>
    [Fact]
    public void Typing_the_number_back_is_what_sends()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);
        using var output = new StringWriter();
        LiveCheck? sent = null;

        var code = DeepgramCommands.Live(
            Typed("--ceiling-minutes", "5"),
            output,
            () => "1",
            (run, _, language, _) =>
            {
                sent = run;
                language.ShouldBe("es");
                return Cli.Ok;
            });

        code.ShouldBe(Cli.Ok);
        sent.ShouldNotBeNull();
        sent!.Audio.Select(one => one.File.Name).ShouldBe(["meeting.wav"]);
        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// What somebody is agreeing to is a quantity, so agreeing to it means saying it. A keystroke
    /// on a prompt that happened to be waiting cannot then spend their money.
    /// </summary>
    [Fact]
    public void Confirming_is_typing_back_the_minutes()
    {
        Wav("meeting.wav", seconds: 480, 0.4f, 0.2f);
        var run = LiveCheck.Of(audio, into, ceilingMinutes: 9);

        using var yes = new StringWriter();
        run.Confirmed(yes, () => "y").ShouldBeFalse();

        using var wrong = new StringWriter();
        run.Confirmed(wrong, () => "9").ShouldBeFalse();

        using var nobody = new StringWriter();
        run.Confirmed(nobody, () => null).ShouldBeFalse();

        using var typed = new StringWriter();
        run.Confirmed(typed, () => " 8 ").ShouldBeTrue();

        yes.ToString().ShouldNotContain("sent");
        wrong.ToString().ShouldNotContain("sent");
        nobody.ToString().ShouldNotContain("sent");
    }

    /// <summary>
    /// One number does the report, the ceiling and the confirmation, so nobody has to work out
    /// whether eight minutes and twenty-three seconds is eight minutes or nine.
    /// </summary>
    [Fact]
    public void The_confirmation_and_the_ceiling_talk_about_one_number()
    {
        Wav("meeting.wav", seconds: 503, 0.4f, 0.2f);
        using var output = new StringWriter();

        var run = LiveCheck.Of(audio, into, ceilingMinutes: 9);
        run.Say(output);
        run.Confirmed(output, () => null);

        run.Minutes.ShouldBe(9);
        output.ToString().ShouldContain("0:08:23 (9 minute(s))");
        output.ToString().ShouldContain("type 9");
        run.UnderTheCeiling.ShouldBeTrue();
        LiveCheck.Of(audio, into, ceilingMinutes: 8).UnderTheCeiling.ShouldBeFalse();
    }

    /// <summary>
    /// Two runs over one folder send the same files in the same order, so what a second run costs
    /// is something somebody can work out from what the first one did.
    /// </summary>
    [Fact]
    public void What_a_run_would_send_is_the_folder_in_name_order()
    {
        Wav("c.wav", seconds: 10, 0.4f, 0.2f);
        Wav("a.wav", seconds: 10, 0.4f, 0.2f);
        Wav("b.wav", seconds: 10, 0.3f);

        var run = LiveCheck.Of(audio, into, ceilingMinutes: 5);

        run.Audio.Select(sent => sent.File.Name).ShouldBe(["a.wav", "b.wav", "c.wav"]);
        run.Audio.Select(sent => sent.Profile).ShouldBe(
            [SourceProfile.Multichannel, SourceProfile.Diarize, SourceProfile.Multichannel]);
    }

    /// <summary>
    /// Everything a person needs to decide with is on the screen before the question is asked —
    /// including where the money lands, which is what this product has instead of a second key for
    /// a test account.
    /// </summary>
    [Fact]
    public void Nothing_is_asked_before_the_run_is_described()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);
        using var output = new StringWriter();
        var run = LiveCheck.Of(audio, into, ceilingMinutes: 5);

        run.Say(output);
        Should.Throw<InvalidOperationException>(() => run.Confirmed(output, Nobody));

        var said = output.ToString();
        said.ShouldContain("files");
        said.ShouldContain("audio");
        said.ShouldContain("ceiling");
        said.ShouldContain("Deepgram account");
        said.ShouldContain("key --set");

        // Named with the profile it would be sent under, because that is the one thing on this
        // screen nobody typed and the one a wrong folder shows up as.
        said.ShouldContain("meeting.wav (0:01:00, multichannel)");

        // Written before the keyboard was asked, which is the property: the numbers are on screen
        // by the time anybody can answer.
        said.ShouldContain("confirm");
    }

    /// <summary>
    /// Files go in name order and the first failure ends the run, so an eight-file run that died on
    /// the sixth is re-run to finish it. Nothing skipped a stem whose response was already in
    /// <c>--out</c>, and the ceiling was recomputed over the whole <c>--audio</c> folder — so the
    /// second run was offered at the same total and re-bought five files.
    /// </summary>
    /// <remarks>
    /// The ceiling is the sharp half: eight minutes over a five-minute ceiling is refused, and the
    /// same folder with five of those minutes already answered is three minutes and goes through.
    /// Red when <c>Of</c> stops reading <c>--out</c>, and red when the skipped files are subtracted
    /// from the total but still sent.
    /// </remarks>
    [Fact]
    public void A_file_whose_response_is_already_there_is_not_sent_or_paid_for_again()
    {
        Wav("a.wav", seconds: 300, 0.4f, 0.2f);
        Wav("b.wav", seconds: 180, 0.4f, 0.2f);
        Answered("a.wav");

        using var output = new StringWriter();
        var run = LiveCheck.Of(audio, into, ceilingMinutes: 5);
        run.Say(output);

        run.Audio.Select(sent => sent.File.Name).ShouldBe(["b.wav"]);
        run.Answered.Select(file => file.Name).ShouldBe(["a.wav"]);
        run.Minutes.ShouldBe(3);
        run.UnderTheCeiling.ShouldBeTrue();

        // Said on its own line and never only subtracted: a run that quietly sent fewer files than
        // the folder holds would read exactly like one with nothing left to do.
        output.ToString().ShouldContain("already");
        output.ToString().ShouldContain("a.wav");
        output.ToString().ShouldContain("will send");
        output.ToString().ShouldContain("b.wav");
    }

    /// <summary>
    /// The one way this could cost somebody a file rather than save them one. A folder holding
    /// <c>a.wav</c> and <c>a-b.wav</c> has an <c>a-b</c> response that begins <c>a-</c>, so a rule
    /// stopping at the prefix would leave <c>a.wav</c> out of a run that had never sent it.
    /// </summary>
    [Fact]
    public void A_response_to_another_file_whose_name_starts_the_same_is_not_mistaken_for_one()
    {
        Wav("a.wav", seconds: 60, 0.4f, 0.2f);
        Wav("a-b.wav", seconds: 60, 0.4f, 0.2f);
        Answered("a-b.wav");

        var run = LiveCheck.Of(audio, into, ceilingMinutes: 5);

        run.Audio.Select(sent => sent.File.Name).ShouldBe(["a.wav"]);
        run.Answered.Select(file => file.Name).ShouldBe(["a-b.wav"]);
    }

    /// <summary>
    /// A folder that is finished is somebody re-running a command they already ran, which is not a
    /// failure and is not a confirmation to ask for either — the alternative is a prompt saying
    /// "type 0 and press Enter to send 0 minute(s)".
    /// </summary>
    [Fact]
    public void A_folder_that_is_already_answered_sends_nothing_and_asks_nobody()
    {
        Wav("a.wav", seconds: 60, 0.4f, 0.2f);
        Answered("a.wav");
        using var output = new StringWriter();

        var code = DeepgramCommands.Live(
            Typed("--ceiling-minutes", "5"),
            output,
            Nobody,
            (_, _, _, _) => throw new InvalidOperationException("Nothing was left to send."));

        code.ShouldBe(Cli.Ok);
        output.ToString().ShouldContain("not sent");
        output.ToString().ShouldContain("a.wav");
    }

    /// <summary>A keyboard nobody is at, for the paths that must never reach one.</summary>
    private static string? Nobody() =>
        throw new InvalidOperationException("Nothing on this path may ask anybody to confirm.");

    /// <summary>
    /// The response an earlier run left for <paramref name="wav"/>, named exactly as that run would
    /// have named it.
    /// </summary>
    private void Answered(string wav) => File.WriteAllText(
        Path.Combine(
            into.FullName,
            LiveCheck.ResponseNamed(
                new FileInfo(wav), LiveCheck.StampOf(UtcTimestamp.Parse("2026-09-10T11:22:33.000Z")))),
        "{}");

    private static void Erase(DirectoryInfo folder)
    {
        try
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over, which is the same
            // shrug TemporaryCorpus makes and for the same Windows reason.
        }
    }

    private Run Line(params string[] rest) => CommandLine.Of(
        ["deepgram-live", "--audio", audio.FullName, "--out", into.FullName, .. rest]);

    /// <summary>The same line, parsed, for the overload that takes its keyboard and its sending.</summary>
    private Arguments Typed(params string[] rest) => Arguments.Parse(
        ["--audio", audio.FullName, "--out", into.FullName, .. rest]);

    private FileInfo Wav(string name, int seconds, params float[] levels) => ForeignWav.Steady(
        new FileInfo(Path.Combine(audio.FullName, name)), Rate, Rate * seconds, levels);
}
