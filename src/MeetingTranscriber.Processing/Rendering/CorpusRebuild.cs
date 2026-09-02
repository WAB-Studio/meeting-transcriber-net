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
                refused.Add($"{meeting}: {unrebuilt.Message}");
            }
        }

        CorpusIntegrity.RebuildSearchIndexes(context);
        rebuild.Commit();

        clock.Stop();
        return new RebuildReport(rebuilt, turns, refused, clock.Elapsed);
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
