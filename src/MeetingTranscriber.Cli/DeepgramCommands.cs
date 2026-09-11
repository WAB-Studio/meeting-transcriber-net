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
/// so that the only overload a suite can reach cannot spend: the one implementation that reads this
/// machine's key and builds a client is private to <see cref="DeepgramCommands"/>, and the overload
/// that binds it is <see langword="internal"/> — which in this repository means unreachable, there
/// being no <c>InternalsVisibleTo</c> anywhere. What a test can hand over here is something that
/// does not send, and writing one that does would mean building an <c>HttpClient</c> under
/// <c>tests/</c>, which is a deliberate act with a check standing over it rather than a line
/// somebody slips in.
/// </remarks>
public delegate int Sending(LiveCheck run, DirectoryInfo into, string language, TextWriter output);

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
    /// The key is read here and not before, so a run nobody confirmed reaches the credential store
    /// not at all. The blocking wait is the shape a synchronous entry point takes: nothing in this
    /// project is <see langword="async"/>, the command table's delegate is not, and nothing here
    /// runs on a UI thread.
    /// </remarks>
    private static int WithThisMachinesKey(
        LiveCheck run, DirectoryInfo into, string language, TextWriter output) =>
        SendAsync(run, into, language, DeepgramKey.OfThisInstall().Read(), output)
            .GetAwaiter()
            .GetResult();

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
    /// No <see cref="CancellationToken"/> is passed, and
    /// <see cref="DeepgramTranscription.SendAsync"/> says what that costs: the client's timeout
    /// stops covering anything once the response headers arrive, so a provider that answers and
    /// then goes quiet is bounded by nothing here. What bounds it is the person at the prompt, who
    /// can press Ctrl+C — and a deadline for the body read would be a number nobody has measured
    /// standing over a failure nobody has had. The day one is measured is the day it goes here.
    /// </para>
    /// </remarks>
    private static async Task<int> SendAsync(
        LiveCheck run, DirectoryInfo into, string language, string key, TextWriter output)
    {
        using var http = new HttpClient { Timeout = LongEnoughForAWholeMeeting };
        var transcription = new DeepgramTranscription(http);

        // One stamp for the run and not one per file, because what it identifies is a run: files
        // that straddle a second would otherwise stop sorting and grouping together, which is the
        // whole of what it is for. The clock is the machine's, for the reason Clock's own remarks
        // give. How it is spelled is `LiveCheck`'s, because `LiveCheck.Of` reads these names back
        // to leave out what an earlier run already bought.
        var stamp = LiveCheck.StampOf(Clock.Now());
        var held = true;

        for (var index = 0; index < run.Audio.Count; index++)
        {
            var sent = run.Audio[index];
            var response = new FileInfo(
                Path.Combine(into.FullName, LiveCheck.ResponseNamed(sent.File, stamp)));
            var partial = new FileInfo(response.FullName + RecordingFiles.UnfinishedSuffix);

            // A working name, and the point of it: SendAsync's own remarks say a caller writing
            // straight at the final name is left with a truncated artifact on any failure after the
            // first byte. A .json in this folder is a whole response; anything under the corpus's
            // own unfinished suffix is not one and says so in its name.
            long bytes;
            try
            {
                await using var stream = partial.Create();
                bytes = await transcription
                    .SendAsync(sent.File, new DeepgramRequest(sent.Profile, language), key, stream)
                    .ConfigureAwait(false);
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

            // Named before anything reads it. A response that came back whole and that this build
            // cannot parse is exactly what a live probe is for, and it is the failure that leaves
            // the irreplaceable artifact — so where it landed is on the screen before the parser is
            // given the chance to throw.
            Report.Line(output, "sent", $"{sent.File.Name} ({Report.Offset(sent.Length)})");
            Report.Line(output, "response", response.FullName);
            Report.Line(output, "bytes", bytes.ToString(CultureInfo.InvariantCulture));

            var read = DeepgramTranscriptParser.ParseFile(response.FullName, sent.Profile);
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
                held = false;
                Unsent(output, run.Audio.Skip(index + 1));
                break;
            }
        }

        Report.Line(
            output,
            "fixtures",
            "a response here is not a fixture. What one has to be — every word replaced from the "
            + "closed vocabulary, every timing, confidence and channel number left as sent — is in "
            + "tests/fixtures/deepgram/README.md, and there is no tool that does it to a response "
            + "from here. Nothing happens to these files automatically.");

        return held ? Cli.Ok : Cli.Refused;
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
