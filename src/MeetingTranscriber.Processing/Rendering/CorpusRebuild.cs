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
/// Summaries, decisions and actions are left where they are rather than reprojected. They are
/// derived from the accepted extractions and those files are kept, but nothing reads one back into
/// rows yet — that arrives with extraction validation — so deleting them would be losing what this
/// cannot put back. When it arrives, it is a step in here.
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
    public static RebuildReport Run(CorpusDbContext context, DirectoryInfo root, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);

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
            MeetingManifest.Write(context, root, meeting, now);

            try
            {
                turns += MeetingRenderer.Render(context, root, meeting, now).Turns;
                rebuilt++;
            }
            catch (RenderException missing)
            {
                refused.Add(missing.Message);
            }
        }

        CorpusIntegrity.RebuildSearchIndexes(context);
        rebuild.Commit();

        clock.Stop();
        return new RebuildReport(rebuilt, turns, refused, clock.Elapsed);
    }
}
