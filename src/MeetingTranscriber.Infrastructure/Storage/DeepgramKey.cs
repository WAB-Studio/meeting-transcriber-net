using System.ComponentModel;
using System.Runtime.Versioning;

using Meziantou.Framework.Win32;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// Something about the Deepgram key this machine holds, or does not hold, that the caller has to
/// be told: there is no key, what was offered is not one, this machine would not store it, or one
/// was written and did not come back.
/// </summary>
/// <remarks>
/// Its own type rather than <c>CommandException</c>, which is declared in the command line and is
/// not something this project can see. Every one of them is an answer and not a defect — somebody
/// asked to transcribe on a machine with no key on it, or pasted something that was not a key — so
/// the command line lists this among the refusals and a front end gets a sentence rather than a
/// stack trace.
/// <para>
/// No message here ever carries a key or any part of one. A sentence quoting half a secret is the
/// thing this file exists to stop. What Windows itself said travels as the inner exception, for
/// whoever is diagnosing rather than for whoever is standing at a prompt.
/// </para>
/// </remarks>
public sealed class DeepgramKeyException : Exception
{
    public DeepgramKeyException(string message)
        : base(message)
    {
    }

    public DeepgramKeyException(string message, Exception cause)
        : base(message, cause)
    {
    }
}

/// <summary>
/// The Deepgram key, in Windows Credential Manager, under one name.
/// </summary>
/// <remarks>
/// <para>
/// A key is read for the length of the operation that needs it and is held nowhere else — not in a
/// field, not in <c>app_state</c>, not in a manifest, not in a log line, and never on a command
/// line, where it would be in this machine's shell history and in the process list of everything
/// that can see it. The half of that nothing here can enforce is <em>nowhere else</em>, and what
/// holds it is two sweeps in <c>DeepgramKeyTests</c>:
/// <c>Nothing_but_the_key_itself_reaches_the_credential_store</c>, which fails when a second file
/// under <c>src/</c> names <c>CredentialManager</c>, and
/// <c>Nothing_but_the_key_itself_reads_a_Deepgram_key</c>, which fails when a fourth names this
/// type — the three on it are <c>MeetingTranscriber.Cli\KeyCommands.cs</c>, which puts a key there,
/// <c>MeetingTranscriber.Cli\DeepgramCommands.cs</c>, which spends with one, and this file.
/// </para>
/// <para>
/// An instance over a target name, with one static factory for the name the product uses. The
/// constructor touches nothing: it holds a name, and only <see cref="Keep"/>, <see cref="Read"/>,
/// <see cref="Forget"/> and <see cref="IsThere"/> reach the store. That is what lets a test drive a
/// whole command line without a developer's real key being read, and what lets the tests run
/// against the real Windows store on a target of their own instead of against a fake vault nobody
/// would learn anything from.
/// </para>
/// <para>
/// Windows-only, said outright rather than left to the platform this assembly happens to be built
/// for. <c>MeetingTranscriber.Infrastructure</c> is plain <c>net10.0</c> because
/// <c>MeetingTranscriber.Processing</c> references it and must not be dragged onto a Windows target
/// framework — <c>docs/layout.md</c> says why — so a Windows-only type lives here under this
/// attribute and nowhere else.
/// </para>
/// <para>
/// With a version on it, and the version is the one every Windows project here declares it supports
/// — <c>TargetPlatformMinVersion</c> is <c>10.0.22000.0</c> in all four. A bare <c>windows</c> reads
/// as <em>every Windows there has ever been</em>, and the credential package annotates its own
/// surface from <c>windows5.1.2600</c>, so a bare one warns CA1416 at all three call sites below and
/// <c>-warnaserror</c> makes that a failed build in CI rather than on the machine it was written on.
/// This product's own floor is the true number to say; the package's is a fact about the package.
/// </para>
/// <para>
/// What a caller in <c>MeetingTranscriber.Processing</c> does about that is not settled here and is
/// not settled by this being annotated. That project is platform-neutral and stays so, which means
/// the key reaches it as a <c>string</c> handed over by a front end that is already Windows —
/// never by <c>Processing</c> naming this type.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.22000.0")]
public sealed class DeepgramKey
{
    /// <summary>
    /// The one name this install's key is stored under. It is what is on disk in Credential
    /// Manager, so renaming it loses whatever key somebody already had — the same kind of fact as
    /// a table name, and pinned by a test for the same reason.
    /// </summary>
    private const string ThisInstall = "MeetingTranscriber:deepgram";

    /// <summary>
    /// What a generic credential's user name is here. It is not a person and nothing reads it; it
    /// is what somebody sees beside the entry in Windows' own Credential Manager panel.
    /// </summary>
    private const string ItsUserName = "api-key";

    /// <param name="target">The name the credential is stored under.</param>
    public DeepgramKey(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Target = target;
    }

    /// <summary>The name this key is stored under in Credential Manager.</summary>
    public string Target { get; }

