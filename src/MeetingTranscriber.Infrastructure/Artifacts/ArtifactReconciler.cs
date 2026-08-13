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
/// So <see cref="Sweep"/> removes unfinished writes and nothing else. Adopting an unrecorded file
/// back into the corpus is a recovery decision with a person in it, not a thing that happens
/// silently while the app starts.
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

        var known = new HashSet<string>(StringComparer.Ordinal);

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
    /// Removes the unfinished writes and answers with what it removed. The only thing start-up
    /// gets to do without asking, and it is safe because one of these was never an artifact.
    /// </summary>
    /// <remarks>
    /// It takes the corpus and not a folder even though it never reads a row: this deletes, and
    /// what it deletes from has to be the folder of a corpus rather than a directory somebody
    /// named that happens to have files ending the same way.
    /// </remarks>
    public static IReadOnlyList<string> Sweep(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.Root;
        var swept = new List<string>();
        foreach (var file in Walk(root).Where(file => CorpusFiles.IsUnfinished(file.Name)))
        {
            var relativePath = CorpusFiles.RelativePathOf(root, file);
            try
            {
                file.Delete();
                swept.Add(relativePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Something else has it open, which on a single user machine means a write that is
                // still running. Leaving it is what the next start-up is for.
            }
        }

        swept.Sort(StringComparer.Ordinal);
        return swept;
    }

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
