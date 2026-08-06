namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>
/// Which side of the line an artifact sits on. The whole backup and deletion policy hangs off
/// this distinction, so it is stored rather than inferred at the point of use.
/// </summary>
public enum ArtifactOrigin
{
    /// <summary>Cannot be recovered from anything left on the machine. Never rewritten.</summary>
    Source = 1,

    /// <summary>Can be produced again from the sources. Safe to delete and rebuild.</summary>
    Derived = 2,
}
