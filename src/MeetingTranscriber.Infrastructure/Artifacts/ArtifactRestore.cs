using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>Bytes that cannot put anything back, saying what the corpus made of them.</summary>
public sealed class ArtifactRestoreException(string message) : Exception(message);

/// <summary>What one restore did with the bytes it was handed.</summary>
/// <param name="Sha256">
/// What they hash to, which is the only name they have here — the file they arrived in is
/// somebody's backup and says nothing about which artifact it is.
/// </param>
/// <param name="PutBack">The paths that had no file and have one again, in a stable order.</param>
/// <param name="AlreadyThere">
/// The paths a file was already at. Whether that file is the one the row describes is
/// <see cref="ArtifactReconciler"/>'s question and not this one's.
/// </param>
public sealed record Restoration(
    string Sha256,
    IReadOnlyList<string> PutBack,
    IReadOnlyList<string> AlreadyThere);

/// <summary>
/// A file the corpus records and the disk has lost, put back from bytes somebody still has.
/// </summary>
/// <remarks>
/// <para>
/// The corpus can produce a derivative again, so losing one costs a render. A source it cannot:
/// the response was paid for, the audio was recorded once, and the extraction is the accepted
/// output of a run. <see cref="ArtifactReconciler"/> reports that a row's file is gone and stops
/// there, because putting a file back is a decision with a person in it — this is the operation
/// that person runs when they have the bytes.
/// </para>
/// <para>
/// <b>The hash the corpus already recorded is the whole authority.</b> Nothing is accepted for
/// being the right size, having the right name or arriving from the right folder: the bytes are
/// restored only where the corpus already says those exact bytes belong, which makes putting a
/// different file under a paid artifact's row impossible rather than guarded against. It is also
/// the address — a loose <c>deepgram.json</c> in a backup folder does not say which meeting it is,
/// and the corpus does.
/// </para>
/// <para>
/// Which is why nothing here takes a row from its caller. A method handed an
/// <see cref="Domain.Artifacts.Artifact"/> and a stream would be comparing two things the same
/// caller supplied, and <see cref="StagedArtifact.Commit"/> writes the hash of what it wrote back
/// onto the row it finds — so a fabricated hash with bytes to match would land under a real path
/// and leave the corpus saying it had always been that. The row is looked up here, from the bytes,
/// for the same reason the kind is looked up there rather than believed.
/// </para>
/// <para>
/// Only where there is no file. A file that is there and is not what its row describes is damage,
/// and this does not decide what to do about it: overwriting could destroy the only copy of
/// something, and the person who deletes it and runs this again has decided that on purpose.
/// </para>
/// </remarks>
public static class ArtifactRestore
{
    /// <summary>
    /// Puts back every artifact of this corpus that is the content of this file and has no file of
    /// its own, and answers with what it did.
    /// </summary>
    /// <param name="offered">A file somebody kept. It is read and never moved or removed.</param>
    /// <exception cref="ArtifactRestoreException">
    /// The file is not there, or no row of this corpus describes what is in it.
    /// </exception>
    public static Restoration Restore(
        CorpusDbContext context,
        DirectoryInfo root,
        FileInfo offered,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(offered);

        offered.Refresh();
        if (!offered.Exists)
        {
            throw new ArtifactRestoreException($"There is nothing at '{offered.FullName}' to put back.");
        }

        // One handle for the hash and for every copy taken from it, so what identified the rows and
        // what lands in the corpus are the same bytes rather than two reads of a file anything
        // could have replaced in between.
        using var bytes = offered.Open(new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
        });

        return Restore(context, root, bytes, $"'{offered.FullName}'", now);
    }

    /// <summary>
    /// The same thing for a caller that already has the bytes open — filing a response reads it
    /// once and must not read it again, since a second read is a second chance for something to
    /// have replaced the file in between.
    /// </summary>
    /// <param name="bytes">
    /// A seekable stream of somebody's copy. It is rewound and hashed here: which rows these bytes
    /// belong under is never something a caller gets to say.
    /// </param>
    public static Restoration Restore(
        CorpusDbContext context,
        DirectoryInfo root,
        Stream bytes,
        UtcTimestamp now) =>
        Restore(context, root, bytes, "what was handed over", now);

    private static Restoration Restore(
        CorpusDbContext context,
        DirectoryInfo root,
        Stream bytes,
        string whereFrom,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(bytes);

        bytes.Position = 0;
        var sha256 = CorpusFiles.Sha256Of(bytes);

        var recorded = context.Artifacts
            .Where(artifact => artifact.Sha256 == sha256)
            .OrderBy(artifact => artifact.RelativePath)
            .ToList();

        if (recorded.Count == 0)
        {
            throw new ArtifactRestoreException(
                $"Nothing in this corpus is {sha256}. What is in {whereFrom} may be a file of "
                + "another corpus, or a version of this one that was never confirmed here — either "
                + "way, no row of this corpus is asking for it.");
        }

        var putBack = new List<string>();
        var alreadyThere = new List<string>();

        // Every row, not the first: the same bytes under two paths are the same artifact twice, so
        // there is nothing to choose between them and putting them at both is what restoring them
        // means. Picking one would be this deciding something it has no way to decide.
        foreach (var artifact in recorded)
        {
            if (CorpusFiles.Locate(root, artifact.RelativePath).Exists)
            {
                alreadyThere.Add(artifact.RelativePath);
                continue;
            }

            bytes.Position = 0;

            // The same six steps every artifact reaches the corpus through, and the same refusal at
            // the end of them: a source whose file came back between the look above and the move is
            // not overwritten. The row is the one it already had — the bytes hash to what it says,
            // so only the moment it was last confirmed on disk moves, which is what that column
            // means.
            DurableArtifact.Write(
                context, root, artifact.MeetingId, artifact.Kind, artifact.RelativePath, now, bytes.CopyTo);
            putBack.Add(artifact.RelativePath);
        }

        return new Restoration(sha256, putBack, alreadyThere);
    }
}
