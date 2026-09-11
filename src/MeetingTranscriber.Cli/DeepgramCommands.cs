using System.Globalization;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Cli;

/// <summary>
/// Sending a run somebody has already agreed to, and what came back of it.
/// </summary>
/// <remarks>
/// A parameter of <see cref="DeepgramCommands.Live(Arguments, TextWriter, Func{string}, Sending)"/>
/// so that the overload a suite drives cannot spend: the one implementation that reads this
/// machine's key and builds a client is <see cref="DeepgramCommands.WithThisMachinesKey"/>, which is
/// private, and the overload that binds it is <see langword="internal"/> — which in this repository
/// means unreachable, there being no <c>InternalsVisibleTo</c> anywhere. What a test hands over here
/// does not send, and writing one that does would mean building an <c>HttpClient</c> under
/// <c>tests/</c>, which is a deliberate act with a check standing over it rather than a line
/// somebody slips in.
/// <para>
/// What that is <em>not</em> is the only thing between a suite and a charge, and the branch that
/// made it <see langword="internal"/> did not make it one. <see cref="Cli.Run"/> is public and the
/// command table binds the same entry point, so a test typing the command line still reaches it;
/// what stops that one is the keyboard refusing a redirected console and the confirmation nobody
/// can answer. This seam is what makes the refusals, the ceiling and the confirmation provable
/// without either.
/// </para>
/// </remarks>
public delegate int Sending(LiveCheck run, DirectoryInfo into, string language, TextWriter output);

/// <summary>
/// Sending one file of a run and writing what comes back, which is the only thing in
/// <see cref="DeepgramCommands.SendAsync"/> that reaches the network.
/// </summary>
/// <remarks>
/// A parameter for the reason <see cref="Sending"/> is one, one layer further down: everything
/// around it decides — where the bytes land, which name they end up under, what a broken invariant
/// costs, what a stop says — and none of that could be driven while the only way in built an
/// <see cref="HttpClient"/> out of this machine's key. What a suite hands over here reaches no
/// socket; the one implementation that does is private to <see cref="DeepgramCommands"/> and only
/// the command table binds it.
/// </remarks>
/// <param name="sent">The file being sent, and what it is.</param>
/// <param name="wrote">Where the response body is written, byte for byte as it arrives.</param>
/// <param name="stopping">What a person at the prompt pressing Ctrl+C sets.</param>
/// <returns>How many bytes came back.</returns>
public delegate Task<long> Transcribing(LiveAudio sent, Stream wrote, CancellationToken stopping);

/// <summary>
/// The one command that spends money: known audio to the real Deepgram, under a ceiling somebody
/// typed back, with what came back checked against the contract rather than against words.
/// </summary>
/// <remarks>
/// <para>
/// Every decision it makes lives in <see cref="LiveCheck"/> and <see cref="LiveInvariants"/>, which
/// hold no client and can be driven whole by a suite. What is here is the client, the key, the
/// files on disk and the report.
/// </para>
/// <para>
/// <b>Nothing sends until <see cref="LiveCheck.Confirmed"/> has said so, and the key is not read
/// until then either.</b> A run somebody declines touches this machine's credential store not at
/// all, which is what lets the suite drive the whole line without a developer's real key being
/// read.
/// </para>
/// </remarks>
public static class DeepgramCommands
{
    /// <summary>
    /// How long one call may take, upload and all.
    /// </summary>
    /// <remarks>
    /// The default 100 seconds kills every real call — <see cref="DeepgramTranscription"/>'s
    /// constructor says so and says it cost money to find out: the timeout covers the upload as
    /// well as the wait, and it stops covering anything once the response headers arrive. A whole
    /// meeting is what this command sends, so this is what a whole meeting needs.
    /// </remarks>
    private static readonly TimeSpan LongEnoughForAWholeMeeting = TimeSpan.FromMinutes(30);

    /// <summary>The command as the table runs it: this prompt's keyboard, and this machine's key.</summary>
    /// <remarks>
    /// <see langword="internal"/> and not public, which is how this repository keeps something out
    /// of the test tree — there is no <c>InternalsVisibleTo</c> anywhere, so no suite can name this.
    /// The four-argument overload beside it is what a suite drives, and it decides everything
    /// without holding a key or a client. <c>Cli</c>'s command table is in the same assembly, so
    /// binding this there is unaffected.
    /// </remarks>
    internal static int Live(Arguments arguments, TextWriter output) =>
        Live(arguments, output, FromAPersonAtThisPrompt, WithThisMachinesKey);

