using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

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
        watch.TheListHasRead(itWentThrough: true);

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
    public async Task A_list_that_could_not_read_is_told_again_with_nothing_else_changing()
    {
        // The one way back from a read that failed. The corpus is fine and one connection was
        // unlucky, so without this the sentence saying so would stay on screen until something
        // else in the corpus moved.
        using var context = corpus.OpenMigrated();
        Record(context);

        using var watch = Watching();
        var first = new Telling(watch);

        watch.TheListHasRead(itWentThrough: false);

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
        var prepared = MeetingRecordings.Open(context, "es", Recorded);
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
