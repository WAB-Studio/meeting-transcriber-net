using System.Collections.Frozen;

namespace MeetingTranscriber.Recording;

/// <summary>What the recording screen is doing, which is what decides what can be pressed on it.</summary>
public enum RecorderState
{
    /// <summary>
    /// No meeting is being recorded. What is happening is somebody saying what the next one will
    /// record, and this is the only state any of those choices can be made in.
    /// </summary>
    Choosing,

    /// <summary>A meeting is being recorded.</summary>
    Recording,

    /// <summary>
    /// A meeting is being recorded and is paused. Its clock is still running, so this is a stretch
    /// of the meeting rather than a break in it.
    /// </summary>
    Paused,

    /// <summary>
    /// Record was pressed and the devices are being opened. Nothing is being recorded yet and
    /// nothing is pressable: each device has its own deadline for one that never answers, and the
    /// corpus may have a migration to run before the first row of the meeting.
    /// </summary>
    Starting,

    /// <summary>
    /// Stop was pressed and the meeting is being made. The devices are already let go of and
    /// nothing is being recorded, but the spools are still being poured onto a timeline, read back
    /// and hashed — minutes of work for a long meeting, and none of it is undone by pressing
    /// anything.
    /// </summary>
    Finishing,

    /// <summary>
    /// There is nowhere to record into: the folder the corpus was supposed to be in was refused.
    /// Nothing is pressable, and what is on screen is which folder and why.
    /// </summary>
    WithoutACorpus,
}

/// <summary>
/// A press that has been made and has not come back yet. Two of them open or close devices, so
/// neither is over when its handler returns, and while one is running the screen takes no press
/// at all.
/// </summary>
public enum RecorderStep
{
    /// <summary>Nothing is in flight.</summary>
    Nothing,

    /// <summary>Record was pressed and the devices are being opened.</summary>
    Starting,

    /// <summary>Stop was pressed and the meeting is being made.</summary>
    Finishing,
}

/// <summary>What somebody can press on the recording screen.</summary>
public enum RecorderPress
{
    /// <summary>Record.</summary>
    Start,

    /// <summary>Pause.</summary>
    Pause,

    /// <summary>Carry on.</summary>
    Resume,

    /// <summary>Stop, which finishes the meeting and starts nothing else.</summary>
    Stop,

    /// <summary>
    /// Take the whole machine's audio in place of the program channel 0 is following. Offered by
    /// the recording, never by the screen, and it puts every notification and every other
    /// application into the file from the moment it is pressed.
    /// </summary>
    RecordTheWholeMachine,
}

/// <summary>
/// Which presses each state reaches, as one table rather than as a condition repeated by every
/// control that has to decide whether it is live.
/// </summary>
/// <remarks>
/// The table is only half the answer and deliberately so: it says what the meeting's own state
/// allows, and <see cref="RecorderScreen"/> is what asks it and then applies what else has to be
/// true — that a source has been chosen, that an offer was made. Splitting it that way is what
/// keeps "a paused meeting cannot be paused again" from being restated inside every one of those
/// other conditions.
/// </remarks>
public static class RecorderStates
{
    private static readonly FrozenDictionary<RecorderState, FrozenSet<RecorderPress>> Presses =
        new Dictionary<RecorderState, FrozenSet<RecorderPress>>
        {
            // Nothing is being recorded. Record is the only press, and it is the choices that say
            // whether it is live yet.
            [RecorderState.Choosing] = Set(RecorderPress.Start),

            // Being recorded. The whole machine's audio is takeable only here: the offer comes
            // from channel 0 having heard nothing, and a paused meeting hears nothing by
            // definition, so a paused recording is exactly where that rule would say the wrong
            // thing.
            [RecorderState.Recording] = Set(
                RecorderPress.Pause, RecorderPress.Stop, RecorderPress.RecordTheWholeMachine),

            // Paused. Stopping from here is allowed and finishes the meeting with the pause in it
            // as the silence it was, rather than needing somebody to resume first.
            [RecorderState.Paused] = Set(RecorderPress.Resume, RecorderPress.Stop),

            // Being started. Nothing, and least of all record again: the devices are opening and
            // a second press would open a second meeting over the top of the first.
            [RecorderState.Starting] = Set(),

            // Being made. Nothing: the devices are gone and the work left cannot be interrupted
            // into a meeting anybody would want.
            [RecorderState.Finishing] = Set(),

            // Nowhere to record into.
            [RecorderState.WithoutACorpus] = Set(),
        }.ToFrozenDictionary();

    /// <summary>What a screen in this state reaches.</summary>
    public static IReadOnlySet<RecorderPress> Reaches(this RecorderState state) =>
        Presses.TryGetValue(state, out var presses)
            ? presses
            : throw new RecordingException($"Unknown recorder state '{state}'.");

    /// <summary>
    /// Which state a screen is in, read off the meeting rather than remembered beside it.
    /// </summary>
    /// <remarks>
    /// Derived and never stored, because the one bug this shape cannot have is the screen and the
    /// recording disagreeing about whether a meeting is running — which is what a second copy of
    /// the state, updated by whichever handler remembered to, eventually is.
    /// <para>
    /// The order is the whole of it. A press still in flight is asked about before the meeting is:
    /// stop lets the devices go before it starts making the meeting, so a recording that was
    /// paused when stop was pressed is still paused as far as anything can see, and asking that
    /// first would leave a screen offering resume for the minutes it takes to write the file.
    /// Starting is the same shape from the other end — nothing is recorded yet, and a screen that
    /// read that as nothing having been chosen would offer record a second time.
    /// </para>
    /// </remarks>
    /// <param name="corpus">Whether there is a corpus to record into at all.</param>
    /// <param name="started">Whether a meeting is being recorded.</param>
    /// <param name="paused">Whether that meeting is paused.</param>
    /// <param name="step">Whichever press is still running, if one is.</param>
    public static RecorderState Of(bool corpus, bool started, bool paused, RecorderStep step)
    {
        if (!corpus)
        {
            return RecorderState.WithoutACorpus;
        }

        if (step != RecorderStep.Nothing)
        {
            return step == RecorderStep.Starting ? RecorderState.Starting : RecorderState.Finishing;
        }

        if (!started)
        {
            return RecorderState.Choosing;
        }

        return paused ? RecorderState.Paused : RecorderState.Recording;
    }

    private static FrozenSet<RecorderPress> Set(params RecorderPress[] presses) => presses.ToFrozenSet();
}
