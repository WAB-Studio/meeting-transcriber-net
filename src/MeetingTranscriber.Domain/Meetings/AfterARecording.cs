namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// What should happen to a meeting once its recording ends, settled once rather than asked per
/// meeting.
/// </summary>
/// <remarks>
/// <para>
/// Three answers and there is no fourth. <em>Ask me every time</em> was drawn on the artboard and
/// is not here: there is no screen anywhere that asks, and a preference offering an answer nothing
/// can carry out is a control that lies. What stands in its place is <see cref="DoNothing"/> — the
/// meeting is recorded and sits in the list, and transcribing it is a press on its row, which is
/// what makes those presses load-bearing rather than a convenience.
/// </para>
/// <para>
/// Here in <c>Domain/Meetings/</c> beside <see cref="Setting"/>, which is what stores it, and
/// deliberately not in <c>Domain/Jobs/</c>: that folder says where a job state reaches and what a
/// charge is allowed to do, and a preference about what to queue is neither. What turns this into
/// queued work is <c>WhatStoppingStarts</c>, one place, and nothing here knows about a job at all.
/// </para>
/// <para>
/// The numbers are given rather than left to the compiler, for the reason every stored enum in
/// this repository gives them: the member names are what <c>WireNames</c> derives the stored
/// strings from, so a member inserted in the middle must not silently renumber the ones after it.
/// </para>
/// </remarks>
public enum AfterARecording
{
    /// <summary>
    /// Nothing is queued. The meeting is recorded, it sits in the list, and what is spent on it is
    /// spent by somebody pressing something.
    /// </summary>
    DoNothing = 1,

    /// <summary>Transcribing is queued the moment the recording is finished.</summary>
    Transcribe = 2,

    /// <summary>
    /// Transcribing is queued at the stop, and summarising follows the transcription.
    /// </summary>
    /// <remarks>
    /// A meeting that has just stopped cannot be offered a stage whose input does not exist, so the
    /// stop queues the transcription only — exactly what <see cref="Transcribe"/> queues — and the
    /// summarising half is queued by <c>JobRunner</c> in the same settle that files the
    /// transcription, through <c>MeetingWork.TakeIfItIsOffered</c>. The setting is read when the
    /// transcription settles, so a change made between the stop and that moment applies.
    /// <para>
    /// ISC-190.1 gates this on a person having been shown how the summary provider is paid; the
    /// settings screen is where that is said.
    /// </para>
    /// </remarks>
    TranscribeAndSummarise = 3,
}
