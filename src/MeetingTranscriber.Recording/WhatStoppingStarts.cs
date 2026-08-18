using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Recording;

/// <summary>
/// The one thing that decides what pressing stop sets going, and today it always answers nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stopping a meeting starts no work on it.</b> The recording is finished and the meeting sits
/// there; transcribing it is a separate press, made from the meeting itself once somebody has
/// decided they want it. That is not a limitation waiting to be lifted — transcription spends the
/// user's own Deepgram credit, and a stop that queued it would be this application spending
/// somebody's money for having stopped recording.
/// </para>
/// <para>
/// It exists as a type answering an empty list rather than as no code at all, and that is the
/// whole point of it. What a meeting is waiting for, and which stage offers which button, is being
/// built on top of this; when a stage does become something stopping sets going, it becomes so
/// here, once, where every caller already asks. The alternative is the decision arriving as an
/// <c>if</c> inside whichever caller needed it first, and a second one written differently in the
/// caller after that — which is how an application ends up spending money on one path and not on
/// the other, with nothing saying which was meant.
/// </para>
/// <para>
/// It takes the meeting because the answer is about that meeting and not about the application's
/// mood. Nothing reads it today; a rule that could not see the meeting it is deciding about would
/// have to be rewritten rather than extended the first time one did.
/// </para>
/// </remarks>
public static class WhatStoppingStarts
{
    /// <summary>
    /// The work that should be queued now that <paramref name="meeting"/> has stopped recording.
    /// Empty, always, and every caller is expected to handle a non-empty answer anyway.
    /// </summary>
    public static IReadOnlyList<JobKind> For(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        return [];
    }
}
