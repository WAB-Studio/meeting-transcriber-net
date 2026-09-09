namespace MeetingTranscriber.Audio;

/// <summary>
/// A file whose meaning is that a process has it open, and the two things anybody ever asks one:
/// whether somebody is holding it, and to be given it.
/// </summary>
/// <remarks>
/// <para>
/// Three marks are made of this, so the mechanism is here and the meanings are theirs.
/// <see cref="SavingMark"/> says a finish is writing a folder down into a meeting;
/// <see cref="CaptureMark"/> says a capture is writing one; <see cref="ReadingMark"/> says a read
/// of a spool folder is under way.
/// They differ in what they are called, in what a refusal has to say to somebody, and in whether a
/// second claimant is refused — which is <see cref="Take"/> against <see cref="Join"/> and nothing
/// else. The part they share is the part it would be dangerous to
/// have two of. <b>Held-ness, not existence</b>, is one decision: a mark that meant
/// something by being there is one a process that died leaves behind for good, and the folder it
/// names is then out of reach of every answer, permanently, because of a crash. Windows closes the
/// handles of a process that is gone, however it went, so a mark lifts itself and nobody has to
/// come back for it.
/// </para>
/// <para>
/// What that rests on is a file system that enforces share modes, which is the local volume a
/// corpus lives on; one that does not would answer "nobody is holding it" to everything, silently.
/// </para>
/// <para>
/// It says nothing and throws nothing of its own. What comes out of <see cref="Take"/> is the file
/// system's own refusal, because the sentence a person reads is about a save or about a recording
/// and never about a file — so each mark words its own, over the one it is about.
/// </para>
/// </remarks>
internal static class HeldMark
{
    /// <summary>
    /// How long a claim waits out a handle somebody else has, before deciding it is a holder.
    /// </summary>
    /// <remarks>
    /// Far longer than the one thing it is waiting for and far shorter than the one thing it must
    /// not wait out. <see cref="IsHeldIn"/> holds the file for the length of one open and a listing
    /// asks it once per folder, so a claim and a question can meet; what a real holder holds it for
    /// is the length of a save or the length of a meeting, which is minutes or hours. Refusing one
    /// of those because somebody was looking at the list in that microsecond would cost the press
    /// rather than the meeting, and there is nothing to tell the person to do about it.
    /// <para>
    /// Still right for <see cref="Join"/>, where what it is waiting out is narrower: no
    /// <see cref="Join"/> ever waits for another, so the handles it can collide with are
    /// <see cref="IsHeldIn"/>'s own open — one open long — and whatever on the machine reads a file
    /// without sharing writes, which is a scanner or a backup passing over a mark it has no reason
    /// to hold. Neither of those is a holder worth failing a read over, which is why what
    /// <see cref="ReadingMark"/> does after this wait runs out is go on reading rather than refuse.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan WaitsOutAQuestion = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether something still living is holding <paramref name="mark"/>.
    /// </summary>
    /// <remarks>
    /// The same shape <see cref="BlockSpool.IsStillBeingWritten"/> asks of a spool, and for the
    /// same reason: it asks for the file the way a reader does, so what refuses it is a holder that
    /// will not let anything write — which is <see cref="Take"/> and <see cref="Join"/> and nothing
    /// else. Both flavours hold <see cref="FileAccess.Write"/>, which this open's own
    /// <see cref="FileShare.Read"/> does not permit, so widening what a claim shares did not widen
    /// what this question sees. Two of these
    /// arriving together do not refuse each other, and a scanner or a backup passing over the file
    /// is not read as a holder.
    /// </remarks>
    internal static bool IsHeldIn(FileInfo mark)
    {
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
            // over a folder nothing may be done to anyway, and it says it again next time rather
            // than once and for all, because nothing here is remembered.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Not a holder. A mark this process may not read sits in a folder it may not remove or
            // pour either, so the refusal belongs to whichever of those was attempted and says what
            // was really wrong — where reading it as held would hold the meeting for a permission.
            return false;
        }
    }

    /// <summary>
    /// Takes <paramref name="mark"/> and holds it until the stream is let go of.
    /// </summary>
    /// <remarks>
    /// The claim is the file system's and not a question asked a moment ago, which is what makes it
    /// the one authority: a listing's answer is a snapshot, and two claimants arriving together
    /// would both read it as free. Whichever gets the handle has it; the other is refused here,
    /// before it has done anything.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">There is no folder to put the mark in.</exception>
    /// <exception cref="IOException">Something held it open throughout.</exception>
    internal static FileStream Take(FileInfo mark) => Claim(mark, FileShare.Read);

    /// <summary>
    /// Takes <paramref name="mark"/> beside anything else already holding it, and holds it until
    /// the stream is let go of.
    /// </summary>
    /// <remarks>
    /// For a mark whose meaning is that <em>somebody</em> is doing a thing rather than that exactly
    /// one somebody is — which is <see cref="ReadingMark"/>, and which says why a read is that and a
    /// save is not. Two holders each ask <see cref="FileAccess.Write"/> with
    /// <see cref="FileShare.ReadWrite"/>, so each one's share permits the other's access and they
    /// coexist. Windows applies a share mode per handle and not per process, so two claims taken
    /// inside one process coexist the same way — which is what lets one command hold this across a
    /// call that claims it again.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">There is no folder to put the mark in.</exception>
    internal static FileStream Join(FileInfo mark) => Claim(mark, FileShare.ReadWrite);

    /// <summary>
    /// The one mechanism both claims are, with <paramref name="besideIt"/> carrying the whole of
    /// the difference between them: what a second claimant is allowed to ask for.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">There is no folder to put the mark in.</exception>
    private static FileStream Claim(FileInfo mark, FileShare besideIt)
    {
        var waitingSince = Environment.TickCount64;

        while (true)
        {
            try
            {
                // Written over rather than created new: what may already be there is a mark a
                // process that died left, and that one says nothing about anybody. A live one is
                // refused by the share mode and never by the name.
                return new FileStream(
                    mark.FullName, FileMode.Create, FileAccess.Write, besideIt, bufferSize: 1);
            }

            // Before the wait and not inside it, because this one is an <see cref="IOException"/>
            // too and waiting it out would spend two seconds on a folder that is not coming back.
            catch (DirectoryNotFoundException)
            {
                throw;
            }
            catch (IOException) when (
                Environment.TickCount64 - waitingSince < WaitsOutAQuestion.TotalMilliseconds)
            {
                Thread.Sleep(25);
            }
        }
    }
}
