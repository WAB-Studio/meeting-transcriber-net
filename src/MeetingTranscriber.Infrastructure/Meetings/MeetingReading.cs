using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>
/// One meeting as it is read: its row, what the screen makes of it, and the audio under it.
/// </summary>
/// <param name="Meeting">The row, which is what a person reads the meeting by.</param>
/// <param name="Screen">What the screen shows and offers at the stage this meeting is at.</param>
/// <param name="Audio">
/// The file the meeting can be played from, or none. It is a path that was found on disk rather
/// than one the corpus merely has a row for: a screen offered a player over a file that is not
/// there is one whose play button does nothing.
/// </param>
public sealed record MeetingAsRead(Meeting Meeting, MeetingScreen Screen, FileInfo? Audio);

/// <summary>
/// The corpus side of the screen a meeting is read from: what to show, what to unfold when
/// somebody presses a citation, and the one thing the screen writes back.
/// </summary>
/// <remarks>
/// <para>
/// It is the singular of <see cref="MeetingWork"/> and leans on it rather than repeating it: how
/// far a meeting has got and what is owed on it is one rule, asked here for one meeting and there
/// for all of them. What this adds is everything the list has no room for — what an extraction
/// left, who produced it, and where the audio is.
/// </para>
/// <para>
/// Nothing here caches. Every call reads the corpus it was handed, for the reason
/// <c>MeetingsDrawer</c> gives about opening one per read: what is on screen has to be what is on
/// disk, and a meeting whose transcription landed while somebody was looking at it is exactly the
/// case a remembered answer gets wrong.
/// </para>
/// </remarks>
public sealed class MeetingReading(CorpusDbContext context, TimeProvider clock)
{
    /// <summary>
    /// How many turns either side of a cited one are unfolded with it.
    /// </summary>
    /// <remarks>
    /// A citation on its own is the sentence the extraction already showed, said again — so
    /// unfolding one would answer nothing. What a reader is checking is whether the thing above
    /// really follows from what was said, and that takes what came before it and what came after.
    /// Two either side is what fits under a line without becoming the transcript screen this
    /// product deliberately does not have.
    /// </remarks>
    public const int TurnsEitherSide = 2;

    /// <summary>One meeting, as the screen that reads it needs it.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public MeetingAsRead Of(Guid meetingId)
    {
        var meeting = context.Meetings.AsNoTracking().FirstOrDefault(row => row.Id == meetingId)
            ?? throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");

        var owed = new MeetingWork(context, clock).On(meetingId);
        var audio = Audio(meetingId, out var recorded);

        return new MeetingAsRead(meeting, new MeetingScreen(owed, Left(meetingId), recorded), audio);
    }

    /// <summary>
    /// The transcript around one cited turn, which is what pressing a citation opens.
    /// </summary>
    /// <remarks>
    /// By position and never by a turn's id, for the reason a citation is anchored that way: the
    /// ids belong to the projection and a rebuild mints new ones, so an id read off a decision
    /// written in March would find nothing after the meeting was rendered again in April.
    /// <para>
    /// An empty answer is a real one and not an error. A meeting whose turns have not been
    /// produced yet has nothing to unfold, and the screen says so where the turns would be rather
    /// than refusing to open the meeting at all.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting.</param>
    /// <param name="ordinal">The position of the cited turn on that meeting's timeline.</param>
    public IReadOnlyList<Turn> Around(Guid meetingId, int ordinal)
    {
        var first = Math.Max(0, ordinal - TurnsEitherSide);
        var last = ordinal + TurnsEitherSide;

        return
        [
            .. context.Utterances
                .AsNoTracking()
                .Where(turn => turn.MeetingId == meetingId
                    && turn.Ordinal >= first
                    && turn.Ordinal <= last)
                .OrderBy(turn => turn.Ordinal)
                .ToList()
                .Select(turn => new Turn(
                    turn.Ordinal,
                    turn.Start,
                    turn.End,
                    turn.Channel,
                    turn.SpeakerLabel,
                    turn.Text,
                    turn.Confidence)),
        ];
    }

    /// <summary>
    /// Puts the name somebody typed on the meeting, or takes the name off it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whitespace is no name. A field somebody emptied has to leave the meeting reading as one
    /// nobody has named rather than as one named the empty string, which would look on the list
    /// exactly like a meeting with a blank title and read to a screen reader as nothing at all.
    /// </para>
    /// <para>
    /// It goes through <see cref="HumanLayer.Describe"/> and not through the row, because a title
    /// is the one thing a person changes that the folder also carries: the recovery card beside
    /// the audio names the meeting, and a rename that reached only the database would leave the
    /// card saying something else until the next <c>rebuild</c>. The notes are handed back
    /// unchanged — this screen does not offer them, and passing null would erase whatever
    /// somebody wrote somewhere else.
    /// </para>
    /// </remarks>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public void Name(Guid meetingId, string? title)
    {
        var meeting = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");

        var named = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

        if (string.Equals(named, meeting.Title, StringComparison.Ordinal))
        {
            // Nothing typed is nothing written. A screen that saves on every leave would touch the
            // row and rewrite the recovery card each time somebody looked at a meeting.
            return;
        }

        new HumanLayer(context, clock).Describe(meeting, named, meeting.Context);
    }

    /// <summary>
    /// The file this meeting plays from, when one is really there, and which of the three states
    /// its recording is in either way.
    /// </summary>
    /// <remarks>
    /// The row and the file are two reads and they answer two different questions. A meeting with
    /// no row never had a recording — it arrived as a paid response, or its own is still being
    /// written. A meeting with a row and no file had one and the disk has lost it, which is a
    /// source gone and the one of the three somebody has to do something about.
    /// </remarks>
    private FileInfo? Audio(Guid meetingId, out RecordedAudio recorded)
    {
        var filed = context.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.MeetingId == meetingId && artifact.Kind == ArtifactKind.Audio)
            .Select(artifact => artifact.RelativePath)
            .FirstOrDefault();

