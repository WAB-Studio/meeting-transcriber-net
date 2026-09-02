using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// What the screen a meeting is read from shows and offers, whatever stage that meeting is at.
/// </summary>
/// <remarks>
/// <para>
/// One screen and not three. A meeting gains a transcription and then a summary, and each of those
/// adds to what is already on the screen rather than replacing it — so what changes between the
/// three stages is how much of this record is filled in, and nothing about which screen somebody
/// is looking at. A screen per stage would be three blueprints for one thing, and the day a fourth
/// stage arrives it would be four.
/// </para>
/// <para>
/// It is here and not beside the window for the reason <c>docs/layout.md</c> gives about
/// <c>RecorderScreen</c>: touching a type from <c>MeetingTranscriber.App</c> fires the Windows App
/// SDK module initializer, so anything that lives there has no probe a build agent can run. What
/// this holds is every question the screen asks and no answer about a device or a control, which
/// is what makes the half of that screen with rules in it the half that gets tested.
/// </para>
/// </remarks>
/// <param name="Owed">The stage the meeting is at and where that stands.</param>
/// <param name="Left">What the AI has left of it, which is nothing until something has.</param>
/// <param name="AudioIsThere">
/// Whether the meeting's recording is really on the disk. A fact about a file and not about the
/// stage: the stage passes a rung when a job says so as well as when a file does, so a meeting can
/// read as transcribed with its audio gone.
/// </param>
public sealed record MeetingScreen(OwedWork Owed, WhatTheAiLeft Left, bool AudioIsThere)
{
    /// <summary>How far the meeting has got.</summary>
    public MeetingStage Stage => Owed.Stage;

    /// <summary>
    /// Whether what this meeting recorded can be played back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file and nothing else. Hearing what was recorded is not something anybody is charged
    /// for, so no part of this question reaches a transcription, a job, a standing or a price: a
    /// meeting somebody recorded this morning and has bought nothing for plays exactly as one
    /// summarised last month does, and one whose job is stopped over a charge nobody has settled
    /// plays too.
    /// </para>
    /// <para>
    /// It is deliberately not read off <see cref="Stage"/>, which would have been the shorter
    /// sentence and the wrong one. A stage passes a rung when a <em>job</em> came back saying so as
    /// well as when a file is there, so a meeting whose response landed and whose audio has since
    /// gone reads as transcribed — and reading the player off that would put a play button over
    /// nothing. The stage is about what has been bought; this is about what is on the disk.
    /// </para>
    /// </remarks>
    public bool MayBePlayedBack => AudioIsThere;

    /// <summary>
    /// What this screen offers to buy next, or nothing when there is nothing to offer or the
    /// standing is one where offering would do harm.
    /// </summary>
    /// <remarks>
    /// The same two answers the list carries and worked out the same way, because they are the
    /// same question asked from another screen. Pressing it costs nothing: what it opens is where
    /// the charge is agreed to, which is not this screen's either.
    /// </remarks>
    public JobKind? TheActOffered => Owed.MayBeTaken ? Owed.Next : null;

    /// <summary>Whether the act can be left for now.</summary>
    public bool TheActMayBeLeft => Owed.MayBeLeft && Owed.Next is not null;

    /// <summary>
    /// Whether the name is the reader's to type here.
    /// </summary>
    /// <remarks>
    /// Every stage but the bottom one. A meeting whose recording has not been filed yet is one the
    /// application is still writing rows about, and a title typed into that window is a write
    /// racing the save that made the meeting — so the field is not there rather than there and
    /// liable to be lost. Every other stage is fair game, which is what "at any time after it was
    /// recorded" means.
    /// </remarks>
    public bool TheNameMayBeTyped => Stage is not MeetingStage.Recording;

    /// <summary>Where each thing the AI left sits along the meeting.</summary>
    public IReadOnlyList<Duration> MarkedAlongTheMeeting => Left.MarkedAlongTheMeeting;
}
