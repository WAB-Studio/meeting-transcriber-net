namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// The one thing a screen says about where a meeting is: a single word, decided once by
/// <see cref="OwedWork.Status"/> out of the stage the meeting has reached and where that stage
/// stands. Closed, so a screen's table over it can hold the whole of it.
/// </summary>
/// <remarks>
/// Not <see cref="MeetingStage"/> and not <see cref="StageStanding"/>: those are the two halves
/// of the answer, and a window that crossed them would be the rule living where no build agent
/// runs it. This is the crossing, written once, in the domain.
/// </remarks>
public enum MeetingStatus
{
    /// <summary>
    /// There is no audio under this meeting yet: it is being recorded, or its recording never
    /// finished. Nothing is owed it, and nothing can be sent.
    /// </summary>
    NoAudio = 1,

    /// <summary>Recorded, and nothing has been asked of it.</summary>
    Recorded = 2,

    /// <summary>A transcription or a summary is asked for and nothing has started it.</summary>
    Queued = 3,

    /// <summary>The transcription has been sent and its answer is not in.</summary>
    Transcribing = 4,

    /// <summary>Transcribed, and not summarised.</summary>
    Transcribed = 5,

    /// <summary>A summary, the first or a later one, is being run.</summary>
    Summarising = 6,

    /// <summary>Summarised, and nothing is running about it.</summary>
    Summarised = 7,

    /// <summary>The next step was offered and turned down; it can still be taken.</summary>
    Ignored = 8,

    /// <summary>
    /// A job stopped on a person somewhere on this meeting: a charge that may already have
    /// happened. It outranks every other status, whatever the stage.
    /// </summary>
    Stopped = 9,
}
