using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording;

/// <summary>
/// Corrects somebody's name and renders again every meeting whose voices name them, in the same
/// transaction, so the transcripts say what the screens say.
/// </summary>
/// <remarks>
/// <see cref="MeetingRenderer.Header"/> reads <c>people.display_name</c> at render time and a rename
/// moves no <c>speaker_assignments</c> row, so without this every <c>transcript.md</c> naming this
/// person would keep the old name while every live reading showed the new one.
/// </remarks>
public static class RenamingSomebody
{
    /// <summary>
    /// Renames <paramref name="person"/> and renders again every meeting a voice of theirs is on, at
    /// <paramref name="clock"/>'s instant. Answers with the renamed row, or <see langword="null"/>
    /// when this corpus no longer holds them — in which case nothing is written.
    /// </summary>
    /// <exception cref="RenderException">
    /// One of their meetings could not be rendered again, which takes the rename back with it.
    /// </exception>
    public static Person? Rename(DirectoryInfo root, Person person, string displayName, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(clock);

        using var context = CorpusDatabase.Open(root);
        using var renaming = context.Database.BeginTransaction();

        var renamed = new HumanLayer(context, clock).Rename(person, displayName);
        if (renamed is null)
        {
            return null;
        }

        var namedOn = context.SpeakerAssignments
            .Where(row => row.PersonId == person.Id)
            .Select(row => row.MeetingId)
            .Distinct()
            .ToArray();

        var ordered = context.Meetings
            .Where(meeting => namedOn.Contains(meeting.Id))
            .OrderBy(meeting => meeting.StartedAt)
            .Select(meeting => meeting.Id)
            .ToArray();

        var now = UtcTimestamp.From(clock.GetUtcNow());
        foreach (var meeting in ordered)
        {
            RenderingAgain.OneMeeting(context, meeting, now);
        }

        renaming.Commit();
        return renamed;
    }
}
