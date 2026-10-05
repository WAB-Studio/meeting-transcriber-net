using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.App;

/// <summary>
/// What the settings screen may do with this machine's Deepgram key: ask whether there is one, put
/// one there, and take it away. Never read it.
/// </summary>
/// <remarks>
/// <para>
/// A file of its own rather than <see cref="DeepgramKey"/> named inside <c>Configuracion</c>, and
/// the reason is what is missing here: there is no <c>Read</c>. The whole screen on the list
/// <c>DeepgramKeyTests.Nothing_but_the_key_itself_reads_a_Deepgram_key</c> allows would be a screen
/// that could read the key and put it in a sentence; this way it cannot reach the secret at all,
/// and <c>DeepgramKeyTests.The_settings_screen_s_way_to_the_key_cannot_read_it</c> keeps it so.
/// </para>
/// <para>
/// Every rule — trimming, refusing an empty key, reading a write back — is
/// <see cref="DeepgramKey.Keep"/>'s, so this screen and <c>meeting-transcriber key --set</c> give
/// the same answer to the same paste. Called on the UI thread: Credential Manager is a local call
/// that answers at once, and a press held in flight would be a state the row does not need.
/// </para>
/// </remarks>
internal static class KeepingThisMachinesKey
{
    /// <summary>Whether this machine holds a key, answered without the key reaching the caller.</summary>
    /// <exception cref="DeepgramKeyException">Windows would not say.</exception>
    public static bool IsThere => DeepgramKey.OfThisInstall().IsThere;

    /// <summary>Puts <paramref name="key"/> on this machine, in place of whatever was there.</summary>
    /// <exception cref="DeepgramKeyException">It has nothing in it, or this machine would not keep it.</exception>
    public static void Keep(string key) => DeepgramKey.OfThisInstall().Keep(key);

    /// <summary>Takes the key off this machine; there being none already is not a failure.</summary>
    /// <exception cref="DeepgramKeyException">Windows would not take it away.</exception>
    public static void Forget() => DeepgramKey.OfThisInstall().Forget();
}
