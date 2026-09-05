namespace MeetingTranscriber.Audio;

/// <summary>
/// The mark a read holds over the spool folder it is reading through, so that anything deciding
/// about that recording can tell it is being read right now.
/// </summary>
/// <remarks>
/// <para>
/// What it says is that a read of this folder is under way: the listing that says how long each
/// waiting recording is, a keep, or an export. Every one of those is a pass over every byte of
/// every source. <b>It is not a save.</b> <see cref="SavingMark"/> was written narrowly on purpose,
/// and widening <em>it</em> would have drawn <em>"it is being saved"</em> over somebody's export —
/// a false statement about the product, printed at a prompt, over a folder nothing is saving.
/// </para>
/// <para>
/// Its lifetime is the other half of why it is a second object rather than a wider first one. A
/// save runs for minutes and ends by itself; a read is held for as long as somebody is standing at
/// a prompt or at a drawer, and ends when they have their answer.
/// </para>
/// <para>
/// <b>What it buys is not that a discard refuses whole.</b> That is
/// <see cref="UnfinishedRecordings.Remove"/>'s: it renames the folder before it takes anything
/// away, and Windows refuses that rename outright while any handle under the folder is open. What
/// this buys is the window that rename cannot see. Reading a recording through opens one source at
/// a time — <c>Survived</c> and <see cref="BlockSpool.ToWav"/> both finish one before they reach
/// the next — and the card and the changes are held by nobody at any point, so between the two
/// sources there is an instant in which somebody is reading the recording and <em>nothing under the
/// folder is open at all</em>. A rename arriving there goes straight through, and the reader's next
/// open fails over a recording that is gone. One file, held for the whole read, is what closes it.
/// The other two things it buys are smaller and both are for a person: the refusal a discard prints
/// can say somebody is reading the recording, and it can say it without spending the removal's
/// patience twice on a refusal that was knowable.
/// </para>
/// <para>
/// <b>What says a read is under way is that this file is held, and never that it is there.</b> That
/// is <see cref="HeldMark"/>'s, which says why. So the file is deliberately left on disk when a
/// read ends, well or badly, and nothing clears it — the next read writes over it, throwing the
/// recording away takes it with the folder, and until either happens it is a stale empty file every
/// reader already reads as nothing. It is shared where the other two are exclusive, through
/// <see cref="HeldMark.Join"/>: two people reading one folder are both reading it, and a prompt
/// whose read is two passes over the same folder holds this across both of them.
/// </para>
/// <para>
/// <b>The mark is what a read leaves where it can, and never what a read needs to happen.</b> A
/// spool folder was readable with read access alone until this file existed; taking the mark makes
/// a read a write, and a folder that will not take one — no room on the disk, an access this
/// process does not have — would otherwise turn a recording whose every block is intact into one
/// the application reports as unreadable. What a person meets that as is the drawer drawing
/// <em>the blocks of this one would not read</em> and offering them nothing but <em>Discard</em>,
/// over a meeting that is perfectly there. So a claim that cannot be made is not a read that
/// failed: the read goes on, holding nothing, exactly as it did before this mark existed. What is
/// lost in that folder is the sentence and the one window below, which is what there was to lose
/// anyway, and the rename <see cref="UnfinishedRecordings.Remove"/> performs is still refused by
/// every block a reader has open.
/// </para>
/// </remarks>
public sealed class ReadingMark : IDisposable
{
    /// <summary>What the mark is called, beside the blocks it is about.</summary>
    /// <remarks>
    /// Named for the read rather than for the folder being in use, the same way
    /// <see cref="CaptureMark.FileName"/> and <see cref="SavingMark.FileName"/> are named for what
    /// is happening.
    /// </remarks>
    public const string FileName = "reading.mark";

    /// <summary>
    /// The claim, or nothing when the folder would not take one — see the last paragraph on this
    /// type for why that is a read going on rather than a read refused.
    /// </summary>
    private readonly FileStream? held;

    private ReadingMark(FileStream? held) => this.held = held;

    /// <summary>
    /// Whether something still living is reading the recording in <paramref name="folder"/> right
    /// now, which is whether something is holding its mark.
    /// </summary>
    public static bool IsHeldIn(DirectoryInfo folder) => HeldMark.IsHeldIn(MarkIn(folder));

    /// <summary>
    /// Says a read of the folder is under way, and goes on saying it until this is let go of.
    /// </summary>
    /// <remarks>
    /// It joins whatever is already reading rather than refusing it. A second reader is a second
    /// read, which is true, and one command whose read is two passes claims this twice.
    /// </remarks>
    /// <param name="folder">The recording's folder.</param>
    /// <exception cref="AudioCaptureException">
    /// There is no folder, which is the one thing here that is not a read going on unmarked: the
    /// recording is gone, and this is the sentence a person is owed instead of the one the first
    /// block file is about to throw.
    /// </exception>
    public static ReadingMark Take(DirectoryInfo folder)
    {
        var mark = MarkIn(folder);

        try
        {
            return new ReadingMark(HeldMark.Join(mark));
        }
        catch (DirectoryNotFoundException gone)
        {
            throw new AudioCaptureException(
                $"There is no folder at '{folder.FullName}', so there is no recording in it to "
                + "read.", gone);
        }
        catch (Exception wouldNotTakeIt) when (
            wouldNotTakeIt is IOException or UnauthorizedAccessException)
        {
            // The folder is there and will not take the mark: no room on the disk, an access this
            // process does not have, or — after `HeldMark`'s own wait — something sitting on the
            // file. None of those is a reason a recording somebody can read becomes one they are
            // told they cannot, so the read goes on holding nothing. `SavingMark` and `CaptureMark`
            // are right to refuse where this is right to shrug: both of those write into the folder
            // anyway and would be refused whatever they did next, and both are one act somebody
            // asked for rather than a pass a listing runs over every recording on every start.
            return new ReadingMark(null);
        }
    }

    /// <summary>Lets the mark go, which is what says the read is over.</summary>
    public void Dispose() => held?.Dispose();

    /// <summary>Where the mark of the recording in <paramref name="folder"/> goes.</summary>
    /// <remarks>
    /// Private, for the reason <see cref="SavingMark"/>'s is: whether the file is there is not a
    /// question anybody gets to ask, because both answers this type has are about a handle.
    /// </remarks>
    private static FileInfo MarkIn(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }
}
