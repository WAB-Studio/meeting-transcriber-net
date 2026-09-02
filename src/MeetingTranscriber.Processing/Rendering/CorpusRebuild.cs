using System.Diagnostics;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>
/// What one rebuild did. The middle list is the point: a meeting the corpus cannot rebuild is a
/// meeting whose response is gone, and that is worth a line rather than a silently smaller number.
/// </summary>
/// <param name="Elapsed">
/// How long the whole thing took, so a corpus that has outgrown this can be seen to have.
/// </param>
public sealed record RebuildReport(
    int Meetings,
    int Turns,
    IReadOnlyList<string> CouldNotRebuild,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Meetings} meetings, {Turns} turns, {Elapsed.TotalSeconds:0.00}s"
        + (CouldNotRebuild.Count is 0
            ? string.Empty
            : $"{Environment.NewLine}could not rebuild ({CouldNotRebuild.Count}):{Environment.NewLine}  "
                + string.Join($"{Environment.NewLine}  ", CouldNotRebuild));
}

/// <summary>
/// Throws away every projection in the corpus and produces it again from the sources, calling
/// nothing outside the machine.
/// </summary>
/// <remarks>
/// <para>
/// The turns of a meeting are deleted and reprojected, and the claims that cite them are neither
/// deleted nor touched. That is possible because a citation anchors on the meeting and the turn's
/// position rather than on a turn's id, so projecting the same response again lands every claim
/// back on the turn it came from — and it is <em>checked</em> because the whole rebuild runs in one
/// transaction with foreign keys deferred to its commit. A rebuild that moved an ordinal therefore
/// fails at the end instead of quietly rewriting what every stored claim points at, which is the
/// one failure this operation could have that nothing else would notice.
/// </para>
/// <para>
/// Summaries, decisions, actions and open questions are left where they are rather than
/// reprojected. They are derived from the accepted extractions and those files are kept, but
/// nothing reads one back into rows yet — that arrives with extraction validation — so deleting
/// them would be losing what this cannot put back. When it arrives, it is a step in here, and what
/// makes it safe to add is that every one of those rows is named by its run and its position rather
/// than by an id this would mint again.
/// </para>
/// <para>
/// A meeting the rebuild cannot do is named and costs that meeting: absorbed, never rolled back.
/// The rollback is what <see cref="OwedRenders"/> does and it would be wrong here.
/// <see cref="Infrastructure.Artifacts.DurableArtifact"/> moves a file into place before it records
/// the row, so undoing the row of a meeting that already had one leaves the corpus naming a size
/// and a hash the file on disk no longer has — the one direction that whole type exists to avoid.
/// A catch-up only ever renders meetings nothing has rendered, so there is no row to revert to and
/// its rollback leaves a file nothing has recorded, which is the recoverable direction; a rebuild
/// re-renders meetings that already have both. What a meeting refused partway keeps is therefore
/// what it managed to write, with its rows and its files still saying the same thing, and the next
/// rebuild does it again. See <see cref="Absorbable"/> for why the boundary is drawn around every
/// refusal rather than around a list of them.
/// </para>
/// <para>
/// Costing that meeting is a promise about the meetings behind it and not only about that one, so
/// what a refusal leaves pending does not survive into the next meeting: what it managed to write
/// stays, what it had not managed to write goes. Every meeting here writes through one shared
/// context, and a refusal from inside a save leaves the rows that save was sending on it; the next
/// save would send them again and be refused again, and the corpus would lose every meeting behind
/// the first one it could not read. <see cref="Discard"/> is what stops that, and says why it
/// detaches rather than opening a context per meeting.
/// </para>
/// <para>
/// The promise does not yet reach the refused meeting's own turns. <c>MeetingRenderer</c> drops
/// them before projecting the new ones and the drop is not pending — it is a statement that has
/// run — so a meeting refused between the two keeps neither generation, and a rebuild is the only
/// thing that would put them back and refuses the same way every time. Where that lands is on the
/// corpus-wide commit and never quietly: a claim citing one of those turns fails the deferred
/// foreign key check, which takes the whole run rather than the meeting. Narrowing that to the
/// meeting it came from is the same open question as <c>rebuild.Commit()</c> sitting outside the
/// guard, and it is not this loop's to answer — the delete is the renderer's.
/// </para>
/// <para>
/// EF Core tracking is the write path on purpose, for now. This is the bulk write of the system and
/// dropping to SQL over the same connection is the measured exception, not the default: the number
/// that would justify it is in the report, so the decision can be made from a measurement rather
/// than from a worry.
/// </para>
/// </remarks>
public static class CorpusRebuild
{
    /// <summary>
    /// Rebuilds every meeting that is still here, oldest first, then the search indexes.
    /// </summary>
    public static RebuildReport Run(CorpusDbContext context, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var clock = Stopwatch.StartNew();
        var meetings = context.Meetings
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active)
            .OrderBy(meeting => meeting.StartedAt)
            .Select(meeting => meeting.Id)
            .ToArray();

