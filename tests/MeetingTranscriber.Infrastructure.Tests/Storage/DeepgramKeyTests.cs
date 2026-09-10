using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using MeetingTranscriber.Infrastructure.Storage;

using Meziantou.Framework.Win32;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// ISC-84: the Deepgram key lives in Windows Credential Manager and is read from nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// The real Windows credential store, not a fake vault. What a fake would prove is that this type
/// calls the methods it calls; what somebody needs is that a key written on this machine comes back
/// off this machine. Nothing here reaches the network or spends a credit — Credential Manager is
/// local and there is no Deepgram call in this repository to make — but this suite does write and
/// delete generic credentials in the running user's vault, which no suite did before it.
/// </para>
/// <para>
/// So every test but the last two works on a target of its own,
/// <c>MeetingTranscriber:test:&lt;guid&gt;</c>, and forgets it afterwards. Never
/// <see cref="DeepgramKey.OfThisInstall"/>: a suite that wrote the product's own target would take
/// a developer's real key away on the machine it ran on.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.22000.0")]
public class DeepgramKeyTests : IDisposable
{
    /// <summary>Not a key, and long enough that a report printing a tail of it is caught.</summary>
    private const string NotAKey = "sk-not-a-real-key";

    private readonly DeepgramKey key = new(TestVault.ATargetOfItsOwn());

    public void Dispose()
    {
        key.Forget();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_key_kept_on_this_machine_is_the_key_that_comes_back()
    {
        key.IsThere.ShouldBeFalse();

        key.Keep(NotAKey);

        key.IsThere.ShouldBeTrue();
        key.Read().ShouldBe(NotAKey);
    }

    /// <summary>
    /// A key pasted with a space around it fails every Deepgram call with an authentication error
    /// nobody can see the cause of, so the trim is here and not at whichever front end took it.
    /// </summary>
    [Fact]
    public void A_key_is_kept_without_the_space_around_it()
    {
        key.Keep($"  {NotAKey}  ");

        key.Read().ShouldBe(NotAKey);
    }

    [Fact]
    public void A_key_that_was_forgotten_is_not_on_this_machine_any_more()
    {
        key.Keep(NotAKey);

        key.Forget();

        key.IsThere.ShouldBeFalse();
        Should.Throw<DeepgramKeyException>(() => key.Read());
    }

    /// <summary>
    /// The caller asked for the key not to be on this machine, and it is not. Same shape
    /// <c>HumanLayer.Unlink</c> already states.
    /// </summary>
    [Fact]
    public void Forgetting_a_key_that_was_never_there_is_not_a_failure()
    {
        Should.NotThrow(() => key.Forget());

        key.IsThere.ShouldBeFalse();
    }

    /// <summary>
    /// Refused before anything is written, and the machine that has to say so is one that already
    /// holds a key: a guard below the write costs somebody the key they had over a paste that came
    /// out empty, which is the failure worth a test. An empty target is unobservable on a machine
    /// that held nothing — Windows will not store an empty secret — so the assertion that could be
    /// made there would be one nothing could break.
    /// </summary>
    /// <remarks>
    /// A refusal and not an <see cref="ArgumentException"/>, because a paste that came out empty is
    /// something somebody did. <c>Cli.IsRefusal</c> carries this type, so every front end answers
    /// it in words; an <see cref="ArgumentException"/> would reach the next one as a stack trace. A
    /// key that is null at all is the other kind of thing — a caller's bug — and stays one.
    /// </remarks>
    [Fact]
    public void A_key_that_is_not_a_key_is_refused_before_anything_is_written()
    {
        key.Keep(NotAKey);

        Should.Throw<DeepgramKeyException>(() => key.Keep(string.Empty));
        Should.Throw<DeepgramKeyException>(() => key.Keep("   "));
        Should.Throw<ArgumentNullException>(() => key.Keep(null!));

        key.Read().ShouldBe(NotAKey);
    }

    /// <summary>
    /// A key far longer than one Deepgram issues is what Windows itself says no to, by throwing out
    /// of advapi32. Left alone that reaches somebody who has just typed a secret at an unechoed
    /// prompt as a stack trace, so it is a refusal here — and the refusal quotes neither the key nor
    /// what Windows said.
    /// </summary>
    [Fact]
    public void A_key_this_machine_will_not_store_is_a_refusal_and_not_a_crash()
    {
        var refused = Should.Throw<DeepgramKeyException>(() => key.Keep(new string('k', 4000)));

        refused.InnerException.ShouldNotBeNull();
        refused.Message.ShouldNotContain("kkkk");
        key.IsThere.ShouldBeFalse();
    }

    /// <summary>
    /// An entry with nothing in it — one somebody typed into Windows' own Credential Manager panel
    /// by hand — is not a key, so <em>there is a key on this machine</em> means the same thing on
    /// the way out as <see cref="DeepgramKey.Keep"/> makes it mean on the way in. Otherwise the
    /// report says <c>kept</c> and the thing about to spend money gets an empty string.
    /// </summary>
    [Fact]
    public void A_credential_with_nothing_in_it_is_not_a_key()
    {
        CredentialManager.WriteCredential(
            key.Target, "api-key", "   ", CredentialPersistence.LocalMachine);

        key.IsThere.ShouldBeFalse();
        Should.Throw<DeepgramKeyException>(() => key.Read());
    }

    /// <summary>
    /// The name is what is on disk in Credential Manager, so renaming it loses whatever key
    /// somebody already had and nothing migrates it. Today that is a one-line fix because nothing
    /// has shipped; after it ships it is somebody typing their key in again.
    /// </summary>
    [Fact]
    public void The_key_this_install_keeps_lives_under_one_name() =>
        DeepgramKey.OfThisInstall().Target.ShouldBe("MeetingTranscriber:deepgram");

    /// <summary>
    /// Nothing but this type reaches Windows' credential store. The narrower half of <em>read from
    /// nowhere else</em>, and the one that actually guards it: a second reader that wanted to go
    /// unnoticed would call <c>CredentialManager.ReadCredential</c> itself and name no type of ours
    /// at all.
    /// </summary>
    /// <remarks>
    /// The project graph does most of this already — the credential package is referenced by
    /// <c>MeetingTranscriber.Infrastructure</c> alone, so no other project can call it — which is
    /// exactly why the residual risk is inside this project and worth a sweep.
    /// </remarks>
    [Fact]
    public void Nothing_but_the_key_itself_reaches_the_credential_store() =>
        Naming("CredentialManager")
            .ShouldBe(
                [Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "DeepgramKey.cs")],
                "the Windows credential store is reached by one file, so that one file is the whole "
                + "of what has to be read to know where a Deepgram key can go. A second caller is a "
                + "second place the key can be written down, and it does not have to name "
                + "DeepgramKey to be one.");

