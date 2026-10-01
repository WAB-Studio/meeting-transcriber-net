using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The one place that decides what channel 0 is watched for, handed whatever channel 0 is
/// listening to: a followed program gets a <see cref="FollowedProgram"/> on it, and the whole
/// machine gets nothing, because there is no program in it to end.
/// </summary>
/// <remarks>
/// <para>
/// A type of its own so that the rule is written once. It used to be three hand-kept call sites in
/// <see cref="CaptureSession"/>, and a fourth path that moved the channel would have left the old
/// program named as the one that closed. <see cref="CaptureSession"/> hands every target over from
/// the one method that moves channel 0 and from opening, and nowhere else.
/// </para>
/// <para>
/// <b>Threads.</b> One writer, under the session's gate; one reader, the screen's thread, every
/// second, taking no gate — a move holds the gate for device deadlines of seconds, and a meter
/// that waited on it would freeze the window it is drawn in. So the watch is a volatile field,
/// replaced or cleared <i>before</i> the old one is disposed, over a handle whose wait cannot race
/// its own closing (see <see cref="FollowedProgram.HasGone"/>).
/// </para>
/// </remarks>
public sealed class ProgramWatch : IDisposable
{
    private volatile FollowedProgram? watching;

    /// <summary>
    /// Whether the program being watched ended since it began to be. False with no watch.
    /// </summary>
    public bool HasGone => watching?.HasGone ?? false;

    /// <summary>
    /// Watches what channel 0 is listening to from here on, and lets the watch before it go.
    /// </summary>
    /// <param name="channelZero">What channel 0 now listens to.</param>
    /// <exception cref="AudioContractException">
    /// The target feeds another channel, which is never a program channel 0 follows.
    /// </exception>
    public void Watch(CaptureTarget channelZero)
    {
        ArgumentNullException.ThrowIfNull(channelZero);

        if (channelZero.Channel != AudioChannel.Loopback)
        {
            throw new AudioContractException(
                $"Channel 0's watch was handed '{channelZero.Name}', which feeds the "
                + $"{channelZero.Channel} channel and is never a program channel 0 follows.");
        }

        var next = channelZero is CaptureTarget.Program program
            ? FollowedProgram.Watching(program.Process)
            : null;

        // In place first and disposed after, so a look from the screen's thread finds the old
        // watch or the new one, and never one being closed.
        var old = watching;
        watching = next;
        old?.Dispose();
    }

    /// <summary>Lets go of the watch. Saying it twice is saying it once.</summary>
    public void Dispose()
    {
        var old = watching;
        watching = null;
        old?.Dispose();
    }
}
