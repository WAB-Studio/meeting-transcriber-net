namespace MeetingTranscriber.Audio;

/// <summary>
/// Whether a channel whose stream ended by itself is moved onto another device without anybody
/// being asked, and whether the device Windows now names is one worth opening.
/// </summary>
/// <remarks>
/// <para>
/// The whole rule the thread that follows a device reads, in one place, because it is where two
/// opposite promises meet. A microphone Windows takes away is followed at once: the person is still
/// speaking, Windows has already chosen what replaced it, and asking them to answer a prompt is
/// asking them to lose the half of the conversation they are in the middle of. Channel 0 is the
/// other promise entirely — what it would be moved onto is everything the machine plays, which puts
/// every notification and every other application into a file somebody sends to a transcription
/// service, and nothing is allowed to take that on because a stream ended.
/// </para>
/// <para>
/// It is here rather than inside the thread that does the following, for the reason
/// <see cref="SilentProgram"/> is not inside a screen: nothing on that thread can be run on a
/// machine with no devices, so a decision left in there is one no probe reaches. What it takes is
/// plain values — what a channel is listening to, whether it ended, what was tried last — so the
/// cases that matter can be driven without Windows answering about anything.
/// </para>
/// </remarks>
public static class ReplacedDevice
{
    /// <summary>
    /// Whether this source is one to move onto whatever Windows now names in its place.
    /// </summary>
    /// <param name="listening">What the source is listening to.</param>
    /// <param name="ended">Whether its stream ended by itself.</param>
    /// <remarks>
    /// An endpoint and nothing else, and it is the kind of thing rather than the channel number
    /// that answers: what can be replaced is a device, and a device is what an endpoint is. Neither
    /// way of obtaining channel 0 is one — a program that closed is a program that closed, and the
    /// whole machine's audio has no endpoint to lose — so neither is ever followed anywhere.
    /// </remarks>
    public static bool IsFollowed(CaptureTarget listening, bool ended)
    {
        ArgumentNullException.ThrowIfNull(listening);

        return ended && listening is CaptureTarget.Endpoint;
    }

    /// <summary>
    /// Whether opening <paramref name="replacement"/> is worth doing, given what was tried last.
    /// </summary>
    /// <param name="replacement">The endpoint Windows names now.</param>
    /// <param name="lastTried">
    /// The endpoint this channel was last moved onto by following, or nothing when it has not been
    /// followed anywhere yet.
    /// </param>
    /// <param name="broughtNothing">
    /// Whether that last move brought no audio at all — the device opened and then ended without
    /// handing over a block.
    /// </param>
    /// <remarks>
    /// <para>
    /// The bound on following, and it needs no number to be one. A device Windows keeps naming as
    /// the default and that keeps dying is the ordinary shape of a failing hub or a headset
    /// flapping, and without this the recording moves onto it every two seconds for the rest of the
    /// meeting: a line in the folder's changes, a stretch in the spool and a stream held to the end
    /// for each, so a two-hour meeting ends with a changes file longer than its transcript and none
    /// of it audio.
    /// </para>
    /// <para>
    /// What it does not refuse is the same endpoint after a move that <em>did</em> record something.
    /// A driver that resets is the same id and a different open, and the recording that heard
    /// twenty minutes through it before it dropped is one worth trying again — the evidence that a
    /// device is worth opening is that it was, not that it has a different id.
    /// </para>
    /// </remarks>
    public static bool IsWorthTrying(AudioDevice replacement, string? lastTried, bool broughtNothing)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        return !broughtNothing
            || !string.Equals(replacement.Id, lastTried, StringComparison.OrdinalIgnoreCase);
    }
}
