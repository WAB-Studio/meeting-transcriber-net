using System.Runtime.CompilerServices;

using MeetingTranscriber.Domain.Artifacts;
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
/// parsing, in <see cref="SendingMark.Take"/> or in <see cref="LiveCheck.Of"/>. What matters is the
/// ordering and not the list: the key is read inside the <see cref="Sending"/> the table binds, and
/// nothing on this side of the confirmation has reached it. Everything past that point drives the
/// four-argument
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
/// <c>docs/layout.md</c> says this project holds no rule of its own bar one named exception, which
/// is what a live run decides — which files are sent, what the ceiling allows, what a person
/// confirmed, and the claim over the folder the responses land in. Those four are what this file is
/// about, beside the argument reading and the exit code; what a response has to hold is on the
/// other side of that exception and is proved where the rule lives.
/// </para>
/// </remarks>
public sealed class LiveDeepgramTests : IDisposable
{
    /// <summary>
    /// A hundred frames a second. What is under test is a length and the arithmetic over it, and a
    /// realistic rate would make a four-minute file eight megabytes for nothing.
    /// </summary>
    private const int Rate = 100;

    /// <summary>
    /// A ceiling that is not what is being tested. The tests that are about the ceiling say their
    /// own number; the ones about what a send does say this, so a fixture's real length never has
    /// to be worked out twice.
    /// </summary>
    private const int Generous = 60;

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
        Landed().ShouldBeEmpty();
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
        Landed().ShouldBeEmpty();
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
        Landed().ShouldBeEmpty();
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
        Landed().ShouldBeEmpty();
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
        Landed().ShouldBeEmpty();
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

    /// <summary>
    /// The chain the resume rule is made of, end to end: the name a run writes is the name the next
    /// run reads back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one that was missing, and what it found is that nothing ever renamed the working
    /// file. Every byte went to <c>&lt;stem&gt;-&lt;stamp&gt;.json.partial</c>, the report named a
    /// path that was not there, and the parser opened <see cref="FileMode.Open"/> on it and threw —
    /// after the money was spent. <see cref="LiveCheck.Of"/> lists <c>*.json</c>, so nothing it
    /// could ever find existed and the resume rule matched nothing on a real machine.
    /// </para>
    /// <para>
    /// Writing the response by hand and reading it back proves only that
    /// <see cref="LiveCheck.ResponseNamed"/> agrees with itself. What makes this a probe is that the
    /// file it reads is the one the send loop left.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_response_that_held_lands_under_the_name_the_next_run_reads_back()
    {
        AudioFor(DeepgramFixtures.TwoChannelOneVoiceMe, "a.wav");
        using var output = new StringWriter();

        var code = await DeepgramCommands.SendAsync(
            LiveCheck.Of(audio, into, Generous),
            into,
            output,
            Answering(DeepgramFixtures.TwoChannelOneVoiceMe),
            CancellationToken.None);

        code.ShouldBe(Cli.Ok, output.ToString());

        var response = into.EnumerateFiles("*.json").ShouldHaveSingleItem();
        response.Name.ShouldStartWith("a-");
        into.EnumerateFiles("*" + RecordingFiles.UnfinishedSuffix).ShouldBeEmpty();
        output.ToString().ShouldContain(response.FullName);

        // And what the next run over the same two folders makes of it.
        var again = LiveCheck.Of(audio, into, Generous);
        again.Audio.ShouldBeEmpty();
        again.Answered.Select(file => file.Name).ShouldBe(["a.wav"]);
    }

