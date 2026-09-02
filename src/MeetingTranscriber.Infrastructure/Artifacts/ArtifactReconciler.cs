using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>What the reconciler found on disk that the database does not agree with.</summary>
public enum ArtifactState
{
    /// <summary>
    /// A write that never finished. It is not an artifact and never becomes one: the sequence
    /// puts a file in place under its own name or leaves nothing, so the existence of one of these
    /// says only that the machine stopped in the middle.
    /// </summary>
    Unfinished = 1,

    /// <summary>
    /// A file with no row. The write reached the disk and the machine stopped before the corpus
    /// recorded it, so this may be a paid response with no other copy — which is exactly why
    /// nothing deletes it on its own.
    /// </summary>
    Unrecorded = 2,

    /// <summary>A row whose file is not there. The corpus is claiming something it does not have.</summary>
    Missing = 3,

    /// <summary>
    /// A row whose file is not the one it describes. A source is never rewritten, so this is
    /// damage; a derivative can be thrown away and rendered again.
    /// </summary>
    Changed = 4,

    /// <summary>
    /// Blocks of a recording that was never turned into a WAV. They are the only copy of that
    /// audio, so recovery is a person's decision and this is only the report that there is one to
    /// make.
    /// </summary>
    Spooled = 5,

    /// <summary>
    /// The copy of a derived file a replace moved out of the way, still there after the replace was
    /// over. Either the machine stopped inside the run of renames or the tidy-up at the end of it
    /// was refused.
    /// </summary>
    /// <remarks>
    /// One state and two things to do about it, so the finding says which: with the file it came
    /// out of missing, this is the last copy of it and a rebuild is the answer; with that file back,
    /// a sweep takes this and nothing else is owed.
    /// </remarks>
    Superseded = 6,
}

/// <summary>One thing the corpus and the disk disagree about.</summary>
/// <param name="RelativePath">
/// Where it is, as a stored path — the same string <c>artifacts.relative_path</c> holds, so a
/// finding can be looked up without composing a path twice.
/// </param>
public sealed record ArtifactFinding(ArtifactState State, string RelativePath, string Detail)
{
    public override string ToString() => $"{State}: {RelativePath}: {Detail}";
}

/// <summary>What a sweep took, and what it reached for and was refused.</summary>
/// <param name="Removed">What it deleted, as stored paths, in a stable order.</param>
/// <param name="Left">What the delete was refused, so it is still there.</param>
/// <remarks>
/// The second list is not a consolation for the first. A write is held for as long as it exists,
/// so leaving one is what a sweep run beside a working application is supposed to do — and a run
/// that says only how many it removed answers "none" to a corpus full of live writes and to an
/// empty one alike, leaving somebody who has just read a report of unfinished writes with no way
/// to tell a healthy sweep from a broken one.
///
/// What the sweep never reached for is in neither list: a copy whose destination is still missing
/// belongs to <see cref="ArtifactReconciler.Check"/>, which says what it is and what to do.
/// </remarks>
public sealed record SweptFiles(IReadOnlyList<string> Removed, IReadOnlyList<string> Left);

