namespace MeetingTranscriber.Audio;

/// <summary>
/// The mark a finish holds over the folder it is reading, so that anything else looking at that
/// folder can tell a save that is running from a recording nobody stopped.
/// </summary>
/// <remarks>
/// <para>
/// The two look alike on disk and that is the whole problem. A finish opens the blocks the way any
/// reader does, so nothing holds them the way a capture does; the meeting has no length until the
/// save's last write, so the corpus still calls it waiting. A second reader — another window, a
/// prompt, the next start — therefore offers keep, export and discard over a meeting that is being
/// written down, and reads a length off blocks that are being poured. What a discard costs is now a
/// refusal rather than a recording: <see cref="UnfinishedRecordings.Remove"/> moves the folder aside
/// before it takes anything away, so a save this could not see leaves somebody told to wait instead
/// of somebody told nothing. What the mark buys is the sentence, and a second save kept off the
/// same blocks.
/// </para>
/// <para>
/// <b>What says a save is running is that this file is held, and never that it is there.</b> That
/// is <see cref="HeldMark"/>'s, which says why, and it is the whole of the mechanism here — what
/// this type adds is the meaning and the sentences, which are a save's and not a capture's.
/// </para>
/// <para>
/// So the file is deliberately left on disk when a save ends, well or badly, and nothing clears it.
/// Removing it would buy nothing — the next save writes over it, throwing the recording away takes
/// it with the folder, and until either happens it is a stale empty file every reader already reads
/// as nothing — and it would be this engine taking a file away on somebody else's behalf, which is
/// the one thing it is careful never to do.
/// </para>
/// <para>
/// It holds no bytes for the same reason. A process id and an instant would be a second thing to
/// keep true, a second thing to find torn, and a second answer to disagree with the handle — and
/// the handle is the one Windows keeps honest for free.
/// </para>
/// </remarks>
public sealed class SavingMark : IDisposable
{
    /// <summary>What the mark is called, beside the blocks it is about.</summary>
    /// <remarks>
    /// Named for the save rather than for the folder being in use. What a capture holds is
    /// <see cref="CaptureMark"/> and the blocks themselves, and a reader tells that apart by the
    /// share mode a writer took — see <see cref="BlockSpool.IsStillBeingWritten"/>. Three facts,
    /// three names: a folder being captured into, one being read out of — <see cref="ReadingMark"/>
    /// — and one being written down into a meeting are different things to say to somebody.
    /// </remarks>
    public const string FileName = "saving.mark";

    private readonly FileStream held;

    private SavingMark(FileStream held) => this.held = held;

    /// <summary>
    /// Whether a save of the recording in <paramref name="folder"/> is running right now, which is
    /// whether something still living is holding its mark.
    /// </summary>
    public static bool IsHeldIn(DirectoryInfo folder) => HeldMark.IsHeldIn(MarkIn(folder));

    /// <summary>
    /// Claims the folder for a save and holds it until this is let go of.
    /// </summary>
    /// <remarks>
    /// The claim is the file system's and not a question asked a moment ago: two finishes arriving
    /// together would both read a listing as free. Whichever gets the handle saves; the other is
    /// refused here, before it has read a block.
    /// </remarks>
    /// <param name="folder">The recording's folder.</param>
    /// <exception cref="AudioCaptureException">
    /// A save of this recording is already running, or there is no folder to claim.
    /// </exception>
    public static SavingMark Take(DirectoryInfo folder)
    {
        var mark = MarkIn(folder);

        try
        {
            return new SavingMark(HeldMark.Take(mark));
        }
        catch (DirectoryNotFoundException gone)
        {
            throw new AudioCaptureException(
                $"There is no folder at '{folder.FullName}', so there is no recording in it to "
                + "save.", gone);
        }
        catch (IOException taken)
        {
            throw new AudioCaptureException(
                $"A save of the recording in '{folder.FullName}' is already running: something "
                + "held its mark open throughout, and two saves of one recording would read the "
                + "same blocks into the same meeting at the same time. This one is refused "
                + "rather than started, and the blocks are untouched.", taken);
        }
    }

    /// <summary>Lets the mark go, which is what says the save is over.</summary>
    public void Dispose() => held.Dispose();

    /// <summary>Where the mark of the recording in <paramref name="folder"/> goes.</summary>
    /// <remarks>
    /// Private, because whether the file is there is not a question anybody gets to ask: this type
    /// has two answers and both of them are about a handle.
    /// </remarks>
    private static FileInfo MarkIn(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }
}
