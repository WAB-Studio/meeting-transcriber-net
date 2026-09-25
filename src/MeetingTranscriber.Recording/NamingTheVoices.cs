using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording;

/// <summary>
/// Saves the names somebody put on a meeting's voices, and renders that meeting's files again in
/// the same transaction, so a name saved is a name the transcript already shows.
/// </summary>
/// <remarks>
/// The files a meeting renders are what a later reading sees, and <c>transcript.md</c> is one of
/// them. Saving the names and leaving the file for a rebuild nothing forces would let the screen say
/// one thing and the file another, so the two happen together or not at all.
/// </remarks>
public static class NamingTheVoices
{
    /// <summary>
    /// Saves <paramref name="answers"/> on <paramref name="meetingId"/>'s voices and, only when that
    /// changed something, renders the meeting again. Answers with how many labels it changed.
    /// </summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    /// <exception cref="ArgumentException">
    /// An answer names a label twice, a label no turn of the meeting carries, or a person this
    /// corpus does not hold.
    /// </exception>
    /// <exception cref="RenderException">
    /// The meeting could not be rendered again, which takes the names back with it.
    /// </exception>
    public static int Save(
        DirectoryInfo root, Guid meetingId, IReadOnlyList<VoiceAnswer> answers, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(clock);

        using var context = CorpusDatabase.Open(root);
        using var saving = context.Database.BeginTransaction();

        var changed = new MeetingVoices(context, clock).Save(meetingId, answers);

        if (changed > 0)
        {
            RenderingAgain.OneMeeting(context, meetingId, UtcTimestamp.From(clock.GetUtcNow()));
        }

        saving.Commit();
        return changed;
    }
}