    /// <summary>
    /// And nothing but the key itself and the command that puts one there names the type. The wider
    /// half: who holds a key, as opposed to who can reach the store.
    /// </summary>
    /// <remarks>
    /// Matched as the whole word, so <c>DeepgramKeyException</c> does not trip it. Catching the
    /// failure is not reading the key — a screen's reportable-failure list will name that exception
    /// long before anything else reads a key, and a guard whose maintenance instruction is
    /// <em>widen the allowlist</em> stops being a guard after two widenings.
    /// </remarks>
    [Fact]
    public void Nothing_but_the_key_itself_reads_a_Deepgram_key() =>
        Naming(@"\bDeepgramKey\b(?!Exception)")
            .ShouldBe(
                [
                    Path.Combine("MeetingTranscriber.Cli", "DeepgramCommands.cs"),
                    Path.Combine("MeetingTranscriber.Cli", "KeyCommands.cs"),
                    Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "DeepgramKey.cs"),
                ],
                "a Deepgram key is read by the thing about to spend money with it and by nothing "
                + "else, so a fourth file naming this type is a place the key can be written down. "
                + "The answer is to add the caller here on purpose, saying what it does with the "
                + "key — DeepgramCommands is on it because it is the one thing that spends with a "
                + "key — and never to widen the rule.");

    /// <summary>
    /// Which files under <c>src/</c> match <paramref name="pattern"/>, as paths from the project
    /// folder down, in a fixed order. Both directions of every sweep above come off this: a file
    /// that should not be there shows up in the list, and one that stopped naming what it names
    /// goes missing from it, so a sweep that read nothing cannot be green.
    /// </summary>
    private static IReadOnlyList<string> Naming(string pattern)
    {
        var src = new DirectoryInfo(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Here())!, "..", "..", "..", "src")));

        return
        [
            .. src
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !Inside(file, "obj") && !Inside(file, "bin"))
                .Where(file => Regex.IsMatch(File.ReadAllText(file.FullName), pattern))
                .Select(file => file.FullName[(src.FullName.Length + 1)..])
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool Inside(FileInfo file, string folder) => file.FullName.Contains(
        $"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}",
        StringComparison.Ordinal);

    private static string Here([CallerFilePath] string file = "") => file;
}
