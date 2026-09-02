using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
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
/// A render that fails is tried again next time and nobody is told. The files cost nothing and can
/// be produced again from what has already been paid for, so failing to produce them is not a
/// decision a person has to make — the same reason they are never a button on the meetings list.
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
    /// A folder is only ever read, never made into a corpus: somebody's corpus not being where it
    /// was is exactly what an empty new one beside it would hide, and the first recording is what
    /// makes a corpus.
    /// </remarks>
    public static RendersCaughtUp CatchUpOn(DirectoryInfo root, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);

        IReadOnlyList<Guid> owed;

        try
        {
            if (!CorpusDatabase.HoldsACorpus(root))
            {
                return new RendersCaughtUp([], []);
            }

            using var reading = CorpusDatabase.Open(root);
            owed = Owed(reading);
        }
        catch (Exception unreadable) when (Reportable(unreadable))
        {
            return new RendersCaughtUp([], [unreadable.Message]);
        }

        var now = UtcTimestamp.From(clock.GetUtcNow());
        var rendered = new List<Guid>();
        var refused = new List<string>();

        foreach (var meeting in owed)
        {
            try
            {
                Produce(root, meeting, now);
                rendered.Add(meeting);
            }
            catch (Exception unrendered) when (Reportable(unrendered))
            {
                // The meeting is named here rather than left to the message. Only some of these
                // say which meeting they are about, and a line saying access was denied is a line
                // nobody can act on.
                refused.Add($"{meeting}: {unrendered.Message}");
            }
        }

        return new RendersCaughtUp(rendered, refused);
    }

    /// <summary>
    /// One meeting's turns and its two files, together or not at all, on a connection of its own.
    /// </summary>
    /// <remarks>
    /// The transaction is opened here rather than left to <see cref="MeetingRenderer"/>, which
    /// opens one around the two files only: the turns it projects are saved before that, so
    /// without this a meeting whose files could not be written keeps the turns of a render that
    /// did not happen. Foreign keys are not deferred the way a rebuild defers them, because a
    /// meeting owed its first render has no turns yet and so nothing cites one.
    /// </remarks>
    private static void Produce(DirectoryInfo root, Guid meeting, UtcTimestamp now)
    {
        using var context = CorpusDatabase.Open(root);
        using var write = context.Database.BeginTransaction();

        MeetingRenderer.Render(context, meeting, now);
        write.Commit();
    }

    /// <summary>
    /// What a corpus, a disk or a response that is no longer there says, as against what a defect
    /// says. One of these costs the meeting it happened to and nothing else; anything outside the
    /// set leaves this class, and what becomes of it is the caller's.
    /// </summary>
    private static bool Reportable(Exception thrown) => thrown
        is RenderException
        or ArtifactWriteException
        or IOException
        or UnauthorizedAccessException
        or SqliteException
        or DbUpdateException;

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

        return context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active
                && responded.Contains(meeting.Id)
                && !(readable.Contains(meeting.Id) && lined.Contains(meeting.Id)))
            .OrderBy(meeting => meeting.StartedAt)
            .Select(meeting => meeting.Id)
            .ToArray();
    }

    private static IQueryable<Guid> Filed(CorpusDbContext context, ArtifactKind kind) => context.Artifacts
        .Where(artifact => artifact.Kind == kind)
        .Select(artifact => artifact.MeetingId);
}
