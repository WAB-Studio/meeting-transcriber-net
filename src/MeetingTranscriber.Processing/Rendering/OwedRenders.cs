using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>
/// What one catch-up did: the meetings that got their files, and one line naming each meeting that
/// did not and what stopped it.
/// </summary>
public sealed record RendersCaughtUp(
    IReadOnlyList<Guid> Rendered,
    IReadOnlyList<string> CouldNotRender);

/// <summary>
/// The meetings whose transcription has arrived and whose readable files have not, and producing
/// those files without anybody asking.
/// </summary>
/// <remarks>
/// <para>
/// It is owed work read off the corpus rather than a queue, and that is what makes it safe to ask
/// for at any moment: a meeting is owed a render exactly while its response is filed and its two
/// files are not. A catch-up that was interrupted, crashed or never ran leaves the same answer
/// behind for the next one, so nothing is remembered between runs and nothing has to be.
/// </para>
/// <para>
/// It is also owed while its transcript was written before a person named on one of its voices was
/// last changed — a rename is the case it exists for, and any other edit of the person costs one
/// render more, after which the transcript is newer again. A rename renders each meeting it
/// touches itself and names the ones it could not; this is what finds those, and any the
/// application closed before reaching, on the next launch.
/// </para>
/// <para>
/// It is owed as well while a correction that reaches one of its words — in a turn, its title or its
/// note — was made after its transcript was written; saving a correction renders what it touches
/// itself, and this is what finds the meetings it did not reach.
/// </para>
/// <para>
/// A render that fails is tried again next time and nobody is told. The files cost nothing and can
/// be produced again from what has already been paid for, so failing to produce them is not a
/// decision a person has to make — the same reason they are never a button on the meetings list.
/// That covers a render that failed and would work next time; a response the parser can never read
/// fails the same way on every launch and nobody hears it either, which is a gap this leaves open
/// deliberately — <see cref="RendersCaughtUp.CouldNotRender"/> carries the line and is waiting for
/// somewhere to say it.
/// </para>
/// <para>
/// One meeting is one unit of work, down to its own connection and its own transaction. That is
/// the deliberate difference from <see cref="CorpusRebuild"/>, which needs a single commit over
/// the whole corpus to check that every claim landed back on the turn it cited. Here the meetings
/// are unrelated, and the alternative is worse than untidy: the sweep runs oldest first with no
/// memory between launches, so one meeting that can never be written — a path something else is
/// sitting on, a folder this user may not write into — would otherwise take every newer meeting
/// down with it on every launch, which is the failure this whole thing exists to prevent.
/// </para>
/// </remarks>
public static class OwedRenders
{
    /// <summary>
    /// Renders every meeting in the corpus that is owed one, oldest first, and answers with what
    /// happened instead of throwing about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Instead of throwing about it" is the whole contract and not a description of the usual
    /// case. Past its two arguments — which are checked at the boundary and are the caller's own
    /// mistake to make — nothing leaves here that <see cref="RenderingAgain.Absorbable"/> would take, so a caller
    /// that forgets to read the answer loses the report and never loses the sweep. That is why the
    /// clock is asked inside the boundary and not before it: it is a collaborator a caller hands
    /// in, and a sweep is not something to abandon over one.
    /// </para>
    /// <para>
    /// A folder is only ever read, never made into a corpus: somebody's corpus not being where it
    /// was is exactly what an empty new one beside it would hide, and the first recording is what
    /// makes a corpus.
    /// </para>
    /// </remarks>
    public static RendersCaughtUp CatchUpOn(DirectoryInfo root, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);

        IReadOnlyList<Guid> owed;
        UtcTimestamp now;

        try
        {
            if (!CorpusDatabase.HoldsACorpus(root))
            {
                return new RendersCaughtUp([], []);
            }

            now = UtcTimestamp.From(clock.GetUtcNow());

            using var reading = CorpusDatabase.Open(root);
            owed = Owed(reading);
        }

        // The same rule as the loop in RenderingAgain.Each, for a different reason. Here there is no next meeting to
        // protect — a corpus that will not open owes nobody a sweep — so what the rule buys is only
        // the contract: the answer says the corpus could not be read, rather than a caller that
        // reads the answer being obliged to catch as well. Which is why the two are one predicate
        // and not two: a second one narrow enough to leave a defect through would have to name what
        // opening a corpus can refuse, and that is the enumeration this whole class stopped doing.
        catch (Exception unreadable) when (RenderingAgain.Absorbable(unreadable))
        {
            return new RendersCaughtUp([], [unreadable.Message]);
        }

