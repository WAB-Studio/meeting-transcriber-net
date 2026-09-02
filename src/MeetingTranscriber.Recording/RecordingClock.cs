using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Recording;

/// <summary>
/// The meeting's own clock while it is being recorded: how long it has been going, and whether
/// there is a meeting to say that of at all.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the window for the reason <see cref="RecordingMeters"/> is: reaching a
/// WinUI tree needs a UI thread and a packaged host, and a rule living there is a rule nothing
/// runs. What the window keeps is drawing a number from this.
/// </para>
/// <para>
/// <b>It is the stretch since the devices opened, and a pause is inside it.</b> That is the
/// recording's own arithmetic rather than a second opinion about it: what a paused meeting records
/// is silence of exactly the length the pause lasted, so a screen that stopped counting through a
/// pause would be the one thing on it disagreeing with the file.
/// </para>
/// <para>
/// <b>It is a count and not a statement of what the meeting turned out to be</b>, and the
/// difference is the whole reason this reads a clock rather than the recording's own counters.
/// Those count frames at the rate each device's header claims; the file is laid down at the rate
/// the engine measured that crystal really running at, and the two differ by the drift the whole
/// timeline exists to take out — under a second an hour for an ordinary device, and a great deal
/// more for one at the edge of what is accepted. So nothing read while the devices are open is
/// the meeting's length, and how long the meeting turned out to be is said once, by the save,
/// when there is an answer to say.
/// </para>
/// <para>
/// It is built and not updated, for the reason the meters are: every field on it is what was true
/// when it was built, so a screen cannot hold a clock it made a minute ago.
/// </para>
/// </remarks>
public sealed record RecordingClock
{
    /// <summary>No meeting is being recorded, so there is no clock.</summary>
    public static RecordingClock Nothing { get; } = new();

    /// <summary>How long the meeting has been going, and nothing when none is.</summary>
    public Duration Ran { get; init; }

    /// <summary>Whether there is a meeting to show a clock for.</summary>
    public bool Showing { get; init; }

    /// <summary>
    /// The clock as it is for a screen in <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Running for exactly as long as a meeting is being recorded, paused included — the meeting's
    /// clock keeps running through a pause, so a screen that took the number away for it would
    /// hide the one thing saying the pause is inside the meeting rather than a break in it.
    /// Starting and finishing show nothing, the same answer the meters give and for the same
    /// reason: in neither is a meeting being recorded, and a clock left standing through the
    /// minutes it takes to make a long meeting is a screen saying one still is.
    /// </para>
    /// <para>
    /// Asked which instant is later rather than subtracted blind. A <see cref="Duration"/> refuses
    /// to be negative, and a machine that stepped its clock back mid meeting — an NTP correction
    /// or a resume, not a fault — would throw out of a redraw, on a screen with both devices open
    /// and a meeting nobody has stopped. A clock that ran backwards reads as no time instead, and
    /// climbs again from wherever the machine now says it is.
    /// </para>
    /// </remarks>
    /// <param name="state">What the screen is doing.</param>
    /// <param name="startedAt">
    /// When the recording's devices opened, or nothing when there is no recording. Nullable
    /// because that is the shape of what a screen holds — the state and the recording are read off
    /// the same field one line apart — and not to allow a case where the two disagree.
    /// </param>
    /// <param name="now">What time it is.</param>
    public static RecordingClock Of(RecorderState state, UtcTimestamp? startedAt, UtcTimestamp now)
    {
        if (!state.IsRecording() || startedAt is not { } opened)
        {
            return Nothing;
        }

        return new RecordingClock
        {
            Ran = now > opened ? now - opened : Duration.Zero,
            Showing = true,
        };
    }
}
