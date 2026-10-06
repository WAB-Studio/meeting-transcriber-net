using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Infrastructure.Artifacts;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>Why a corpus will not be moved to the folder somebody named, before anything is written.</summary>
public enum CorpusMoveRefusal
{
    /// <summary>The folder is not there, or something is already in it.</summary>
    NotEmpty = 1,

    /// <summary>The folder is one an uninstall deletes, with everything that was paid for.</summary>
    GoesWhenThePackageDoes,

    /// <summary>One of the two folders is the other, or inside it.</summary>
    InsideTheOther,

    /// <summary>A job is pending or running: it is writing the corpus this would be copying.</summary>
    WorkPending,

    /// <summary>The spool holds a recording nobody has decided about yet.</summary>
    ARecordingWaits,
}

/// <summary>A move that was refused before it wrote anything. Which refusal it was is what is said.</summary>
public sealed class CorpusMoveRefused(CorpusMoveRefusal refusal)
    : Exception($"The corpus was not moved: {refusal}.")
{
    public CorpusMoveRefusal Refusal { get; } = refusal;
}

/// <summary>What a move copied, so the screen that asked can say it was not nothing.</summary>
public sealed record CorpusMoved(int Files, long Bytes);

/// <summary>
/// Moves the whole corpus to an empty folder: the database, then every file beside it, and only
/// then a second reading of whether what arrived is what the database says is there.
/// </summary>
/// <remarks>
/// <para>
/// Two halves and a gap between them that is somebody's choice. <see cref="Copy"/> writes the new
/// folder and proves it, and leaves the old one exactly as it was; <see cref="RemoveTheOldCopyAsync"/>
/// is what somebody who ticked <em>Borrar la copia anterior</em> runs once the application is
/// using the new folder, and it proves the new one again before it removes anything. Nothing in
/// either removes the old copy on the strength of a copy that was merely finished: a file that was
/// written and never found whole is the loss ISC-223 is about.
/// </para>
/// <para>
/// The database goes through SQLite's online backup and never as a file copy. In write-ahead mode
/// the committed pages are partly in <c>corpus.db-wal</c>, so copying <c>corpus.db</c> alone is
/// copying a database that is missing its last writes, and copying all three while a writer is
/// open is copying them at three different instants.
/// </para>
/// <para>
/// A folder a person would not pick for anything else is refused here rather than at the screen,
/// because this is what writes: the same checks as <see cref="CorpusLocation.Inspect"/> make for a
/// folder somebody names, plus the ones only a move has — the folders overlapping, work in flight,
/// a recording the spool still owes a decision about.
/// </para>
/// </remarks>
public static class CorpusMove
{
    /// <summary>
    /// Copies the corpus in <paramref name="from"/> into the empty folder <paramref name="to"/>,
    /// finds every file the corpus records there whole, and only then runs
    /// <paramref name="whenWhole"/> — the one place a caller records that the new folder is where
    /// the meetings are.
    /// </summary>
    /// <param name="whenWhole">
    /// What is done once the copy has been found whole. It is inside the rollback on purpose: a
    /// folder that could not be recorded is left empty, and a retry is not refused as one that
    /// already holds something.
    /// </param>
    /// <param name="applicationData">
    /// The application data folders a corpus is refused for being under, which is this user's when
    /// it is not given. Named only by tests, for the reason <see cref="CorpusLocation"/> gives for
    /// the same overload: a build agent's temporary folders are under its profile's application
    /// data, so the answer for a real profile cannot be the one a test gets.
    /// </param>
    /// <remarks>
    /// The caller has to have stopped whatever writes <paramref name="from"/>: the runner's pump
    /// holds the corpus's lease and a job that finishes after the backup lands only in the old
    /// folder. The refusal for work in flight below is only the proof that nothing was left
    /// queued.
    /// </remarks>
    /// <exception cref="CorpusMoveRefused">Nothing was written.</exception>
    /// <exception cref="IOException">
    /// Something did not arrive whole, or could not be written. Everything in
    /// <paramref name="to"/> has been taken away again, which is safe because it was found empty;
    /// <paramref name="from"/> was never touched.
    /// </exception>
    public static CorpusMoved Copy(
        DirectoryInfo from,
        DirectoryInfo to,
        CancellationToken stopping,
        Action? whenWhole = null,
        IReadOnlyList<string>? applicationData = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        EnsureItMayBeCopied(from, to, applicationData ?? CorpusLocation.ApplicationDataOfThisUser());

        try
        {
            var moved = Write(from, to, stopping);

            stopping.ThrowIfCancellationRequested();
            FindEveryFileWhole(to);
            whenWhole?.Invoke();

            return moved;
        }
        catch
        {
            TakeAwayWhatWasWritten(to);
            throw;
        }
    }

