using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Recording;

/// <summary>
/// The one thing that decides what pressing stop sets going.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stopping a meeting starts what the person settled beforehand, and nothing else.</b>
/// Transcription spends the user's own Deepgram credit, so a stop that queued it on its own would
/// be this application spending somebody's money for having stopped recording. What makes queueing
/// it legitimate is that it was asked for once and in advance, on the settings screen — which is
/// <see cref="AfterARecording"/>, and why the answer arrives as a parameter rather than being read
/// here.
/// </para>
/// <para>
/// It answers at most transcription, whichever of the two paid answers was settled. A meeting that
/// has just stopped is at <c>MeetingStage.Recorded</c>; summarising is a stage it cannot be offered
/// yet, because the transcription a summary is made from does not exist — and queueing work whose
/// input does not exist is the one thing <c>JobStates</c> cannot describe. What the preference says
/// about summarising is therefore read again by whatever finishes a transcription, and that is
/// nothing today.
/// </para>
/// <para>
/// One place and not an <c>if</c> inside whichever caller needed it first. A second one written
/// differently in the caller after that is how an application ends up spending money on one path
/// and not on the other, with nothing saying which was meant.
/// </para>
/// <para>
/// <b>It does not take the meeting, and that is the other half of one place.</b> What a stop may
/// queue does not depend on which meeting stopped — a meeting that has just been recorded is at
/// the same stage as every other one. Whether <em>this</em> meeting still offers that stage is a
/// different question with a different answer, it is about rows nothing here can see, and it has to
/// be asked inside the transaction that writes: <c>MeetingWork.TakeIfItIsOffered</c> is where it
/// lives. A parameter here that was read by nothing would only make the caller's own check look
/// like a second opinion on this one.
/// </para>
/// </remarks>
public static class WhatStoppingStarts
{
    /// <summary>
    /// The work a recording that has just stopped should have queued, given what
    /// <paramref name="settled"/> says was asked for beforehand.
    /// </summary>
    /// <param name="settled">What the person settled about a recording that ends.</param>
    public static IReadOnlyList<JobKind> For(AfterARecording settled) =>

        // Never `Extract`, whichever of the two asked for a summary. The meeting has no
        // transcription for one to be made from, so the job's input does not exist and the row
        // would describe a stage the meeting cannot reach from where it is.
        settled is AfterARecording.Transcribe or AfterARecording.TranscribeAndSummarise
            ? [JobKind.Transcribe]
            : [];
}
