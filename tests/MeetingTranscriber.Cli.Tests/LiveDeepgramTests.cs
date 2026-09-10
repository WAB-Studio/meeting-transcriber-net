using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
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
/// driven against <c>tests/fixtures/deepgram/</c>, which is real responses with every timing,
/// confidence and channel number left as sent.
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

    /// <summary>
    /// Declared here rather than on <see cref="DeepgramFixtures"/> for the reason that type's own
    /// remarks give: xunit's analyzer crashes on a <c>MemberData</c> whose member lives in another
    /// assembly, and a crashed analyzer is a warning CI fails on.
    /// </summary>
    public static TheoryData<string> Fixtures => new(DeepgramFixtures.All);

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
        var run = LiveCheck.Of(audio, ceilingMinutes: 9);

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

        var run = LiveCheck.Of(audio, ceilingMinutes: 9);
        run.Say(output);
        run.Confirmed(output, () => null);

        run.Minutes.ShouldBe(9);
        output.ToString().ShouldContain("0:08:23 (9 minute(s))");
        output.ToString().ShouldContain("type 9");
        run.UnderTheCeiling.ShouldBeTrue();
        LiveCheck.Of(audio, ceilingMinutes: 8).UnderTheCeiling.ShouldBeFalse();
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

        var run = LiveCheck.Of(audio, ceilingMinutes: 5);

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
        var run = LiveCheck.Of(audio, ceilingMinutes: 5);

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
    /// Every committed response holds everything a live call is checked for. This is the half that
    /// would otherwise only ever run on a call somebody paid for.
    /// </summary>
    /// <remarks>
    /// It is also what stops a rule being written that reads a speaker label's list position as the
    /// provider's speaker number. All five fixtures number their speakers contiguously from zero,
    /// so such a rule would go green here and fail for the first time on a paid call — which is why
    /// <see cref="LiveInvariants"/> checks what the provider decides and nothing the parser built.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_committed_response_holds_every_invariant(string fixture)
    {
        var read = Read(fixture);

        var verdict = LiveInvariants.Of(read, Sent(fixture, read));

        verdict.Broken.ShouldBeEmpty();
        verdict.Held.ShouldBeTrue();
        verdict.Turns.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A response describing audio nobody sent is the failure only a real call can produce, and the
    /// one worth every other rule being left out for.
    /// </summary>
    [Fact]
    public void A_response_whose_length_is_not_the_audio_s_is_named()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var sent = Sent(DeepgramFixtures.TwoChannelShort, read) with
        {
            Length = read.Audio + Duration.FromSeconds(120),
        };

        var verdict = LiveInvariants.Of(read, sent);

        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain($"{read.Audio}");
        verdict.Broken[0].ShouldContain($"{sent.Length}");
        verdict.Broken[0].ShouldContain(sent.File.Name);
    }

    /// <summary>
    /// A provider reports its own rounding of what it received and this end counts frames, so the
    /// two never agree to the millisecond. Comparing for equality would fail every real call.
    /// </summary>
    [Fact]
    public void A_length_a_second_out_is_not_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var sent = Sent(DeepgramFixtures.TwoChannelShort, read) with
        {
            Length = read.Audio + Duration.FromMilliseconds(900),
        };

        LiveInvariants.Of(read, sent).Held.ShouldBeTrue();
    }

    /// <summary>
    /// The response's two halves against each other, which nothing else compares: the parser holds
    /// an utterance's start against its own end and neither against the length it reports.
    /// </summary>
    [Fact]
    public void An_utterance_after_the_end_of_the_audio_is_named()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var beyond = read with
        {
            Segments =
            [
                .. read.Segments,
                new SpeechSegment(
                    read.Audio + Duration.FromSeconds(30),
                    read.Audio + Duration.FromSeconds(35),
                    AudioChannel.Loopback,
                    "ch0:speaker_0",
                    "coati"),
            ],
        };

        var verdict = LiveInvariants.Of(beyond, Sent(DeepgramFixtures.TwoChannelShort, read));

        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain("1 utterance(s)");
        verdict.Broken[0].ShouldContain($"{read.Audio}");
    }

    /// <summary>
    /// What a run in the wrong language produces, and what a live probe exists to catch: a response
    /// that came back whole, cost money and holds nobody.
    /// </summary>
    [Fact]
    public void A_response_nobody_was_heard_in_is_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);

        var verdict = LiveInvariants.Of(
            read with { Segments = [] }, Sent(DeepgramFixtures.TwoChannelShort, read));

        verdict.Turns.ShouldBe(0);
        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain("nothing was heard");
        verdict.Broken[0].ShouldContain("language");
    }

    /// <summary>
    /// A channel that carried nobody was transcribed and was charged for, so it is said — and it is
    /// not a reason to fail a run, because a meeting where one side never spoke is a meeting.
    /// </summary>
    [Fact]
    public void A_silent_channel_is_said_and_is_not_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelSilentMe);

        var verdict = LiveInvariants.Of(read, Sent(DeepgramFixtures.TwoChannelSilentMe, read));

        verdict.Held.ShouldBeTrue();
        verdict.Broken.ShouldBeEmpty();
        verdict.WorthSaying.Count.ShouldBe(1);
        verdict.WorthSaying[0].ShouldContain("channel 1");
        verdict.WorthSaying[0].ShouldContain("the microphone");
    }

    /// <summary>
    /// A rule and not an example: what a live check may compare is structure. A provider rewords
    /// itself between models and between days, and a check against its words is a check that goes
    /// red for a reason nobody can act on — on a call somebody paid for.
    /// </summary>
    [Fact]
    public void Nothing_here_reads_a_word_of_what_was_said()
    {
        var read = Read(DeepgramFixtures.TwoChannelLong);
        var sent = Sent(DeepgramFixtures.TwoChannelLong, read);
        var reworded = read with
        {
            Segments = [.. read.Segments.Select(segment => segment with { Text = "coati" })],
        };

        var said = LiveInvariants.Of(read, sent);
        var otherwise = LiveInvariants.Of(reworded, sent);

        otherwise.Turns.ShouldBe(said.Turns);
        otherwise.Broken.ShouldBe(said.Broken);
        otherwise.WorthSaying.ShouldBe(said.WorthSaying);
    }

    /// <summary>A committed response, read exactly as the command reads a paid one.</summary>
    private static DeepgramTranscript Read(string fixture) => DeepgramTranscriptParser.ParseFile(
        DeepgramFixtures.PathOf(fixture), DeepgramFixtures.ProfileOf(fixture));

    /// <summary>
    /// The audio that response is a transcription of. The file is named and never opened: what a
    /// fixture is missing is its audio, and every rule under test is about the response.
    /// </summary>
    private static LiveAudio Sent(string fixture, DeepgramTranscript read) => new(
        new FileInfo(fixture + ".wav"), DeepgramFixtures.ProfileOf(fixture), read.Audio);

    /// <summary>A keyboard nobody is at, for the paths that must never reach one.</summary>
    private static string? Nobody() =>
        throw new InvalidOperationException("Nothing on this path may ask anybody to confirm.");

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
