using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording;

/// <summary>A meeting that exists and has somewhere to record into, before anything is recorded.</summary>
/// <param name="MeetingId">The meeting, settled before any audio of it exists.</param>
/// <param name="Spool">The folder its blocks and the card beside them go in.</param>
public sealed record PreparedRecording(Guid MeetingId, DirectoryInfo Spool);

/// <summary>What a recording became once somebody stopped it.</summary>
/// <param name="MeetingId">The meeting.</param>
/// <param name="Audio">The row describing the meeting's audio, hashed as it was written.</param>
/// <param name="Length">How long the meeting turned out to be, pauses included.</param>
/// <param name="Queued">
/// What stopping set going, which is nothing — see <see cref="WhatStoppingStarts"/>. Reported
/// rather than assumed, so a caller reads the answer instead of the absence of one.
/// </param>
public sealed record FinishedRecording(
    Guid MeetingId,
    Artifact Audio,
    Duration Length,
    IReadOnlyList<JobKind> Queued);

/// <summary>
/// Recording a meeting into a corpus: the steps that put rows and files where the rest of the
/// application looks for them, with no device in any of them.
/// </summary>
/// <remarks>
/// <para>
/// This is the join the two halves of the product were built either side of. The audio engine
/// takes a folder and a meeting id and knows nothing about a corpus; the corpus takes rows and
/// artifacts and knows nothing about WASAPI. Neither may reference the other — SQLite would drag
/// WASAPI into rendering a transcript, and the engine would stop being provable on a machine with
/// no corpus — so the composition is a project of its own, and this is it.
/// </para>
/// <para>
/// Every step here is a call somebody could make with no microphone on the machine, which is what
/// makes the corpus side of recording testable at all: a build agent has no device, so the parts
/// that must be proved automatically are the parts that never open one. Opening the devices is
/// <see cref="MeetingRecording"/>, which is as thin as it can be for exactly that reason.
/// </para>
/// </remarks>
public static class MeetingRecordings
{
    /// <summary>
    /// Everything that has to be true before the first sample: the meeting exists, the corpus says
    /// so, and the folder its audio goes into is there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of it. The identity is minted here and never derived from a title, a
    /// file name or anything a provider says, so a meeting is a meeting from the moment somebody
    /// presses record — before it has a name, before there is a byte of it, and whether or not
    /// anything it needs later is reachable. What that buys is a recording that can always be
    /// attached to something: audio found in a folder after a crash belongs to a row that was
    /// written before the audio was.
    /// </para>
    /// <para>
    /// The row is saved before the folder is made, and that way round on purpose. A folder with no
    /// row is what the reconciler calls a spooled recording and recovery already knows how to
    /// offer; a row with no folder is a meeting with nothing in it, which is what pressing record
    /// and having the disk refuse actually is.
    /// </para>
    /// </remarks>
    /// <param name="corpus">The corpus the meeting is being recorded into.</param>
    /// <param name="language">
    /// What the meeting is expected to be spoken in. Asked for rather than assumed: nothing here
    /// can know it, and a default guessed from the application's own language would file an
    /// English meeting as Spanish for having a Spanish menu.
    /// </param>
    /// <param name="now">When record was pressed.</param>
    public static PreparedRecording Open(CorpusDbContext corpus, string language, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = now,
            SourceProfile = CapturedAudio.Profile,
            Language = language,
            LifecycleState = LifecycleState.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        corpus.Meetings.Add(meeting);
        corpus.SaveChanges();

        var spool = CorpusFiles.SpoolFolderFor(corpus.Root, meeting.Id);
        spool.Create();

        return new PreparedRecording(meeting.Id, spool);
    }

    /// <summary>
    /// Writes the row describing the run that has just opened, from what the recording wrote about
    /// itself when its devices opened.
    /// </summary>
    /// <remarks>
    /// Built from the card and from nothing else, which is what keeps the two from disagreeing:
    /// the card is the only account of what was true when the devices opened, and a row filled
    /// from what the caller believed it asked for would say the program was followed on a
    /// recording that opened on the whole machine instead. It is also why the card carries a run id
    /// at all — this row can be written again from a folder found after a crash.
    /// </remarks>
    /// <remarks>
    /// It takes no instant of its own: when the run started is the card's, read off the devices
    /// that opened, and a second answer here would be the moment this row happened to be written.
    /// </remarks>
    public static CaptureRun Began(CorpusDbContext corpus, SpoolCard card)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(card);