        var refused = new List<string>();
        var turns = 0;
        var rebuilt = 0;

        using var rebuild = context.Database.BeginTransaction();

        // Every claim in the corpus points at a turn that is about to be deleted and put back. The
        // deferral is what lets that happen at all, and what turns "the ordinals came out the same"
        // from an assumption into something the commit refuses to accept if it is false.
        context.Database.ExecuteSqlRaw("PRAGMA defer_foreign_keys = ON;");

        foreach (var meeting in meetings)
        {
            // The card first, and for every meeting rather than only the ones that render. It is
            // what makes this the command that puts a corpus right: a meeting filed before the
            // corpus had cards at all, or one whose title somebody has changed since, gets the card
            // the corpus now describes — and a meeting whose response is missing is exactly the one
            // worth being able to recognise in a folder.
            //
            // It is written inside the same guard as the render and still survives one that fails,
            // because nothing here is rolled back: a meeting whose response cannot be read is left
            // holding the card and nothing else. The card write reaches the artifact writer and the
            // disk too, so leaving it outside the guard would be the same defect one line up.
            try
            {
                MeetingManifest.Write(context, meeting, now);
                turns += MeetingRenderer.Render(context, meeting, now).Turns;
                rebuilt++;
            }
            catch (Exception unrebuilt) when (Absorbable(unrebuilt))
            {
                // Named here rather than left to the message. Only some of these say which meeting
                // they are about, and a line saying a provider numbers speakers from zero is a
                // line nobody can act on without one.
                refused.Add($"{meeting}: {Why(unrebuilt)}");
                Discard(context);
            }
        }

        CorpusIntegrity.RebuildSearchIndexes(context);
        rebuild.Commit();

