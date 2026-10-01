namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// One thing a person may choose to take out of the corpus, each a tick on the settings screen.
/// </summary>
/// <remarks>
/// A kind is a set of files, and <em>Correcciones a mano</em> is also a record the export writes
/// itself. Which artifacts fall under which kind is decided in one place, the switch in
/// <c>CorpusExport</c>. The names are stored, inside the <c>last-export</c> settings row, so
/// renaming a member is a change to what an existing corpus says it last took.
/// </remarks>
public enum ExportKind
{
    /// <summary>The recording: <c>audio.wav</c>.</summary>
    Audio = 1,

    /// <summary>Every paid response, and a readable <c>transcript.md</c>.</summary>
    Transcripts = 2,

    /// <summary>Every accepted extraction.</summary>
    Summaries = 3,

    /// <summary>What somebody settled by hand: names, notes, filing, corrections.</summary>
    HandCorrections = 4,
}