        var others = card.On(AudioChannel.Loopback);
        var me = card.On(AudioChannel.Microphone);

        var run = new CaptureRun
        {
            Id = card.CaptureRunId,
            MeetingId = card.MeetingId,
            StartedAt = card.StartedAt,
            // Empty, both of them, and not a branch that could fill them: channel 0 is not a
            // device either way round, and a card saying it was is refused where the card is read.
            // The two columns outlive this by a migration and nothing else.
            OthersDeviceId = null,
            OthersDeviceName = null,
            OthersProcess = card.Mode is CaptureMode.ProcessLoopback ? others.Heard : null,
            OthersCaptureMode = card.Mode,
            MeDeviceId = me.DeviceId,
            MeDeviceName = me.Heard,

            // The contract's numbers and not a device's: this says what the recording comes out
            // as, and every device's own format is already in the header of its own spool.
            SampleRate = CapturedAudio.SampleRate,
            ChannelCount = CapturedAudio.ChannelCount,
            BitsPerSample = CapturedAudio.BitsPerSample,
            Recovered = false,
        };

        corpus.CaptureRuns.Add(run);
        corpus.SaveChanges();

        return run;
    }

    /// <summary>
    /// What stopping does: the spools become the meeting's audio, the corpus is told how long the
    /// meeting was, and nothing is set going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Making the recording is not work that was started — it is the recording being finished. It
    /// costs nothing, reaches nothing off this machine, and produces the same bytes every time
    /// from the same spools, so doing it now rather than queueing it is what lets somebody play
    /// the meeting the moment they stop it. What is deliberately not done here is everything that
    /// would spend money or somebody's quota, and <see cref="WhatStoppingStarts"/> is the one
    /// place that says so.
    /// </para>
    /// <para>
    /// The audio lands in the meeting's folder through the corpus's own write, so the bytes and the
    /// row describing them arrive together and the hash is of what was actually written. The copy
    /// the engine makes beside the spools is left where it is: those blocks are still the only
    /// recording of the meeting that exists independently of this write, and deciding what happens
    /// to a spool is somebody's, never a side effect of stopping.
    /// </para>
    /// <para>
    /// The row describing that audio and the meeting's length arrive in one commit, so no reader is
    /// ever handed a meeting that was recorded and has no length. They used to arrive in two, and
    /// what made that reachable is a list that looks every two seconds rather than when somebody
    /// asks. Inside the commit the length is written first and the audio row last, because the audio
    /// row's save is the one that renames the file into place and that is the only step here nothing
    /// can undo. The recovery card is written after the commit and deliberately outside it — the
    /// comment above <see cref="MeetingManifest.Write"/> below says what that costs and what holding
    /// it inside would cost instead.
    /// </para>
    /// <para>
    /// <paramref name="told"/> is somebody watching it happen and is never anything else. It comes
    /// before every step of the save and reads nothing, so what is filed cannot depend on whether
    /// anybody is watching — which is what makes a meeting stopped on a screen and one stopped at a
    /// prompt the same meeting. What is in front of it is the two refusals that mean the save never
    /// began. Nothing catches what a report throws: a caller whose watcher runs on this thread and
    /// fails has stopped the finish, and swallowing that would be this hiding a defect in the
    /// caller's own screen while the meeting quietly does not get made.
    /// </para>
    /// <para>
    /// It says the sources are behind it, and that is true of both callers rather than only of
    /// stopping: nothing may read a spool that is still being written, so by the time this may run
    /// the devices have been let go — by <see cref="MeetingRecording.Stop"/>, or by the process
    /// that held them dying, which is what recovery finds.
    /// </para>
    /// <para>
    /// It holds the folder for as long as it runs, through <see cref="SavingMark"/>, and that is
    /// the one thing about a save anything else can see. Between the devices being let go of and
    /// the meeting's length landing there is nothing else to tell these blocks from a recording
    /// nobody stopped, and this may take minutes; the mark is what keeps a second reader from
    /// offering the three answers over them, and what keeps a second finish from starting. It is
    /// let go however this ends, and a process that never got to let go of it leaves a file nothing
    /// is holding, which every reader reads as no save at all.
    /// </para>
    /// </remarks>
    /// <param name="corpus">The corpus the meeting is being recorded into.</param>
    /// <param name="meetingId">The meeting being finished.</param>
    /// <param name="now">When stop was pressed.</param>
    /// <param name="told">Whoever is watching the save, when anybody is.</param>
    public static FinishedRecording Finish(
        CorpusDbContext corpus,
        Guid meetingId,
        UtcTimestamp now,
        IProgress<SavingWork>? told = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        // The folder is worked out from the meeting rather than taken from the caller. A meeting id
        // and a folder passed side by side are two facts that can disagree, and the disagreement is
        // silent and unrecoverable: one meeting's conversation written down, hashed and filed as
        // another's, with a card confidently naming the wrong one.
        var spool = CorpusFiles.SpoolFolderFor(corpus.Root, meetingId);

        // The first thing, before the corpus is even read. It is what says a save is running to
        // everything else looking at this corpus — a second window, a prompt, the next start — none
        // of which could otherwise tell these blocks from a recording the machine died in the
        // middle of, and any of which would then be free to answer for the folder underneath this.
        // Before the row because the row is a query, and a query against a corpus somebody else is
        // writing waits out `busy_timeout`: what happens in that wait is the recording sitting
        // there with its devices already let go of and nothing saying anybody has it.
        //
        // It also refuses a second finish over the same meeting, here, before a block is read.
        //
        // Claimed only where there is a folder to claim. A caller naming a meeting this corpus has
        // never heard of has a folder that was never made, and what they are owed then is the
        // sentence below — which says what is actually wrong — rather than one about a mark that
        // could not be written. A folder that goes between this line and the next is the one case
        // `SavingMark.Take` answers for itself.
        spool.Refresh();
        using var saving = spool.Exists ? SavingMark.Take(spool) : null;

        var meeting = corpus.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new RecordingException(
                $"There is no meeting {meetingId} in this corpus, so there is nothing for a "
                + "recording of it to be finished into.");

        // After the claim and before the work. The report is what a screen shows for the minutes
        // this takes, so it comes before any of that — but a save refused for a folder somebody
        // else has, or for a meeting this corpus does not hold, never started, and announcing it
        // first would put a step on screen that was never under way.
        told?.Report(SavingWork.WritingTheMeetingDown);

        var card = SpoolManifest.Find(spool);

        if (card is not null && card.MeetingId != meeting.Id)
        {
            throw new RecordingException(
                $"'{spool.FullName}' holds a recording of meeting {card.MeetingId}, not of "
                + $"{meeting.Id}. A recording says which meeting it is, and that is what decides.");
        }

        var made = MeetingAudio.Materialise(spool);
        var path = CorpusFiles.PathFor(meeting.Id, MeetingAudio.FileName);

        var filed = Filed(corpus, meeting.Id, path, made);

        // Staged outside the transaction, and only its commit is inside. Writing the copy, flushing
        // it to the disk, hashing what was meant and hashing what came back off a fresh handle are
        // minutes for a long meeting; EF issues `BEGIN IMMEDIATE`, so the corpus's only write lock
        // is taken at the `BeginTransaction` line rather than at the first write, and
        // `CorpusDatabase.BusyTimeoutMilliseconds` is five seconds. Staged inside, stopping an hour
        // of meeting would refuse every other writer in the application — somebody being named on
        // another meeting, a classification being filed, a job finishing — for as long as the copy
        // took. Staged outside, the lock is held for two saves.
        using var staging = filed is null
            ? StagedArtifact.Stage(
                corpus,
                meeting.Id,
                ArtifactKind.Audio,
                path,
                into =>
                {
                    using var recording = made.File.OpenRead();
                    recording.CopyTo(into);
                })
            : null;

        // The row describing the audio and the meeting's length are one commit and not two. Between
        // two commits the corpus holds a meeting `MeetingStage.Of` answers `Recorded` for with no
        // length on it, and the meetings list reads the corpus every two seconds, so that is a wrong
        // sentence about somebody's meeting rather than a state only a test could catch.
        //
        // A bare `BeginTransaction()` and not the `CurrentTransaction is null ? … : null` the other
        // writers over this corpus spell, so a caller holding one is refused here and loudly. It
        // would be the wrong thing to join: joining means `filing` is null, so the commit below is a
        // no-op, the card is written inside the caller's transaction — the trap the comment above
        // that call names — and the caller's write lock has already been held across `Materialise`
        // and the copy, which is what staging outside this line exists to avoid. Composing over a
        // finish is not something this method can do safely, so it says so at the first attempt
        // rather than by stranding a meeting later.
        //
        // What it covers is the write and not the lookup: `Filed` above read the `artifacts` table
        // before this line. Nothing else may write an `Audio` row for a meeting being finished —
        // `SavingMark.Take` holds the folder for the whole of this — so the two cannot disagree.
        //
        // A finish that threw hands back a context nobody may save again: rolling the transaction
        // back does not undo EF's tracker, so `meeting.Duration` and `run.FinishedAt` are still
        // pending on it and a second save would write a length over no audio row. Every caller
        // today either disposes the context or lets the throw straight out.
        using var filing = corpus.Database.BeginTransaction();

        meeting.Duration = made.Length;
        meeting.UpdatedAt = now;

        // The run the card names, and never the most recent one. A meeting recorded, recovered and
        // recorded again has several, and closing off whichever started last would put an end on a
        // run that is still going while leaving the one that just ended open — an ordering guess
        // standing in for an identity the recording already wrote down.
        var run = Ran(corpus, meeting.Id, card);
        if (run is not null)
        {
            run.FinishedAt = now;
        }

        corpus.SaveChanges();

        // The audio row is committed after the length and not before it — the reverse of the order
        // these two lines used to sit in, and the same order `MeetingIntake.Record` and
        // `AudioIntake.Filed` already write in. This save is the one that renames `audio.wav` into
        // place, and that is the only step in this method nothing can undo, so it goes last:
        // everything that can still be refused for free has been asked and accepted before anything
        // irreversible happens. A save that throws above this line rolls back over a folder nothing
        // was moved into, the recording is still on the waiting list because the length went back to
        // null, and the next attempt finishes cleanly.
        //
        // Written the other way round it would not be a preference but a trap:
        // `StagedArtifact.Refusals` asks about the destination file and not about the row, and an
        // `ArtifactKind.Audio` is never rewritten — so a rollback that took the row back out from
        // under a file already renamed into place would leave a meeting the application refuses to
        // finish, every attempt answered with `AlreadyThere`, until somebody deletes that file.
        // Nothing an outside reader can see tells the two orders apart, so what pins this is
        // `MeetingRecordingsTests.No_reader_is_ever_handed_a_meeting_recorded_with_no_length`
        // asserting that `audio.wav` is not yet on disk when the length is saved.
        var audio = filed ?? staging!.Commit(now);

        filing.Commit();

        // After the commit and outside it, which is the answer `MeetingIntake.Record` already
        // reached for a paid response and for the same reason — its own comment says a transaction
        // over both "would have rolled the response's row back and left the file behind as something
        // nothing may adopt". The card is a derivative the corpus writes again from the row just
        // committed, so a refused card leaves a meeting that is recorded, has its length and plays,
        // with one file `ArtifactReconciler.Check` names and a rebuild replaces. Inside, the same
        // refusal would roll the audio row back over a file already renamed into place — the trap
        // above — and the meeting could then never be finished without somebody deleting that file
        // by hand. `AudioIntake.Filed` is the writer that still holds its card inside a transaction
        // over an `ArtifactKind.Audio`, and it has the window this does not.
        //
        // Not because of the length: the card carries the meeting, when it started, the profile, the
        // language and the title, and says nothing about how long it is — which is what the sentence
        // here used to claim.
        MeetingManifest.Write(corpus, meeting.Id, now);

        var queued = WhatStoppingStarts.For(meeting);
        if (queued.Count > 0)
        {
            // Nothing here queues, because the answer has always been nothing. Whoever makes it
            // answer otherwise is changing what stopping does, and this is the line that tells them
            // the queueing has to be written — rather than the meeting quietly waiting for work
            // that was decided on and never created.
            throw new RecordingException(
                $"Stopping meeting {meeting.Id} was answered with {string.Join(", ", queued)}, and "
                + "nothing here queues anything. What decides changed without what acts on it.");
        }

        return new FinishedRecording(meeting.Id, audio, made.Length, queued);
    }

    /// <summary>
    /// This meeting's audio, when the corpus already holds exactly the bytes this finish just
    /// made — and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this answers for is a finish run a second time over a meeting whose audio the corpus
    /// already holds. The finish committed and then the machine died, or the card was refused,
    /// before <see cref="MeetingManifest.Write"/> — the meeting has its audio and its length and no
    /// recovery card, and finishing it again is what puts the card there. Or a corpus that arrived
    /// at audio-without-length by some other route, a row edited by hand or a restore, which the
    /// recovery path still has to complete. An <see cref="ArtifactKind.Audio"/> is never rewritten,
    /// so without this either second attempt would be refused by the one rule that exists to stop a
    /// paid or unrepeatable file being destroyed.
    /// </para>
    /// <para>
    /// It asks the corpus and not the disk, which is worth saying out loud: a row whose file has
    /// gone — a restore that dropped it, somebody deleting it — is answered here as filed, and the
    /// finish then reports a meeting that will not play. That is what
    /// <c>ArtifactReconciler.Check</c> reports as missing, and putting it back is a restore rather
    /// than a finish, so this is not the place that notices. Said because the paragraph reads like
    /// an account of every way in and is not one.
    /// </para>
    /// <para>
    /// It is no longer a recovery from a half-written corpus this method's own caller produces: the
    /// audio row and the length are one commit, and a rollback takes both. That is why
    /// <c>WaitingRecordingsTests.A_finish_that_was_cut_off_after_filing_the_audio_is_still_waiting_and_completes</c>
    /// builds its state by hand. What it still does not answer for is a finish cut off between
    /// <c>StagedArtifact.Move</c> renaming the file into place and the commit landing: there is no
    /// row for this to find then, and the destination standing there is refused by the same rule.
    /// That window is a rename, one save of one row and the <c>COMMIT</c>; it was the first two of
    /// those before the length joined the commit; and closing it means letting a finish adopt a file
    /// hashing to what it just made, which is a change to what the corpus may adopt with nobody
    /// watching.
    /// </para>
    /// <para>
    /// Only when the bytes are the same, and the hash is what says so. The recording is read out of
    /// the blocks by the same code every time, so the same spools give the same file; bytes that
    /// differ mean the folder is not the recording the corpus filed, and that is refused rather
    /// than reconciled — one of the two is a meeting nothing else would ever notice was wrong.
    /// </para>
    /// </remarks>
    private static Artifact? Filed(CorpusDbContext corpus, Guid meetingId, string path, Materialised made)
    {
        var filed = corpus.Artifacts.FirstOrDefault(
            row => row.MeetingId == meetingId
                && row.Kind == ArtifactKind.Audio
                && row.RelativePath == path);

        if (filed is null)
        {
            return null;
        }

        var hash = CorpusFiles.Sha256Of(made.File);
        if (!string.Equals(filed.Sha256, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecordingException(
                $"Meeting {meetingId} already has its audio filed, and the recording in "
                + $"'{made.File.Directory?.FullName}' does not hash to it. One of the two is not "
                + "this meeting, and a recording is never written over another one.");
        }

        return filed;
    }

    /// <summary>
    /// The run this recording was made by: the one its card names, or the meeting's only one when
    /// the card was never written or was torn in half.
    /// </summary>
    /// <remarks>
    /// Nothing when a meeting whose card is gone has more than one run. That is a recovery somebody
    /// has to look at rather than a guess this may make: the runs are what say which devices caught
    /// which stretch, and putting an end on the wrong one is a lie about a meeting that nothing
    /// afterwards can detect. The meeting still finishes — its audio and its length are read from
    /// the blocks, which never needed the card.
    /// </remarks>
    internal static CaptureRun? Ran(CorpusDbContext corpus, Guid meetingId, SpoolCard? card)
    {
        if (card is not null)
        {
            return corpus.CaptureRuns.FirstOrDefault(row => row.Id == card.CaptureRunId);
        }

        var runs = corpus.CaptureRuns.Where(row => row.MeetingId == meetingId).Take(2).ToList();
        return runs.Count == 1 ? runs[0] : null;
    }
}