        if (filed is null)
        {
            recorded = RecordedAudio.NoneYet;
            return null;
        }

        var file = CorpusFiles.Locate(context.Root, filed);

        if (!file.Exists)
        {
            recorded = RecordedAudio.NotWhereTheCorpusSaysItIs;
            return null;
        }

        recorded = RecordedAudio.Playable;
        return file;
    }

    /// <summary>
    /// What the AI left of this meeting, out of the one extraction that counts.
    /// </summary>
    /// <remarks>Which run that is, and why it is only ever one, is <see cref="TheRunThatCounts"/>.</remarks>
    private WhatTheAiLeft Left(Guid meetingId)
    {
        var summarised = TheRunThatCounts(meetingId);
        var wrote = Wrote(meetingId, summarised);

        if (summarised is not { } accepted)
        {
            return WhatTheAiLeft.Nothing with { Wrote = wrote };
        }

        var summary = context.Summaries
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId && row.ExtractionRunId == accepted)
            .Select(row => row.Abstract)
            .FirstOrDefault();

        var decisions = context.Decisions
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId && row.ExtractionRunId == accepted)
            .ToList()
            .Select(row => new LeftThing(
                LeftKind.Decision,
                row.Statement,
                row.Evidence.Start,
                row.Evidence.UtteranceOrdinal,
                row.Evidence.QuotedText,
                row.Evidence.SpeakerLabel));

        var actions = context.ActionItems
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId && row.ExtractionRunId == accepted)
            .ToList()
            .Select(row => new LeftThing(
                LeftKind.Action,
                row.Statement,
                row.Evidence.Start,
                row.Evidence.UtteranceOrdinal,
                row.Evidence.QuotedText,
                row.Evidence.SpeakerLabel));

        var questions = context.OpenQuestions
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId && row.ExtractionRunId == accepted)
            .ToList()
            .Select(row => new LeftThing(
                LeftKind.Question,
                row.Question,
                row.Evidence.Start,
                row.Evidence.UtteranceOrdinal,
                row.Evidence.QuotedText,
                row.Evidence.SpeakerLabel));

        return new WhatTheAiLeft(
            summary,
            WhatTheAiLeft.InTheOrderTheyWereSaid([.. decisions, .. actions, .. questions]),
            wrote);
    }

    /// <summary>
    /// Who transcribed this meeting and who summarised it, and when each of them did.
    /// </summary>
    /// <remarks>
    /// The run that finished rather than the run that started: a transcription that was queued and
    /// never came back has a row, and naming its provider under the meeting would say a provider
    /// wrote something it has not written. The summary is the run handed in — the same one whose
    /// decisions are on the screen, so the line saying who wrote this is about the words above it
    /// and not about whichever run a second query happened to reach first.
    /// </remarks>
    private WhoWroteThis Wrote(Guid meetingId, Guid? summarised)
    {
        var transcription = context.TranscriptionRuns
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId && row.FinishedAt != null)
            .OrderByDescending(row => row.FinishedAt)
            .ThenByDescending(row => row.CreatedAt)
            .Select(row => new { row.Provider, row.Model, row.FinishedAt })
            .FirstOrDefault();

        var extraction = context.ExtractionRuns
            .AsNoTracking()
            .Where(row => row.Id == summarised)
            .Select(row => new { row.Provider, row.Model, row.AcceptedAt })
            .FirstOrDefault();

        return new WhoWroteThis(
            transcription is null ? null : Named(transcription.Provider, transcription.Model),
            transcription?.FinishedAt,
            extraction is null ? null : Named(extraction.Provider, extraction.Model),
            extraction?.AcceptedAt);
    }

    /// <summary>
    /// Which extraction the screen reads, or none when no run of this meeting was accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The newest accepted one. A corpus keeps every extraction it was given — a newer one never
    /// replaces an older one, which is what makes re-summarising safe — so a screen reading them
    /// all would show the same decision two and three times over, worded slightly differently each
    /// time, with nothing on it saying which one is current. One accepted at a later moment is the
    /// one somebody accepted last, and that is the answer.
    /// </para>
    /// <para>
    /// A run nobody accepted is not read at all. Acceptance is what says a person looked at what
    /// the model wrote and let it into the corpus, and a screen that showed the unaccepted ones
    /// would be putting sentences nobody has vouched for under the meeting's own name.
    /// </para>
    /// <para>
    /// Asked once and handed to both readers rather than asked by each, and the order runs out to
    /// the id so there is no tie left over. Two runs accepted in the same millisecond, with two
    /// queries breaking that tie their own way, would put one run's decisions under another run's
    /// model — which is precisely what the line naming who wrote this exists to get right.
    /// </para>
    /// </remarks>
    private Guid? TheRunThatCounts(Guid meetingId) => context.ExtractionRuns
        .AsNoTracking()
        .Where(row => row.MeetingId == meetingId && row.AcceptedAt != null)
        .OrderByDescending(row => row.AcceptedAt)
        .ThenByDescending(row => row.CreatedAt)
        .ThenByDescending(row => row.Id)
        .Select(row => (Guid?)row.Id)
        .FirstOrDefault();

    /// <summary>
    /// A provider and the model it ran, as the one name a person reads.
    /// </summary>
    /// <remarks>
    /// Data and not a sentence, so it reads the same in either language and is exactly what the
    /// run recorded. A run that named no model is the provider on its own rather than the provider
    /// followed by a gap.
    /// </remarks>
    private static string Named(string provider, string? model) =>
        string.IsNullOrWhiteSpace(model) ? provider : $"{provider} {model}";
}