    /// <summary>
    /// The whole of the command, with the keyboard and the sending handed in.
    /// </summary>
    /// <remarks>
    /// Both are parameters and neither is here for tidiness. The keyboard, because a gate that
    /// reads the console cannot be driven by a suite at all — it hangs under a host that inherited
    /// one and answers nothing under a host that redirected it, so the card's promise would rest on
    /// which of those <c>dotnet test</c> happened to launch. The sending, because the keyboard on
    /// its own would then be a public seam a suite could type the right number at: with both handed
    /// in, everything a test can reach decides, and the one thing that spends is private, bound
    /// only by the command table, and reachable only through an <see langword="internal"/> overload
    /// no suite in this repository can name.
    /// </remarks>
    /// <param name="arguments">What was typed after the command name.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="typed">What somebody at the prompt typed, or nothing when nobody is at it.</param>
    /// <param name="send">What sending a confirmed run is.</param>
    public static int Live(Arguments arguments, TextWriter output, Func<string?> typed, Sending send)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(send);

        var audio = new DirectoryInfo(arguments.Required("--audio"));

        // Arguments.Number answers the fallback only when the flag is absent — '0' and 'banana' are
        // already refused by name — so nought here means nobody said, and nobody saying is not
        // something this command gets to decide for them.
        var ceiling = arguments.Number("--ceiling-minutes", 0);
        if (ceiling == 0)
        {
            throw new UsageException(
                "--ceiling-minutes says how much audio one run may send. There is no default, "
                + "because the default would be somebody's bill.");
        }

        var into = new DirectoryInfo(arguments.Required("--out"));
        var language = arguments.Language("--language", MeetingCommands.DefaultLanguage);
        arguments.EnsureNothingLeftOver();

        into.Refresh();
        if (!into.Exists)
        {
            throw new CommandException(
                $"There is no folder '{into.FullName}' for the responses to land in. A paid "
                + "response has to have somewhere to go before the call is made.");
        }

        // Said before it happens, because it is a full read of every file: a ceiling is spent
        // against frames that are there rather than against a length a header claims, and a folder
        // of hour-long meetings is minutes of disk before there is anything on the screen.
        Report.Line(
            output,
            "reading",
            $"every .wav in '{audio.FullName}', counting the frames rather than believing a header.");

        var run = LiveCheck.Of(audio, into, ceiling);
        run.Say(output);

        if (run.Audio.Count == 0)
        {
            // Not a refusal. Somebody re-running a folder that is finished has asked for nothing,
            // and the lines above already name every file and say why each was left out.
            Report.Line(
                output,
                "not sent",
                "every .wav in that folder already has a response where responses land, so there "
                + "was nothing left to send.");
            return Cli.Ok;
        }

        if (!run.UnderTheCeiling)
        {
            throw new CommandException(
                $"{run.Minutes} minute(s) of audio is over the {ceiling} minute ceiling this run "
                + "was given. Nothing was sent.");
        }

        if (!run.Confirmed(output, typed))
        {
            // Somebody reading the lines and saying no is this command working, not a failure.
            Report.Line(output, "not sent", "nobody typed the number, so no audio left this machine.");
            return Cli.Ok;
        }

