namespace MeetingTranscriber.Audio;

/// <summary>
/// The mark held over the folder a recording is being written into, from the moment the folder is
/// made until the devices are let go of.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one window, and the window is the beginning of every recording. A folder is made
/// before a device is opened, and until the first source has its spool file there is nothing in it
/// at all — so a folder holding no block and one a capture started writing a moment ago are the
/// same empty folder to anything looking. Once the spools are there the blocks themselves say it:
/// a capture holds them in a share mode nothing else can write, which is
/// <see cref="BlockSpool.IsStillBeingWritten"/>. This is what says it before them.
/// </para>
/// <para>
/// That stretch is as short as Windows lets it be, because the mark is taken by whatever makes the
/// folder rather than by whatever opens the first device — in <see cref="CaptureSession.Start"/>
/// for a folder somebody named, and in <c>MeetingRecordings.Open</c> for a meeting recorded into a
/// corpus, which hands the claim on to <see cref="CaptureSession.StartUnder"/>. What is left
/// unheld is the two adjacent syscalls that make the folder and claim it, and no more: there is no
/// atomic create-directory-and-file, so that residue cannot be closed, only narrowed. A folder
/// taken away inside it loses its meeting, and what the person gets is
/// <c>MeetingRecordings.Open</c>'s own sentence rather than one about a second recording.
/// </para>
/// <para>
/// What it buys is that the folder cannot be taken away underneath a capture that is starting.
/// <see cref="UnfinishedRecordings.EraseWhereNothingWasRecorded"/> removes a folder nothing was
/// recorded into, and the mark is what it runs into: it is held in a share mode that forbids
/// unlinking, so the delete throws and the folder stands — an answer from the file system, at the
/// moment of the act, where a question asked a moment earlier would already be stale.
/// </para>
/// <para>
/// <b>What says a capture is running is that this file is held, and never that it is there.</b>
/// That is <see cref="HeldMark"/>'s, which says why. A capture runs for the length of a meeting, so
/// this is the mark most likely to be left stranded by a machine that died — and a stranded one
/// that meant something by being there would keep its folder out of reach forever, which is a
/// crash making a folder permanent rather than a crash being survived. Nothing clears it: the
/// folder goes when it is taken away, and a folder that became a recording has blocks that say
/// what the mark used to.
/// </para>
/// <para>
/// It is not a second answer to <see cref="UnfinishedRecording.Running"/>, which is asked of
/// folders that hold blocks and is the blocks' own answer. This is about the stretch before there
/// are any.
/// </para>
/// </remarks>
public sealed class CaptureMark : IDisposable
{
    /// <summary>What the mark is called, in the folder the recording is being written into.</summary>
    /// <remarks>
    /// Named for the capture and not for the folder being in use, the same way
    /// <see cref="SavingMark.FileName"/> is named for the save. A folder being captured into and
    /// one being read out of are different things to say to somebody, and a machine can die in
    /// either.
    /// </remarks>
    public const string FileName = "capture.mark";

    private readonly FileStream held;

    private CaptureMark(FileStream held, DirectoryInfo folder)
    {
        this.held = held;
        Folder = folder;
    }

    /// <summary>What this is a claim over.</summary>
    /// <remarks>
    /// Carried rather than left to the caller to remember, because the claim and the folder
    /// travel together — out of <c>MeetingRecordings.Open</c> and back into
    /// <see cref="CaptureSession.StartUnder"/> — and a session opened into one folder under a mark
    /// over another would be a recording nothing holds, which is the one state this type exists to
    /// make unreachable. Two arguments could disagree; one cannot.
    /// </remarks>
    public DirectoryInfo Folder { get; }

    /// <summary>
    /// Whether something is recording into <paramref name="folder"/>, or about to be, which is
    /// whether something still living is holding its mark. True from the moment the folder is
    /// claimed, which is before any device is opened.
    /// </summary>
    public static bool IsHeldIn(DirectoryInfo folder) => HeldMark.IsHeldIn(MarkIn(folder));

    /// <summary>
    /// Claims the folder for a capture and holds it until this is let go of.
    /// </summary>
    /// <param name="folder">The folder the recording is being written into.</param>
    /// <exception cref="AudioCaptureException">
    /// A capture is already recording into this folder, or there is no folder to claim.
    /// </exception>
    public static CaptureMark Take(DirectoryInfo folder)
    {
        var mark = MarkIn(folder);

        try
        {
            return new CaptureMark(HeldMark.Take(mark), folder);
        }
        catch (DirectoryNotFoundException gone)
        {
            throw new AudioCaptureException(
                $"There is no folder at '{folder.FullName}' to record into.", gone);
        }
        catch (IOException taken)
        {
            throw new AudioCaptureException(
                $"A capture is already recording into '{folder.FullName}': something held its mark "
                + "open throughout, and two recordings writing one folder would each be half a "
                + "meeting. This one is refused rather than started, and nothing was recorded.",
                taken);
        }
    }

    /// <summary>Lets the mark go, which is what says the capture is over.</summary>
    public void Dispose() => held.Dispose();

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
