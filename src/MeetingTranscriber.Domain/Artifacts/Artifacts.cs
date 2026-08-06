namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>Which side of the source line each kind of artifact belongs on.</summary>
public static class Artifacts
{
    /// <summary>
    /// The manifest counts as a source even though the database could regenerate it. It exists
    /// for the case where the database is gone, and a recovery card that can only be rebuilt
    /// from the thing it is meant to replace is no recovery card at all.
    /// </summary>
    public static ArtifactOrigin OriginOf(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.SpoolBlock
            or ArtifactKind.Audio
            or ArtifactKind.DeepgramResponse
            or ArtifactKind.Extraction
            or ArtifactKind.Manifest => ArtifactOrigin.Source,
        ArtifactKind.Transcript
            or ArtifactKind.Utterances
            or ArtifactKind.Summary => ArtifactOrigin.Derived,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown artifact kind."),
    };

    /// <summary>True when re-rendering may overwrite this artifact.</summary>
    public static bool IsRebuildable(this ArtifactKind kind) => kind.OriginOf() is ArtifactOrigin.Derived;
}