    /// <summary>
    /// Removes the corpus in <paramref name="from"/>, once the one in <paramref name="to"/> has been
    /// found whole a second time. A failure leaves both folders where they were.
    /// </summary>
    /// <remarks>
    /// The old folder is moved aside under <see cref="RecordingFiles.BeingRemovedPrefix"/> beside
    /// it and that is deleted, which is how a recording's folder goes: one rename that either
    /// happened or did not, and then a delete nothing is waiting on. Called after the pump over
    /// <paramref name="from"/> has stopped, because the pump holds <c>runner.mark</c> open and a
    /// folder with a file held in it will not move.
    /// </remarks>
    public static Task RemoveTheOldCopyAsync(DirectoryInfo from, DirectoryInfo to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return Task.Run(() =>
        {
            // Asked again, and of the folder the application is using: the proof that the new copy
            // was whole was true when the move finished, and this is what the removal rests on.
            FindEveryFileWhole(to);

            CorpusDatabase.ClearPoolsFor(from);

            var above = from.Parent
                ?? throw new IOException(
                    $"'{from.FullName}' was not removed: there is nothing above it to move it aside to.");

            var aside = Path.Combine(above.FullName, RecordingFiles.BeingRemovedPrefix + from.Name);

            if (Directory.Exists(aside))
            {
                throw new IOException(
                    $"'{from.FullName}' was not removed: '{aside}' is already there from an earlier removal.");
            }

            Directory.Move(from.FullName, aside);
            Directory.Delete(aside, recursive: true);
        });
    }

    private static void EnsureItMayBeCopied(
        DirectoryInfo from, DirectoryInfo to, IReadOnlyList<string> applicationData)
    {
        // The package first: a folder an uninstall deletes is refused whether or not it is there.
        if (CorpusLocation.GoesWhenThePackageDoes(to.FullName, applicationData))
        {
            throw new CorpusMoveRefused(CorpusMoveRefusal.GoesWhenThePackageDoes);
        }

        if (!Directory.Exists(to.FullName) || Directory.EnumerateFileSystemEntries(to.FullName).Any())
        {
            throw new CorpusMoveRefused(CorpusMoveRefusal.NotEmpty);
        }

        // Only the new folder inside the old one can happen: a folder holding the old corpus is not
        // empty, which is refused above.
        if (CorpusLocation.IsAtOrUnder(to.FullName, from.FullName))
        {
            throw new CorpusMoveRefused(CorpusMoveRefusal.InsideTheOther);
        }

        if (WorkIsInFlightIn(from))
        {
            throw new CorpusMoveRefused(CorpusMoveRefusal.WorkPending);
        }

        if (ARecordingWaitsIn(from))
        {
            throw new CorpusMoveRefused(CorpusMoveRefusal.ARecordingWaits);
        }
    }

    private static bool WorkIsInFlightIn(DirectoryInfo corpus)
    {
        using var context = CorpusDatabase.OpenReadOnly(corpus);

        try
        {
            return context.ProcessingJobs.Any(
                job => job.State == JobState.Pending || job.State == JobState.Running);
        }
        finally
        {
            CorpusDatabase.ClearPoolsFor(corpus);
        }
    }

