using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>A meeting <see cref="RenderingAgain.Each"/> could not render, and what stopped it.</summary>
public sealed record NotRenderedAgain(Guid Meeting, string Why);

/// <summary>What <see cref="RenderingAgain.Each"/> did: the meetings that got their files and the ones that did not.</summary>
public sealed record RenderedAgain(IReadOnlyList<Guid> Rendered, IReadOnlyList<NotRenderedAgain> NotRendered);

/// <summary>
/// Renders a meeting that has been rendered before, with the check that every claim still lands on
/// a turn made at the commit rather than at the delete.
/// </summary>
/// <remarks>
/// A plain <see cref="MeetingRenderer.Render"/> of a meeting that has a summary refuses at the
/// <c>ExecuteDelete</c> of its turns — <see cref="MeetingRenderer"/>'s own remark says so: a claim
/// citing one of them stops it. This is what a caller reaches for once it means to render such a
/// meeting anyway: saving the names somebody put on a meeting's voices, or correcting a person's
/// name, has already changed what the transcript should say, and deferring the check to the commit
/// is what makes producing that file possible at all.
/// </remarks>
public static class RenderingAgain
{
    /// <summary>
    /// Renders one meeting with foreign keys deferred to the commit, joining the caller's
    /// transaction when one is open and opening one of its own otherwise.
    /// </summary>
    /// <remarks>
    /// <c>NamingTheVoices</c> already opens a transaction before reaching here, so it joins it.
    /// <see cref="Each"/> renders each meeting on a connection of its own, with no transaction open
    /// yet, so for it this is the branch that opens one — a caller with none of its own would
    /// otherwise render a summarised meeting straight into <see cref="MeetingRenderer"/>'s ordinary
    /// refusal.
    /// </remarks>
    public static RenderedMeeting OneMeeting(CorpusDbContext context, Guid meetingId, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var own = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        context.Database.ExecuteSqlRaw("PRAGMA defer_foreign_keys = ON;");

        var rendered = MeetingRenderer.Render(context, meetingId, now);

        own?.Commit();
        return rendered;
    }

    /// <summary>
    /// Renders each of <paramref name="meetings"/> in order, every one on a connection and a
    /// transaction of its own, and answers with what happened instead of throwing about it.
    /// </summary>
    /// <remarks>
    /// The one place that decides what a failed render does to the meetings behind it: nothing,
    /// except out of memory, which says nothing about the meeting it was thrown on and would only be
    /// thrown again by moving to the next one under the same pressure. A render that throws leaves
    /// its rows in its own context and nowhere else, so carrying on past it is safe; one context
    /// shared across the renders would resend them at the next meeting's save. A naming, a correction
    /// and the launch's catch-up all render this way, and a change to the rule lands here once.
    /// </remarks>
    public static RenderedAgain Each(DirectoryInfo root, IEnumerable<Guid> meetings, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(meetings);

        var rendered = new List<Guid>();
        var refused = new List<NotRenderedAgain>();

        foreach (var meeting in meetings)
        {
            try
            {
                using var context = CorpusDatabase.Open(root);
                OneMeeting(context, meeting, now);
                rendered.Add(meeting);
            }
            catch (Exception unrendered) when (Absorbable(unrendered))
            {
                refused.Add(new NotRenderedAgain(meeting, unrendered.Message));
            }
        }

        return new RenderedAgain(rendered, refused);
    }

    /// <summary>
    /// What a render's failure turns into an entry on a list instead of ending the sweep behind it:
    /// everything except out of memory, which says nothing about the meeting it was thrown on and
    /// would only be thrown again by moving to the next one under the same pressure.
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
    /// are on the ordinary path of an imported meeting, because the importer that read the Python
    /// corpus filed a <c>deepgram.json</c> on its sha256 without ever parsing it and the meetings it
    /// wrote sort first.
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
    public static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
