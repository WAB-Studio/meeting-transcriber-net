using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>
/// A file of the corpus that has been confirmed on disk. A row here means the write completed,
/// was re-read and was hashed. A file without a row is what the reconciler finds at start-up,
/// and it is never taken for a success just because it exists.
/// </summary>
public class Artifact
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public ArtifactKind Kind { get; set; }

    /// <summary>Stored rather than derived from <see cref="Kind"/> so a query can filter on it.</summary>
    public ArtifactOrigin Origin { get; set; }

    public required string RelativePath { get; set; }

    public long ByteSize { get; set; }

    /// <summary>Lowercase hex of the SHA-256, so always 64 characters.</summary>
    public required string Sha256 { get; set; }

    /// <summary>
    /// When the bytes that are there now were re-read and hashed — not when the first file to
    /// carry this path was. Re-rendering a derivative confirms a different file under the same
    /// name, and the row is meant to say which one is on disk.
    /// </summary>
    public UtcTimestamp ConfirmedAt { get; set; }
}
