namespace MeetingTranscriber.Recording;

/// <summary>
/// Where a recording nobody got to stop stands on the list it is offered in, which is what decides
/// what may be pressed on its row.
/// </summary>
public enum WaitingStanding
{
    /// <summary>
    /// A capture is still writing it, which on this machine is a meeting in progress. There is
    /// nothing to decide about a meeting somebody is in the middle of.
    /// </summary>
    StillBeingRecorded,

    /// <summary>
    /// Its save is running right now. The blocks are no longer held by a device, so nothing on
    /// disk tells this apart from a recording the machine died in the middle of — the only thing
    /// that knows is the recorder doing the saving, and it says so.
    /// </summary>
    BeingSavedNow,

    /// <summary>
    /// Nobody has said what happens to it. Keeping it and throwing it away are both open, and it
    /// stays here however many starts go past it.
    /// </summary>
    Waiting,

    /// <summary>
    /// It cannot be made into the meeting it was of, and why is said rather than found by pressing.
    /// Throwing it away is still open, because a recording nothing can be made of is still
    /// somebody's to be rid of.
    /// </summary>
    CannotBecomeAMeeting,
}

/// <summary>What somebody may say about a recording the application never finished.</summary>
/// <remarks>
/// Two, and taking the audio out to a folder is deliberately not one of them. A recording somebody
/// exported is still sitting there waiting afterwards, so it was never an answer to the question
/// the row asks — <c>UnfinishedRecording.Export</c> is where it lives and what it is for, and a
/// third button here would be a row asking two questions.
/// </remarks>
public enum WaitingAnswer
{
    /// <summary>Throw it away: the blocks, the card and the folder holding them.</summary>
    Discard,

    /// <summary>Keep it, which is making it the meeting it is of.</summary>
    Keep,
}

/// <summary>One recording waiting to be decided about, as a list offers it.</summary>
/// <param name="Recording">The recording itself, and everything on disk about it.</param>
/// <param name="Standing">Where it stands, which is what says which answers it takes.</param>
public sealed record WaitingRow(WaitingRecording Recording, WaitingStanding Standing)
{
    /// <summary>Whether this row is one somebody has to answer.</summary>
    /// <remarks>
    /// What a list counts when it says how many things are waiting on the person. A recording
    /// being made and one being saved are not: both are the application working, and neither is a
    /// question.
    /// </remarks>
    public bool WaitsOnSomebody =>
        Standing is WaitingStanding.Waiting or WaitingStanding.CannotBecomeAMeeting;

    /// <summary>
    /// Whether this recording's blocks may be read through to say how long it turned out to be.
    /// </summary>
    /// <remarks>
    /// The same two it takes no answer about, for two different reasons that happen to coincide: a
    /// capture holds the files of one, and the other is having them poured onto a timeline by the
    /// save. Neither is a recording anybody is deciding on, so neither is worth the pass over a few
    /// hundred megabytes a source that saying how long it is costs.
    /// </remarks>
    public bool MayBeReadThrough => WaitsOnSomebody;

    /// <summary>Whether <paramref name="answer"/> is one this row offers.</summary>
    /// <exception cref="RecordingException">This row is in a standing nothing here answers for.</exception>
    public bool Allows(WaitingAnswer answer) => Standing switch
    {
        WaitingStanding.StillBeingRecorded => false,
        WaitingStanding.BeingSavedNow => false,
        WaitingStanding.Waiting => true,
        WaitingStanding.CannotBecomeAMeeting => answer == WaitingAnswer.Discard,
        _ => throw new RecordingException(
            $"There is nothing to say about '{answer}' on a recording that is '{Standing}'."),
    };
}

/// <summary>
/// The recordings a corpus is holding that nobody got to stop, in the order a list shows them and
/// each carrying what may be done to it.
/// </summary>
/// <remarks>
/// <para>
/// The rule and not the screen. What a person sees is a row per recording with at most two buttons
/// on it, and everything deciding which of them exist is here — in a project a build agent runs,
/// for the reason <see cref="RecorderStates"/> and <see cref="SavingTheMeeting"/> are: a screen
/// reads the answer and decides nothing, so what somebody is offered is provable with no window.
/// </para>
/// <para>
/// It adds one fact to what <see cref="WaitingRecordings"/> already answers, and it is the fact no
/// corpus read can hold. A meeting being saved is a row with no length whose blocks no device is
/// holding any more, which on disk is indistinguishable from a recording the machine died in the
/// middle of — so a list built off what is there alone offers a keep-or-discard on the meeting
/// somebody stopped four seconds ago, over blocks that are being read at that moment. The recorder
/// doing the saving is the only thing that knows, so it is asked.
/// </para>
/// </remarks>
public static class WaitingRows
{
    /// <summary>
    /// Every waiting recording as a row: the ones nothing is to be decided about yet first, and
    /// the rest newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the statement and no line says it. A meeting still being recorded and one
    /// being saved sit above the recordings somebody has to answer for, because they are what the
    /// application is doing rather than what it is asking. The rest go newest first, which is the
    /// order the meetings under them are already in, so one list does not read in two directions.
    /// </para>
    /// <para>
    /// When a recording started is asked of the corpus first and of the folder's own card second,
    /// and one that says neither sorts last rather than first: a row with no date is not evidence
    /// of being recent.
    /// </para>
    /// </remarks>
    /// <param name="waiting">What the corpus is holding, as it was listed.</param>
    /// <param name="beingSavedNow">
    /// The meeting whose save is running, or nothing when none is. Told and never worked out here,
    /// for the reason this type gives.
    /// </param>
    public static IReadOnlyList<WaitingRow> Of(
        IEnumerable<WaitingRecording> waiting, Guid? beingSavedNow)
    {
        ArgumentNullException.ThrowIfNull(waiting);

        return
        [
            .. waiting
                .Select(recording => new WaitingRow(recording, StandingOf(recording, beingSavedNow)))
                .OrderBy(row => row.WaitsOnSomebody)
                .ThenByDescending(row => Started(row.Recording) ?? DateTimeOffset.MinValue),
        ];
    }

    /// <summary>
    /// Where one recording stands, asked in the order the answers overrule each other.
    /// </summary>
    /// <remarks>
    /// The engine's own refusal first, because a meeting a device is still writing is not a
    /// recording anything else gets to find a fault in. The save second, because it is the one
    /// fact the disk cannot show. Only then what the folder and the corpus say about each other,
    /// which is what tells a recording that can be kept from one that can only be thrown away.
    /// </remarks>
    private static WaitingStanding StandingOf(WaitingRecording recording, Guid? beingSavedNow) =>
        recording.NothingToDecideYet is not null ? WaitingStanding.StillBeingRecorded
        : recording.MeetingId is Guid meeting && meeting == beingSavedNow ? WaitingStanding.BeingSavedNow
        : recording.Unrecoverable is not null ? WaitingStanding.CannotBecomeAMeeting
        : WaitingStanding.Waiting;

    /// <summary>
    /// When this recording started: what the corpus holds, or failing that what the folder wrote
    /// about itself when the devices opened.
    /// </summary>
    private static DateTimeOffset? Started(WaitingRecording recording) =>
        recording.Meeting?.StartedAt.Value ?? recording.Spooled.Card?.StartedAt.Value;
}
