using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Rendering;

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
    /// <c>NamingTheVoices</c> already opens a transaction before reaching here, so it joins it. <c>OwedRenders</c>
    /// opens one too, for a meeting a rename left on an older name.
    /// <c>RenamingSomebody</c> and <c>CorrectingWords</c> render each meeting they touch on a connection of
    /// their own, with no transaction open yet, so for them this is the branch that opens one — a caller with none of its
    /// own would otherwise render a summarised meeting straight into <see cref="MeetingRenderer"/>'s
    /// ordinary refusal.
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
}