    /// <summary>
    /// A response that broke an invariant is paid for and is kept — and it does not wear the name
    /// that says it is a response to read a meeting from, because that name is what a later run
    /// reads to decide what it need not buy.
    /// </summary>
    /// <remarks>
    /// The failure this stops is the entry's own headline case got backwards: a run of eight stops
    /// on the sixth because nobody was heard in it, somebody fixes <c>--language</c> and runs the
    /// same command again — and the sixth is reported <c>already</c> and never sent while seven and
    /// eight are bought. Red when the working name is moved onto the final one before the verdict
    /// is in.
    /// </remarks>
    [Fact]
    public async Task A_response_that_did_not_hold_is_kept_under_a_name_the_next_run_sends_again()
    {
        // A one-minute file against a thirteen-minute response, which is what a wrong account or a
        // wrong key comes back as: a response describing audio nobody sent.
        Wav("a.wav", seconds: 60, 0.4f, 0.2f);
        Wav("b.wav", seconds: 60, 0.4f, 0.2f);
        using var output = new StringWriter();

        var code = await DeepgramCommands.SendAsync(
            LiveCheck.Of(audio, into, Generous),
            into,
            output,
            Answering(DeepgramFixtures.TwoChannelOneVoiceMe),
            CancellationToken.None);

        code.ShouldBe(Cli.Refused);
        output.ToString().ShouldContain("not held");
        output.ToString().ShouldContain("kept");

        // Nothing wears the name of an answer, and what came back is still on disk.
        into.EnumerateFiles("*.json").ShouldBeEmpty();
        into.EnumerateFiles("*" + RecordingFiles.UnfinishedSuffix).ShouldHaveSingleItem()
            .Length.ShouldBeGreaterThan(0);

        // So the next run sends it again rather than calling it bought, and b.wav with it.
        LiveCheck.Of(audio, into, Generous).Audio.Select(one => one.File.Name)
            .ShouldBe(["a.wav", "b.wav"]);
    }

    /// <summary>
    /// Ctrl+C part way through a call: the fragment is named rather than left for somebody to find,
    /// what the run never reached is named too, and it comes back a refusal rather than an
    /// unhandled <see cref="OperationCanceledException"/> — which is what it would be,
    /// <c>Cli.IsRefusal</c> not naming that type and having no reason to.
    /// </summary>
    [Fact]
    public async Task A_run_stopped_part_way_through_a_call_says_what_it_stopped_and_what_is_left()
    {
        AudioFor(DeepgramFixtures.TwoChannelOneVoiceMe, "a.wav");
        Wav("b.wav", seconds: 60, 0.4f, 0.2f);
        using var stopping = new CancellationTokenSource();
        using var output = new StringWriter();

        var code = await DeepgramCommands.SendAsync(
            LiveCheck.Of(audio, into, Generous),
            into,
            output,
            (sent, wrote, token) =>
            {
                stopping.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(0L);
            },
            stopping.Token);

        code.ShouldBe(Cli.Refused);

        var said = output.ToString();
        said.ShouldContain("stopped");
        said.ShouldContain("a.wav");
        said.ShouldContain("not sent");
        said.ShouldContain("b.wav");

        // Nought bytes is a call that never delivered, so there is nothing to keep and nothing to
        // name: what would stand otherwise is litter under a name claiming to be about a paid
        // response. The whole folder and not `Landed()`, because this drives `SendAsync`
        // directly: no `SendingMark` is ever taken on this path, so nothing here is entitled to
        // leave a file of any name behind.
        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// A press that lands between two files. Nothing was in flight, so nothing may have been
    /// charged for — and the one command whose job is saying true things about a call must not
    /// invent a possible charge for a request that never left the machine.
    /// </summary>
    /// <remarks>
    /// Red without the question asked at the top of the loop: the next call is entered with a token
    /// already set, throws with nothing sent, and the run reports that file as stopped part way
    /// through a call and then leaves it out of what was not sent — wrong twice about one file.
    /// </remarks>
    [Fact]
    public async Task A_press_between_two_files_stops_the_run_without_claiming_a_call_was_in_flight()
    {
        AudioFor(DeepgramFixtures.TwoChannelOneVoiceMe, "a.wav");
        AudioFor(DeepgramFixtures.TwoChannelOneVoiceMe, "b.wav");
        using var stopping = new CancellationTokenSource();
        using var output = new StringWriter();
        var asked = new List<string>();
        var answer = Answering(DeepgramFixtures.TwoChannelOneVoiceMe);

        var code = await DeepgramCommands.SendAsync(
            LiveCheck.Of(audio, into, Generous),
            into,
            output,
            async (sent, wrote, token) =>
            {
                asked.Add(sent.File.Name);
                var bytes = await answer(sent, wrote, token);

                // The press, landing as this call comes back.
                await stopping.CancelAsync();
                return bytes;
            },
            stopping.Token);

        code.ShouldBe(Cli.Refused);
        asked.ShouldBe(["a.wav"]);

        var said = output.ToString();
        said.ShouldContain("between files");
        said.ShouldNotContain("Whether it was charged for");
        said.ShouldContain("b.wav");

        // The one that did come back held, so it is under the name a later run reads.
        into.EnumerateFiles("*.json").ShouldHaveSingleItem().Name.ShouldStartWith("a-");
    }

    /// <summary>
    /// Two runs pointed at one <c>--out</c>: the second is refused by name, before the folder has
    /// been listed and before anything has been sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a listing decides is what need not be bought, so two runs that both reached it would
    /// both see nothing answered, both pass the ceiling, both be confirmed and both pay for the same
    /// audio — and the run stamps would keep the files from colliding, so nothing on disk or on
    /// screen would ever say it happened. A listing is not a claim; a handle is.
    /// </para>
    /// <para>
    /// Driven through <see cref="CommandLine.Of"/>, which is this suite's rule and not a
    /// convenience: the claim is taken before <see cref="LiveCheck.Confirmed"/> is reached, so no
    /// keyboard and no key are touched on this path, and what the refusal is worth is the exit code
    /// and the sentence a person actually gets. <b>Red when</b> the claim is not taken: the run
    /// reaches the ceiling and the confirmation instead of stopping here.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_second_run_over_a_folder_a_run_is_already_using_is_refused_before_anything_is_sent()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);
        using var first = SendingMark.Take(into);

        var run = Line("--ceiling-minutes", "5");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain(into.FullName);
        run.Error.ShouldContain("could not be claimed");
        run.Error.ShouldContain(SendingMark.FileName);
        Landed().ShouldBeEmpty();
    }

