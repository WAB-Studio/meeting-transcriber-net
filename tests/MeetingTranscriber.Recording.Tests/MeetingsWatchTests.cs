using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-170.1, over the thing that does the telling rather than over the window it tells. A stage
/// that moved behind the application's back is the case: every write here goes through a context
/// the watch never saw, which is what a command line filing a paid response, a second window, or
/// anything running a job is from where the watch stands.
/// </summary>
/// <remarks>
/// <para>
/// Real time and a real timer, deliberately. What is claimed is that nobody presses anything and
/// the application is not started again, which is a claim about something happening on its own — a
/// clock somebody advances by hand would leave exactly that untested. The gap between two looks is
/// the constructor's rather than <see cref="MeetingsWatch.HowOften"/>, so the suite is not waiting
/// two seconds a case.
/// </para>
/// <para>
/// Every case that asserts silence carries a telling of its own first, and the budget for the
/// silence starts after the write it is about. Without both, a case saying "nothing arrived" passes
/// on a timer that never fired, on a corpus that would not open, and on a look that is dead for any
/// reason at all — which is the shape of a negative test that proves nothing.
/// </para>
/// <para>
/// Nothing here opens a device. A recording nobody stopped is fabricated the way one lands on disk,
/// the way <c>WaitingRecordingsTests</c> does it, so the half of the list that comes off the spool
/// folder is probed on a build agent with no sound card.
/// </para>
/// </remarks>
public sealed class MeetingsWatchTests : IDisposable
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    /// <summary>How often the watch under test looks, which is as often as it can be asked to.</summary>
    private static readonly TimeSpan Often = TimeSpan.FromMilliseconds(25);

    /// <summary>How long a telling is waited for before the case is called failed.</summary>
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(10);

    /// <summary>How long nothing has to arrive for, for nothing to have been told.</summary>
    private static readonly TimeSpan ALittle = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Slow enough that a case can get a write and the list's answer to it in before the first look.
    /// </summary>
    private static readonly TimeSpan Slowly = TimeSpan.FromMilliseconds(400);

    /// <summary>Several of those, which is how long silence has to hold to be silence.</summary>
    private static readonly TimeSpan SeveralLooks = TimeSpan.FromMilliseconds(2_000);

    private readonly TemporaryCorpus corpus = new();

    public void Dispose() => corpus.Dispose();

    [Fact]
    public async Task A_stage_that_moved_behind_the_application_is_told_about()
    {
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        Stage(context, meeting).ShouldBe(MeetingStage.Recorded);

        using var watch = Watching();
        var telling = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            Add(elsewhere, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        (await telling.Arrived()).ShouldBeTrue();
        Stage(context, meeting).ShouldBe(MeetingStage.Transcribed);
    }

    [Fact]
    public async Task A_stage_somebody_asked_for_behind_the_application_is_told_about()
    {
        // The half the card is really about, and the one no artifact reaches: what changes when a
        // job appears is the standing, not the stage. It is what a second window pressing
        // Transcribe leaves, and what anything running a job will leave on every meeting it
        // finishes.
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        using var watch = Watching();
        var telling = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            new MeetingWork(elsewhere, TimeProvider.System).Take(meeting);
        }

        (await telling.Arrived()).ShouldBeTrue();
        new MeetingWork(context, TimeProvider.System).On(meeting)
            .Standing.ShouldBe(StageStanding.Underway);
    }

    [Fact]
    public async Task A_meeting_that_arrived_behind_the_application_is_told_about()
    {
        using var context = corpus.OpenMigrated();
        Record(context);

        using var watch = Watching();
        var telling = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            Record(elsewhere);
        }

        (await telling.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_name_somebody_gave_a_meeting_elsewhere_is_told_about()
    {
        // The stage is what the claim is about and it is not the only thing a row draws. What is
        // watched is every fact a card is built out of, so a name typed into this corpus from
        // anywhere else corrects the row it is on rather than waiting for a relaunch.
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        using var watch = Watching();
        var telling = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            elsewhere.Meetings.Single(stored => stored.Id == meeting).Title = "Consejo de agosto";
            elsewhere.SaveChanges();
        }

        (await telling.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_recording_that_stopped_being_written_is_told_about()
    {
        // The half of the list that is not in the corpus at all. A recording nobody stopped is a
        // folder on disk, and what its row offers comes off marks in that folder: while a capture
        // holds it there is nothing to decide about, and the moment the capture is gone there are
        // two answers to give. Nothing in any table moves between those two states, so a watch
        // over the meetings alone would leave a dead recording saying it is still running for as
        // long as the window stayed open.
        SpoolCard card;
        using (var recording = corpus.OpenMigrated())
        {
            card = Killed(recording, seconds: 1);
        }

        using var context = corpus.Open();

        // The handle a capture holds on its own spool for as long as the meeting lasts, in the mode
        // a capture holds it in, which is what says on this machine that one is in progress.
        // Letting it go is the capture ending, however it ended.
        var capturing = BlockSpool
            .FileFor(CorpusFiles.SpoolFolderFor(corpus.Root, card.MeetingId), AudioChannel.Microphone)
            .Open(FileMode.Open, FileAccess.Write, FileShare.Read);

        Waiting(context).Single().Running.ShouldBeTrue();

        using var watch = Watching();
        var telling = new Telling(watch);

        capturing.Dispose();

        (await telling.Arrived()).ShouldBeTrue();
        Waiting(context).Single().Running.ShouldBeFalse();
    }

    [Fact]
    public async Task A_recording_somebody_threw_away_elsewhere_is_told_about()
    {
        // `recovery --discard` at a prompt: the folder goes and the corpus is not written to at
        // all, so the row above the meetings is a phantom until something looks at the disk again.
        using (var recording = corpus.OpenMigrated())
        {
            Killed(recording, seconds: 1);
        }

        using var context = corpus.Open();
        var waiting = Waiting(context).Single();

        using var watch = Watching();
        var telling = new Telling(watch);

        waiting.Spooled.Discard();

        (await telling.Arrived()).ShouldBeTrue();
        Waiting(context).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_corpus_nobody_wrote_to_is_never_told_about()
    {
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        using var watch = Watching();
        var first = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            Add(elsewhere, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        // The control. Without it this case passes on a watch that never looked at anything.
        (await first.Arrived()).ShouldBeTrue();

        (await new Telling(watch).Arrived(ALittle)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_change_the_list_has_already_read_is_never_told_about()
    {
        // The window's own writes. Pressing Transcribe leaves a job in the corpus and a sentence on
        // the status line saying so; a watch that then told about the change it had just been shown
        // would clear that sentence and rebuild every card under whoever was reading them.
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        // Looking slowly, which is what makes this case say anything: the write and the list's
        // answer both land before the first look, so what the look then finds is the whole of what
        // is being claimed. At the gap the other cases use, a look landing between the two would
        // tell — and that telling is the one `OnTheCorpusChanged` answers by keeping what the last
        // press said, which is on the screen the probe drives rather than here.
        using var watch = Watching(Slowly);
        var told = new Telling(watch);

        new MeetingWork(context, TimeProvider.System).Take(meeting);
        TheListHasRead(watch);

        (await told.Arrived(SeveralLooks)).ShouldBeFalse();

        // And the watch is not deaf afterwards: the next change from outside still arrives.
        var next = new Telling(watch);
        using (var elsewhere = corpus.Open())
        {
            elsewhere.Meetings.Single(stored => stored.Id == meeting).Title = "Consejo";
            elsewhere.SaveChanges();
        }

        (await next.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_change_that_landed_while_the_list_was_drawing_is_still_told_about()
    {
        // The gap a list's own read leaves, and the whole reason the watch is handed what the list
        // read rather than taking one of its own. `MeetingsDrawer.Read` queries the corpus, draws
        // what came back, and only then says what it read — so anything filed in between is in the
        // corpus and not on the screen. A watch answering by reading again would take that change
        // for one the list is already showing, mark it spent and never mention it, and the row
        // would sit wrong for the rest of the session over the exact write this class exists to
        // notice: the command line filing a response somebody paid for.
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        using var watch = Watching();

        // The list's read, whole and through its own connection, the way the drawer takes it.
        IReadOnlyList<MeetingAndWork> drawn;
        IReadOnlyList<WaitingRecording> waiting;

        using (var read = corpus.Open())
        {
            drawn = new MeetingWork(read, TimeProvider.System).Listed();
            waiting = WaitingRecordings.In(read);
        }

        using (var elsewhere = corpus.Open())
        {
            Add(elsewhere, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        watch.TheListHasRead(drawn, waiting);

        // Made after the hand-over on purpose, so nothing here turns on a look having been slow
        // enough to miss the write: a look that saw it first is not the telling being waited for,
        // and the hand-over puts the watch back to what the list drew either way.
        var telling = new Telling(watch);

        (await telling.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_list_that_could_not_read_is_told_again_with_nothing_else_changing()
    {
        // The one way back from a read that failed. The corpus is fine and one connection was
        // unlucky, so without this the sentence saying so would stay on screen until something
        // else in the corpus moved.
        using var context = corpus.OpenMigrated();
        Record(context);

        using var watch = Watching();
        var first = new Telling(watch);

        watch.TheListCouldNotRead();

        (await first.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_watch_let_go_of_stops_looking()
    {
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        var watch = Watching();
        var first = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            Add(elsewhere, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        // The control, before anything is let go of.
        (await first.Arrived()).ShouldBeTrue();

        var after = new Telling(watch);
        watch.Dispose();

        using (var elsewhere = corpus.Open())
        {
            elsewhere.Meetings.Single(stored => stored.Id == meeting).Title = "Consejo";
            elsewhere.SaveChanges();
        }

        (await after.Arrived(ALittle)).ShouldBeFalse();
    }

    [Fact]
    public void A_watch_is_started_once_and_never_after_it_was_let_go_of()
    {
        using (var context = corpus.OpenMigrated())
        {
            Record(context);
        }

        var watch = Watching();
        Should.Throw<InvalidOperationException>(watch.Start);

        watch.Dispose();
        Should.Throw<ObjectDisposedException>(watch.Start);
    }

    [Fact]
    public async Task A_folder_with_no_corpus_in_it_is_watched_until_there_is_one()
    {
        // The state every installation starts in: the first recording is what makes the corpus, so
        // a watch that refused an empty folder would be a watch that never ran on a new machine.
        using var watch = Watching();
        var telling = new Telling(watch);

        using (var made = corpus.OpenMigrated())
        {
            Record(made);
        }

        (await telling.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_corpus_that_will_not_open_never_reaches_the_launch()
    {
        // The launch path, which is where this costs most. `MeetingsDrawer.Open` starts a watch
        // from `MainWindow`'s constructor, and `App.OnLaunched` calls that constructor before there
        // is a window on screen — so a read throwing out of `Start` is an application that never
        // opens, over a corpus somebody else is holding. Which is this card's own premise: the
        // command line filing a paid response is exactly the change the watch exists to notice, and
        // it is also the thing most likely to be holding the corpus when a launch reads it.
        using (var context = corpus.OpenMigrated())
        {
            Record(context);
        }

        using var held = HeldByAnotherProcess();

        // The control. Without it this case passes over a corpus that reads perfectly well, and
        // the failure it names is the one `ScreenFailures.Reportable` already has a sentence for.
        using (var refused = corpus.Open())
        {
            Should.Throw<SqliteException>(() => new MeetingWork(refused, TimeProvider.System).Listed());
        }

        using var watch = new MeetingsWatch(corpus.Root, TimeProvider.System, Often);
        Should.NotThrow(watch.Start);

        // And it is looking rather than merely quiet. A start that swallowed the read and gave up
        // on the timer would leave the list never re-reading for the rest of the session, which
        // passes every other case in this file.
        var telling = new Telling(watch);
        held.Dispose();

        (await telling.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_look_that_could_not_read_tells_nobody_and_asks_again()
    {
        // The list is showing a good read and one look cannot get one. That is not a change, and
        // reporting it as one is expensive rather than merely wrong: a telling is answered by
        // rebuilding every card on the thread the window draws on, which is the cost this whole
        // class exists not to pay for nothing. What it does instead is ask again.
        using (var context = corpus.OpenMigrated())
        {
            Record(context);
        }

        using var watch = Watching();

        // What is held is the corpus a look read and not what `Start` read, which is what makes
        // this a case about a look at all: the good read is watched being made rather than waited
        // out, so the state the watch is holding when the file goes away is one this case saw it
        // take. Left behind the handle is a meeting no look has seen — the change the silence below
        // is silence about, and the one the release below is found by.
        using var held = await HeldWithAChangeWaitingBehindIt(watch);
        var telling = new Telling(watch);

        (await telling.Arrived(SeveralLooks)).ShouldBeFalse();

        // The control, and the other half of the claim: unable to read rather than deaf, and a
        // change that outlives the reads that could not find it. Without this the case passes on a
        // watch that stopped looking for any reason at all. Nothing is written here and nothing
        // needs to be — the corpus has been holding this change for the whole of the silence, so
        // what is being waited for is one look getting through.
        var next = new Telling(watch);
        held.Dispose();

        (await next.Arrived()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_telling_that_did_not_get_through_is_told_again()
    {
        // A handler that threw is absorbed, because a look runs on a timer's callback and an
        // exception nobody observes there ends the process. What must not be absorbed with it is
        // the change: the watch would be holding a state the list was never shown, the next look
        // would find nothing new in the corpus, and the row would sit wrong until something else
        // in it moved — which is this claim failing quietly rather than loudly.
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        using var watch = Watching();

        var refused = true;
        watch.Changed += (_, _) =>
        {
            if (refused)
            {
                refused = false;
                throw new InvalidOperationException("The handler did not take this telling.");
            }
        };

        // After the one above, so the first telling never reaches it. Both run under the watch's
        // own lock, which is what makes the flag between them visible either way.
        var telling = new Telling(watch);

        using (var elsewhere = corpus.Open())
        {
            Add(elsewhere, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        (await telling.Arrived()).ShouldBeTrue();
        refused.ShouldBeFalse();
    }

    /// <summary>
    /// The corpus held the way another process holds it: exclusively, so every read of it comes
    /// back as the failure a screen already knows to report, and it is the corpus it was again the
    /// moment the handle is let go of.
    /// </summary>
    /// <remarks>
    /// Which of the real causes this stands in for does not matter and is deliberately not claimed
    /// — the command line past its <c>busy_timeout</c>, a volume that came back refusing, a folder
    /// that moved. What is asserted is the failure the read produces, and each case checks that for
    /// itself before it starts a watch. The pools go first because this process's own connections
    /// hold it too, and a corpus already open here is not one another process could take.
    /// <para>
    /// Nothing calls this while a look could be reading. The launch case takes it before any watch
    /// exists; the other goes through <see cref="HeldWithAChangeWaitingBehindIt"/>, which is that
    /// same rule holding for a watch that is already running.
    /// </para>
    /// </remarks>
    private FileStream HeldByAnotherProcess()
    {
        CorpusDatabase.ClearPoolsFor(corpus.Root);

        var held = Exclusively(corpus.DatabasePath);

        // The gate in front of every read this class makes, and it answers `false` over an
        // `IOException` of its own — so on a machine where the handle above also hid the file's
        // metadata, every case using this would go green over a watch that read nothing and threw
        // nothing. Asserted here rather than in each case, because it is this helper's claim.
        CorpusDatabase.HoldsACorpus(corpus.Root).ShouldBeTrue();

        return held;
    }

    /// <summary>
    /// The same handle, taken while a watch is running, with a meeting filed behind it that no look
    /// has seen. What comes back is the corpus held immediately after a look read it, holding one
    /// change more than the watch believes it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is taken from inside a telling, which is the one moment a case can be sure of taking it
    /// at all. A watch that is looking holds the database file for as long as it lives: the
    /// connection a look opens goes back to the pool rather than away, so emptying the pool frees
    /// the file only until the next look, and a case that then waits for a gap is waiting for one
    /// that closed behind it. What is used instead is the watch's own promise that a look already
    /// running is not joined by the next — a telling is raised from inside a look, so for as long
    /// as a handler stands there no look is reading and none can start. Taking the file there is
    /// not a race that usually goes the right way; it is the only way of taking it that is not one.
    /// </para>
    /// <para>
    /// Two meetings are filed and they do different jobs. The first is what makes the telling this
    /// gets inside of, and the look that raised it has already read it, so it is part of what the
    /// watch believes. The second is filed under the handler, after that read and before the file
    /// goes away, which is the only place a change can be left that no look can reach: while the
    /// corpus is held nothing can be written to it, and writing to it afterwards is what must not
    /// happen here. A read SQLite could not make read-write it retries read-only, so a connection
    /// opened as the handle is being let go of can come back read-only and go on being handed out
    /// of the pool — which fails a write for a reason no case here is about.
    /// </para>
    /// <para>
    /// Both writes are waited out before the file is taken, because the connection each went
    /// through holds it as well. The first is waited for across threads; the second is the
    /// handler's own and is let go of on the line above. Standing still in a handler holds a look,
    /// which is exactly what a case wanting the corpus to itself is asking for.
    /// </para>
    /// <para>
    /// A second telling finds the file already taken and passes, rather than taking it twice.
    /// Tellings are raised one at a time under the watch's own lock, so that is a plain check
    /// rather than one that has to hold against another thread.
    /// </para>
    /// </remarks>
    private async Task<FileStream> HeldWithAChangeWaitingBehindIt(MeetingsWatch watch)
    {
        var taken = new TaskCompletionSource<FileStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        watch.Changed += Take;

        try
        {
            using var seen = corpus.Open();
            Record(seen);
        }
        finally
        {
            // Set however the write went, so a handler waiting on it is never the reason a failed
            // case hangs instead of failing.
            told.SetResult();
        }

        return await taken.Task.WaitAsync(LongEnough, TestContext.Current.CancellationToken);

        void Take(object? sender, EventArgs telling)
        {
            told.Task.Wait();

            if (taken.Task.IsCompleted)
            {
                return;
            }

            try
            {
                using (var unseen = corpus.Open())
                {
                    Record(unseen);
                }

                taken.SetResult(HeldByAnotherProcess());
            }
            catch (Exception thrown)
            {
                // Out through the awaiter and not through the timer, which absorbs everything a
                // look throws: a corpus this could not take has to redden the case that asked for
                // it rather than leave it waiting for a telling that is never coming.
                taken.SetException(thrown);
            }
        }
    }

    /// <summary>
    /// The handle, waited for rather than demanded once. What this suite does not control is
    /// whatever Windows itself is doing to a file written a moment ago, and it lets go. A case that
    /// failed over that would be failing for something it never asserted.
    /// </summary>
    /// <remarks>
    /// Patience and not a race: this waits out a handle that is going away on its own, and every
    /// caller has already made sure no connection of this process's is holding one. Waiting out a
    /// watch that is looking is what it must never be asked to do — a look puts its connection back
    /// in the pool rather than away, so the file is held again within a gap and stays held, and
    /// every retry after the first is spent on a handle nothing is going to let go of.
    /// </remarks>
    private static FileStream Exclusively(string database)
    {
        var giveUpAt = DateTime.UtcNow + LongEnough;

        while (true)
        {
            try
            {
                return new FileInfo(database).Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < giveUpAt)
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>
    /// The list reading the corpus for itself and handing over what it read, which is the shape
    /// <c>MeetingsDrawer.Read</c> has: its own connection, opened for the read and let go of again,
    /// and both halves of the list off the one context.
    /// </summary>
    private void TheListHasRead(MeetingsWatch watch)
    {
        using var context = corpus.Open();

        watch.TheListHasRead(
            new MeetingWork(context, TimeProvider.System).Listed(), WaitingRecordings.In(context));
    }

    private MeetingsWatch Watching(TimeSpan? every = null)
    {
        var watch = new MeetingsWatch(corpus.Root, TimeProvider.System, every ?? Often);
        watch.Start();
        return watch;
    }

    private static IReadOnlyList<WaitingRecording> Waiting(CorpusDbContext context) =>
        WaitingRecordings.In(context);

    private static MeetingStage Stage(CorpusDbContext context, Guid meeting) =>
        new MeetingWork(context, TimeProvider.System).On(meeting).Stage;

    /// <summary>
    /// A meeting recorded up to the moment the machine died: the row, the folder, the card, whole
    /// blocks and a last one cut off inside itself. The same fabrication
    /// <c>WaitingRecordingsTests</c> uses, which is how a killed recording really lands on disk.
    /// </summary>
    private static SpoolCard Killed(CorpusDbContext context, double seconds)
    {
        using var prepared = MeetingRecordings.Open(context, "es", Recorded);
        var card = Fabricated.CardFor(prepared.MeetingId, Recorded);

        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);
        Fabricated.Spools(prepared.Spool, seconds);
        Fabricated.KilledMidBlock(BlockSpool.FileFor(prepared.Spool, AudioChannel.Microphone), inside: 700);

        return card;
    }

    private static Guid Record(CorpusDbContext context)
    {
        var meeting = Guid.NewGuid();
        Add(context, NewMeeting(meeting));
        Add(context, NewArtifact(meeting, ArtifactKind.Audio));
        return meeting;
    }

    private static void Add(CorpusDbContext context, object row)
    {
        context.Add(row);
        context.SaveChanges();
    }

    private static Meeting NewMeeting(Guid id) => new()
    {
        Id = id,
        StartedAt = Recorded,
        SourceProfile = SourceProfile.Multichannel,
        Language = "es",
        CreatedAt = Recorded,
        UpdatedAt = Recorded,
    };

    private static Artifact NewArtifact(Guid meeting, ArtifactKind kind) => new()
    {
        Id = Guid.NewGuid(),
        MeetingId = meeting,
        Kind = kind,
        Origin = kind.OriginOf(),
        RelativePath = $"meetings/{meeting}/{kind}",
        ByteSize = 1024,
        Sha256 = new string('a', 64),
        ConfirmedAt = Recorded,
    };

    /// <summary>
    /// One telling, waited for from the moment it is made. Made before the write it is about, so
    /// nothing is missed, and its budget is spent on the write rather than on whatever the case set
    /// up beforehand.
    /// </summary>
    private sealed class Telling
    {
        private readonly TaskCompletionSource told =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Telling(MeetingsWatch watch) => watch.Changed += (_, _) => told.TrySetResult();

        /// <summary>
        /// Whether it arrived inside the budget. False rather than a failure, so a case about being
        /// told and a case about being left alone read the same way and neither asserts by timing
        /// out.
        /// </summary>
        public async Task<bool> Arrived(TimeSpan? within = null) =>
            await Task.WhenAny(told.Task, Task.Delay(within ?? LongEnough)) == told.Task;
    }
}
