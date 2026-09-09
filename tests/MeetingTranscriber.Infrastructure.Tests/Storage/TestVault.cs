using System.Runtime.Versioning;

using Meziantou.Framework.Win32;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

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
/// name. So the first target handed out sweeps every one this suite has ever left. It is why the
/// prefix is fixed and only the tail is a guid.
/// </para>
/// <para>
/// Sweeping on the way in rather than on the way out, deliberately: the run that leaves one behind
/// is by definition the run that does not get to clean up, so the only pass that can collect it is
/// a later one. Two runs at once are still safe — each one's own targets are unique, and deleting a
/// stale target that another live run happens to be using is not possible, because that run made
/// its own guid after this sweep read the list.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.22000.0")]
internal static class TestVault
{
    /// <summary>What every target this suite writes begins with, so a sweep can find them all.</summary>
    private const string Prefix = "MeetingTranscriber:test:";

    private static readonly Lock Swept = new();
    private static bool sweptAlready;

    /// <summary>A credential name no other test is using, after the stale ones are cleared.</summary>
    public static string ATargetOfItsOwn()
    {
        SweepWhatEarlierRunsLeft();
        return $"{Prefix}{Guid.NewGuid():n}";
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
            // Whatever this finds is this suite's own litter, under a name nothing else writes.
            CredentialManager.TryDeleteCredential(stale.ApplicationName);
        }
    }
}