/// <summary>
/// What start-up does about the fact that the filesystem and SQLite cannot be written together.
/// </summary>
/// <remarks>
/// <para>
/// Every durable write leaves one of four states behind if it is cut: nothing, an unfinished
/// write, a file with no row, or a finished artifact. The first and the last need no attention.
/// This is what finds the other two, and it reports rather than repairs, because the two of them
/// are not the same kind of thing: an unfinished write is worth nothing to anybody, and a file
/// with no row may be the only copy of something that was paid for.
/// </para>
/// <para>
/// So <see cref="Sweep"/> removes what nobody can want back, and nothing else. Adopting an
/// unrecorded file into the corpus is a recovery decision with a person in it, not a thing that
/// happens silently while the app starts.
/// </para>
/// <para>
/// Two things are nobody's, and each is a claim about bytes rather than about a suffix. A
/// <c>.partial</c> is a write that never became an artifact, so nothing is lost. A
/// <c>.superseded</c> copy <i>was</i> an artifact, and what says it is finished with is that the
/// file it was moved out of the way of is back where it was: the only thing that ever puts one of
/// these back does so into a destination it emptied itself, and it gives up before anything moves
/// in. Until then it is the last copy of a derived file and the sweep does not reach for it at all.
/// </para>
/// <para>
/// The third thing the sweep must not take is a write still being made, and no suffix can say that
/// one. An artifact write holds its temporary open, so the delete is refused, the file is left and
/// the command says which ones it left — that, and nothing about time, is how a <c>sweep</c> run
/// from a terminal beside a working application is safe. It is the artifact write that guarantees
/// it: a <c>.partial</c> the audio engine wrote beside a recording it is materialising is held only
/// while something is reading or writing it.
/// </para>
/// </remarks>
public static class ArtifactReconciler
{
    /// <summary>
    /// Everything the corpus and the disk disagree about, in a stable order and empty when there
    /// is nothing.
    /// </summary>
    /// <param name="verifyContents">
    /// Whether to hash every recorded file. Off by default: a corpus keeps hours of WAV, and a
    /// start-up whose cost grows with the corpus is one the user learns to skip. Size is compared
    /// either way, which is free and catches a truncated write.
    /// </param>
    public static IReadOnlyList<ArtifactFinding> Check(
        CorpusDbContext context,
        bool verifyContents = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.Root;
        var findings = new List<ArtifactFinding>();
        var recorded = context.Artifacts
            .AsNoTracking()
            .Select(artifact => new
            {
                artifact.RelativePath,
                artifact.ByteSize,
                artifact.Sha256,
            })
            .ToList();

        // The scan finds a file under whatever case the disk spells it, and the row holds whatever
        // case the write recorded. On this filesystem those are one file, so telling them apart
        // exactly would report a meeting's transcript as both recorded and unaccounted for.
        var known = new HashSet<string>(CorpusFiles.PathComparer);

        foreach (var artifact in recorded.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            known.Add(artifact.RelativePath);
            var file = CorpusFiles.Locate(root, artifact.RelativePath);

            if (!file.Exists)
            {
                findings.Add(new ArtifactFinding(
                    ArtifactState.Missing,
                    artifact.RelativePath,
                    "the corpus has a row for it and the file is not there"));
                continue;
            }

            if (file.Length != artifact.ByteSize)
            {
                findings.Add(new ArtifactFinding(
                    ArtifactState.Changed,
                    artifact.RelativePath,
                    $"the row says {artifact.ByteSize} bytes and the file is {file.Length}"));
                continue;
            }

            if (verifyContents && !string.Equals(
                    CorpusFiles.Sha256Of(file), artifact.Sha256, StringComparison.Ordinal))
            {
                findings.Add(new ArtifactFinding(
                    ArtifactState.Changed,
                    artifact.RelativePath,
                    "the file is the size the row says and not the content it says"));
            }
        }

        findings.AddRange(OnDisk(root, known));
        return findings;
    }

