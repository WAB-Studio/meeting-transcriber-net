using System.Globalization;
using System.Runtime.Versioning;

using Meziantou.Framework.Win32;

namespace MeetingTranscriber.Testing;

/// <summary>
/// Where a test keeps a credential, and what clears up after the runs that did not finish.
/// </summary>
/// <remarks>
/// <para>
/// These suites write into the running user's real Windows credential store, because a fake vault
/// would only prove that this code calls the methods it calls. Each test gets a target of its own
/// and forgets it afterwards, and never the product's — a suite that wrote
/// <c>MeetingTranscriber:deepgram</c> would take a developer's real key away on the machine it ran
/// on.
/// </para>
/// <para>
/// What a per-test target does not survive is a run that never reaches its cleanup: Ctrl+C, a
/// cancelled CI job, a crashed test host. Those leave a credential behind, and
/// <c>CredentialPersistence.LocalMachine</c> means it outlives the reboot too — one more entry in
/// somebody's Credential Manager panel per interrupted run, forever, since nothing else knows the
/// name. So the first target handed out sweeps every one either suite has left long enough ago to
/// be dead. It is why the prefix is fixed and only the tail is a guid.
/// </para>
/// <para>
/// Sweeping on the way in rather than on the way out, deliberately: the run that leaves one behind
/// is by definition the run that does not get to clean up, so the only pass that can collect it is
/// a later one. What makes that safe is the minting time in the name and nothing else. Two suites
/// share this prefix and <c>dotnet test</c> runs them in parallel, so a sweep here is looking at
/// targets another live process made; an age no run of these reaches is what tells a dead one from
/// a live one, and it holds across processes, across assemblies and across two runs of the same
/// assembly at once. The older argument — that a live run made its guid after this sweep read the
/// list — was only ever true inside one process and stopped being true the day the second suite
/// came through here.
/// </para>
/// <para>
/// The process id was the other way to say <em>alive</em> and it is worse for this, which is why
/// the time is what is in the name. These credentials are <c>LocalMachine</c> and outlive the
/// reboot; a process id does not, so a target left by a run that died would be swept or spared by
/// whatever happens to hold that id now.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.22000.0")]
public static class TestVault
{
    /// <summary>What every target either suite writes begins with, so a sweep can find them all.</summary>
    private const string Prefix = "MeetingTranscriber:test:";

    /// <summary>How the minting time is written into a target, and read back out of one.</summary>
    private const string Stamp = "yyyyMMdd'T'HHmmss";

    /// <summary>
    /// How old a target has to be before a sweep may take it. These suites run in minutes; an hour
    /// is longer than any run of them and shorter than a developer will tolerate the litter.
    /// </summary>
    private static readonly TimeSpan OlderThanAnyRunOfThese = TimeSpan.FromHours(1);

    private static readonly Lock Swept = new();
    private static bool sweptAlready;

    /// <summary>A credential name no other test is using, after the dead ones are cleared.</summary>
    public static string ATargetOfItsOwn()
    {
        SweepWhatEarlierRunsLeft();

        // DateTime.UtcNow and not the domain's clock, deliberately: this is a segment of a
        // credential name in a test helper and never an instant that crosses into the domain,
        // which is what the contract's rule about a bare DateTime is about.
        return $"{Prefix}{DateTime.UtcNow.ToString(Stamp, CultureInfo.InvariantCulture)}:{Guid.NewGuid():n}";
    }

    private static void SweepWhatEarlierRunsLeft()
    {
        lock (Swept)
        {
            if (sweptAlready)
            {
                return;
            }

            sweptAlready = true;
        }

        foreach (var stale in CredentialManager.EnumerateCredentials($"{Prefix}*"))
        {
            // Whatever this finds is either suite's own litter, under a name nothing else writes —
            // and only the ones old enough that the run which made them is certainly gone.
            if (Dead(stale.ApplicationName))
            {
                CredentialManager.TryDeleteCredential(stale.ApplicationName);
            }
        }
    }

    /// <summary>
    /// Whether this target was minted long enough ago that the run which made it is certainly gone.
    /// </summary>
    /// <remarks>
    /// A name this cannot read a minting time out of is left alone rather than taken. It is litter
    /// from a build before this one — an older spelling, or a checkout of an older commit running
    /// beside this one out of another worktree — and the second of those is a live target, under a
    /// name this type has no way of telling apart from a dead one. Taking only what it can prove is
    /// dead costs an old entry somebody clears by hand once; taking what it cannot costs another
    /// run its credential in the middle of a test.
    /// </remarks>
    private static bool Dead(string target)
    {
        if (!target.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var tail = target[Prefix.Length..];
        var cut = tail.IndexOf(':', StringComparison.Ordinal);

        return cut >= 0
            && DateTime.TryParseExact(
                tail[..cut],
                Stamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var minted)
            && DateTime.UtcNow - minted > OlderThanAnyRunOfThese;
    }
}