    /// <summary>
    /// And the half without which the guard would be worse than the hazard: a run that ended leaves
    /// the folder free for the next one.
    /// </summary>
    /// <remarks>
    /// A claim nothing lets go of turns one finished run into a folder nobody may ever send into
    /// again — which costs somebody every file in it, where the hazard cost them one duplicate bill.
    /// <b>Red when</b> the <c>using</c> on the mark is dropped or the handle outlives the command.
    /// </remarks>
    [Fact]
    public void A_run_that_ended_leaves_the_folder_free_for_the_next_one()
    {
        Wav("meeting.wav", seconds: 60, 0.4f, 0.2f);
        var sent = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var output = new StringWriter();

            var code = DeepgramCommands.Live(
                Typed("--ceiling-minutes", "5"),
                output,
                () => "1",
                (_, _, _, _) =>
                {
                    sent++;
                    return Cli.Ok;
                });

            code.ShouldBe(Cli.Ok, output.ToString());
        }

        sent.ShouldBe(2);
    }

    /// <summary>
    /// A rename that fails for a reason of its own says what that reason was, and does not report
    /// the one cause it happens to have a sentence for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>File.Move(..., overwrite: false)</c> raises <see cref="IOException"/> for a destination
    /// that exists and equally for a full disk or a path too long — and
    /// <see cref="UnauthorizedAccessException"/>, which is not one, for a denied path. The cost
    /// lands after the call was paid for: the response is safe under its working name, and the one
    /// command whose job is saying true things about a spend was saying a false thing about why.
    /// </para>
    /// <para>
    /// The destination is made a <em>directory</em> of that name, which is the one obstruction that
    /// is deterministic here and is not a file: <c>File.Exists</c> is false for it, so the
    /// already-there arm's filter does not take it, and Windows refuses the rename all the same. It
    /// is created from inside the stand-in provider because only there is the name knowable — the
    /// run stamp comes off the clock inside <c>SendAsync</c>, and the stream this is handed is the
    /// working file, whose name is the response's with the unfinished suffix on the end.
    /// </para>
    /// <para>
    /// <b>Red when</b> the two arms collapse back into one: the message then says the destination is
    /// already there, over a path that holds no response at all, and carries nothing of what really
    /// refused.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_response_that_cannot_be_put_in_place_for_a_reason_of_its_own_says_what_that_reason_was()
    {
        AudioFor(DeepgramFixtures.TwoChannelOneVoiceMe, "a.wav");
        using var output = new StringWriter();
        var answering = Answering(DeepgramFixtures.TwoChannelOneVoiceMe);

        var refused = await Should.ThrowAsync<CommandException>(async () => await DeepgramCommands.SendAsync(
            LiveCheck.Of(audio, into, Generous),
            into,
            output,
            async (sent, wrote, stopping) =>
            {
                var working = ((FileStream)wrote).Name;
                Directory.CreateDirectory(
                    working[..^RecordingFiles.UnfinishedSuffix.Length]);

                return await answering(sent, wrote, stopping);
            },
            CancellationToken.None));

        // The working name, so somebody can find what they paid for, and the underlying failure's
        // own words, so they find out what actually stopped it.
        refused.Message.ShouldContain(RecordingFiles.UnfinishedSuffix);
        refused.Message.ShouldContain("could not be moved onto");
        refused.Message.ShouldNotContain("is already there");
        into.EnumerateFiles("*" + RecordingFiles.UnfinishedSuffix).ShouldHaveSingleItem();
    }

    /// <summary>
    /// The rule that keeps the one entry point that spends out of reach of every suite there is,
    /// asserted rather than written down.
    /// </summary>
    /// <remarks>
    /// <c>DeepgramCommands.Live(Arguments, TextWriter)</c> binds this prompt's keyboard and this
    /// machine's key, and it is <see langword="internal"/> — which keeps a suite out only for as
    /// long as this repository has no <c>InternalsVisibleTo</c>. Seven places state that in prose
    /// and nothing held it: one attribute in <c>Directory.Build.props</c> silently reopens it, and
    /// the failure would show up as a test that suddenly compiles.
    /// <para>
    /// The whole tree and not <c>src/</c> alone, because the attribute is assembly-level and can be
    /// written anywhere the assembly compiles — a project file, a props file, any <c>.cs</c>. Red
    /// with the attribute added anywhere in either.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_suite_can_be_let_in_to_what_spends()
    {
        // Spelled in two halves, because a file hunting for a declaration must not be one its own
        // hunt finds. Seven files in this repository say the word in prose and none of them is
        // declaring anything; what a compiler reads is the word with one of these in front of it.
        const string Named = "InternalsVisible" + "To";
        const string InCode = "[assembly: " + Named;
        const string InAProjectFile = "<" + Named;

        var repository = new DirectoryInfo(Path.GetFullPath(Path.Combine(Here(), "..", "..")));

        var declaring = repository
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".cs" or ".csproj" or ".props" or ".targets")
            .Where(file => !file.FullName.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file =>
            {
                var written = File.ReadAllText(file.FullName);

                return written.Contains(InCode, StringComparison.Ordinal)
                    || written.Contains(InAProjectFile, StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(repository.FullName, file.FullName))
            .ToArray();

        declaring.ShouldBeEmpty();
    }

    /// <summary>
    /// Everything in the responses folder that somebody would have been charged for — which is
    /// everything in it except the run's own claim over it.
    /// </summary>
    /// <remarks>
    /// What these facts are about is whether anything was bought, and a <see cref="SendingMark"/> is
    /// a handle rather than an artifact: it says a run reached the folder, never that a call was
    /// made. Left out by name and not by extension, so a <c>.partial</c> — a call that was paid for
    /// and did not land — is still caught by every one of them.
    /// </remarks>
    private IEnumerable<FileInfo> Landed() =>
        into.EnumerateFiles().Where(file => file.Name != SendingMark.FileName);

    /// <summary>A keyboard nobody is at, for the paths that must never reach one.</summary>
    private static string? Nobody() =>
        throw new InvalidOperationException("Nothing on this path may ask anybody to confirm.");

    /// <summary>
    /// Where this file is, so the repository is found from the tree rather than from whatever
    /// working directory the runner happened to start in.
    /// </summary>
    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    /// <summary>
    /// A stand-in for the provider that answers with a committed response and reaches no socket.
    /// </summary>
    private static Transcribing Answering(string fixture) => async (sent, wrote, stopping) =>
    {
        await using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
        await response.CopyToAsync(wrote, stopping);
        return response.Length;
    };

    /// <summary>
    /// The audio a committed response is a transcription of, at this suite's cheap frame rate — so
    /// that what the response says about its length and what this end counts agree, which is the
    /// first thing <c>LiveInvariants</c> holds them to.
    /// </summary>
    private FileInfo AudioFor(string fixture, string name)
    {
        var read = DeepgramTranscriptParser.ParseFile(
            DeepgramFixtures.PathOf(fixture), DeepgramFixtures.ProfileOf(fixture));

        return ForeignWav.Steady(
            new FileInfo(Path.Combine(audio.FullName, name)),
            Rate,
            (int)(read.Audio.Milliseconds * Rate / 1000),
            0.4f,
            0.2f);
    }

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
