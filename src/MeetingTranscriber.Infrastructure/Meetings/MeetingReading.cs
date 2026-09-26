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
/// Reading one meeting out of the corpus: what a screen shows, what to unfold when somebody
/// presses a citation, a stretch of the transcript, and the one thing the screen writes back.
/// </summary>
/// <remarks>
/// <para>
/// It is the singular of <see cref="MeetingWork"/> and leans on it rather than repeating it: how
/// far a meeting has got and what is owed on it is one rule, asked here for one meeting and there
/// for all of them. What this adds is everything the list has no room for — what an extraction
/// left, who produced it, and where the audio is.
/// </para>
/// <para>
/// It was the screen's read and is now the meeting's. <see cref="Of"/> is still exactly what the
/// screen needs, and the reads beside it — <see cref="Row"/>, <see cref="Between"/>,
/// <see cref="EveryTurn"/>, <see cref="TranscribedFrom"/> — are what a reader that is not a screen
/// asks about one meeting.
/// The alternative was a second type over the same tables, which would have put two answers to
/// <em>which turns does this meeting have</em> in one assembly.
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

    /// <summary>
    /// The meeting's own row, and nothing else read on its behalf.
    /// </summary>
    /// <remarks>
    /// What a reader wants when it needs the meeting's own words — when it started, what it is
    /// called — and not the screen. <see cref="Of"/> costs a stage computation, four reads of what
    /// an extraction left and a stat of the audio file on disk, all of which a caller that only
    /// wants those two fields pays for and then throws away. It is also the one lookup every method
    /// here begins with, so the refusal for a meeting this corpus does not hold is written once.
    /// </remarks>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public Meeting Row(Guid meetingId) =>
        context.Meetings.AsNoTracking().FirstOrDefault(row => row.Id == meetingId)
            ?? throw new MeetingStageException($"This corpus holds no meeting {meetingId}.");

    /// <summary>Every turn of the meeting, in ordinal order.</summary>
    /// <remarks>
    /// What the screen that names voices needs, and what <see cref="WhoIsWho.Of"/> is built from:
    /// that screen asks about a whole meeting rather than one stretch of it, so it reads through
    /// <see cref="AsTurns"/> like every other read here and not through a second projection.
    /// </remarks>
    public IReadOnlyList<Turn> EveryTurn(Guid meetingId) =>
        AsTurns(context.Utterances
            .Where(turn => turn.MeetingId == meetingId)
            .OrderBy(turn => turn.Ordinal));

    /// <summary>One meeting, as the screen that reads it needs it.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public MeetingAsRead Of(Guid meetingId)
    {
        var meeting = Row(meetingId);

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

        return AsTurns(context.Utterances
            .Where(turn => turn.MeetingId == meetingId
                && turn.Ordinal >= first
                && turn.Ordinal <= last)
            .OrderBy(turn => turn.Ordinal));
    }

    /// <summary>
    /// The turns said in one stretch of a meeting, which is what opening part of a transcript is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By where a turn <em>starts</em> and not by where it overlaps. A turn is what a citation
    /// anchors on, so a stretch is the turns that began inside it, and one that was still being
    /// said when the stretch opened belongs to the stretch before — which is what makes two
    /// adjoining calls answer exactly what one call over both would have.
    /// </para>
    /// <para>
    /// Closed at the bottom and open at the top, for that same reason. Two calls walking a meeting
    /// back to back must not each return the turn on the boundary: an agent reading a meeting in
    /// stretches would quote it twice, and nothing downstream can tell a thing said twice from a
    /// thing reported twice.
    /// </para>
    /// <para>
    /// An empty answer is a real one, exactly as it is for <see cref="Around"/>. A stretch nobody
    /// spoke in and a meeting whose turns have not been produced yet are both nothing to show,
    /// rather than something to refuse.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting.</param>
    /// <param name="from">Where the stretch opens, from the meeting's start.</param>
    /// <param name="to">Where it closes, which is the first offset outside it.</param>
    /// <param name="limit">
    /// How many turns at most. Bounded in the query and not by the caller afterwards: the stretch
    /// somebody asks for can be the whole of a three-hour meeting, and reading every turn of one to
    /// hand back the first two hundred is the same answer at a thousand times the cost.
    /// </param>
    public IReadOnlyList<Turn> Between(Guid meetingId, Duration from, Duration to, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return AsTurns(context.Utterances
            .Where(turn => turn.MeetingId == meetingId
                && turn.Start >= from
                && turn.Start < to)
            .OrderBy(turn => turn.Ordinal)
            .Take(limit));
    }

    /// <summary>
    /// The SHA-256 of the paid response this meeting's turns were produced from, or nothing when
    /// they came from no response anybody paid for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run that <em>finished</em> and the artifact that run recorded, which is the corpus's own
    /// link from the turns on disk to what they were read out of. Not the newest response filed
    /// against the meeting: re-transcribing files a new one and never replaces the old, so between
    /// a response landing and the projection being rebuilt from it the newest artifact is a
    /// response these turns did not come from — and a hash a reader checks a quote against and
    /// cannot find is worse than no hash, because it is actionable and wrong.
    /// </para>
    /// <para>
    /// The same ordering as the transcription <see cref="Wrote"/> names, so the provider a reader
    /// is shown and the hash beside it are the one run and not two answers to two queries.
    /// </para>
    /// <para>
    /// This is about the live projection. What a decision, an action or an open question was quoted
    /// out of is the citation's own <c>SourceArtifactSha256</c>, which is a different question and
    /// is stored on the row for exactly this reason.
    /// </para>
    /// <para>
    /// <b>A re-transcription keeps this true, and two of its edges are worth saying plainly.</b> A
    /// response filed and read whose run could not then be recorded still names the response
    /// before this one — the write that would have moved this answer forward failed, and until it
    /// is written again this still reads as the last thing that really happened. A render refused
    /// before its turns were swapped leaves this naming the new response over turns that are still
    /// the old one's, until `render &lt;id&gt;` runs. Both are said at the moment they happen, by the
    /// answer the transcription gives — see <c>TranscribingAMeeting</c>'s own remarks on each.
    /// </para>
    /// </remarks>
    public string? TranscribedFrom(Guid meetingId)
    {
        var response = context.TranscriptionRuns
            .AsNoTracking()
            .Where(run => run.MeetingId == meetingId
                && run.FinishedAt != null
                && run.ResponseArtifactId != null)
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.CreatedAt)
            .Select(run => run.ResponseArtifactId)
            .FirstOrDefault();

        return response is null
            ? null
            : context.Artifacts
                .AsNoTracking()
                .Where(artifact => artifact.Id == response)
                .Select(artifact => artifact.Sha256)
                .FirstOrDefault();
    }

    /// <summary>
    /// Whatever rows a read narrowed to, as turns, in the order it put them in. One projection
    /// because two of them are two places a field goes missing the day <see cref="Turn"/> gains
    /// one.
    /// </summary>
    private static IReadOnlyList<Turn> AsTurns(IQueryable<Utterance> narrowed) =>
    [
        .. narrowed
            .AsNoTracking()
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
    /// <see cref="CorpusSearch.TheRunThatCounts"/> asked about one meeting, with the correlation
    /// replaced by a bound parameter.
    /// </summary>
    /// <remarks>
    /// A <c>SELECT</c> of a subquery with no <c>FROM</c> produces exactly one row whatever the
    /// corpus holds, so what says no run was accepted is that row carrying <c>NULL</c> and never an
    /// empty result — which is why the read below ends in <c>Single</c>.
    /// </remarks>
    private static readonly string TheAcceptedRunOfThisMeeting =
        $"SELECT {CorpusSearch.TheRunThatCounts("@meeting")} AS run;";

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
    /// <para>
    /// It is <see cref="CorpusSearch.TheRunThatCounts"/> and not a second spelling of it. The rule
    /// used to be written twice — this in LINQ and that in SQL — and held together by a test
    /// asserting the two agreed, which is what two readers of one rule need and is not the same as
    /// there being one rule. It reads through <see cref="RawSql"/> rather than through EF for the
    /// ordinary reason a raw read does: the ordering is a SQL string and LINQ cannot be handed one.
    /// The meeting is bound as a parameter and never put into the text.
    /// </para>
    /// <para>
    /// The bind goes through the provider's mapping for a <c>Guid</c> rather than through EF's, and
    /// the two agree because no <c>Guid</c> in this model carries a conversion. Giving one a
    /// conversion — a strongly-typed id, or ids stored as BLOB — moves EF and leaves this behind,
    /// and the symptom is not an error: the parameter matches no row, this answers <c>null</c>, and
    /// a meeting that has an accepted run renders as one that has none. So a <c>Guid</c> converter
    /// is a change that has to come here too.
    /// </para>
    /// </remarks>
    private Guid? TheRunThatCounts(Guid meetingId) => RawSql.Rows(
            context,
            TheAcceptedRunOfThisMeeting,
            reader => reader.IsDBNull(0) ? (Guid?)null : Guid.Parse(reader.GetString(0)),
            command => RawSql.Bind(command, "@meeting", meetingId))
        .Single();

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
