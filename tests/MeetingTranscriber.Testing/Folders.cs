using System.Diagnostics;

namespace MeetingTranscriber.Testing;

/// <summary>
/// A folder operation a test needs to have happened, done the way Windows makes necessary.
/// </summary>
/// <remarks>
/// <para>
/// NTFS refuses to rename a folder while anything under it still has a handle open, and it does
/// not care what sharing that handle was opened with — one that allows deletes blocks the rename
/// exactly as an exclusive one does. Windows is full of things that open a file for a moment
/// right after it is closed, a real-time malware scanner being the usual explanation, so a folder
/// whose files were written a millisecond ago is a folder that cannot reliably be renamed. What
/// comes back is <c>Access to the path '&lt;the source&gt;' is denied</c>.
/// </para>
/// <para>
/// <see cref="TemporaryCorpus"/> meets the same rule on the way out and answers it by shrugging:
/// a temporary folder that outlived a test is not worth a red. An operation a test is relying on
/// cannot shrug, and there is no handle of ours to close, so this one waits. Nothing in
/// <c>src/</c> renames a folder — a corpus one or the legacy one the importer's tests build — so
/// this is a fact about writing tests rather than a retry the product needs.
/// </para>
/// <para>
/// It is a rule about renaming a <em>directory</em>, and does not carry over to a file. A file
/// rename is refused only by a holder that withheld <c>FILE_SHARE_DELETE</c>, which the thing
/// that reads a file the moment it is written does not, so the <c>File.Move</c> calls in
/// <c>src/</c> are not this defect and must not grow a retry over it. If a file rename is ever
/// seen refused, that is a different holder and wants finding rather than waiting out.
/// </para>
/// </remarks>
public static class Folders
{
    /// <summary>
    /// How long a refusal is taken for somebody else still reading rather than for the answer.
    /// Well over any refusal seen here, and deliberately not far over: past this the likelier
    /// holder is this suite's own undisposed context or unemptied pool, and that is a red worth
    /// having instead of a test that quietly got slower.
    /// </summary>
    private const int DefaultPatienceMilliseconds = 250;

    /// <summary>The two refusals that are somebody else still reading, and not an answer.</summary>
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/>, waiting out whatever still has a
    /// file under it open.
    /// </summary>
    /// <param name="patienceMilliseconds">
    /// How long to wait. Here so the giving up can be tested without waiting for the default, and
    /// every caller that is not that test leaves it alone.
    /// </param>
    /// <returns>
    /// How many refusals it waited out — zero when the rename went through first time. Every
    /// caller but one discards it. The one is the test that has to tell the waiting working from
    /// the race simply not having fired that run, which a green rename cannot say on its own; and
    /// it is the number to quote if this ever gives up, because who was holding the folder is
    /// still an inference and a count is the one measured thing about it.
    /// </returns>
    /// <exception cref="IOException">
    /// If the refusal outlasts the patience — carrying the refusal itself as its inner exception —
    /// or for any reason that is not somebody else reading: a destination already there, a parent
    /// that is not, another volume. Those come back untouched and at once.
    /// </exception>
    public static int MoveWaitingOutWhoeverHasIt(
        DirectoryInfo from,
        DirectoryInfo to,
        int patienceMilliseconds = DefaultPatienceMilliseconds)
    {
        var clock = Stopwatch.StartNew();
        var refusals = 0;

        while (true)
        {
            try
            {
                Directory.Move(from.FullName, to.FullName);
                return refusals;
            }
            catch (IOException refused) when (SomebodyIsStillReading(refused))
            {
                refusals++;

                if (clock.ElapsedMilliseconds >= patienceMilliseconds)
                {
                    throw new IOException(
                        $"'{from.FullName}' was still refused {refusals} times over "
                        + $"{clock.ElapsedMilliseconds} ms. Whatever reads a file the moment it is "
                        + "written has let go long before that, so look past it. Either a handle "
                        + "this suite still holds — a stream or a context never disposed, or a "
                        + "connection pool never emptied for this folder — or a refusal that is "
                        + "nobody's handle at all, which is what 0x80070005 also comes back as for "
                        + "a denied ACL or a folder somebody marked read-only.",
                        refused);
                }

                // A sleep rather than a spin, so waiting does not take the core off whoever is
                // reading. How long it actually is, is the platform's timer resolution.
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>
    /// Whether this is a refusal that goes away on its own. Anything else — the destination
    /// already there, a parent that is not, another volume — is the answer and not a delay.
    /// </summary>
    private static bool SomebodyIsStillReading(IOException refused) =>
        refused.HResult is AccessDenied or SharingViolation;
}