    /// <summary>
    /// Removes what the corpus is finished with and answers with what it took and what it was
    /// refused. Everything it removes is either a write that was never an artifact or a copy the
    /// file that replaced it is standing in for.
    /// </summary>
    /// <remarks>
    /// It takes the corpus and not a folder even though it never reads a row: this deletes, and
    /// what it deletes from has to be the folder of a corpus rather than a directory somebody
    /// named that happens to have files ending the same way. A row is still not read — what says a
    /// replace is over is the destination being on disk, not the corpus agreeing about it, and the
    /// two come apart for as long as it takes a crash to land between the rename and the save. The
    /// file there is the newer one either way, and the row disagreeing with it is what
    /// <see cref="Check"/> reports.
    /// </remarks>
    public static SweptFiles Sweep(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.Root;
        var removed = new List<string>();
        var left = new List<string>();
        var takeable = Walk(root)
            .Where(file => CorpusFiles.IsUnfinished(file.Name) || ReplacementIsBack(file));

        foreach (var file in takeable)
        {
            var relativePath = CorpusFiles.RelativePathOf(root, file);
            try
            {
                // The read-only bit is not somebody's answer about this file: it rides in on a
                // backup medium or a policy and survives the rename that set the copy aside, and
                // the corpus replaces a derivative wearing it without asking. Left standing, it
                // refuses the delete the same way a live handle does, and a person reading "run
                // this again once it has finished" would be running it forever.
                file.IsReadOnly = false;
                file.Delete();
                removed.Add(relativePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The delete was refused, and beside a working application that is a write still
                // being made: a staged artifact holds its temporary from the moment it is created
                // until the moment it is put in place, so this is the liveness test and not a
                // consolation for one. It is carried out rather than swallowed, because a sweep
                // beside a working application leaves files as a matter of course and somebody who
                // just read a report of unfinished writes has to be able to tell that from a sweep
                // that is not working.
                left.Add(relativePath);
            }
        }

        removed.Sort(StringComparer.Ordinal);
        left.Sort(StringComparer.Ordinal);
        return new SweptFiles(removed, left);
    }

    /// <summary>
    /// Whether this is a copy a replace of this corpus set aside and the file it came out of is on
    /// disk again. False for everything that is not one of those copies at all.
    /// </summary>
    private static bool ReplacementIsBack(FileInfo aside) =>
        CorpusFiles.DestinationOfSuperseded(aside) is { } destination && destination.Exists;

    /// <summary>
    /// The other direction: what is on disk that the corpus has not accounted for.
    /// </summary>
    private static IEnumerable<ArtifactFinding> OnDisk(DirectoryInfo root, HashSet<string> known)
    {
        foreach (var file in Walk(root))
        {
            var relativePath = CorpusFiles.RelativePathOf(root, file);

            if (CorpusFiles.IsUnfinished(file.Name))
            {
                yield return new ArtifactFinding(
                    ArtifactState.Unfinished,
                    relativePath,
                    "a write that never finished; it is not an artifact and can be removed");
                continue;
            }

            // Asked as "which file did this come out of" rather than as the suffix, so a name no
            // replace of this corpus wrote falls through to the file-with-no-row below. That is
            // what somebody else's file in a meeting's folder is, and the advice under this state
            // — rebuild the derived file it is a copy of — is advice about a file that does not
            // exist.
            if (CorpusFiles.DestinationOfSuperseded(file) is { } destination)
            {
                yield return new ArtifactFinding(
                    ArtifactState.Superseded,
                    relativePath,
                    destination.Exists
                        ? "the copy a replace moved aside and did not get to remove; the file that "
                        + "replaced it is back, so a sweep is what takes it from here"
                        : "the copy a replace moved aside and did not get to remove; the file it "
                        + "came out of is not there, so this is the last one of what that derived "
                        + "file held and a rebuild is what puts it back");
                continue;
            }

            if (known.Contains(relativePath))
            {
                continue;
            }

            yield return relativePath.StartsWith($"{CorpusFiles.Spool}/", StringComparison.Ordinal)
                ? new ArtifactFinding(
                    ArtifactState.Spooled,
                    relativePath,
                    "a block of a recording that was never materialised; recovering it is a decision")
                : new ArtifactFinding(
                    ArtifactState.Unrecorded,
                    relativePath,
                    "the file is there and the corpus has no row for it; it may be the only copy");
        }
    }

    /// <summary>
    /// Every file of the corpus, in a stable order. Only the two folders the layout puts artifacts
    /// in: the database and its journals live at the root and are not artifacts.
    /// </summary>
    private static IEnumerable<FileInfo> Walk(DirectoryInfo root) =>
        new[] { CorpusFiles.Meetings, CorpusFiles.Spool }
            .Select(folder => new DirectoryInfo(Path.Combine(root.FullName, folder)))
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateFiles("*", SearchOption.AllDirectories))
            .OrderBy(file => file.FullName, StringComparer.Ordinal);
}
