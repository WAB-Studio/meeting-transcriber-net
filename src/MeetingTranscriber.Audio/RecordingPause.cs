using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Whether one recording is paused, and what reaches its spools while it is: the same blocks the
/// devices handed over, at the same positions and the same instants, with nothing in them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pause is silence while it is recorded and nothing once the meeting is made.</b> The spool
/// goes on receiving blocks, with nothing in them, and that is not the meeting's last word: each
/// pause is written down beside the blocks (<see cref="RecordingPauses"/>) and
/// <see cref="MeetingAudio.Materialise"/> leaves those stretches out when it pours the blocks into
/// the recording. A meeting somebody paused for ten minutes is ten minutes shorter than the clock
/// on the wall, and what was said after the pause is where it was said in the meeting that remains.
/// </para>
/// <para>
/// Substituting the block rather than dropping it is still what the spool does, for two reasons
/// that have not changed. The timeline gives up on a source that has fallen half a minute behind,
/// so any pause longer than that would lose the meeting rather than quieten it; and the blocks keep
/// the positions the devices counted, so the marks and the blocks are two accounts of one clock and
/// the cut is made at the end from both rather than guessed at while recording.
/// </para>
/// <para>
/// <b>One of these per recording, not one per source.</b> That is the whole reason the state and
/// the substitution are one object: a flag on each source would be two writes, and between them
/// one channel records silence while the other records the room. Nothing downstream could see that
/// — it reads exactly like a moment when only one person was talking — and it is somebody's voice
/// going into a file after they pressed pause. One flag, both callbacks reading it, so the
/// transition is a single write and the two channels can only disagree for whichever blocks were
/// already in flight.
/// </para>
/// <para>
/// Blocks already in flight are the irreducible part, and it is bounded rather than unhandled: a
/// callback that read this a moment before the press finishes writing what its device had already
/// handed over, which is a fraction of the packet size — tens of milliseconds. Closing that would
/// mean blocking the capture thread on the thread that pressed the button, which risks the
/// recording to tidy up its edge.
/// </para>
/// </remarks>
public sealed class RecordingPause
{
    /// <summary>
    /// The nothing every paused block is made of, shared by every recording in the process.
    /// </summary>
    /// <remarks>
    /// Shared safely because it is never written to. A scratch buffer reused across blocks would
    /// need a thread and a lifetime argument; a constant does not — two devices pausing at once
    /// hand out slices of the same zeroes, and neither can see the other's block change under it,
    /// because nothing ever changes it. Sized past the tens of milliseconds a shared-mode device
    /// hands over at a time, with anything larger falling back to a block of its own rather than
    /// to a ceiling nobody would find out they had hit.
    /// </remarks>
    private static readonly byte[] Silence = new byte[64 * 1024];

    /// <summary>
    /// Written by whoever pressed pause and read on both capture threads, which never synchronise
    /// on anything else — so a pause nobody's device noticed would go on recording the room.
    /// </summary>
    private volatile bool paused;

    /// <summary>What the clock on screen reads, and so what is left out of it.</summary>
    private readonly Lock counting = new();

    private Duration before;
    private UtcTimestamp? since;

    /// <summary>Whether the meeting is paused.</summary>
    public bool IsPaused => paused;

    /// <summary>
    /// Pauses the meeting, at <paramref name="at"/> on the clock the screen reads. Saying it twice is
    /// saying it once, and says so.
    /// </summary>
    /// <returns>Whether the state changed.</returns>
    public bool Pause(UtcTimestamp at)
    {
        lock (counting)
        {
            if (paused)
            {
                return false;
            }

            since = at;
            paused = true;
            return true;
        }
    }

    /// <summary>Lets the devices reach the recording again, at <paramref name="at"/>.</summary>
    /// <returns>Whether the state changed.</returns>
    public bool Resume(UtcTimestamp at)
    {
        lock (counting)
        {
            if (!paused)
            {
                return false;
            }

            before += Stretch(at);
            since = null;
            paused = false;
            return true;
        }
    }

    /// <summary>
    /// How much of the meeting so far was paused, a pause that is still going on counted to
    /// <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// What the clock on screen takes away, so that it reads the length of what is being written. A
    /// clock that stepped back is no time rather than a throw, the way <c>RecordingClock</c> reads it.
    /// </remarks>
    public Duration PausedFor(UtcTimestamp now)
    {
        lock (counting)
        {
            return before + Stretch(now);
        }
    }

    private Duration Stretch(UtcTimestamp to) =>
        since is { } from && to > from ? to - from : Duration.Zero;

    /// <summary>
    /// What <paramref name="packet"/> is worth to the recording right now: itself, or the same
    /// block with the audio taken out and every number on it left alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place the pause is applied, and every source's blocks go through it — the meter and
    /// the tally are fed what this returns as well as the spool, so a paused meeting reads as
    /// silent on screen and nothing the microphone caught is written anywhere.
    /// </para>
    /// <para>
    /// Zero bytes are silence in both of the encodings this build reads — a signed integer at zero
    /// and a float at zero are both the middle of the scale. An unsigned encoding would put full
    /// negative deflection here instead, so <see cref="SampleEncoding"/> gaining one is a change
    /// this method has to be told about; it is an enum of two, and this comment is where somebody
    /// adding the third one is looking.
    /// </para>
    /// </remarks>
    public CapturePacket Reaching(CapturePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // Read once. This is another thread's to change and the packet is one instant of the
        // meeting: reading it twice could tally a block as heard and spool it as silence.
        return paused ? packet with { Samples = Nothing(packet.Samples.Length) } : packet;
    }

    /// <summary>That many bytes of silence, without allocating for the ordinary block.</summary>
    private static ReadOnlyMemory<byte> Nothing(int bytes) =>
        bytes <= Silence.Length ? Silence.AsMemory(0, bytes) : new byte[bytes];
}