        clock.Stop();
        return new RebuildReport(rebuilt, turns, refused, clock.Elapsed);
    }

    /// <summary>
    /// The rows a refused meeting was still trying to insert, thrown away, so the next meeting
    /// starts from a change tracker holding nothing of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is undone here: a row that is <see cref="EntityState.Added"/> was never written.
    /// What this stops is the other way one meeting costs the corpus. A refusal from inside a save
    /// leaves the rows that save was sending pending on a context every later meeting shares, and
    /// the next save of anything at all sends them again and is refused again — so the meeting the
    /// rebuild absorbed takes every meeting behind it, which is the failure absorbing exists to
    /// stop, arriving through the other door.
    /// </para>
    /// <para>
    /// <see cref="EntityState.Added"/> and nothing else, and the line is
    /// <see cref="Infrastructure.Artifacts.DurableArtifact"/>'s own rather than a scope chosen for
    /// tidiness. An added row corresponds to nothing the corpus has recorded, so dropping it leaves
    /// at worst a file no row names — the direction that type calls recoverable and the reconciler
    /// finds. A modified one is the opposite: <c>StagedArtifact.Commit</c> moves the file into place
    /// and only then updates the row's size and hash, so an update abandoned between those two
    /// leaves the corpus naming a size and a hash the disk no longer has. That is the one direction
    /// this whole design exists to avoid, and it is why the wider sweep <c>CorpusImporter.Commit</c>
    /// can afford — it rolls its meeting back and this never does — would be wrong here. A tracked
    /// delete would need the same answer and there is none to give: nothing in this loop produces
    /// one, because the turns of a meeting go through <c>ExecuteDelete</c>.
    /// </para>
    /// <para>
    /// A context of its own per meeting is the other shape, and it is what <see cref="OwedRenders"/>
    /// does. There the context comes with its own connection and its own transaction, and a
    /// transaction per meeting is the one thing a rebuild cannot have: every claim in the corpus is
    /// checked against the turns it cites at a single commit. A second context sharing this
    /// connection and this transaction would buy a clean tracker and, unlike this, would also stop
    /// the tracker growing across the run — which it does, by every turn of every meeting rebuilt
    /// so far. That growth is the same decision the remarks on this type leave to a measurement off
    /// the report, and it is not settled by a repair to the failure path.
    /// </para>
    /// </remarks>
    private static void Discard(CorpusDbContext context)
    {
        foreach (var pending in context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added)
            .ToArray())
        {
            pending.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Everything the refusal said, outermost first, because the one that names the meeting and the
    /// one that names the cause are not always the same exception.
    /// </summary>
    /// <remarks>
    /// The parser and the domain say what is wrong in their own message and wrap the cause under
    /// it; the corpus refusing a write says <c>"An error occurred while saving the entity changes.
    /// See the inner exception for details."</c> and puts the constraint that fired underneath. A
    /// report naming a meeting nobody can then look into has already failed at the one thing this
    /// list is for, so the whole chain is printed. That is a third answer to a question two other
    /// files answer differently — <c>CorpusImporter.Commit</c> prints one level down and
    /// <see cref="OwedRenders"/> prints the head — and it wins because either of those drops the
    /// sentence that names the meeting or the sentence that names the cause, depending on which
    /// refusal arrived. Reaching one spelling across the three is a card of its own.
    /// </remarks>
    private static string Why(Exception thrown)
    {
        var said = new List<string>();
        for (var cause = thrown; cause is not null; cause = cause.InnerException)
        {
            said.Add(cause.Message);
        }

        // ASCII, unlike the arrow this would read better as: nothing sets the console's encoding
        // and a rebuild is read in whatever code page the machine came with, where U+2192 is a
        // question mark. The rest of what this report prints is ASCII already.
        return string.Join(" -> ", said);
    }

    /// <summary>
    /// What a rebuild turns into a line instead of throwing, which is everything except a closed
    /// list of one. Stated as the negative on purpose: the list that can be closed is the one being
    /// excluded, and the list being included cannot be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rebuild walks the response parser, the domain's audio and speaker contracts, the artifact
    /// writer, the filesystem and SQLite, so naming what one <em>may</em> throw is guaranteed to be
    /// incomplete — and here the incompleteness was not a missing line in a report. This caught
    /// <c>RenderException</c> only, inside a loop under one transaction, so a response the parser
    /// rejects escaped the loop and rolled back every meeting rebuilt before it. Nothing was
    /// rebuilt, and the next run failed in exactly the same place, forever.
    /// </para>
    /// <para>
    /// The two refusals that actually did it are on the ordinary path of an imported meeting rather
    /// than on an exotic one: the legacy importer files a <c>deepgram.json</c> on its sha256 without
    /// ever parsing it, and imported meetings are the oldest in the corpus, so they are rebuilt
    /// first. A third arrives through <see cref="Domain.Knowledge.SpeakerLabels"/> on a speaker
    /// numbered below zero — an <c>ArgumentOutOfRangeException</c>, which reads like somebody's bug
    /// rather than a refusal and belongs to no list anybody would have written. That is the point:
    /// the list cannot be closed, so the one that is closed has to be the other one.
    /// </para>
    /// <para>
    /// Excluded is what says nothing about the meeting it was thrown on and would only be thrown
    /// again on the next one. Out of memory is that, and today it is all of it: carrying on would
    /// mean attempting the rest of the corpus under the pressure that just refused this meeting. It
    /// reads the exception it was handed and not the chain under it — an out-of-memory arriving
    /// wrapped in a <c>DbUpdateException</c> is the corpus refusing a write, and that is a meeting.
    /// </para>
    /// </remarks>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
