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
/// <param name="TheRecording">What this meeting has to play, if anything.</param>
public sealed record MeetingScreen(OwedWork Owed, WhatTheAiLeft Left, RecordedAudio TheRecording)
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
    /// well as when a file is there, and a meeting that arrived as a paid response has no audio at
    /// all — so a stage above the bottom one says nothing about there being a file, and a player
    /// read off it would be a play button over nothing. The stage is about what has been bought;
    /// this is about what is on the disk.
    /// </para>
    /// </remarks>
    public bool MayBePlayedBack => TheRecording is RecordedAudio.Playable;

    /// <summary>Whether a transcription of this meeting exists at all.</summary>
    /// <remarks>
    /// Which is not the same question as who made it. A meeting that arrived here already
    /// transcribed carries the response and no run, so nothing in the corpus can name a provider —
    /// and a screen that read the absence of a name as nobody having transcribed it would be
    /// saying that under a heading that says it was.
    /// </remarks>
    public bool ThereIsATranscription =>
        Stage is MeetingStage.Transcribed or MeetingStage.Summarised;

    /// <summary>Whether a summary of this meeting exists at all.</summary>
    /// <remarks>Not the same question as who made it, for the reason above.</remarks>
    public bool ThereIsASummary => Stage is MeetingStage.Summarised;

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
    /// Whether the application has finished writing the rows this meeting is made of.
    /// </summary>
    /// <remarks>
    /// Every stage but the bottom one, and it is one fact rather than two that agree. A meeting
    /// whose recording has not been filed yet is one the application is still writing rows about,
    /// and anything a person writes into that window — a title, a link, a name — is a write racing
    /// the save that made the meeting. So each of those is offered or not offered off this, rather
    /// than each restating the stage for itself and coming to disagree.
    /// </remarks>
    private bool TheRowsAreWritten => Stage is not MeetingStage.Recording;

    /// <summary>
    /// Whether the name is the reader's to type here.
    /// </summary>
    /// <remarks>
    /// The field is not there rather than there and liable to be lost. Every other stage is fair
    /// game, which is what "at any time after it was recorded" means.
    /// </remarks>
    public bool TheNameMayBeTyped => TheRowsAreWritten;

    /// <summary>Whether this meeting is one to file under what it was about.</summary>
    /// <remarks>
    /// The same window and the same reason: links written while the meeting's own rows are still
    /// being written race the save that made it, so the press is not there.
    /// </remarks>
    public bool ItMayBeFiled => TheRowsAreWritten;

    /// <summary>Where each thing the AI left sits along the meeting.</summary>
    public IReadOnlyList<Duration> MarkedAlongTheMeeting => Left.MarkedAlongTheMeeting;
}

/// <summary>
/// What a meeting has to play, which is a fact about a file rather than about a stage.
/// </summary>
/// <remarks>
/// Three and not a pair of yes-or-nos, so that no reading of it can hold two answers at once. The
/// middle one is why it is not simply "is there a file": a meeting with no recording under it and
/// one whose recording the corpus records and cannot find are the same absence on screen and two
/// entirely different things to a person. The second is a source gone — audio is never produced
/// from anything and cannot be produced again — and saying nothing about it would hide the one
/// state of a meeting that somebody has to act on.
/// </remarks>
public enum RecordedAudio
{
    /// <summary>Nothing has been recorded under this meeting, and the corpus records nothing.</summary>
    NoneYet = 1,

    /// <summary>The corpus records a recording and the file is not where it says it is.</summary>
    NotWhereTheCorpusSaysItIs = 2,

    /// <summary>It is there, and it plays.</summary>
    Playable = 3,
}
