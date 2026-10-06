using System.Text.RegularExpressions;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>What of a meeting a person asks to have deleted.</summary>
public enum MeetingPart
{
    /// <summary>The meeting row, everything that hangs off it, and its folder.</summary>
    Whole = 1,

    /// <summary>The recording: the audio artifact and <c>audio.wav</c>.</summary>
    Audio = 2,

    /// <summary>
    /// Everything that was bought or derived from the audio: every version of the paid response,
    /// the summary, and the turns and the rows projected from them.
    /// </summary>
    Transcript = 3,
}

/// <summary>Why a deletion is not made, in a form the screen can say in one sentence.</summary>
public enum RemovalRefusal
{
    /// <summary>A job of the meeting is queued, running, waiting to retry, or stopped on a person.</summary>
    WorkIsUnderWay = 1,

    /// <summary>
    /// <c>spool/&lt;meeting_id&gt;/</c> stands: the meeting is being recorded or saved, or a
    /// recording a finish left there is waiting on a decision about it.
    /// </summary>
    ARecordingOfItIsWaiting = 2,

    /// <summary>The meeting has no such part.</summary>
    NothingToRemove = 3,

    /// <summary>
    /// The part is the last thing the meeting has. Deleting it would leave a meeting with neither
    /// audio nor transcript, and that is deleting the meeting.
    /// </summary>
    TheOtherHalfIsGone = 4,
}

/// <summary>
/// What a person deleting a meeting's audio, its transcript or the meeting itself does to the
/// corpus, and what putting a meeting away does.
/// </summary>
/// <remarks>
/// <para>
/// <b>It deletes paid and unrepeatable things, on a person's explicit word.</b> Everything the
/// transcript takes was bought or derived from what was bought: every version of the paid response,
/// every refused response the corpus kept, the summary, and the rows projected from them. The
/// audio is a source nothing can produce again. Nothing here is ever called by anything but a
/// person's press, and the screen asks first.
/// </para>
/// <para>
/// <b>What a deleted transcript keeps</b> is everything a person said about the meeting: the title,
/// the note, the filing, the people, the corrections and the names given to voices. A name given
/// to a voice is read by nothing until the meeting is transcribed again, and the render that files
/// the next transcription drops the labels it does not carry. What goes with the summary and is
/// said to a person is the progress somebody recorded on an action, and which summary was put back
/// as the one shown: each hangs off an extraction run and has nothing to point at without it.
/// </para>
/// <para>
/// <b>Nothing reads as whole while it is half gone.</b> The order is the guarantee, and a machine
/// that dies anywhere in it leaves something <see cref="FinishIn"/> can tell the end of.
/// </para>
/// <list type="bullet">
/// <item><description>
/// The whole meeting: the refusals are checked and the row is set to
/// <see cref="LifecycleState.Deleting"/> in one transaction, which every reader (they all filter on
/// <see cref="LifecycleState.Active"/>) stops offering from the instant it commits. Then the folder
/// is renamed to <c>meetings/.removing-&lt;id&gt;</c>, then the row and its audit rows go, then
/// the renamed folder is erased. A disk that refuses the rename puts the row back.
/// </description></item>
/// <item><description>
/// The audio or the transcript: in one transaction opened first, the refusals are checked, each
/// file is renamed to <c>&lt;name&gt;.removing</c> (and every one renamed so far is renamed back if
/// any is refused), the rows go, the transaction commits, and only then are the renamed files
/// erased. A file held open — the player, a scanner — refuses the rename and so leaves the meeting
/// exactly as it was, which an erase after the commit could not.
/// </description></item>
/// </list>
/// <para>
/// <b>How the launch tells a committed deletion from one that was not.</b> A file renamed to
/// <c>.removing</c> whose artifact row still stands is one the deletion never committed, and goes
/// back. One whose row is gone was committed, and is erased. A refused response has no artifact row,
/// ever, so it is told by its transcription run, which the same transaction deletes.
/// </para>
/// <para>
/// <b>The check is made where the write is.</b> <c>BeginTransaction</c> on this corpus issues
/// <c>BEGIN IMMEDIATE</c>, so the refusals are read under the corpus's only write lock: a job
/// queued by another connection is either already visible here or waits for this commit, and then
/// finds its meeting gone.
/// </para>
/// </remarks>
public sealed class MeetingRemoval(CorpusDbContext context, UtcTimestamp at)
{
    /// <summary>What a file being deleted is renamed to, so a refused rename leaves nothing changed.</summary>
    public const string RemovingSuffix = ".removing";

