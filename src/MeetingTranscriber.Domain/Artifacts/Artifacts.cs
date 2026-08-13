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

    /// <summary>True when writing this kind again may replace what is already there.</summary>
    /// <remarks>
    /// Not <see cref="OriginOf"/> asked a second way, and the manifest is the whole reason the two
    /// are separate questions. The origin decides what a backup carries and what a deletion spares;
    /// this decides whether a second write destroys anything. For every source but one it does —
    /// those bytes are the only copy of something the corpus cannot produce again. The manifest is
    /// produced from the meetings row every time it is written, so refusing to replace it protects
    /// nothing: it would pin whatever the card happened to say first, and leave a meeting whose
    /// card was never written without one for good.
    /// </remarks>
    public static bool MayBeReplaced(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.Manifest
            or ArtifactKind.Transcript
            or ArtifactKind.Utterances
            or ArtifactKind.Summary => true,
        ArtifactKind.SpoolBlock
            or ArtifactKind.Audio
            or ArtifactKind.DeepgramResponse
            or ArtifactKind.Extraction => false,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown artifact kind."),
    };
}
