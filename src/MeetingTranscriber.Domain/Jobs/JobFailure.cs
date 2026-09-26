namespace MeetingTranscriber.Domain.Jobs;

/// <summary>
/// Why a job failed for good, as what was observed rather than as one more sentence of English.
/// </summary>
/// <remarks>
/// A person reads a word chosen from this, never <see cref="ProcessingJob.LastError"/> verbatim:
/// that column is for whoever is diagnosing, and this is for the meeting's own row. There are
/// nine members: eight about a transcription that did not happen, and one about a summary that
/// was refused.
/// </remarks>
public enum JobFailure
{
    /// <summary>This machine keeps no Deepgram key at all.</summary>
    NoKeyOnThisMachine = 1,

    /// <summary>Deepgram would not accept the key this machine sent.</summary>
    KeyRefused = 2,

    /// <summary>The account behind the key has no credit left.</summary>
    OutOfCredit = 3,

    /// <summary>The account behind the key went over its rate.</summary>
    OverItsRate = 4,

    /// <summary>Deepgram refused the request for some other reason.</summary>
    RequestRefused = 5,

    /// <summary>No connection to Deepgram was ever made.</summary>
    ProviderNotReached = 6,

    /// <summary>The meeting's audio is not where the corpus says it is.</summary>
    AudioMissing = 7,

    /// <summary>The corpus would not record the attempt before anything was sent.</summary>
    CorpusRefused = 8,

    /// <summary>
    /// The summary that came back did not hold up against the meeting, and was not accepted.
    /// </summary>
    ExtractionRefused = 9,
}
