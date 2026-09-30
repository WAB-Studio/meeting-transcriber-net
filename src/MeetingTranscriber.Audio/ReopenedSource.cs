namespace MeetingTranscriber.Audio;

/// <summary>
/// Which sources a channel that is already being recorded can go on being laid out by the same
/// sequence of frame positions on, and the words each of the others refuses in.
/// </summary>
/// <remarks>
/// <para>
/// One place, for the reason <see cref="ReplacedDevice"/> is one place. The rule used to be three
/// refusals written inside three <c>Open</c> implementations, and a caller had to know all three to
/// know what it could offer.
/// </para>
/// <para>
/// What decides is the shape of the source and never the channel number. Today the two agree —
/// both ways of obtaining channel 0 carry the sequence on and the microphone does not — and that is
/// three shapes happening to line up rather than the rule: a fourth way in is answered here, by its
/// shape, or the arm below refuses to build it.
/// </para>
/// <para>
/// <b>This is not the question "may a screen offer to re-open this source".</b> A microphone is
/// re-opened under a running channel today and it works —
/// <c>CaptureSession.OpenTheMicrophoneAgain</c> is that path, and an endpoint's stream numbers its
/// own frames so there is no sequence to carry and nothing here to refuse. What this answers is the
/// narrower thing the three <c>Open</c> bodies actually decided: whether the channel goes on being
/// placed by the instants it was already being placed by. A screen asking the wrong one of those
/// two would refuse the microphone retry that ships.
/// </para>
/// <para>
/// The one refusal left keeps its own words: a microphone numbers its own frames. A running channel
/// 0 used to be refused a program as well, on the argument that which program a recording follows
/// is what the recording is. #164 decided otherwise: a program's audio numbers no frames either, and
/// somebody who picked the wrong one of three processes called Teams had no way out but recording
/// the whole machine.
/// </para>
/// </remarks>
public static class ReopenedSource
{
    /// <summary>
    /// Whether a channel already being recorded can go on being laid out by the sequence it was
    /// being laid out by, on this source.
    /// </summary>
    /// <remarks>
    /// Both ways of obtaining channel 0. Each comes off a virtual device that numbers no frames, so
    /// the channel keeps the instants it already had and there is no seam to reconcile, whichever of
    /// the two it is moved onto. A microphone numbers its own frames and opens a stretch of its own.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// A way of opening a channel that has not been answered in this file. It is not asked and
    /// answered <see langword="false"/>, because a shape nobody has thought about is not the same
    /// as a shape that was thought about and refused.
    /// </exception>
    public static bool CarriesTheSequenceOn(CaptureTarget listening)
    {
        ArgumentNullException.ThrowIfNull(listening);

        return Refusing(listening) is null;
    }

    /// <summary>
    /// Throws unless <paramref name="destination"/> may go on being laid out by
    /// <paramref name="carryingOn"/>. Nothing at all when <paramref name="carryingOn"/> is null,
    /// which is a channel starting a sequence rather than continuing one — and is how every
    /// microphone is opened, re-opened included.
    /// </summary>
    /// <param name="destination">What the channel would be listening to from here on.</param>
    /// <param name="carryingOn">
    /// The sequence the channel is being laid out by, or null when it is starting one.
    /// </param>
    /// <exception cref="AudioCaptureException">
    /// A sequence was offered to a source that cannot take one, in that source's own words.
    /// </exception>
    public static void EnsureMayCarryOn(CaptureTarget destination, FramePositions? carryingOn)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (carryingOn is null)
        {
            return;
        }

        if (Refusing(destination) is { } refusal)
        {
            throw new AudioCaptureException(refusal);
        }
    }

    /// <summary>
    /// Why this shape will not take a sequence, in its own words, or <see langword="null"/> when it
    /// takes one.
    /// </summary>
    /// <remarks>
    /// One arm per shape, including the one that says yes, so a fourth way of opening a channel is
    /// one edit here rather than an answer inherited from whichever arm it happens to resemble. Its
    /// refusal is <see cref="NotSupportedException"/> and not <see cref="AudioCaptureException"/>:
    /// what has gone wrong is that a shape was added and this file was not, which is a build to fix
    /// and not a capture that failed, and <c>Cli.IsRefusal</c> would otherwise put a developer's
    /// instruction in front of somebody recording a meeting.
    /// </remarks>
    private static string? Refusing(CaptureTarget destination) => destination switch
    {
        CaptureTarget.TheWholeMachine => null,

        CaptureTarget.Program => null,

        CaptureTarget.Endpoint endpoint =>
            $"'{endpoint.Name}' numbers its own frames, so it cannot carry on placing a channel by "
            + "instants the way the device before it was placed: what says where its audio "
            + "belongs is its own counter, and that counter starts again at its own zero. "
            + "What it opens is a stretch of its own.",

        _ => throw new NotSupportedException(
            $"'{destination.Name}' is a way of opening a channel that has not said whether a "
            + "running channel may be carried on to it. Answer it in ReopenedSource."),
    };
}
