namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// Everything an extraction can be refused for: eight things observed about it, closed.
/// </summary>
/// <remarks>
/// The set is closed and stored under a CHECK, so renaming one is a migration. The order the
/// members are declared in is the order <c>ExtractionCheck</c> asks them in: shape first, then the
/// input, then the meeting, then each participant, then each statement's own citation, closest
/// failure first.
/// </remarks>
public enum ExtractionCondition
{
    /// <summary>What came back does not have the shape an extraction must have.</summary>
    NotTheSchema = 1,

    /// <summary>It was made from a transcript that is not this meeting's, as it stands now.</summary>
    InputNotAsPrepared = 2,

    /// <summary>It says it is about another meeting.</summary>
    AnotherMeeting = 3,

    /// <summary>It names a voice this meeting does not have.</summary>
    SpeakerNotInTheMeeting = 4,

    /// <summary>It states something without citing where it was said.</summary>
    NoEvidence = 5,

    /// <summary>It cites a turn this meeting does not have.</summary>
    NoSuchTurn = 6,

    /// <summary>It cites a turn at a moment or in a voice that is not that turn's own.</summary>
    NotTheTurnCited = 7,

    /// <summary>It quotes words that turn does not say.</summary>
    QuoteNotInTheTurn = 8,
}
