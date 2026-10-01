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
    public static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
