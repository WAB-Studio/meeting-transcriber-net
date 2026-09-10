using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// ISC-84's other half at the prompt: a key is typed and never given as an argument, and no
/// refusal and no report ever repeats one.
/// </summary>
/// <remarks>
/// <para>
/// Two ways in, and which one a test takes is a rule rather than a preference.
/// <see cref="CommandLine.Of"/> drives <see cref="Cli.Run"/> and binds
/// <see cref="DeepgramKey.OfThisInstall"/>, so it is used only where the command refuses before the
/// store is reached — which is safe because constructing a <see cref="DeepgramKey"/> touches
/// nothing, and it is the only way to prove the exit code somebody actually gets. Everything else
/// drives the four-argument overload with a key on a target of its own.
/// </para>
/// <para>
/// No test drives <c>key</c> with no flags through <see cref="CommandLine.Of"/>: that would ask
/// <see cref="DeepgramKey.IsThere"/> about the running user's own key and make the answer a
/// property of the machine the suite happened to run on.
/// </para>
/// </remarks>
public class KeyCommandTests : IDisposable
{
    /// <summary>Not a key. Long enough that a report printing a tail of it is caught.</summary>
    private const string NotAKey = "sk-not-a-real-key";

    /// <summary>The tail of it, which is what a report trying to be helpful would print.</summary>
    private const string ItsTail = "not-a-real-key";

    private readonly DeepgramKey kept = new(TestVault.ATargetOfItsOwn());

    /// <summary>The line as this command's own table entry has it parsed.</summary>
    /// <remarks>
    /// Not <c>Arguments.Parse(...)</c> bare. What stops a refusal repeating a key back is a
    /// sentence <c>Cli</c>'s table hands the parser, so a test that parsed without it would be
    /// driving a different command line from the one somebody types.
    /// </remarks>
    private static Arguments Typed(params string[] arguments) =>
        Arguments.Parse(arguments, KeyCommands.TypedAtThePrompt);

    public void Dispose()
    {
        kept.Forget();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_key_typed_at_the_prompt_is_kept_and_the_report_never_says_it()
    {
        using var output = new StringWriter();

        var code = KeyCommands.Key(Typed("--set"), output, kept, () => NotAKey);

        code.ShouldBe(Cli.Ok);
        kept.Read().ShouldBe(NotAKey);
        output.ToString().ShouldContain("kept");
        output.ToString().ShouldNotContain(NotAKey);
        output.ToString().ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// The trap this command is about. <see cref="Arguments.Flag"/> quotes what it got, so
    /// <c>--set</c> read through it would put the key on the screen and into whatever is capturing
    /// this program's error stream — which is the one thing this command exists to prevent.
    /// </summary>
    [Fact]
    public void A_key_typed_after_the_flag_is_refused_without_being_repeated()
    {
        var run = CommandLine.Of("key", "--set", NotAKey);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--set");
        run.Error.ShouldContain("at the prompt");
        run.Error.ShouldNotContain(NotAKey);
        run.Error.ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// And with no flag at all, where what would otherwise answer is
    /// <see cref="Arguments.EnsureNothingLeftOver"/> — whose sentence lists every positional it was
    /// handed.
    /// </summary>
    [Fact]
    public void A_key_typed_with_no_flag_at_all_is_refused_without_being_repeated()
    {
        var run = CommandLine.Of("key", NotAKey);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("at the prompt");
        run.Error.ShouldNotContain(NotAKey);
        run.Error.ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// The spelling a guard on one reader does not reach, and the reason the rule is a property of
    /// the whole line. This grammar takes a whole <c>--</c> token as an option name, so
    /// <c>--set=&lt;key&gt;</c> is an option <em>named</em> <c>--set=&lt;key&gt;</c> that no reader
    /// of this command ever asks for — it walks past both flags and comes back out of
    /// <see cref="Arguments.EnsureNothingLeftOver"/>, which names every option nothing read.
    /// </summary>
    /// <remarks>
    /// <c>--flag=value</c> is the GNU spelling, so somebody reading <c>key [--set | --forget]</c>
    /// and typing <c>--set=&lt;key&gt;</c> is not an exotic input, and what they would have got is
    /// their key on the error stream and in whatever is capturing it.
    /// </remarks>
    [Fact]
    public void A_key_typed_after_an_equals_sign_is_refused_without_being_repeated()
    {
        var run = CommandLine.Of("key", $"--set={NotAKey}");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("at the prompt");
        run.Error.ShouldNotContain(NotAKey);
        run.Error.ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// The same, for the refusal <see cref="Arguments.Parse"/> itself makes. Every message this
    /// command's line can produce is the one sentence, including the ones no reader of this command
    /// asks for.
    /// </summary>
    [Fact]
    public void A_key_typed_twice_is_refused_without_being_repeated()
    {
        var run = CommandLine.Of("key", $"--set={NotAKey}", $"--set={NotAKey}");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldNotContain(NotAKey);
        run.Error.ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// And a guess at a subcommand is not accused of leaking a key. The sentence says what is wrong
    /// with the line first and where a key goes second, so it is true for both.
    /// </summary>
    [Fact]
    public void A_guess_at_a_subcommand_is_told_what_is_wrong_with_the_line()
    {
        var run = CommandLine.Of("key", "status");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("takes nothing of its own");
    }

    [Fact]
    public void A_prompt_nobody_typed_anything_at_keeps_no_key()
    {
        using var output = new StringWriter();

        Should.Throw<CommandException>(
            () => KeyCommands.Key(Typed("--set"), output, kept, () => null));
        kept.IsThere.ShouldBeFalse();

        Should.Throw<CommandException>(
            () => KeyCommands.Key(Typed("--set"), output, kept, () => "   "));
        kept.IsThere.ShouldBeFalse();
    }

    /// <summary>
    /// A yes-or-no question, answered from <see cref="DeepgramKey.IsThere"/> so the report cannot
    /// hold the secret at all — no length, no last four, no fingerprint. A tail of a secret is a
    /// smaller secret.
    /// </summary>
    [Fact]
    public void Asking_what_this_machine_holds_says_kept_or_not_kept_and_nothing_else()
    {
        using var output = new StringWriter();
        KeyCommands.Key(Typed("--set"), output, kept, () => NotAKey);

        using var holding = new StringWriter();
        KeyCommands.Key(Typed(), holding, kept, Nobody);
        holding.ToString().ShouldContain("kept");
        holding.ToString().ShouldNotContain("not kept");
        holding.ToString().ShouldNotContain(ItsTail);

        using var forgetting = new StringWriter();
        KeyCommands.Key(Typed("--forget"), forgetting, kept, Nobody);
        forgetting.ToString().ShouldContain("forgotten");

        using var empty = new StringWriter();
        KeyCommands.Key(Typed(), empty, kept, Nobody);
        empty.ToString().ShouldContain("not kept");
        empty.ToString().ShouldNotContain(ItsTail);
    }

    /// <summary>
    /// Two opposite asks in one line say nothing this program can act on, so neither is guessed at:
    /// reading them as an ordered preference would take somebody's key away because they typed both.
    /// </summary>
    [Fact]
    public void Both_at_once_is_a_misuse()
    {
        using var output = new StringWriter();
        kept.Keep(NotAKey);

        Should.Throw<UsageException>(
            () => KeyCommands.Key(Typed("--set", "--forget"), output, kept, Nobody));

        kept.Read().ShouldBe(NotAKey);
    }

    /// <summary>A keyboard nobody is at, for the paths that must never reach one.</summary>
    private static string? Nobody() =>
        throw new InvalidOperationException("Nothing on this path may ask for a key to be typed.");
}