        // The meeting is named on each line rather than left to the message. Only some of these
        // say which meeting they are about, and a line saying access was denied is a line nobody
        // can act on.
        var again = RenderingAgain.Each(root, owed, now);

        return new RendersCaughtUp(
            again.Rendered,
            [.. again.NotRendered.Select(refused => $"{refused.Meeting}: {refused.Why}")]);
    }

    /// <summary>
    /// The meetings a response has arrived for and whose two files have not been produced from it,
    /// oldest first.
    /// </summary>
    /// <remarks>
    /// The rows and not the files on disk: a row whose file the disk has lost is what <c>check</c>
    /// and <c>restore</c> are for, and what is owed here is a meeting nothing has ever rendered.
    /// Both rows, because the two files are one answer — a transcript naming turns the jsonl does
    /// not have reads as two different meetings depending on which was opened.
    /// <para>
    /// A meeting on its way out is left alone: the application owes nothing to a meeting somebody
    /// asked it to get rid of, and rendering one would be writing files into a folder about to go.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Guid> Owed(CorpusDbContext context)
    {
        var responded = Filed(context, ArtifactKind.DeepgramResponse);
        var readable = Filed(context, ArtifactKind.Transcript);
        var lined = Filed(context, ArtifactKind.Utterances);
        var named = StillOnAnOlderName(context);
        var uncorrected = StillWithoutACorrection(context);

        return context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active
                && responded.Contains(meeting.Id)
                && (!(readable.Contains(meeting.Id) && lined.Contains(meeting.Id))
                    || named.Contains(meeting.Id)
                    || uncorrected.Contains(meeting.Id)))
            .OrderBy(meeting => meeting.StartedAt)
            .Select(meeting => meeting.Id)
            .ToArray();
    }

    /// <summary>
    /// The meetings whose transcript was written before somebody named on one of its voices was last
    /// changed — the name it shows may be an older one. A render always moves the transcript's
    /// <c>ConfirmedAt</c>, so one catch-up settles it and the next finds nothing.
    /// </summary>
    /// <remarks>
    /// Compared in memory over rows already narrowed to the assignments and the transcripts:
    /// <see cref="UtcTimestamp"/> is stored as text, and ordering text is not what the comparison
    /// means. Both timestamps are read with <c>Max</c> so a meeting holding more than one of either
    /// is judged by the latest of each. Nothing is queued: a rename that committed and was never
    /// followed by its renders, because the application closed or a render failed, leaves this
    /// answer behind for the next launch.
    /// </remarks>
    private static Guid[] StillOnAnOlderName(CorpusDbContext context)
    {
        var namedAt = context.SpeakerAssignments
            .AsNoTracking()
            .Join(context.People, row => row.PersonId, person => person.Id,
                (row, person) => new { row.MeetingId, person.UpdatedAt })
            .ToArray()
            .GroupBy(row => row.MeetingId)
            .ToDictionary(group => group.Key, group => group.Max(row => row.UpdatedAt));

        var writtenAt = context.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Kind == ArtifactKind.Transcript)
            .Select(artifact => new { artifact.MeetingId, artifact.ConfirmedAt })
            .ToArray()
            .GroupBy(row => row.MeetingId)
            .ToDictionary(group => group.Key, group => group.Max(row => row.ConfirmedAt));

        return namedAt
            .Where(named => writtenAt.TryGetValue(named.Key, out var written) && named.Value > written)
            .Select(named => named.Key)
            .ToArray();
    }

    /// <summary>
    /// The meetings a correction touches: active ones with a transcript, in the order they were
    /// held, that a correction in <paramref name="corrections"/> reaches by scope and whose words say
    /// the correction's wrong text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are the render's own. Scope is <see cref="MeetingRenderer.CorrectionsReaching"/>,
    /// the upward walk the render applies, so scope has one rule. The words are the three things the
    /// render runs <see cref="Terminology.Apply"/> over — each stored turn, the title and the context
    /// note — read through <see cref="Terminology.Reaches"/>, so a word the render would replace
    /// is a word this reads. A chain, where one correction only reaches what another has just written, is
    /// not seen.
    /// </para>
    /// <para>
    /// The turns are read and decided in memory and not by a SQL <c>LIKE</c>: SQLite folds case for
    /// ASCII only, and a prefilter that missed <c>Ñ</c> against <c>ñ</c> would leave a meeting
    /// rendered with the old word.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> TouchedBy(
        CorpusDbContext context, IReadOnlyCollection<TerminologyCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(corrections);

        if (corrections.Count is 0)
        {
            return [];
        }

        var withATranscript = Filed(context, ArtifactKind.Transcript);
        var candidates = context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active
                && withATranscript.Contains(meeting.Id))
            .OrderBy(meeting => meeting.StartedAt)
            .ThenBy(meeting => meeting.Id)
            .Select(meeting => new { meeting.Id, meeting.Title, meeting.Context })
            .ToArray();

        return
        [
            .. candidates
                .Where(meeting => Touches(context, meeting.Id, meeting.Title, meeting.Context, corrections))
                .Select(meeting => meeting.Id),
        ];
    }

    /// <summary>
    /// <see cref="TouchedBy"/>'s rule for one meeting, so that a launch asking it of every meeting
    /// does not read the whole corpus once for each.
    /// </summary>
    private static bool Touches(
        CorpusDbContext context,
        Guid meetingId,
        string? title,
        string? note,
        IReadOnlyCollection<TerminologyCorrection> corrections)
    {
        var ids = corrections.Select(correction => correction.Id).ToHashSet();
        var reaching = MeetingRenderer.CorrectionsReaching(context, meetingId)
            .Where(correction => ids.Contains(correction.Id))
            .ToArray();

        if (reaching.Length is 0)
        {
            return false;
        }

        var turns = context.Utterances
            .AsNoTracking()
            .Where(turn => turn.MeetingId == meetingId)
            .Select(turn => turn.Text)
            .ToArray();

        return reaching.Any(correction =>
            (title is not null && Terminology.Reaches(title, correction))
            || (note is not null && Terminology.Reaches(note, correction))
            || turns.Any(turn => Terminology.Reaches(turn, correction)));
    }

    /// <summary>
    /// The meetings whose transcript was written before a correction that reaches one of their words
    /// was made. A render always moves the transcript's <c>ConfirmedAt</c>, so one catch-up settles
    /// it and the next finds nothing.
    /// </summary>
    /// <remarks>
    /// Time first, in memory, as <see cref="StillOnAnOlderName"/> compares: only the corrections
    /// created after a meeting's transcript count, and a meeting left with none is not read at all.
    /// A corpus with nothing newer than any transcript reads no turns.
    /// </remarks>
    private static Guid[] StillWithoutACorrection(CorpusDbContext context)
    {
        var made = context.TerminologyCorrections
            .AsNoTracking()
            .ToArray();

        if (made.Length is 0)
        {
            return [];
        }

        var writtenAt = context.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Kind == ArtifactKind.Transcript)
            .Select(artifact => new { artifact.MeetingId, artifact.ConfirmedAt })
            .ToArray()
            .GroupBy(row => row.MeetingId)
            .ToDictionary(group => group.Key, group => group.Max(row => row.ConfirmedAt));

        var newer = writtenAt
            .Select(written => (
                Meeting: written.Key,
                Corrections: made.Where(correction => correction.CreatedAt > written.Value).ToArray()))
            .Where(row => row.Corrections.Length > 0)
            .ToDictionary(row => row.Meeting, row => row.Corrections);

        if (newer.Count is 0)
        {
            return [];
        }

        var held = context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active)
            .OrderBy(meeting => meeting.StartedAt)
            .ThenBy(meeting => meeting.Id)
            .Select(meeting => new { meeting.Id, meeting.Title, meeting.Context })
            .ToArray();

        return
        [
            .. held
                .Where(meeting => newer.TryGetValue(meeting.Id, out var corrections)
                    && Touches(context, meeting.Id, meeting.Title, meeting.Context, corrections))
                .Select(meeting => meeting.Id),
        ];
    }

    private static IQueryable<Guid> Filed(CorpusDbContext context, ArtifactKind kind) => context.Artifacts
        .Where(artifact => artifact.Kind == kind)
        .Select(artifact => artifact.MeetingId);
}
