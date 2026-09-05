using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What one sweep did: the meetings that turned out never to have been recorded, and one line
/// naming each folder it did not take and why.
/// </summary>
/// <remarks>
/// The second list is the one worth reading. Sweeping is the safe outcome only when it is right, so
/// what a sweep has to be able to say is what it declined to touch — a folder a capture was holding,
/// a folder naming a meeting the corpus has something of, a folder the disk would not give up.
/// </remarks>
/// <param name="Swept">
/// The meetings that are gone, in the order they were found: the folder went and then the row, and
/// an id is here only once both did. A folder naming a meeting this corpus never held is one of
/// these too — it is the half of a sweep a machine died in the middle of, finished.
/// </param>
/// <param name="Left">One line per folder that was looked at and not swept, and why it was not.</param>
public sealed record MeetingsSwept(IReadOnlyList<Guid> Swept, IReadOnlyList<string> Left);

/// <summary>
/// The meetings a press left behind when the recording never started, and taking them off the next
/// start.
/// </summary>
/// <remarks>
/// <para>
/// Somebody presses record and the microphone is refused, or the machine dies while the devices are
/// opening. The meeting's row, then its folder, then the claim over that folder are written before
/// any device is opened — that ordering is <see cref="MeetingRecordings.Open"/>'s, all three steps
/// of it, and it is deliberate — so what is left behind when the devices never record is a meeting
/// of nothing: a row that sits in the list saying it has no audio yet, and a folder holding nothing
/// but a <see cref="CaptureMark"/> nobody is holding, beside every real recording. Nothing ever took
/// either away, because every other path in this product is careful never to remove a recording,
/// and this one is not about a recording at all.
/// </para>
/// <para>
/// <b>What a recording is, is <see cref="UnfinishedRecordings"/>'s answer and never a second one
/// here</b>, and so is what one leaves on disk and what taking a folder away means. This class
/// knows two things the audio engine cannot: which folders to ask about, and whether the corpus
/// still holds something of the meeting a folder is named after. Everything else it asks. A folder
/// holding one block is a recording somebody may keep, take out or throw away however short it is,
/// and a sweep even slightly more willing than recovery is careful would be this product deleting a
/// meeting that happened.
/// </para>
/// <para>
/// The folder goes first and the row second, and that order is the whole safety of it. The press
/// itself holds <see cref="CaptureMark"/> over the folder — taken with the folder in
/// <see cref="MeetingRecordings.Open"/> and handed on to the session, so there is no instant in
/// which a folder exists under <c>spool/</c> for a meeting somebody is starting and nothing holds
/// it — in a share mode that forbids unlinking, so a press arriving at any point before the delete
/// makes the delete throw and this stops having changed nothing — where a row removed first would
/// leave a recording that is about to start with no meeting to be finished into. What it costs is
/// the other order's guarantee: a corpus that
/// refuses the write after the folder is gone leaves a row nothing will look at again, which is one
/// meeting in a list rather than one meeting lost.
/// </para>
/// <para>
/// Nothing here is done by time and nothing waits. A folder is swept on the evidence that nothing
/// was ever recorded into it, on this start or on the tenth one after it, and a folder that is still
/// somebody's to decide about is left however old it is.
/// </para>
/// </remarks>
public static class MeetingsNobodyRecorded
{
    /// <summary>
    /// Sweeps the corpus in <paramref name="root"/>: every meeting no sample was ever captured for
    /// loses its folder and its row, and every folder that holds a recording is left exactly where
    /// it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It answers instead of throwing, because the one caller is a launch nobody is waiting on: a
    /// corpus that will not open owes nobody a sweep, and a phantom meeting standing for one more
    /// start is not something to stop an application over. What it absorbs is everything but running
    /// out of memory, which is the shape <c>OwedRenders</c> settled on at the same launch and for
    /// the same reason — the exceptions a file system and SQLite can raise are not a list anybody
    /// can keep complete, and a sweep that threw would take the launch's background work with it.
    /// The cost is stated rather than hidden: a defect in this code comes back as a line in
    /// <see cref="MeetingsSwept.Left"/> like any refusal, so the tests below are what has to catch
    /// one.
    /// </para>
    /// <para>
    /// A folder is only ever read, and no corpus is ever made: a root holding none is a machine that
    /// has never recorded, and an empty new corpus written beside somebody's real one is exactly
    /// what that would hide. A corpus opened as it stands rather than migrated, so a launch that
    /// finds a schema older than the code sweeps nothing and says so, rather than running a
    /// migration nobody asked it for.
    /// </para>
    /// </remarks>
    /// <param name="root">The corpus root.</param>
    public static MeetingsSwept SweepIn(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var swept = new List<Guid>();
        var left = new List<string>();

        // Everything is inside it, including the two questions about the root: a disk that answers
        // neither is one this says nothing about, and it is the caller who would otherwise be
        // holding an exception thrown off a background task nobody awaits.
        try
        {
            var spool = CorpusFiles.SpoolRootIn(root);
            spool.Refresh();

            if (!spool.Exists || !CorpusDatabase.HoldsACorpus(root))
            {
                return new MeetingsSwept([], []);
            }

            using var corpus = CorpusDatabase.Open(root);

            foreach (var folder in NoRecordingIn(spool))
            {
                try
                {
                    Sweep(corpus, folder, swept, left);
                }
                catch (Exception refused) when (Absorbable(refused))
                {
                    // The folder is named here rather than left to the message: half of these say
                    // which file they are about and none of them says which meeting.
                    left.Add($"{folder.Name}: {refused.Message}");
                }
            }
        }
        catch (Exception unreadable) when (Absorbable(unreadable))
        {
            left.Add(unreadable.Message);
        }

        return new MeetingsSwept(swept, left);
    }

