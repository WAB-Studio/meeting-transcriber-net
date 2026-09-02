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
/// written down, reads a length off blocks that are being poured, and on a discard empties the
/// folder as far as the first file something is holding: the card and the changes beside it are
/// sources, they sort before the blocks, and they are gone before the refusal lands.
/// </para>
/// <para>
/// <b>What says a save is running is that this file is held, and never that it is there.</b> A mark
/// whose meaning is its existence is one a process that died in the middle leaves behind for good,
/// and the meeting it names is then out of reach of every answer, permanently, because of a crash.
/// Held-ness cannot outlive the process holding it — Windows closes the handles of a process that
/// is gone, however it went — so the mark lifts itself and nobody has to come back for it. What
/// that rests on is a file system that enforces share modes, which is the local volume a corpus
/// lives on; one that does not would answer "no save" to everything, silently.
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
    /// Named for the save rather than for the folder being in use. What a capture holds is the
    /// blocks themselves, and a reader tells that apart by the share mode a writer took — see
    /// <see cref="BlockSpool.IsStillBeingWritten"/>. Two facts, two names: a folder that is being
    /// captured into and one that is being read out of are different things to say to somebody.
    /// </remarks>
    public const string FileName = "saving.mark";

    /// <summary>
    /// How long a claim waits out a handle somebody else has, before deciding it is a save.
    /// </summary>
    /// <remarks>
    /// Far longer than the one thing it is waiting for and far shorter than the one thing it must
    /// not wait out. <see cref="IsHeldIn"/> holds the file for the length of one open and a listing
    /// asks it once per folder, so a claim and a question can meet; a save holds it for as long as
    /// it takes to pour a meeting, which is minutes. Refusing a save because somebody was looking
    /// at the list in that microsecond would cost the press rather than the meeting, and there is
    /// nothing to tell the person to do about it.
    /// </remarks>
    private static readonly TimeSpan WaitsOutAQuestion = TimeSpan.FromSeconds(2);

    private readonly FileStream held;

    private SavingMark(FileStream held) => this.held = held;

    /// <summary>
    /// Whether a save of the recording in <paramref name="folder"/> is running right now, which is
    /// whether something still living is holding its mark.
    /// </summary>
    /// <remarks>
    /// The same shape <see cref="BlockSpool.IsStillBeingWritten"/> asks of a spool, and for the
    /// same reason: it asks for the file the way a reader does, so what refuses it is a holder that
    /// will not let anything write — which is <see cref="Take"/> and nothing else. Two of these
    /// arriving together do not refuse each other, and a scanner or a backup passing over the file
    /// is not read as a save.
    /// </remarks>
    public static bool IsHeldIn(DirectoryInfo folder)
    {
        var mark = MarkIn(folder);

        try
        {
            using var reading = new FileStream(
                mark.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            // Somebody has it. A disk that refused the read for its own reasons arrives here too
            // and is answered the same way, which is the safe direction of the two: it says wait
            // over a folder nothing may be decided about anyway, and it says it again next time
            // rather than once and for all, because nothing here is remembered.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Not a save. A mark this process may not read sits in a folder it may not remove or
            // pour either, so the refusal belongs to whichever of those was attempted and says what
            // was really wrong — where reading it as a save would hold the meeting for a permission.
            return false;
        }
    }

    /// <summary>
    /// Claims the folder for a save and holds it until this is let go of.
    /// </summary>
    /// <remarks>
    /// The claim is the file system's and not a question asked a moment ago, which is what makes it
    /// the one authority: a listing's answer is a snapshot, and two finishes arriving together
    /// would both read it as free. Whichever gets the handle saves; the other is refused here,
    /// before it has read a block.
    /// </remarks>
    /// <param name="folder">The recording's folder.</param>
    /// <exception cref="AudioCaptureException">
    /// A save of this recording is already running, or there is no folder to claim.
    /// </exception>
    public static SavingMark Take(DirectoryInfo folder)
    {
        var mark = MarkIn(folder);
        var waitingSince = Environment.TickCount64;

        while (true)
        {
            try
            {
                // Written over rather than created new: what may already be there is a mark a save
                // that died left, and that one says nothing about anybody. A live one is refused by
                // the share mode below and never by the name.
                return new SavingMark(new FileStream(
                    mark.FullName, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1));
            }
            catch (DirectoryNotFoundException gone)
            {
                throw new AudioCaptureException(
                    $"There is no folder at '{folder.FullName}', so there is no recording in it to "
                    + "save.", gone);
            }
            catch (IOException taken)
            {
                if (Environment.TickCount64 - waitingSince < WaitsOutAQuestion.TotalMilliseconds)
                {
                    Thread.Sleep(25);
                    continue;
                }

                throw new AudioCaptureException(
                    $"A save of the recording in '{folder.FullName}' is already running: something "
                    + "held its mark open throughout, and two saves of one recording would read the "
                    + "same blocks into the same meeting at the same time. This one is refused "
                    + "rather than started, and the blocks are untouched.", taken);
            }
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