    /// <summary>
    /// Whether anything is in the spool but the folders of recordings being thrown away. A recording
    /// there is the only copy of audio nobody has decided about, and it is not in any row yet.
    /// </summary>
    private static bool ARecordingWaitsIn(DirectoryInfo corpus)
    {
        var spool = CorpusFiles.SpoolRootIn(corpus);

        return Directory.Exists(spool.FullName)
            && Directory.EnumerateFileSystemEntries(spool.FullName).Any(
                entry => !Path.GetFileName(entry).StartsWith(
                    RecordingFiles.BeingRemovedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static CorpusMoved Write(DirectoryInfo from, DirectoryInfo to, CancellationToken stopping)
    {
        var database = CorpusDatabase.PathIn(to);
        BackUpTheDatabase(from, database);

        var files = 1;
        var bytes = new FileInfo(database).Length;

        foreach (var path in Directory.EnumerateFiles(from.FullName, "*", SearchOption.AllDirectories))
        {
            stopping.ThrowIfCancellationRequested();

            var relative = CorpusFiles.RelativePathOf(from, new FileInfo(path));

            if (LeftBehind(relative))
            {
                continue;
            }

            var destination = CorpusFiles.Locate(to, relative);
            Directory.CreateDirectory(destination.DirectoryName!);

            File.Copy(path, destination.FullName, overwrite: false);
            files++;
            bytes += destination.Length;
        }

        return new CorpusMoved(files, bytes);
    }

    /// <summary>
    /// What is not carried: the database, which the backup made, its write-ahead log and shared
    /// memory, which were folded into it, and the names a write that never finished or a removal in
    /// progress leave — none of which a corpus ever read back.
    /// </summary>
    private static bool LeftBehind(string relative) =>
        string.Equals(relative, CorpusDatabase.DatabaseName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(relative, CorpusDatabase.DatabaseName + "-wal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(relative, CorpusDatabase.DatabaseName + "-shm", StringComparison.OrdinalIgnoreCase)
        || CorpusFiles.IsUnfinished(relative)
        || CorpusFiles.IsBeingRemoved(relative);

    /// <summary>
    /// The backup is SQLite's own, over a connection that opens the old database read only and one
    /// that makes the new. Pooling is off for both: a pooled connection is what holds a file open
    /// after this returns, and the folder this wrote into may need to be emptied the next moment.
    /// </summary>
    private static void BackUpTheDatabase(DirectoryInfo from, string destination)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = CorpusDatabase.PathIn(from),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());

        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        source.Open();
        copy.Open();
        source.BackupDatabase(copy);
    }

    /// <summary>
    /// Opens the corpus in <paramref name="corpus"/> read only and finds every file its
    /// <c>artifacts</c> rows name there, with the bytes the row recorded.
    /// </summary>
    /// <exception cref="IOException">A file is missing, or is not the one the row hashed.</exception>
    private static void FindEveryFileWhole(DirectoryInfo corpus)
    {
        try
        {
            using var context = CorpusDatabase.OpenReadOnly(corpus);

            var recorded = context.Artifacts
                .Select(artifact => new { artifact.RelativePath, artifact.ByteSize, artifact.Sha256 })
                .ToList();

            foreach (var row in recorded)
            {
                var file = CorpusFiles.Locate(corpus, row.RelativePath);

                if (!file.Exists)
                {
                    throw new IOException($"'{row.RelativePath}' is not in '{corpus.FullName}'.");
                }

                if (file.Length != row.ByteSize || CorpusFiles.Sha256Of(file) != row.Sha256)
                {
                    throw new IOException(
                        $"'{row.RelativePath}' in '{corpus.FullName}' is not the file the corpus recorded.");
                }
            }
        }
        finally
        {
            // This opened the folder read only through the pool, and the folder is about to be
            // emptied or to be the one the application opens.
            CorpusDatabase.ClearPoolsFor(corpus);
        }
    }

    /// <summary>
    /// Empties the folder the move was given. Everything in it is the move's: it was found empty
    /// and the screen that asked is dead while the copy runs. Whatever will not go is left rather
    /// than hiding the failure that made this run.
    /// </summary>
    private static void TakeAwayWhatWasWritten(DirectoryInfo to)
    {
        CorpusDatabase.ClearPoolsFor(to);

        foreach (var entry in Directory.EnumerateFileSystemEntries(to.FullName))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (Exception stuck) when (stuck is IOException or UnauthorizedAccessException)
            {
                // The move's own failure is the one to say.
            }
        }
    }
}
