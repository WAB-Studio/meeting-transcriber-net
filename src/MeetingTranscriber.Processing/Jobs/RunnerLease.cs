namespace MeetingTranscriber.Processing.Jobs;

/// <summary>
/// The capability to run one corpus's queue: whoever holds this is the only process that may
/// start a job in it or stop one this restart found running.
/// </summary>
/// <remarks>
/// <para>
/// It follows <c>MeetingTranscriber.Audio.HeldMark</c>'s sharing decision and its wait, copied
/// rather than shared because <c>MeetingTranscriber.Processing</c> may not reference
/// <c>MeetingTranscriber.Audio</c> — the audio engine is a front end's dependency and this project
/// is platform-neutral. The two are the same mechanism for the same reason: held-ness rather than
/// existence is what a mark means, so a process that dies lets it go with no cleanup, and a second
/// claimant is refused by the file system's own share mode rather than by a question asked a moment
/// before.
/// </para>
/// <para>
/// The mark holds no bytes and means nothing while no process holds it. It is here rather than
/// under <c>meetings/</c> or <c>spool/</c> because <c>ArtifactReconciler</c> scans only those two
/// folders, and a file at the corpus root is never a finding it has to explain.
/// </para>
/// <para>
/// A crash lets it go with the process, the same as every other mark in this product: Windows
/// closes the handle, and the next launch or the next pump finds nobody holding it.
/// </para>
/// </remarks>
public sealed class RunnerLease : IDisposable
{
    /// <summary>The name this corpus's lease is held under, at the corpus root.</summary>
    public const string FileName = "runner.mark";

    /// <summary>
    /// How long a claim waits out a handle somebody else has, before deciding it is a holder. The
    /// same span and the same argument as <c>HeldMark</c>'s own: far longer than a question asked in
    /// passing and far shorter than what a real holder — a pump running for the length of a launch —
    /// holds it for.
    /// </summary>
    private static readonly TimeSpan WaitsOutAQuestion = TimeSpan.FromSeconds(2);

    private readonly FileStream handle;

    private RunnerLease(DirectoryInfo root, FileStream handle)
    {
        Root = root;
        this.handle = handle;
    }

    /// <summary>The corpus this lease is over.</summary>
    public DirectoryInfo Root { get; }

    /// <summary>
    /// Takes this corpus's lease, or answers <see langword="null"/> when another process already
    /// holds it.
    /// </summary>
    /// <remarks>
    /// A second claimant also asks for <see cref="FileAccess.Write"/>, so the share mode alone
    /// refuses it — nothing here asks who else has the file open. A scanner or a backup that reads
    /// the mark with <see cref="FileShare.Read"/> is not read as a holder, for the same reason
    /// <c>HeldMark.IsHeldIn</c> is not confused by one either.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The corpus root is not there.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// This process may not write to the corpus root — a <c>runner.mark</c> that came back
    /// read-only from a restore, for one.
    /// </exception>
    public static RunnerLease? TryTake(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var mark = new FileInfo(Path.Combine(root.FullName, FileName));
        var waitingSince = Environment.TickCount64;

        while (true)
        {
            try
            {
                // Written over rather than created new: what may already be there is a mark a
                // process that died left, and that one says nothing about anybody. A live one is
                // refused by the share mode and never by the name.
                var handle = new FileStream(
                    mark.FullName, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1);
                return new RunnerLease(root, handle);
            }

            // Before the wait and not inside it, because this one is an IOException too and
            // waiting it out would spend two seconds on a corpus that is not coming back.
            // UnauthorizedAccessException is not an IOException at all, so it already leaves as
            // itself without a catch of its own.
            catch (DirectoryNotFoundException)
            {
                throw;
            }
            catch (IOException) when (
                Environment.TickCount64 - waitingSince < WaitsOutAQuestion.TotalMilliseconds)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Lets the lease go. The mark is never deleted: what says nobody holds this corpus's queue is
    /// the handle closing, and a mark a live process still owns must not be mistaken for a stale one
    /// left by a name alone.
    /// </summary>
    public void Dispose() => handle.Dispose();
}
