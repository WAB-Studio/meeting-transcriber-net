using System.Text;

using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The Deepgram key at a prompt: whether this machine holds one, and putting one there or taking
/// it away.
/// </summary>
/// <remarks>
/// <para>
/// A machine-level act, the same kind as <c>capture</c>, <c>recovery</c>, <c>rebuild</c> and
/// <c>import-audio</c> — <c>docs/layout.md</c> says that is what the prompt is for. Whether a key
/// field ever appears on a settings screen is not decided here and is not this command's to
/// decide; if one does, it goes through <see cref="DeepgramKey.OfThisInstall"/> like everything
/// else and the sweep in <c>DeepgramKeyTests</c> makes it declare itself.
/// </para>
/// <para>
/// It holds no rule of its own, which is <c>docs/layout.md</c>'s rule about this project and not a
/// preference. Trimming, refusing an empty key and reading a write back are
/// <see cref="DeepgramKey.Keep"/>'s, so a screen built later gets them for free; what is here is
/// argument parsing, a keyboard, a report and an exit code.
/// </para>
/// <para>
/// The four-argument form is public and holds every decision, for the reason
/// <see cref="WholeMachine"/>'s remarks give in full: a gate nothing in CI can stand over is a
/// claim resting on somebody having read it. The shape is deliberately the same, down to the
/// keyboard being a parameter — which is also what lets a test drive this against a store of its
/// own instead of against whatever key the person running the suite really has.
/// </para>
/// </remarks>
public static class KeyCommands
{
    /// <summary>
    /// What every refusal on this command's line says, in place of repeating what it was handed.
    /// </summary>
    /// <remarks>
    /// Handed to <see cref="Arguments.Parse"/> by this command's row in the table, so it answers
    /// every spelling rather than the two somebody thought of: <c>key --set &lt;key&gt;</c>,
    /// <c>key &lt;key&gt;</c>, <c>key --set=&lt;key&gt;</c>, the same flag twice, and whatever
    /// refusal is added to <see cref="Arguments"/> next year.
    /// <para>
    /// It says what is wrong with the line first and where a key goes second, because it is also
    /// what <c>key status</c> gets — a plausible guess at a subcommand rather than somebody leaking
    /// a secret. Accusing that person of typing their key would be a wrong diagnosis; this way the
    /// first sentence is true either way and the rest is the answer they need.
    /// </para>
    /// </remarks>
    public const string TypedAtThePrompt =
        "This command takes nothing of its own. A Deepgram key is typed at the prompt and never on "
        + "the command line, where it would be in this machine's history and in the process list. "
        + "Run 'meeting-transcriber key --set' on its own.";

    /// <summary>The command as the table runs it: this install's key, and this prompt's keyboard.</summary>
    public static int Key(Arguments arguments, TextWriter output) =>
        Key(arguments, output, DeepgramKey.OfThisInstall(), FromTheKeyboard);

    /// <summary>
    /// The whole of the command, with the vault and the keyboard handed in.
    /// </summary>
    /// <remarks>
    /// It throws where it refuses, exactly as every other command's body does;
    /// <see cref="Cli.Run"/> is the one place a <see cref="UsageException"/> or a
    /// <see cref="CommandException"/> becomes an exit code, and nothing here returns one of those
    /// codes itself.
    /// <para>
    /// Nothing on any path writes the secret to <paramref name="output"/>, and nothing derives a
    /// value from it — no length, no last four characters, no fingerprint. Somebody asking whether
    /// a key is kept is asking a yes-or-no question, and <see cref="DeepgramKey.IsThere"/> is what
    /// makes the report unable to hold the secret at all.
    /// </para>
    /// </remarks>
    /// <param name="arguments">What was typed after the command name.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="kept">The key on this machine.</param>
    /// <param name="typed">What somebody typed at the prompt, or nothing when nobody did.</param>
    public static int Key(
        Arguments arguments, TextWriter output, DeepgramKey kept, Func<string?> typed)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(typed);

        // Nothing here guards against a key being repeated back. The line was parsed knowing it
        // could carry one — Cli's table hands TypedAtThePrompt to Arguments.Parse — so every
        // refusal below, and every one Arguments makes about a spelling this command never reads,
        // is already that sentence.
        var set = arguments.Flag("--set");
        var forget = arguments.Flag("--forget");

        if (set && forget)
        {
            throw new UsageException(
                "--set puts a key on this machine and --forget takes one away. Ask for one of them.");
        }

        arguments.EnsureNothingLeftOver();

        if (set)
        {
            Report.Line(
                output, "deepgram key", "type it and press Enter. Nothing is shown as you type.");

            // Replaced with no confirmation where one is already there: --set is explicit, and
            // somebody re-setting a key means to replace it.
            var secret = typed();
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new CommandException("Nothing was typed, so nothing was kept.");
            }

            kept.Keep(secret);
            Report.Line(output, "deepgram key", "kept");
            return Cli.Ok;
        }

        if (forget)
        {
            kept.Forget();
            Report.Line(output, "deepgram key", "forgotten");
            return Cli.Ok;
        }

        Report.Line(output, "deepgram key", kept.IsThere ? "kept" : "not kept");
        return Cli.Ok;
    }

    /// <summary>
    /// What somebody typed, without ever echoing it: a key on screen is a key in whatever recorded
    /// that screen.
    /// </summary>
    /// <remarks>
    /// A redirected run reads the line as it comes, so a key held somewhere that is not this
    /// machine — a secret store a script asks, an environment variable a session set — can be piped
    /// in without ever being an argument. Writing one to a file first is the same exposure the
    /// command line is, and this supporting a pipe is not an argument for doing that.
    /// <para>
    /// A run with nobody at it is nobody typing rather than an error, which is the answer
    /// <see cref="WholeMachine"/>'s own keyboard gives for the same reason.
    /// </para>
    /// </remarks>
    private static string? FromTheKeyboard()
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                return Console.In.ReadLine();
            }

            var typed = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key is ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return typed.ToString();
                }

                if (key.Key is ConsoleKey.Backspace)
                {
                    // A character and not a char. One keystroke put one character in, and taking
                    // half of a surrogate pair back out would leave a lone surrogate buried in the
                    // middle of a key nobody can see — kept, read back equal, and failing every
                    // Deepgram call with an authentication error nobody can find the cause of.
                    Undo(typed);
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                }
            }
        }
        catch (Exception nobodyThere) when (nobodyThere is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Takes back the last character somebody typed, pair and all.</summary>
    private static void Undo(StringBuilder typed)
    {
        if (typed.Length == 0)
        {
            return;
        }

        var last = typed.Length - 1;
        var pair = last > 0 && char.IsLowSurrogate(typed[last]) && char.IsHighSurrogate(typed[last - 1]);
        typed.Length = pair ? last - 1 : last;
    }
}