    /// <summary>
    /// The folders under <paramref name="spool"/> that hold no recording, in the order they are
    /// named.
    /// </summary>
    /// <remarks>
    /// Asked as the difference between every folder there is and the ones recovery calls recordings,
    /// rather than by looking for blocks again. Two places deciding what a recording is, is how a
    /// sweep and a recovery come to disagree about one folder, and the way that disagreement shows
    /// up is somebody's meeting gone.
    /// </remarks>
    private static IReadOnlyList<DirectoryInfo> NoRecordingIn(DirectoryInfo spool)
    {
        var recordings = UnfinishedRecordings.In(spool)
            .Select(recording => recording.Folder.FullName)
            .ToHashSet(CorpusFiles.PathComparer);

        return
        [
            .. spool.EnumerateDirectories()
                .OrderBy(folder => folder.Name, StringComparer.Ordinal)
                .Where(folder => !recordings.Contains(folder.FullName)),
        ];
    }

    /// <summary>
    /// One folder that holds no recording: gone with its meeting when nothing was ever recorded into
    /// it, and left with a line saying why when anything at all says otherwise.
    /// </summary>
    private static void Sweep(
        CorpusDbContext corpus, DirectoryInfo folder, List<Guid> swept, List<string> left)
    {
        // The audio engine's answer, and this asks it twice: here for the sentence, and again inside
        // the delete, which is where it is the file system's and cannot be out of date.
        if (UnfinishedRecordings.WhatSaysARecordingHappenedIn(folder) is { } said)
        {
            left.Add($"{folder.Name}: {said}.");
            return;
        }

        // The folder's own name and never a card: a card names the meeting a recording was of, and a
        // folder with nothing recorded in it is not one anybody has to attach to anything.
        if (!Guid.TryParse(folder.Name, out var named))
        {
            left.Add($"{folder.Name}: it is not named after a meeting.");
            return;
        }

        var meeting = corpus.Meetings.FirstOrDefault(row => row.Id == named);

        if (meeting is not null && SomethingCameOfIt(corpus, meeting) is { } what)
        {
            left.Add($"{folder.Name}: {what}");
            return;
        }

        // The folder before the row, which is where the whole race is settled — see the class. It
        // throws rather than answering, and what it throws lands in `Left` with the row untouched.
        UnfinishedRecordings.EraseWhereNothingWasRecorded(folder);

        if (meeting is not null)
        {
            // Read again rather than reused. What was read above was read before a delete that can
            // take a second, and the drawer is open to somebody the whole time: a title typed on
            // this meeting in that stretch is somebody having had it, and the row they typed on is
            // the one being removed.
            corpus.Entry(meeting).Reload();
            corpus.Meetings.Remove(meeting);
            corpus.SaveChanges();
        }

        swept.Add(named);
    }

    /// <summary>
    /// What the corpus holds of this meeting beyond the row a press wrote, or nothing when it holds
    /// none of it and the meeting is one nobody ever recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row has to be as pressing record left it. Its length is what a finish writes last, so a
    /// meeting that has one was recorded and saved; its title, its notes and the shape it was filed
    /// under are what a person typed on it, and somebody who wrote on a meeting had one whatever the
    /// folder says; and a lifecycle other than active is somebody's decision about it, which a sweep
    /// does not get to complete on their behalf.
    /// </para>
    /// <para>
    /// Then one query, because removing the row cascades and a cascade is silent. An artifact is the
    /// only row worth a round trip: it is the pointer to a file on disk, some of those files were
    /// paid for, and none of them can be obtained again — so a meeting with one is never swept
    /// however empty its folder looks. The rest of the tables a meeting id reaches are downstream of
    /// audio or of a device having opened, and both of those leave the folder holding something,
    /// which the answer above already refused. <c>MeetingsNobodyRecordedTests</c> spells out every
    /// table that carries a meeting id, so one arriving that is downstream of neither is a red test
    /// rather than rows cascaded away here.
    /// </para>
    /// </remarks>
    private static string? SomethingCameOfIt(CorpusDbContext corpus, Meeting meeting)
    {
        if (meeting.Duration is not null)
        {
            return "it is a meeting that was recorded and saved.";
        }

        if (meeting.Title is not null || meeting.Context is not null || meeting.TemplateId is not null)
        {
            return "somebody wrote on it.";
        }

        if (meeting.LifecycleState != LifecycleState.Active)
        {
            return "somebody has already decided what happens to it.";
        }

        var id = meeting.Id;

        return corpus.Artifacts.Any(row => row.MeetingId == id)
            ? "the corpus holds a file of it."
            : null;
    }

    /// <summary>
    /// What a sweep answers about instead of throwing, which is everything a disk or a corpus can
    /// refuse — and, deliberately, a defect here too.
    /// </summary>
    /// <remarks>
    /// The same rule <c>OwedRenders</c> uses at the same launch. A list of the exceptions a file
    /// system and SQLite can produce is one that is wrong the first time a path is a junction or a
    /// volume is a network share, and narrowing it far enough to let a defect through would mean
    /// naming them. So it is wide, and what pays for that is that nothing here acts on what it
    /// caught: a folder that threw is a folder left where it was.
    /// </remarks>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
