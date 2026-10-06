using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// ISC-223, over temporary corpora: the meetings go to an empty folder and are found whole there
/// before anything else is true of the move, and the old copy goes only when somebody asked for it
/// and only once the new one has been found whole a second time.
/// </summary>
public class CorpusMoveTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// ISC-223.1: the folder the meetings arrived in opens with every meeting the old one held and
    /// every file it held for each — a paid response and a rendering among them.
    /// </summary>
    /// <remarks>
    /// Goes red with the database left out of the move: the files arrive and the new folder is not
    /// a corpus, which is what <see cref="CorpusDatabase.HoldsACorpus"/> and the meeting count say.
    /// </remarks>
    [Fact]
    public void A_moved_corpus_opens_with_every_file_whole()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        var meetings = FillIn(old, meetings: 2);

        var moved = Copy(old.Root, arrived.Root);

        moved.Files.ShouldBe(1 + (2 * 2));
        moved.Bytes.ShouldBeGreaterThan(0);
        CorpusDatabase.HoldsACorpus(arrived.Root).ShouldBeTrue();

        using var context = CorpusDatabase.OpenMigrated(arrived.Root);
        context.Meetings.Select(meeting => meeting.Id).ToList().ShouldBe(meetings, ignoreOrder: true);

        // Nothing is wrong with the folder it arrived in: every row has its file with its hash,
        // and no file is there that no row records.
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();

        foreach (var artifact in context.Artifacts.ToList())
        {
            CorpusFiles.Sha256Of(CorpusFiles.Locate(arrived.Root, artifact.RelativePath))
                .ShouldBe(artifact.Sha256);
        }

        // And the old one is exactly as it was.
        CorpusDatabase.HoldsACorpus(old.Root).ShouldBeTrue();
        Directory.EnumerateFiles(old.Root.FullName, "*", SearchOption.AllDirectories)
            .Count(path => !path.Contains("corpus.db", StringComparison.Ordinal))
            .ShouldBe(2 * 2);
    }

    /// <summary>
    /// What was committed to the database and is still in its write-ahead log is moved too, which a
    /// copy of <c>corpus.db</c> alone would not do.
    /// </summary>
    [Fact]
    public void What_the_write_ahead_log_still_holds_is_moved_too()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();

        // The connection stays open, so SQLite has not folded the log back into the database file.
        using var held = old.OpenMigrated();
        var meeting = Meeting(held);
        held.SaveChanges();

        File.Exists(old.DatabasePath + "-wal").ShouldBeTrue();

        Copy(old.Root, arrived.Root);

        using var context = CorpusDatabase.OpenReadOnly(arrived.Root);
        context.Meetings.Select(row => row.Id).ToList().ShouldBe([meeting]);
    }

    /// <summary>
    /// ISC-223.2 and ISC-223.3: a file the corpus records that does not arrive whole takes the move
    /// away, with the new folder empty and the old one as it was.
    /// </summary>
    /// <remarks>
    /// The file is made to disagree with its row in the old folder, which is the way one would
    /// differ in the new: the copy is the bytes it was given, and the proof is against the row.
    /// </remarks>
    [Fact]
    public void A_file_that_does_not_arrive_whole_undoes_the_move()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 2);

        string damaged;
        using (var context = CorpusDatabase.Open(old.Root))
        {
            damaged = context.Artifacts.First().RelativePath;
        }

        CorpusDatabase.ClearPoolsFor(old.Root);
        File.AppendAllText(CorpusFiles.Locate(old.Root, damaged).FullName, "changed after it was hashed");

        var before = Everything(old.Root);

        Should.Throw<IOException>(() => Copy(old.Root, arrived.Root));

        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
        Everything(old.Root).ShouldBe(before);
    }

    /// <summary>
    /// ISC-223.2 and ISC-223.3: the folder is recorded as where the meetings are only after every
    /// file was found whole, so a copy that is not found whole never reaches <c>whenWhole</c>.
    /// </summary>
    /// <remarks>
    /// Goes red with <c>whenWhole?.Invoke()</c> moved above <c>FindEveryFileWhole</c> in
    /// <see cref="CorpusMove.Copy"/>: the flag is set before the damaged file is found.
    /// </remarks>
    [Fact]
    public void A_copy_not_found_whole_is_never_recorded()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);

        string damaged;
        using (var context = CorpusDatabase.Open(old.Root))
        {
            damaged = context.Artifacts.First().RelativePath;
        }

        CorpusDatabase.ClearPoolsFor(old.Root);
        File.AppendAllText(CorpusFiles.Locate(old.Root, damaged).FullName, "changed after it was hashed");

        var recorded = false;

        Should.Throw<IOException>(
            () => CorpusMove.Copy(
                old.Root,
                arrived.Root,
                CancellationToken.None,
                whenWhole: () => recorded = true,
                applicationData: NoApplicationData));

        recorded.ShouldBeFalse();
    }

    [Fact]
    public void A_file_the_corpus_records_and_the_folder_does_not_hold_undoes_the_move()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);

        string missing;
        using (var context = CorpusDatabase.Open(old.Root))
        {
            missing = context.Artifacts.First().RelativePath;
        }

        CorpusDatabase.ClearPoolsFor(old.Root);
        File.Delete(CorpusFiles.Locate(old.Root, missing).FullName);

        Should.Throw<IOException>(() => Copy(old.Root, arrived.Root));

        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void A_move_that_is_stopped_leaves_the_new_folder_empty()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 2);

        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        Should.Throw<OperationCanceledException>(() => CorpusMove.Copy(old.Root, arrived.Root, stopped.Token, applicationData: NoApplicationData));

        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_with_something_in_it_is_refused()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);
        File.WriteAllText(Path.Combine(arrived.Root.FullName, "somebody's.txt"), "mine");

        Refusal(old.Root, arrived.Root).ShouldBe(CorpusMoveRefusal.NotEmpty);
        File.ReadAllText(Path.Combine(arrived.Root.FullName, "somebody's.txt")).ShouldBe("mine");
    }

    [Fact]
    public void A_folder_that_is_not_there_is_refused()
    {
        using var old = new TemporaryCorpus();
        FillIn(old, meetings: 1);

        Refusal(old.Root, new DirectoryInfo(Path.Combine(old.Root.FullName, "..", "not-made-" + Guid.NewGuid().ToString("n"))))
            .ShouldBe(CorpusMoveRefusal.NotEmpty);
    }

    [Fact]
    public void A_folder_inside_the_old_one_is_refused()
    {
        using var old = new TemporaryCorpus();
        FillIn(old, meetings: 1);
        var inside = old.Root.CreateSubdirectory("inside");

        Refusal(old.Root, inside).ShouldBe(CorpusMoveRefusal.InsideTheOther);
        Directory.EnumerateFileSystemEntries(inside.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_the_package_removes_is_refused()
    {
        using var old = new TemporaryCorpus();
        FillIn(old, meetings: 1);

        using var arrived = new TemporaryCorpus();

        var thrown = Should.Throw<CorpusMoveRefused>(
            () => CorpusMove.Copy(old.Root, arrived.Root, CancellationToken.None, applicationData: [arrived.Root.Parent!.FullName]));

        thrown.Refusal.ShouldBe(CorpusMoveRefusal.GoesWhenThePackageDoes);
        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Running)]
    public void Work_that_is_queued_or_running_refuses_the_move(JobState state)
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();

        using (var context = old.OpenMigrated())
        {
            var meeting = Meeting(context);
            var job = ProcessingJob.Queue(
                Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid()}", When);
            context.ProcessingJobs.Add(job);

            if (state == JobState.Running)
            {
                job.Start(When);
            }

            context.SaveChanges();
        }

        Refusal(old.Root, arrived.Root).ShouldBe(CorpusMoveRefusal.WorkPending);
        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void Work_that_has_finished_does_not_refuse_the_move()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();

        using (var context = old.OpenMigrated())
        {
            var meeting = Meeting(context);
            var job = ProcessingJob.Queue(
                Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid()}", When);
            context.ProcessingJobs.Add(job);
            job.Start(When);
            job.Succeed(When);
            context.SaveChanges();
        }

        Should.NotThrow(() => Copy(old.Root, arrived.Root));
    }

    [Fact]
    public void A_recording_in_the_spool_refuses_the_move()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);
        Directory.CreateDirectory(Path.Combine(CorpusFiles.SpoolRootIn(old.Root).FullName, Guid.NewGuid().ToString()));

        Refusal(old.Root, arrived.Root).ShouldBe(CorpusMoveRefusal.ARecordingWaits);
        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void A_recording_being_thrown_away_does_not_refuse_the_move_and_is_not_carried()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);
        var leaving = Path.Combine(
            CorpusFiles.SpoolRootIn(old.Root).FullName,
            RecordingFiles.BeingRemovedPrefix + Guid.NewGuid(),
            "one",
            "blocks");
        Directory.CreateDirectory(Path.GetDirectoryName(leaving)!);
        File.WriteAllText(leaving, "going");

        Copy(old.Root, arrived.Root);

        Directory.Exists(CorpusFiles.SpoolRootIn(arrived.Root).FullName).ShouldBeFalse();
    }

    [Fact]
    public void A_write_that_never_finished_is_not_carried()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        var meetings = FillIn(old, meetings: 1);
        var leftover = CorpusFiles.Locate(
            old.Root, $"meetings/{meetings[0]}/transcript.md.f00d{CorpusFiles.UnfinishedSuffix}");
        File.WriteAllText(leftover.FullName, "half");

        Copy(old.Root, arrived.Root);

        Directory.EnumerateFiles(arrived.Root.FullName, "*" + CorpusFiles.UnfinishedSuffix, SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A folder that could not be recorded as where the meetings are is left empty, so a retry is
    /// not refused as one that already holds something.
    /// </summary>
    [Fact]
    public void A_folder_that_could_not_be_recorded_is_left_empty()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 1);

        Should.Throw<IOException>(
            () => CorpusMove.Copy(
                old.Root,
                arrived.Root,
                CancellationToken.None,
                whenWhole: () => throw new IOException("the setting would not write"),
                applicationData: NoApplicationData));

        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    /// <summary>
    /// A write that stops part-way, here on a file in the old folder that cannot be read, leaves
    /// nothing behind, the half-written file included.
    /// </summary>
    [Fact]
    public void A_file_that_cannot_be_read_leaves_the_new_folder_empty()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        var meetings = FillIn(old, meetings: 2);

        var held = CorpusFiles.Locate(old.Root, CorpusFiles.PathFor(meetings[1], "transcript.md"));
        using var writing = new FileStream(held.FullName, FileMode.Open, FileAccess.Write, FileShare.None);

        Should.Throw<IOException>(() => Copy(old.Root, arrived.Root));

        Directory.EnumerateFileSystemEntries(arrived.Root.FullName).ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-223.4: a move nobody asked to remove the old copy in leaves it, whole.
    /// </summary>
    [Fact]
    public void A_move_on_its_own_leaves_the_old_copy_whole()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 2);
        var before = Everything(old.Root);

        Copy(old.Root, arrived.Root);

        Everything(old.Root).ShouldBe(before);
    }

    /// <summary>
    /// ISC-223.5: a new copy that is not found whole the second time leaves the old one in place.
    /// </summary>
    [Fact]
    public async Task Removing_the_old_copy_needs_the_new_one_whole()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 2);
        Copy(old.Root, arrived.Root);

        // Damaged after the move said it was whole.
        string damaged;
        using (var context = CorpusDatabase.OpenReadOnly(arrived.Root))
        {
            damaged = context.Artifacts.First().RelativePath;
        }

        CorpusDatabase.ClearPoolsFor(arrived.Root);
        File.AppendAllText(CorpusFiles.Locate(arrived.Root, damaged).FullName, "damaged");

        var before = Everything(old.Root);

        await Should.ThrowAsync<IOException>(() => CorpusMove.RemoveTheOldCopyAsync(old.Root, arrived.Root));

        old.Root.Refresh();
        old.Root.Exists.ShouldBeTrue();
        Everything(old.Root).ShouldBe(before);
    }

    [Fact]
    public async Task Removing_the_old_copy_leaves_nothing_behind()
    {
        using var old = new TemporaryCorpus();
        using var arrived = new TemporaryCorpus();
        FillIn(old, meetings: 2);
        Copy(old.Root, arrived.Root);

        await CorpusMove.RemoveTheOldCopyAsync(old.Root, arrived.Root);

        Directory.Exists(old.Root.FullName).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(old.Root.Parent!.FullName)
            .Select(Path.GetFileName)
            .ShouldNotContain(name => name!.StartsWith(RecordingFiles.BeingRemovedPrefix, StringComparison.Ordinal)
                && name.EndsWith(old.Root.Name, StringComparison.Ordinal));

        // The new one is still the corpus, with everything in it.
        using var context = CorpusDatabase.OpenReadOnly(arrived.Root);
        context.Meetings.Count().ShouldBe(2);
    }

    /// <summary>
    /// What a corpus holds for these meetings: a row, a rendering and a paid response each. Every
    /// file is written through the door that hashes it, so every row's hash is the file's.
    /// </summary>
    private static List<Guid> FillIn(TemporaryCorpus corpus, int meetings)
    {
        using var context = corpus.OpenMigrated();
        var ids = new List<Guid>();

        for (var made = 0; made < meetings; made++)
        {
            var meeting = Meeting(context);
            context.SaveChanges();

            DurableArtifact.WriteText(
                context, meeting, ArtifactKind.DeepgramResponse,
                CorpusFiles.PathFor(meeting, "deepgram.json"), When, "{\"paid\":\"for\"}");
            DurableArtifact.WriteText(
                context, meeting, ArtifactKind.Transcript,
                CorpusFiles.PathFor(meeting, "transcript.md"), When, "hola, buenas");

            ids.Add(meeting);
        }

        context.SaveChanges();
        CorpusDatabase.ClearPoolsFor(corpus.Root);
        return ids;
    }

    private static Guid Meeting(CorpusDbContext context)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Language = "es",
            StartedAt = When,
            SourceProfile = SourceProfile.Multichannel,
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        return meeting.Id;
    }

    private static CorpusMoveRefusal Refusal(DirectoryInfo from, DirectoryInfo to) =>
        Should.Throw<CorpusMoveRefused>(() => Copy(from, to)).Refusal;

    /// <summary>
    /// A temporary folder is under this profile's application data, which is what a corpus is
    /// refused for being in; the tests name no application data so the other rules are the ones
    /// they meet, and the one rule that is about it names a folder of its own.
    /// </summary>
    private static readonly IReadOnlyList<string> NoApplicationData = [];

    private static CorpusMoved Copy(DirectoryInfo from, DirectoryInfo to) =>
        CorpusMove.Copy(from, to, CancellationToken.None, applicationData: NoApplicationData);

    /// <summary>Every file of a folder and its length, so "as it was" is a comparison and not a feeling.</summary>
    private static List<string> Everything(DirectoryInfo root) =>
        Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal)
                && !path.EndsWith("-shm", StringComparison.Ordinal))
            .Select(path => $"{Path.GetRelativePath(root.FullName, path)}:{new FileInfo(path).Length}")
            .Order(StringComparer.Ordinal)
            .ToList();
}
