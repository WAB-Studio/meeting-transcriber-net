using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording;

/// <summary>
/// Corrects somebody's name, commits that alone, and then renders again every meeting whose voices
/// name them, one meeting and one transaction at a time, so the transcripts say what the screens
/// say.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MeetingRenderer.Header"/> reads <c>people.display_name</c> at render time and a rename
/// moves no <c>speaker_assignments</c> row, so without this every <c>transcript.md</c> naming this
/// person would keep the old name while every live reading showed the new one.
/// </para>
/// <para>
/// The rename is not held open across the renders. A person named on dozens of meetings would
/// otherwise keep the corpus's write lock for as long as every one of those renders takes, past
/// <see cref="CorpusDatabase.BusyTimeoutMilliseconds"/> for the job runner and every other screen's
/// save waiting behind it — for a rename that has, by then, already committed. So the rename lands
/// on its own, and each render after it opens a connection of its own, the way <c>OwedRenders</c>
/// renders meetings unrelated to each other. A render that throws leaves its entities in its own
/// context's change tracker and nowhere else, which is what makes carrying on past it safe: one
/// context shared across every render would instead resend a failed render's rows at the next
/// meeting's save and be refused again there too, the same rule <c>CorpusRebuild.Discard</c> and
/// <c>MeetingRecordings.Save</c> already hold to.
/// </para>
/// <para>
/// The list of meetings to render is read inside the rename's own transaction, before the lock is
/// let go. A voice of this person's assigned to a meeting after that transaction commits and before
/// the loop below it finishes is not on the list: it is not rendered here and not named in the
/// exception either, because it was never owed by this call in the first place. That meeting reads
/// the new name the next time anything renders it, same as a meeting assigned the day before this
/// call ran — it is simply not this call's to report on.
/// </para>
/// </remarks>
public static class RenamingSomebody
{
    /// <summary>
    /// Renames <paramref name="person"/> and renders again every meeting a voice of theirs is on, at
    /// <paramref name="clock"/>'s instant. Answers with the renamed row, or <see langword="null"/>
    /// when this corpus no longer holds them — in which case nothing is written.
    /// </summary>
    /// <exception cref="RenderException">
    /// The rename itself always lands. One or more of their meetings could not be rendered again;
    /// the exception names every one of them and leaves the rest — and the rename — exactly as they
    /// landed. <c>render &lt;meeting id&gt;</c> is how each named meeting is caught up afterwards.
    /// </exception>
    public static Person? Rename(DirectoryInfo root, Person person, string displayName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(clock);

        Person? renamed;
        Guid[] ordered;

        using (var context = CorpusDatabase.Open(root))
        using (var renaming = context.Database.BeginTransaction())
        {
            renamed = new HumanLayer(context, clock).Rename(person, displayName);
            if (renamed is null)
            {
                return null;
            }

            var namedOn = context.SpeakerAssignments
                .Where(row => row.PersonId == person.Id)
                .Select(row => row.MeetingId)
                .Distinct()
                .ToArray();

            ordered = context.Meetings
                .Where(meeting => namedOn.Contains(meeting.Id))
                .OrderBy(meeting => meeting.StartedAt)
                .ThenBy(meeting => meeting.Id)
                .Select(meeting => meeting.Id)
                .ToArray();

            renaming.Commit();
        }

        var now = UtcTimestamp.From(clock.GetUtcNow());
        var stillTheOldName = new List<Guid>();

        foreach (var meeting in ordered)
        {
            try
            {
                using var context = CorpusDatabase.Open(root);
                RenderingAgain.OneMeeting(context, meeting, now);
            }
            catch (Exception unrendered) when (Absorbable(unrendered))
            {
                stillTheOldName.Add(meeting);
            }
        }

        if (stillTheOldName.Count > 0)
        {
            throw new RenderException(
                $"{person.Id} was renamed to \"{displayName}\", but could not be rendered again for "
                + $"{stillTheOldName.Count} meeting(s): {string.Join(", ", stillTheOldName)}. "
                + "'render <meeting id>' writes each one again.");
        }

        return renamed;
    }

    /// <summary>
    /// What one meeting's failed render turns into an entry on the list instead of aborting every
    /// meeting behind it — everything except a closed list of one, the same rule
    /// <c>OwedRenders.Absorbable</c> holds and for the same reason: naming what a render
    /// <em>may</em> throw is guaranteed to be incomplete, and out of memory is the one refusal that
    /// says nothing about this meeting and would only be thrown again by moving to the next one
    /// under the same pressure that just refused it.
    /// </summary>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