        return send(run, into, language, output);
    }

    /// <summary>Sending it for real, on this machine's key.</summary>
    /// <remarks>
    /// <para>
    /// The key is read here and not before, so a run nobody confirmed reaches the credential store
    /// not at all. The blocking wait is the shape a synchronous entry point takes: nothing in this
    /// project is <see langword="async"/>, the command table's delegate is not, and nothing here
    /// runs on a UI thread.
    /// </para>
    /// <para>
    /// <b>Ctrl+C is taken rather than left to kill the process, and this is the only place it can
    /// be.</b> A kill lands mid-write and leaves a <c>.partial</c> nobody named; taking it lets the
    /// body read stop where it is, the <c>catch</c> run and <see cref="Abandon"/> say what is on
    /// disk and whether it may have been charged for. The handler takes itself off on the way
    /// through, so a second Ctrl+C from somebody who means it kills the process as it always did.
    /// It writes nothing: it runs on a thread of its own while this one may be reporting, and a
    /// <see cref="TextWriter"/> is not two threads' to share. What was stopped is said by the loop,
    /// on the thread that was doing it.
    /// </para>
    /// </remarks>
    private static int WithThisMachinesKey(
        LiveCheck run, DirectoryInfo into, string language, TextWriter output)
    {
        using var http = new HttpClient { Timeout = LongEnoughForAWholeMeeting };
        var transcription = new DeepgramTranscription(http);
        var key = DeepgramKey.OfThisInstall().Read();

        using var stopping = new CancellationTokenSource();
        var gate = new Lock();
        var listening = true;

        void Stop(object? sender, ConsoleCancelEventArgs pressed)
        {
            pressed.Cancel = true;
            Console.CancelKeyPress -= Stop;

            // Under the gate, because this runs on the console's own thread and the one below is on
            // its way out with a `using` behind it: cancelling a source that has been disposed
            // throws here, where nothing is catching, and the process would die with a stack trace
            // over the top of the report of a run that finished and was paid for.
            lock (gate)
            {
                if (listening)
                {
                    stopping.Cancel();
                }
            }
        }

        Console.CancelKeyPress += Stop;
        try
        {
            return SendAsync(
                    run,
                    into,
                    output,
                    (sent, wrote, token) => transcription.SendAsync(
                        sent.File, new DeepgramRequest(sent.Profile, language), key, wrote, token),
                    stopping.Token)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
            lock (gate)
            {
                listening = false;
            }
        }
    }

    /// <summary>
    /// Sends every file of the run, in order, and says what came back of each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing retries, and the first thing that goes wrong ends the run.</b> Whether a failed
    /// call was charged is not something this end can tell, and the contract already says a charge
    /// that may already have happened is not something the application repeats on its own. A broken
    /// invariant stops it too, and for the money rather than for tidiness: a response describing
    /// different audio from the file that was sent is the loudest evidence there is that the key,
    /// the account or the request is wrong, and buying the rest of the folder on top of it spends
    /// what the person who typed the number had no evidence of when they typed it.
    /// </para>
    /// <para>
    /// <see cref="DeepgramTranscription.SendAsync"/> says the client's timeout stops covering
    /// anything once the response headers arrive, so a provider that answers and then goes quiet is
    /// bounded by <paramref name="stopping"/> and by nothing else. It is bound to Ctrl+C and not to
    /// a deadline: a deadline for the body read would be a number nobody has measured standing over
    /// a failure nobody has had, and the day one is measured is the day it goes here. What the
    /// token buys over killing the process is that the stop lands in a <c>catch</c> rather than in
    /// the middle of a write, so the fragment on disk is named rather than left for somebody to
    /// find.
    /// </para>
    /// <para>
    /// <b>It takes what sends as a parameter and holds no client, which is what makes any of this
    /// provable.</b> Everything here decides — where the bytes land, which name they end up under,
    /// what a broken invariant costs, what a stop says — and none of it could be driven while the
    /// only way in built an <see cref="HttpClient"/> out of this machine's key.
    /// <see cref="WithThisMachinesKey"/> is the one implementation that spends, it is private, and
    /// only the command table reaches it. A <see cref="Transcribing"/> a suite writes reaches no
    /// socket, exactly as <see cref="Sending"/> already works one layer up.
    /// </para>
    /// </remarks>
    /// <param name="run">The files this run would send, in order.</param>
    /// <param name="into">Where the responses land.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="transcribe">What sending one file is.</param>
    /// <param name="stopping">What a person at the prompt pressing Ctrl+C sets.</param>
    public static async Task<int> SendAsync(
        LiveCheck run,
        DirectoryInfo into,
        TextWriter output,
        Transcribing transcribe,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(transcribe);

        // One stamp for the run and not one per file, because what it identifies is a run: files
        // that straddle a second would otherwise stop sorting and grouping together, which is the
        // whole of what it is for. The clock is the machine's, for the reason Clock's own remarks
        // give. How it is spelled is `LiveCheck`'s, because `LiveCheck.Of` reads these names back
        // to leave out what an earlier run already bought.
        var stamp = LiveCheck.StampOf(Clock.Now());
        var held = true;
        var stopped = false;

        for (var index = 0; index < run.Audio.Count; index++)
        {
            var sent = run.Audio[index];
            var response = new FileInfo(
                Path.Combine(into.FullName, LiveCheck.ResponseNamed(sent.File, stamp)));
            var partial = new FileInfo(response.FullName + RecordingFiles.UnfinishedSuffix);

            // Asked before the call and not only inside it. A press lands wherever it lands, and
            // between two files is the likeliest place of all — the reports are on screen and
            // nothing is in flight. Entering the call with a token already set would throw with
            // nothing sent, and the catch below would say a file may have been charged for when the
            // request never left the machine.
            if (stopping.IsCancellationRequested)
            {
                Report.Line(output, "stopped", "Ctrl+C, between files. Nothing was in flight.");
                Unsent(output, run.Audio.Skip(index));
                stopped = true;
                break;
            }

            // A working name, and the point of it: SendAsync's own remarks say a caller writing
            // straight at the final name is left with a truncated artifact on any failure after the
            // first byte. A .json in this folder is a whole response; anything under the corpus's
            // own unfinished suffix is not one and says so in its name.
            long bytes;
            try
            {
                await using (var stream = partial.Create())
                {
                    bytes = await transcribe(sent, stream, stopping).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // Somebody at the prompt stopping the run, which is this command working and not a
                // defect — so it says what it stopped in the middle of and what it never reached,
                // and comes back as a refusal rather than as a stack trace. Guarded on the token
                // because the client's own timeout arrives as the same type and is not this: that
                // one is a call that went wrong and belongs in the catch below.
                Abandon(partial, output);
                Report.Line(
                    output,
                    "stopped",
                    $"{sent.File.Name} — Ctrl+C, part way through the call. Whether it was charged "
                    + "for is not something this end can tell.");
                Unsent(output, run.Audio.Skip(index + 1));
                stopped = true;
                break;
            }
            catch
            {
                // Every way this can fail after the file was opened, and not only the provider's:
                // a full disk, a quota and a cancellation all leave the same thing behind, and
                // DeepgramTranscription says in as many words that discarding it is the caller's.
                // What it is called is said rather than guessed — a key the provider would not
                // accept leaves nought bytes and no charge, and calling that a paid fragment would
                // be the one command whose job is saying true things about a call saying a false
                // one.
                Abandon(partial, output);
                throw;
            }

            // Named before anything reads it, and named where it really is. A response that came
            // back whole and that this build cannot parse is exactly what a live probe is for, and
            // it is the failure that leaves the irreplaceable artifact — so where it landed is on
            // the screen before the parser is given the chance to throw.
            Report.Line(output, "sent", $"{sent.File.Name} ({Report.Offset(sent.Length)})");
            Report.Line(output, "landed", partial.FullName);
            Report.Line(output, "bytes", bytes.ToString(CultureInfo.InvariantCulture));

            var read = DeepgramTranscriptParser.ParseFile(partial.FullName, sent.Profile);
            var verdict = LiveInvariants.Of(read, sent);

            Report.Line(output, "channels", $"{read.Channels.Count}");
            Report.Line(
                output,
                "speakers",
                string.Join(
                    ", ",
                    read.Channels.Select(
                        channel => $"channel {channel.Index}: {channel.SpeakerLabels.Count}")));
            Report.Line(output, "turns", $"{verdict.Turns}");

            foreach (var said in verdict.WorthSaying)
            {
                Report.Line(output, "silent", said);
            }

            foreach (var broken in verdict.Broken)
            {
                Report.Line(output, "not held", broken);
            }

            if (!verdict.Held)
            {
                // Kept, named, and left under the working name. It is paid for, so nothing deletes
                // it; and it is not a response to build on, so it must not wear the name that says
                // it is one — `LiveCheck.Of` reads that name back to decide what a later run need
                // not buy, and a response nobody was heard in reported as `already` is the file
                // somebody fixes `--language` for and then never sends again.
                Report.Line(
                    output,
                    "kept",
                    $"{partial.FullName} — paid for and here, under a name that says it is not a "
                    + "response to read a meeting from. Run this again over the same folder and "
                    + $"'{sent.File.Name}' is sent again; the others already answered are not.");

                held = false;
                Unsent(output, run.Audio.Skip(index + 1));
                break;
            }

            // The working name becomes the real one, and only now. Nothing did this until
            // 2026-09-11: every byte went to `<stem>-<stamp>.json.partial`, the line above named a
            // path that was not there, and the parser opened `FileMode.Open` on it and threw
            // `FileNotFoundException` — after the money was spent. It is also what `LiveCheck.Of`
            // reads back, so until this line the resume rule could never match anything.
            // `overwrite: false`, because the one thing this folder holds is responses somebody
            // paid for. A name already taken is a second run inside the same second, and it takes
            // the catch above: the fragment is named and nothing is written over.
            try
            {
                // `File.Move` and not the handle's own spelling, which this repository bans
                // outright: `UnfinishedRecordingsTests.Nothing_removes_or_renames_through_a_handle`
                // is a sweep over the text of `src/`, and what stands between a person and a
                // deleted recording is only as good as the spelling it can see.
                File.Move(partial.FullName, response.FullName, overwrite: false);
            }
            catch (IOException taken)
            {
                _ = taken;
                throw new CommandException(
                    $"'{response.FullName}' is already there, so what just came back for "
                    + $"'{sent.File.Name}' is still at '{partial.FullName}'. Nothing here writes "
                    + "over a response somebody paid for. Move that file somewhere of its own, or "
                    + "send again into a folder that is empty.");
            }

            Report.Line(output, "response", response.FullName);
        }

        Report.Line(
            output,
            "fixtures",
            "a response here is not a fixture. What one has to be — every word replaced from the "
            + "closed vocabulary, every timing, confidence and channel number left as sent — is in "
            + "tests/fixtures/deepgram/README.md, and there is no tool that does it to a response "
            + "from here. Nothing happens to these files automatically.");

        return held && !stopped ? Cli.Ok : Cli.Refused;
    }

    /// <summary>
    /// Throws away what a failed call left under the working name, or names it when there is
    /// something there to name.
    /// </summary>
    /// <remarks>
    /// The two are different things to whoever is standing at the prompt. Nought bytes is a call
    /// that never delivered — a key the provider would not accept, a request it refused, a host
    /// that would not resolve — and leaving an empty file behind would be litter under a name that
    /// claims to be about a paid response. Bytes are a prefix of an answer that was transcribed and
    /// may have been charged for, and that is worth keeping and worth naming.
    /// </remarks>
    private static void Abandon(FileInfo partial, TextWriter output)
    {
        partial.Refresh();
        if (!partial.Exists)
        {
            return;
        }

        if (partial.Length == 0)
        {
            File.Delete(partial.FullName);
            return;
        }

        Report.Line(
            output,
            "partial",
            $"{partial.FullName} — the start of an answer and not an answer. Whether it was "
            + "charged for is not something this end can tell. It is not a fixture and must not be "
            + "filed as one.");
    }

    /// <summary>Names what a run stopped short of sending, so nobody has to work it out.</summary>
    private static void Unsent(TextWriter output, IEnumerable<LiveAudio> rest)
    {
        var left = rest.ToArray();
        if (left.Length == 0)
        {
            return;
        }

        Report.Line(
            output,
            "not sent",
            $"{string.Join(", ", left.Select(sent => sent.File.Name))} — the run stopped on the "
            + "response above rather than buying the rest of the folder on top of it.");
    }

    /// <summary>
    /// What somebody at this prompt typed, and nothing at all when there is nobody at it.
    /// </summary>
    /// <remarks>
    /// Redirected is not a person, in either direction. A scheduled run, a script, a piped line and
    /// a test host all arrive here with no keyboard, and a confirmation something could pipe in is
    /// a confirmation that spends somebody's money without them; a run whose output went to a file
    /// has nobody looking at the question, and asking it into a log and then blocking on a keyboard
    /// is the silent hang. It echoes, unlike <c>key --set</c>'s keyboard: a number of minutes is
    /// not a secret, and somebody has to see what they typed to know it was taken.
    /// </remarks>
    private static string? FromAPersonAtThisPrompt()
    {
        try
        {
            return Console.IsInputRedirected || Console.IsOutputRedirected
                ? null
                : Console.ReadLine();
        }
        catch (Exception nobodyThere) when (nobodyThere is InvalidOperationException or IOException)
        {
            // A console handle that is not there at all, which is the answer the two other
            // keyboards in this assembly already give to the same question.
            return null;
        }
    }
}
