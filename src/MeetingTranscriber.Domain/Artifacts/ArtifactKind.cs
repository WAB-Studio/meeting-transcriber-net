namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>A file of the corpus, named by what it holds rather than by its extension.</summary>
public enum ArtifactKind
{
    /// <summary>Blocks written while recording, before a WAV exists to replace them.</summary>
    SpoolBlock = 1,

    /// <summary>The materialised stereo WAV, kept when the retention policy says so.</summary>
    Audio = 2,

    /// <summary>The response that was paid for, exactly as it arrived.</summary>
    DeepgramResponse = 3,

    /// <summary>One accepted extraction. A newer one never replaces it.</summary>
    Extraction = 4,

    /// <summary>The recovery card that makes a meeting recognisable without the database.</summary>
    Manifest = 5,

    Transcript = 6,
    Utterances = 7,
    Summary = 8,
}
