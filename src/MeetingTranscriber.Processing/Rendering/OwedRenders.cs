using MeetingTranscriber.Domain.Artifacts;
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
    /// mistake to make — nothing leaves here that <see cref="Absorbable"/> would take, so a caller
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

        // The same rule as the loop below, for a different reason. Here there is no next meeting to
        // protect — a corpus that will not open owes nobody a sweep — so what the rule buys is only
        // the contract: the answer says the corpus could not be read, rather than a caller that
        // reads the answer being obliged to catch as well. Which is why the two are one predicate
        // and not two: a second one narrow enough to leave a defect through would have to name what
        // opening a corpus can refuse, and that is the enumeration this whole class stopped doing.
        catch (Exception unreadable) when (Absorbable(unreadable))
        {
            return new RendersCaughtUp([], [unreadable.Message]);
        }

        var rendered = new List<Guid>();
        var refused = new List<string>();

        foreach (var meeting in owed)
        {
            try
            {
                Produce(root, meeting, now);
                rendered.Add(meeting);
            }
            catch (Exception unrendered) when (Absorbable(unrendered))
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
    /// opens none of its own at all: it writes its two files as one act and their rows in one save,
    /// and the turns it projects are saved before either of those. So without this a meeting whose
    /// files could not be written keeps the turns of a render that did not happen. Foreign keys are
    /// not deferred the way a rebuild defers them, because a meeting owed its first render has no
    /// turns yet and so nothing cites one.
    /// </remarks>
    private static void Produce(DirectoryInfo root, Guid meeting, UtcTimestamp now)
    {
        using var context = CorpusDatabase.Open(root);
        using var write = context.Database.BeginTransaction();

        MeetingRenderer.Render(context, meeting, now);
        write.Commit();
    }

    /// <summary>
    /// What a catch-up turns into a line instead of throwing, which is everything except a closed
    /// list of one. Stated as the negative on purpose: the list that can be closed is the one being
    /// excluded, and the list being included cannot be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A render walks the response parser, the domain's audio contract, the artifact writer, the
    /// filesystem and SQLite, and any of those may learn a refusal tomorrow that this file will not
    /// hear about — so naming what a render <em>may</em> throw is guaranteed to be incomplete, and
    /// the incompleteness is not a missing line in a report. The sweep runs oldest first and
    /// remembers nothing between launches, so one escape starves every meeting behind the one that
    /// threw, on every launch, silently. That is precisely what happened here: a list of six types
    /// carried neither <c>DeepgramResponseException</c> nor <c>AudioContractException</c>, and both
    /// are on the ordinary path of a legacy meeting, because the importer files a
    /// <c>deepgram.json</c> on its sha256 without ever parsing it and imported meetings sort first.
    /// </para>
    /// <para>
    /// So the boundary is the meeting, which is what it was always said to be, and a defect inside
    /// the render is absorbed with everything else. That is a departure from what
    /// <c>MeetingsDrawer</c> says about the same choice — that a screen swallowing a defect leaves
    /// it looking like a corpus somebody could not read — and the departure is the point rather
    /// than an oversight. A screen has one person standing in front of it and nothing queued
    /// behind; this has nobody in front of it and every later meeting behind it, so the cost of
    /// absorbing is one named line and the cost of not absorbing is everybody else's files. What
    /// keeps a defect visible here is instead the probes: the sweep's ordinary paths assert that
    /// nothing was refused, so a render that starts throwing on every meeting fails the suite.
    /// </para>
    /// <para>
    /// Excluded is what says nothing about the meeting it was thrown on and would only be thrown
    /// again by moving to the next one. Out of memory is that, and today it is all of it: carrying
    /// on would mean attempting N more renders under the pressure that just refused this one. A
    /// stack overflow is not on the list because the runtime never offers one to a catch, and a
    /// cancellation is not on it because nothing on this path has a token to cancel — a line for
    /// one would be a line for a caller that does not exist. It reads the exception it was handed
    /// and not the chain under it, which is the same call: an out-of-memory wrapped in a
    /// <c>DbUpdateException</c> arrives as the corpus refusing a write, and that is a meeting.
    /// </para>
    /// </remarks>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;

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