    /// <summary>
    /// Whether this machine holds a key, answered without handing the secret to the caller — so
    /// something that only needs to know whether there is one never has one in its hands.
    /// </summary>
    public bool IsThere => StoredKey() is not null;

    /// <summary>The key this install keeps, under the one name it keeps it under.</summary>
    public static DeepgramKey OfThisInstall() => new(ThisInstall);

    /// <summary>
    /// Puts <paramref name="key"/> on this machine, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trimmed here and not at whichever front end took it. A key pasted with a trailing space
    /// fails every Deepgram call with an authentication error nobody can see the cause of, and both
    /// front ends deserve the same answer to the same mistake.
    /// </para>
    /// <para>
    /// A write that fails loudly and a write that quietly did nothing are both refusals here.
    /// Windows says no to a blob over its size ceiling, to a vault a group policy has closed, and
    /// to a machine where credential storage is turned off, and it says it by throwing out of
    /// advapi32 — which, left alone, would reach somebody who has just typed a secret at an
    /// unechoed prompt as a stack trace. The silent half is the read-back: <em>kept</em> means the
    /// key is on this machine afterwards.
    /// </para>
    /// <para>
    /// No refusal here names the key or any part of it, and none of them names what Windows said
    /// either — a driver's own sentence about a credential is not something to put in front of
    /// somebody, and the inner exception carries it for whoever is diagnosing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">There is no key at all, which is a caller's bug.</exception>
    /// <exception cref="DeepgramKeyException">
    /// The key is nothing but whitespace, this machine refused to store it, or what came back is not
    /// what went in.
    /// </exception>
    public void Keep(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var secret = key.Trim();
        if (secret.Length == 0)
        {
            // A refusal and not an ArgumentException, because a paste that came out empty is
            // something somebody did rather than something a caller got wrong — and Cli.IsRefusal
            // is what turns this into an answer instead of a stack trace for every front end,
            // including the ones that do not exist yet.
            throw new DeepgramKeyException(
                "That is not a Deepgram key — there is nothing in it. Whatever key this machine "
                + "already held is still there.");
        }

        try
        {
            CredentialManager.WriteCredential(
                Target, ItsUserName, secret, CredentialPersistence.LocalMachine);
        }
        catch (Exception refused) when (refused is ArgumentOutOfRangeException or Win32Exception)
        {
            // Two ways it says no and they arrive as different types: a secret over Windows' own
            // 2560-byte ceiling is caught before the call, and a vault a policy on this machine has
            // closed comes back out of advapi32. Both are this machine refusing to hold the key.
            throw new DeepgramKeyException(
                "This machine would not store the Deepgram key. A key far longer than one Deepgram "
                + "issues, and a vault a policy on this machine has closed, are the two reasons it "
                + "says no.",
                refused);
        }

        if (!string.Equals(Stored(), secret, StringComparison.Ordinal))
        {
            throw new DeepgramKeyException(
                $"The Deepgram key was written to '{Target}' and what came back is not what went "
                + "in, so nothing on this machine can be relied on to hold it. Nothing is quoted "
                + "here on purpose.");
        }
    }

    /// <summary>The key this machine holds.</summary>
    /// <exception cref="DeepgramKeyException">There is no key on this machine.</exception>
    /// <remarks>
    /// The sentence names no command. This project does not know which front end asked, and the one
    /// that did adds the <em>how</em> in its own words.
    /// </remarks>
    public string Read() =>
        StoredKey()
        ?? throw new DeepgramKeyException(
            "There is no Deepgram key on this machine, so nothing can be transcribed until one is "
            + "kept.");

    /// <summary>
    /// Takes the key off this machine. Forgetting one that was never there is not a failure: the
    /// caller asked for the key not to be on this machine, and it is not. The same shape
    /// <see cref="HumanLayer.Unlink"/> already states.
    /// </summary>
    public void Forget() => CredentialManager.TryDeleteCredential(Target);

    /// <summary>
    /// What Credential Manager holds under <see cref="Target"/>, or nothing when it holds nothing
    /// and nothing when what it holds is not a key.
    /// </summary>
    /// <remarks>
    /// An entry with an empty or whitespace blob — one somebody typed into Windows' own Credential
    /// Manager panel by hand — is nothing, so <em>there is a key on this machine</em> means the same
    /// thing on the way out as <see cref="Keep"/> makes it mean on the way in. Without this,
    /// <see cref="IsThere"/> would report a key kept and <see cref="Read"/> would hand out an empty
    /// string for something to spend money with.
    /// </remarks>
    private string? StoredKey() => Stored() is { } secret && secret.Trim().Length > 0 ? secret : null;

    /// <summary>
    /// What Credential Manager holds under <see cref="Target"/>, exactly as it holds it.
    /// </summary>
    private string? Stored() => CredentialManager.ReadCredential(Target)?.Password;
}