    /// <summary>What a meeting's folder being deleted is renamed to, in <c>meetings/</c>.</summary>
    public const string RemovingFolderPrefix = ".removing-";

    /// <summary>
    /// The spelling of a refused response's file name: <c>deepgram.refused.{run:N}.json</c>, which
    /// <c>TranscribingAMeeting.RefusedResponseFileName</c> composes. That type is in Processing,
    /// which this project does not reference, so the spelling is matched here and a test composes a
    /// name through the real one to keep the two together.
    /// </summary>
    private static readonly Regex RefusedResponse = new(
        @"^deepgram\.refused\.(?<run>[0-9a-f]{32})\.json$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>Everything that is the transcript and what was derived from it.</summary>
    private static readonly ArtifactKind[] TranscriptKinds =
    [
        ArtifactKind.DeepgramResponse,
        ArtifactKind.Extraction,
        ArtifactKind.Transcript,
        ArtifactKind.Utterances,
        ArtifactKind.Summary,
    ];

    /// <summary>
    /// Why this part of this meeting may not be deleted now, or nothing when it may. Asked by the
    /// screen to decide which presses to draw, and asked again by <see cref="Remove"/> inside its
    /// own write transaction.
    /// </summary>
    /// <exception cref="MeetingStageException">There is no such meeting here.</exception>
    public RemovalRefusal? WhyNot(Guid meetingId, MeetingPart part)
    {
        if (!context.Meetings.AsNoTracking().Any(meeting =>
            meeting.Id == meetingId && meeting.LifecycleState == LifecycleState.Active))
        {
            throw new MeetingStageException($"This corpus holds no meeting {meetingId} to delete.");
        }

        if (part is not MeetingPart.Whole)
        {
            var hasAudio = HasAudio(meetingId);
            var hasTranscript = HasTranscript(meetingId);
            var (has, other) = part is MeetingPart.Audio ? (hasAudio, hasTranscript) : (hasTranscript, hasAudio);

            if (!has)
            {
                return RemovalRefusal.NothingToRemove;
            }

            if (!other)
            {
                return RemovalRefusal.TheOtherHalfIsGone;
            }
        }

        // Every state a job can be in before it has settled. A job waiting for its next attempt is
        // the runner's to send, so it counts with the queued and the running; one stopped on a
        // person is a charge that may already have happened, which is the one thing a deletion
        // may never hide (`MeetingWork.Listed`).
        var unsettled = context.ProcessingJobs.AsNoTracking().Any(job =>
            job.MeetingId == meetingId
            && (job.State == JobState.Pending
                || job.State == JobState.Running
                || job.State == JobState.AwaitingUser
                || job.State == JobState.FailedRetryable));

        if (unsettled)
        {
            return RemovalRefusal.WorkIsUnderWay;
        }

        return Exists(CorpusFiles.SpoolFolderFor(context.Root, meetingId))
            ? RemovalRefusal.ARecordingOfItIsWaiting
            : null;
    }

    /// <summary>Deletes this part of the meeting, in the order the class remarks give.</summary>
    /// <exception cref="MeetingStageException">
    /// The meeting is not here, or <see cref="WhyNot"/> answers something for this part.
    /// </exception>
    /// <exception cref="IOException">
    /// The disk refused to move a file or a folder. Nothing has changed.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">As <see cref="IOException"/>.</exception>
    public void Remove(Guid meetingId, MeetingPart part)
    {
        if (part is MeetingPart.Whole)
        {
            RemoveTheMeeting(meetingId);
            return;
        }

        RemoveAPart(meetingId, part);
    }

    /// <summary>
    /// Takes the meeting out of the meetings list without deleting anything. It stays active, so
    /// every other reader still finds it.
    /// </summary>
    /// <exception cref="MeetingStageException">There is no such meeting here.</exception>
    public void Archive(Guid meetingId) => Mark(meetingId, at);

    /// <summary>Puts an archived meeting back in the list.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting here.</exception>
    public void PutBack(Guid meetingId) => Mark(meetingId, null);

    /// <summary>
    /// Finishes what a crash left of a deletion, and answers a line for everything it could not
    /// finish or place. Never throws for something the corpus or the disk did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="LifecycleState.Deleting"/> row is taken the rest of the way. A
    /// <c>meetings/.removing-&lt;id&gt;</c> folder with no row is erased. A <c>.removing</c> file is
    /// put back or erased by whether the deletion committed, which the class remarks say how to
    /// tell. A name it cannot place is left where it is and named.
    /// </para>
    /// <para>
    /// A corpus that is not there is nothing to finish, and no corpus is ever made.
    /// </para>
    /// </remarks>
    /// <param name="root">The corpus root.</param>
    public static IReadOnlyList<string> FinishIn(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var left = new List<string>();

        try
        {
            if (!CorpusDatabase.HoldsACorpus(root))
            {
                return left;
            }

            using var corpus = CorpusDatabase.Open(root);
            var removal = new MeetingRemoval(corpus, UtcTimestamp.From(DateTimeOffset.UtcNow));

            removal.FinishTheMeetingsOnTheirWayOut(left);
            removal.FinishTheFoldersNobodyHolds(left);
            removal.FinishTheFiles(left);
        }
        catch (Exception unreadable) when (unreadable is not OutOfMemoryException)
        {
            left.Add(unreadable.Message);
        }

        return left;
    }

    private bool HasAudio(Guid meetingId) =>
        context.Artifacts.AsNoTracking().Any(artifact =>
            artifact.MeetingId == meetingId && artifact.Kind == ArtifactKind.Audio);

    private bool HasTranscript(Guid meetingId) =>
        context.Artifacts.AsNoTracking()
            .Where(artifact => artifact.MeetingId == meetingId)
            .Select(artifact => artifact.Kind)
            .AsEnumerable()
            .Any(kind => TranscriptKinds.Contains(kind))
        || context.Utterances.AsNoTracking().Any(turn => turn.MeetingId == meetingId);

    private void Mark(Guid meetingId, UtcTimestamp? archivedAt)
    {
        using var write = context.Database.BeginTransaction();

        var changed = context.Meetings
            .Where(meeting => meeting.Id == meetingId && meeting.LifecycleState == LifecycleState.Active)
            .ExecuteUpdate(set => set
                .SetProperty(meeting => meeting.ArchivedAt, archivedAt)
                .SetProperty(meeting => meeting.UpdatedAt, at));

        if (changed == 0)
        {
            throw new MeetingStageException($"This corpus holds no meeting {meetingId} to put away.");
        }

        write.Commit();
    }

    private void Refuse(Guid meetingId, MeetingPart part)
    {
        if (WhyNot(meetingId, part) is { } why)
        {
            throw new MeetingStageException($"Meeting {meetingId} cannot be deleted ({part}): {why}.");
        }
    }

    private void RemoveTheMeeting(Guid meetingId)
    {
        using (var write = context.Database.BeginTransaction())
        {
            Refuse(meetingId, MeetingPart.Whole);

            context.Meetings
                .Where(meeting => meeting.Id == meetingId)
                .ExecuteUpdate(set => set
                    .SetProperty(meeting => meeting.LifecycleState, LifecycleState.Deleting)
                    .SetProperty(meeting => meeting.DeletedAt, (UtcTimestamp?)at)
                    .SetProperty(meeting => meeting.UpdatedAt, at));

            write.Commit();
        }

        try
        {
            MoveTheFolderAside(meetingId);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // The two columns go back together, because `ck_meetings_deleted_at` says one is
            // never set without the other. If even that is refused the row stays `deleting`, and
            // the next launch finishes a deletion this person confirmed; the disk's refusal is what
            // they are told either way, which is why the original exception is the one rethrown.
            try
            {
                context.Meetings
                    .Where(meeting => meeting.Id == meetingId)
                    .ExecuteUpdate(set => set
                        .SetProperty(meeting => meeting.LifecycleState, LifecycleState.Active)
                        .SetProperty(meeting => meeting.DeletedAt, (UtcTimestamp?)null));
            }
            catch (Exception notPutBack) when (notPutBack is Microsoft.Data.Sqlite.SqliteException)
            {
                // Said by the exception below.
            }

            throw;
        }

        DeleteTheRowsOfTheMeeting(meetingId);
        Erase(AsideFolder(meetingId));
    }

    private void RemoveAPart(Guid meetingId, MeetingPart part)
    {
        using var write = context.Database.BeginTransaction();

        Refuse(meetingId, part);

        var filed = context.Artifacts.AsNoTracking()
            .Where(artifact => artifact.MeetingId == meetingId)
            .Select(artifact => new { artifact.Id, artifact.Kind, artifact.RelativePath })
            .AsEnumerable()
            .Where(artifact => part is MeetingPart.Audio
                ? artifact.Kind is ArtifactKind.Audio
                : TranscriptKinds.Contains(artifact.Kind))
            .ToList();

        var files = filed
            .Select(artifact => CorpusFiles.Locate(context.Root, artifact.RelativePath))
            .ToList();

        if (part is MeetingPart.Transcript)
        {
            files.AddRange(RefusedResponsesOf(meetingId));
        }

        var aside = new List<(FileInfo Was, FileInfo Now)>();

        try
        {
            foreach (var file in files.Where(file => file.Exists))
            {
                var moved = new FileInfo(file.FullName + RemovingSuffix);
                File.Move(file.FullName, moved.FullName);
                aside.Add((file, moved));
            }

            if (part is MeetingPart.Audio)
            {
                DeleteTheRowsOfTheAudio(meetingId, filed.Select(artifact => artifact.Id).ToArray());
            }
            else
            {
                DeleteTheRowsOfTheTranscript(meetingId, filed.Select(artifact => artifact.Id).ToArray());
            }

            write.Commit();
        }
        catch
        {
            PutBackWhatWasMovedAside(aside);
            throw;
        }

        foreach (var (_, now) in aside)
        {
            Erase(now);
        }
    }

    private void DeleteTheRowsOfTheAudio(Guid meetingId, Guid[] artifacts)
    {
        context.Artifacts.Where(artifact => artifacts.Contains(artifact.Id)).ExecuteDelete();
        context.Meetings
            .Where(meeting => meeting.Id == meetingId)
            .ExecuteUpdate(set => set
                .SetProperty(meeting => meeting.AudioRemovedAt, (UtcTimestamp?)at)
                .SetProperty(meeting => meeting.UpdatedAt, at));
    }

    /// <summary>
    /// Every row a transcript is, in the order the foreign keys allow.
    /// </summary>
    /// <remarks>
    /// A citation names its turn by meeting and position with no cascade off <c>utterances</c>, so
    /// the claims go first and the turns after. Deleting the extraction runs takes the summaries,
    /// what was refused, and the progress on each action with them. The runs are deleted before the
    /// jobs they hang off, and the transcription runs are deleted <em>here</em> because the launch
    /// reads their absence as the deletion having committed. Every job of the meeting goes, because
    /// a succeeded transcription job would otherwise keep the meeting reading as transcribed.
    /// </remarks>
    private void DeleteTheRowsOfTheTranscript(Guid meetingId, Guid[] artifacts)
    {
        context.Decisions.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.ActionItems.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.OpenQuestions.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.ExtractionRuns.Where(run => run.MeetingId == meetingId).ExecuteDelete();
        context.Summaries.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.TurnSources.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.Utterances.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.TranscriptionRuns.Where(run => run.MeetingId == meetingId).ExecuteDelete();
        context.Artifacts.Where(artifact => artifacts.Contains(artifact.Id)).ExecuteDelete();
        context.ProcessingJobs.Where(job => job.MeetingId == meetingId).ExecuteDelete();
    }

    private void DeleteTheRowsOfTheMeeting(Guid meetingId)
    {
        using var write = context.Database.BeginTransaction();

        // Set null by the schema and not cascaded, so they would outlive the row with their detail.
        context.AuditEvents.Where(row => row.MeetingId == meetingId).ExecuteDelete();
        context.Meetings.Where(meeting => meeting.Id == meetingId).ExecuteDelete();

        write.Commit();
    }

    private IEnumerable<FileInfo> RefusedResponsesOf(Guid meetingId)
    {
        var folder = new DirectoryInfo(Path.Combine(context.Root.FullName, CorpusFiles.Meetings, meetingId.ToString()));

        return folder.Exists
            ? folder.EnumerateFiles().Where(file => RefusedResponse.IsMatch(file.Name))
            : [];
    }

    private DirectoryInfo AsideFolder(Guid meetingId) =>
        new(Path.Combine(context.Root.FullName, CorpusFiles.Meetings, RemovingFolderPrefix + meetingId));

    private void MoveTheFolderAside(Guid meetingId)
    {
        var folder = new DirectoryInfo(Path.Combine(context.Root.FullName, CorpusFiles.Meetings, meetingId.ToString()));

        if (Exists(folder))
        {
            Directory.Move(folder.FullName, AsideFolder(meetingId).FullName);
        }
    }

    private static bool Exists(DirectoryInfo folder)
    {
        folder.Refresh();
        return folder.Exists;
    }

    private static void PutBackWhatWasMovedAside(List<(FileInfo Was, FileInfo Now)> aside)
    {
        // Every file is tried, and a refusal does not stop the rest: whatever stays renamed is
        // put back by the next launch, and the exception the caller sees is the original one.
        foreach (var (was, now) in aside)
        {
            try
            {
                File.Move(now.FullName, was.FullName);
            }
            catch (Exception stillHeld) when (stillHeld is IOException or UnauthorizedAccessException)
            {
                // Left for the launch.
            }
        }
    }

    /// <summary>
    /// Erases what a committed deletion left renamed. A refusal is not thrown: the rows are gone, so
    /// the person has what they asked for, and the next launch finishes the erase.
    /// </summary>
    private static void Erase(FileSystemInfo aside)
    {
        try
        {
            if (aside is DirectoryInfo)
            {
                Directory.Delete(aside.FullName, recursive: true);
            }
            else
            {
                File.Delete(aside.FullName);
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // Left as it is, named so a launch finds it.
        }
    }

    private void FinishTheMeetingsOnTheirWayOut(List<string> left)
    {
        var deleting = context.Meetings.AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Deleting)
            .Select(meeting => meeting.Id)
            .ToList();

        foreach (var meetingId in deleting)
        {
            try
            {
                MoveTheFolderAside(meetingId);
                DeleteTheRowsOfTheMeeting(meetingId);
                Erase(AsideFolder(meetingId));
            }
            catch (Exception refused) when (Absorbable(refused))
            {
                left.Add($"{meetingId}: {refused.Message}");
            }
        }
    }

    private void FinishTheFoldersNobodyHolds(List<string> left)
    {
        var meetings = new DirectoryInfo(Path.Combine(context.Root.FullName, CorpusFiles.Meetings));

        if (!meetings.Exists)
        {
            return;
        }

        foreach (var folder in meetings.EnumerateDirectories(RemovingFolderPrefix + "*"))
        {
            try
            {
                if (!Guid.TryParse(folder.Name[RemovingFolderPrefix.Length..], out var meetingId)
                    || context.Meetings.AsNoTracking().Any(meeting => meeting.Id == meetingId))
                {
                    left.Add($"{folder.Name}: it is not the folder of a meeting that is gone.");
                    continue;
                }

                Erase(folder);
            }
            catch (Exception refused) when (Absorbable(refused))
            {
                left.Add($"{folder.Name}: {refused.Message}");
            }
        }
    }

    private void FinishTheFiles(List<string> left)
    {
        var meetings = new DirectoryInfo(Path.Combine(context.Root.FullName, CorpusFiles.Meetings));

        if (!meetings.Exists)
        {
            return;
        }

        foreach (var aside in meetings.EnumerateFiles("*" + RemovingSuffix, SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains(RemovingFolderPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            try
            {
                FinishTheFile(aside, left);
            }
            catch (Exception refused) when (Absorbable(refused))
            {
                left.Add($"{aside.FullName}: {refused.Message}");
            }
        }
    }

    private void FinishTheFile(FileInfo aside, List<string> left)
    {
        // Inside a write transaction, which waits for a deletion a person is making right now:
        // that one holds the write lock from its check to its commit, with its files renamed in
        // between, and a file read as "never committed" in that window would be put back for good.
        using var deciding = context.Database.BeginTransaction();

        var stored = CorpusFiles.RelativePathOf(context.Root, aside)[..^RemovingSuffix.Length];
        var parts = stored.Split('/');

        if (parts.Length < 3
            || !Guid.TryParse(parts[1], out var meetingId)
            || !context.Meetings.AsNoTracking().Any(meeting => meeting.Id == meetingId))
        {
            left.Add($"{aside.FullName}: it is not a file of a meeting this corpus holds.");
            return;
        }

        var committed = RefusedResponse.Match(Path.GetFileName(stored)) is { Success: true } refused
            ? !context.TranscriptionRuns.AsNoTracking().Any(run => run.Id == RunOf(refused))
            : !context.Artifacts.AsNoTracking().Any(artifact =>
                artifact.MeetingId == meetingId && artifact.RelativePath == stored);

        if (committed)
        {
            File.Delete(aside.FullName);
            return;
        }

        var was = CorpusFiles.Locate(context.Root, stored);

        if (was.Exists)
        {
            left.Add($"{aside.FullName}: its name is taken, so it was not put back.");
            return;
        }

        File.Move(aside.FullName, was.FullName);
    }

    private static Guid RunOf(Match refused) =>
        Guid.ParseExact(refused.Groups["run"].Value, "N");

    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
