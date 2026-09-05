using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Testing;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The meeting a press leaves behind when the recording never starts, and the next start taking it
/// away — together with everything the sweep has to refuse to touch.
/// </summary>
/// <remarks>
/// <para>
/// Half of these are about what is swept and half about what is not, and the second half is the
/// load-bearing one. A sweep that is slightly too eager deletes somebody's recording, and the
/// recording it deletes is the one a crash already cost them once. So every test below that ends
/// in something still being there compares the folder byte for byte rather than by name, and a
/// build that tidied a recording away would fail on the file rather than on a count.
/// </para>
/// <para>
/// Nothing here opens a device: what a press leaves behind is a row and a folder holding a
/// <see cref="CaptureMark"/> nobody is holding, and both are written by the corpus side of
/// recording, which needs no sound card. What still needs a machine is the hand probe in the card's
/// evidence.
/// </para>
/// <para>
/// <b>A press is a handle now, so every one below is in a <c>using</c>.</b> The claim
/// <c>MeetingRecordings.Open</c> takes over the folder is what stops a sweep landing on a press
/// that is still starting, so a press this file means to be swept has to have let it go first —
/// which is what the <c>using</c> inside each <c>using (var recording …)</c> block does, ahead of
/// every <c>SweepIn</c> and every <see cref="OnDisk"/>. One forgotten reads as an empty
/// <c>Swept</c> with no exception, or as an <see cref="IOException"/> naming <c>capture.mark</c>
/// where a folder is being hashed.
/// </para>
/// </remarks>
public sealed class MeetingsNobodyRecordedTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp pressedAt = UtcTimestamp.Parse("2026-09-02T09:30:00.000Z");
    private readonly UtcTimestamp openedAgainAt = UtcTimestamp.Parse("2026-09-02T11:05:00.000Z");

    /// <summary>
    /// ISC-156.1. Somebody presses record, the devices never record, and the next start finds no
    /// meeting from that moment and no folder for it.
    /// </summary>
    [Fact]
    public void A_meeting_no_sample_was_ever_captured_for_does_not_survive_the_next_start()
    {
        Guid pressed;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = prepared.MeetingId;
            folder = prepared.Spool;

            // What the corpus holds a moment after the press: the meeting, and a folder holding
            // nothing but the claim the press has over it. Nothing was recorded, and the row is
            // what the sweep is for.
            folder.Exists.ShouldBeTrue();
            folder.GetFiles().Select(file => file.Name).ShouldBe([CaptureMark.FileName]);
            recording.Meetings.Single(meeting => meeting.Id == pressed).Duration.ShouldBeNull();
        }

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBe([pressed]);
        swept.Left.ShouldBeEmpty();

        // Read back through a connection that never watched any of it happen, which is what a start
        // after the fact really has.
        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == pressed).ShouldBeFalse();
        Directory.Exists(folder.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// ISC-156.1, and the card's own scenario: the machine dies while the devices are opening, so a
    /// spool is on disk carrying its header and nothing else. No sample was ever captured, and the
    /// meeting does not survive as one.
    /// </summary>
    /// <remarks>
    /// The case that separates "a device was opened here" from "something was recorded here", and
    /// the folder looks like a recording to anything that only asks whether a spool file is there:
    /// <see cref="SpoolWriter"/> creates the file and writes its header the instant a device opens,
    /// hundreds of milliseconds before the first block and before the second device is even tried.
    /// A build that read existence rather than content leaves this meeting in the list forever,
    /// which is the defect wearing the fix's clothes, and it fails here and nowhere else.
    /// </remarks>
    [Fact]
    public void A_recording_killed_while_its_devices_opened_does_not_survive_as_a_meeting()
    {
        Guid pressed;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = prepared.MeetingId;
            folder = prepared.Spool;

            // Channel 0's device opened and wrote its header; the machine died before its first
            // packet and before the microphone was reached. This is what that leaves on disk.
            var opened = BlockSpool.FileFor(folder, AudioChannel.Loopback);
            SpoolWriter.Create(opened, AudioChannel.Loopback, Fabricated.StereoFloat).Dispose();
            opened.Refresh();
            opened.Length.ShouldBe(BlockSpool.HeaderBytes);
        }

        // Recovery does not call it a recording either, which is the one authority both ask.
        using (var started = corpus.Open())
        {
            WaitingRecordings.In(started).ShouldBeEmpty();
        }

        MeetingsNobodyRecorded.SweepIn(corpus.Root).Swept.ShouldBe([pressed]);

        using var reopened = corpus.Open();
        reopened.Meetings.Any(meeting => meeting.Id == pressed).ShouldBeFalse();
        Directory.Exists(folder.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// ISC-156.1's other half, and the one that protects a meeting: a recording holding one block
    /// is left exactly where it is, and so is its meeting.
    /// </summary>
    /// <remarks>
    /// One block and no card, which is the smallest thing that is still a recording: a folder a
    /// capture wrote a single packet into before the machine died. Nothing about it says who it
    /// was of and it has no length, so every corpus-side reason to sweep it is true — what stops
    /// the sweep is the block, and that is the whole assertion. The bytes are compared rather than
    /// the names, so a build that removed the folder and made an empty one would still fail.
    /// </remarks>
    [Fact]
    public void A_recording_holding_one_block_is_left_exactly_where_it_is()
    {
        Guid recorded;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            recorded = prepared.MeetingId;
            folder = prepared.Spool;

            Fabricated.Spools(folder, seconds: 0.05);
        }

        var before = OnDisk(folder);
        before.ShouldNotBeEmpty();

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBeEmpty();
        swept.Left.ShouldBeEmpty("a folder that holds a recording is not a folder the sweep looked at.");

        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == recorded).ShouldBeTrue();
        OnDisk(folder).ShouldBe(before);

        // And it is still what recovery calls a recording somebody has to decide about.
        WaitingRecordings.In(started).Single().MeetingId.ShouldBe(recorded);
    }

    /// <summary>
    /// A recording that is being started is not one that never started. While a capture holds the
    /// folder, the sweep leaves it and says which of the two it was.
    /// </summary>
    /// <remarks>
    /// This is the window the mark exists for and the only one that cannot be seen on disk: a press
    /// a moment old and a press that failed an hour ago are the same folder holding the same file,
    /// and the only thing that tells them apart is a handle. A build that swept on the folder alone
    /// deletes the meeting somebody is starting, and it deletes it every time two things look at
    /// one corpus. The claim is taken by hand here, after the press let its own go, so that what is
    /// asserted is the sweep's side of it rather than the press's.
    /// </remarks>
    [Fact]
    public void A_folder_a_capture_is_holding_is_left_and_so_is_its_meeting()
    {
        Guid starting;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            starting = prepared.MeetingId;
            folder = prepared.Spool;
        }

        using (CaptureMark.Take(folder))
        {
            var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

            swept.Swept.ShouldBeEmpty();
            swept.Left.ShouldHaveSingleItem().ShouldContain(folder.Name);
        }

        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == starting).ShouldBeTrue();
        Directory.Exists(folder.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// ISC-156.1 against the crash. A mark a capture that died left behind holds nothing, so the
    /// folder it names is swept like any other — where a mark that meant something by being there
    /// would keep the phantom meeting forever, which is this defect made permanent.
    /// </summary>
    /// <remarks>
    /// The mark file is really on disk and asserted to be before the sweep runs, so a build that
    /// read the mark's existence rather than its held-ness fails here and passes everything else.
    /// </remarks>
    [Fact]
    public void A_capture_mark_nothing_is_holding_does_not_keep_a_meeting_nobody_recorded()
    {
        Guid pressed;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = prepared.MeetingId;
            folder = prepared.Spool;
        }

        // The press took the folder and its process is gone. Nothing clears this file.
        File.Exists(Path.Combine(folder.FullName, CaptureMark.FileName)).ShouldBeTrue(
            "the stranded mark is what this test is about, and it has to be on disk");

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBe([pressed]);

        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == pressed).ShouldBeFalse();
        Directory.Exists(folder.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// A meeting that was recorded and saved keeps its folder even once the blocks are gone from
    /// it, because what says it was recorded is the corpus and not the folder.
    /// </summary>
    /// <remarks>
    /// The blocks are removed by hand here rather than by anything the product does, which is the
    /// point: it is the one arrangement in which a real meeting's folder looks exactly like a
    /// phantom's. A sweep reading only the disk deletes a meeting with its audio filed, its length
    /// written and its transcript ahead of it.
    /// </remarks>
    [Fact]
    public void A_meeting_that_was_recorded_and_saved_is_never_swept()
    {
        Guid saved;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            saved = prepared.MeetingId;
            folder = prepared.Spool;

            var meeting = recording.Meetings.Single(row => row.Id == saved);
            meeting.Duration = Duration.FromMilliseconds(90_000);
            recording.SaveChanges();
        }

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBeEmpty();
        swept.Left.ShouldHaveSingleItem().ShouldContain("recorded and saved");

        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == saved).ShouldBeTrue();
        Directory.Exists(folder.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// Anything the corpus holds of a meeting keeps it, one reason at a time: somebody's typing on
    /// it, a decision already taken about it, or a row in any of the tables that hang off it.
    /// </summary>
    /// <remarks>
    /// One case per reason and each on a corpus of its own, because a sweep that answered on the
    /// first of them and ignored the rest would pass a test that asserted them together. What
    /// makes them worth asserting at all is that removing the row cascades: a meeting swept with a
    /// row hanging off it takes that row with it, silently, and nothing afterwards can tell.
    /// </remarks>
    [Theory]
    [InlineData("title")]
    [InlineData("notes")]
    [InlineData("deleting")]
    [InlineData("artifact")]
    public void A_meeting_the_corpus_holds_something_of_is_never_swept(string held)
    {
        Guid pressed;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = prepared.MeetingId;
            folder = prepared.Spool;

            var meeting = recording.Meetings.Single(row => row.Id == pressed);

            switch (held)
            {
                case "title":
                    meeting.Title = "The one nobody recorded";
                    break;
                case "notes":
                    meeting.Context = "What it was going to be about";
                    break;
                case "deleting":
                    // Both, because the corpus refuses one without the other: a meeting on its way
                    // out says when somebody said so.
                    meeting.LifecycleState = LifecycleState.Deleting;
                    meeting.DeletedAt = openedAgainAt;
                    break;
                case "artifact":
                    recording.Artifacts.Add(new Artifact
                    {
                        Id = Guid.NewGuid(),
                        MeetingId = pressed,
                        Kind = ArtifactKind.Audio,
                        RelativePath = CorpusFiles.PathFor(pressed, MeetingAudio.FileName),
                        Origin = ArtifactOrigin.Source,
                        Sha256 = new string('a', 64),
                        ByteSize = 1,
                        ConfirmedAt = pressedAt,
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(held), held, "No such thing to hold.");
            }

            recording.SaveChanges();
        }

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBeEmpty();
        swept.Left.ShouldHaveSingleItem();

        using var started = corpus.Open();
        started.Meetings.Any(meeting => meeting.Id == pressed).ShouldBeTrue();
        Directory.Exists(folder.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// Every table that carries a meeting id, spelled out, so that a new one arriving is a failure
    /// here rather than rows cascaded away by a sweep that never heard of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep asks one of these directly — the artifact, which points at a file that may have
    /// been paid for and cannot be obtained again — and reaches the rest through the folder: every
    /// other table here is written once a device has opened or once audio has been filed, and both
    /// of those leave the folder holding something the sweep refuses to touch. That reasoning is
    /// load-bearing precisely because it replaced six more queries, and this is what pins it: a
    /// table added that is downstream of neither is one somebody has to come here and think about,
    /// rather than one quietly cascaded away.
    /// </para>
    /// <para>
    /// Distinct names, so that a fourth projection owning citations is not a red test with a
    /// copy-paste fix. What is being watched for is a name nobody has met, not how many places one
    /// is stored in.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_table_that_hangs_off_a_meeting_is_one_the_sweep_has_been_held_to()
    {
        using var context = corpus.OpenMigrated();

        var carrying = context.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("MeetingId") is not null)
            .Select(entity => entity.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        carrying.ShouldBe(
        [
            // Asked by name. Losing it silently is losing a file nothing can produce again.
            nameof(Artifact),

            // Written the moment two devices opened, which puts a spool in the folder.
            nameof(CaptureRun),

            // Downstream of the audio artifact or of a job, both of which are answered above.
            nameof(ProcessingJob),
            nameof(TranscriptionRun),
            nameof(ExtractionRun),
            nameof(Utterance),
            nameof(Summary),
            nameof(Decision),
            nameof(ActionItem),
            nameof(OpenQuestion),
            nameof(SpeakerAssignment),
            nameof(Citation),

            // Somebody's typing on a meeting, which the row's own fields answer for: nothing files
            // or names a meeting it has not first been able to see and write on.
            nameof(MeetingNode),
            nameof(MeetingPerson),
            nameof(TerminologyCorrection),
            nameof(AuditEvent),
        ], ignoreOrder: true);
    }

    /// <summary>
    /// A folder that turned out to hold something is left whole rather than emptied as far as the
    /// first thing that would not go.
    /// </summary>
    /// <remarks>
    /// The file is one nothing in this product writes, which is the case the delete has to survive
    /// rather than the case it has to expect: what it is standing in for is anything that appeared
    /// between the sweep looking and the sweep acting. A recursive delete unlinks in enumeration
    /// order and would take the marks with it on the way to failing; this asserts the folder is
    /// exactly as it was, marks included — and the mark in it is the press's own, left stranded
    /// when the press let it go, rather than one arranged here.
    /// </remarks>
    [Fact]
    public void A_folder_that_holds_something_else_is_left_whole()
    {
        Guid pressed;
        DirectoryInfo folder;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = prepared.MeetingId;
            folder = prepared.Spool;
        }

        File.WriteAllText(Path.Combine(folder.FullName, "notes-somebody-dropped-here.txt"), "hello");

        var before = OnDisk(folder);

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBeEmpty();
        swept.Left.ShouldHaveSingleItem().ShouldContain(folder.Name);

        Directory.Exists(folder.FullName).ShouldBeTrue();
        OnDisk(folder).ShouldBe(before);
    }

    /// <summary>
    /// A folder left over by a sweep that was cut off between the row and the folder is finished by
    /// the next one, and a folder that is not named after a meeting at all is never touched.
    /// </summary>
    /// <remarks>
    /// The row goes first, so the half a machine can die in the middle of is a folder naming a
    /// meeting this corpus does not hold — and that folder holds nothing, because a folder holding
    /// anything is not one the sweep reached. The other case is the guard on the same rule: what
    /// says a folder is the product's is the meeting id it is named after, and a folder somebody
    /// made by hand is somebody's.
    /// </remarks>
    [Fact]
    public void A_folder_naming_no_meeting_is_finished_off_and_one_naming_nothing_is_left()
    {
        var spool = CorpusFiles.SpoolRootIn(corpus.Root);

        using (var recording = corpus.OpenMigrated())
        {
            // A corpus is needed for the sweep to run at all, and this is the meeting that makes
            // one: it holds a recording, so it is never a candidate.
            using var prepared = MeetingRecordings.Open(recording, "es", pressedAt);
            Fabricated.Spools(prepared.Spool, seconds: 0.05);
        }

        var orphaned = CorpusFiles.SpoolFolderFor(corpus.Root, Guid.NewGuid());
        orphaned.Create();

        var somebodys = new DirectoryInfo(Path.Combine(spool.FullName, "old recordings"));
        somebodys.Create();

        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldHaveSingleItem().ShouldBe(Guid.Parse(orphaned.Name));
        swept.Left.ShouldHaveSingleItem().ShouldContain("not named after a meeting");

        Directory.Exists(orphaned.FullName).ShouldBeFalse();
        Directory.Exists(somebodys.FullName).ShouldBeTrue();
    }

    /// <summary>
    /// A machine that has never recorded is swept without anything being made on it. A root with no
    /// corpus in it is not one to fill with an empty new corpus, and a corpus with no spool folder
    /// has nothing to sweep.
    /// </summary>
    [Fact]
    public void A_machine_that_has_never_recorded_is_swept_without_a_corpus_being_made()
    {
        var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

        swept.Swept.ShouldBeEmpty();
        swept.Left.ShouldBeEmpty();
        File.Exists(corpus.DatabasePath).ShouldBeFalse();
        corpus.Root.GetDirectories().ShouldBeEmpty();
    }

    /// <summary>
    /// Somebody presses record while the start's sweep is running, and the sweep leaves the folder
    /// it just made and the meeting it is for.
    /// </summary>
    /// <remarks>
    /// The card's own scenario, and the one the mark was being taken too late for. The press here
    /// arranges nothing by hand: the claim it is holding is the one <c>MeetingRecordings.Open</c>
    /// took with the folder, which is the whole of the fix. A build that made the folder and took
    /// no claim sweeps the row and the folder out from under a press one second old, and the person
    /// who pressed record is told there is no folder to record into.
    /// </remarks>
    [Fact]
    public void A_press_the_sweep_reaches_before_its_devices_open_keeps_its_folder_and_its_meeting()
    {
        PreparedRecording pressed;

        using (var recording = corpus.OpenMigrated())
        {
            pressed = MeetingRecordings.Open(recording, "es", pressedAt);
        }

        using (pressed)
        {
            var swept = MeetingsNobodyRecorded.SweepIn(corpus.Root);

            // The delete reaches `capture.mark` and Windows refuses it, so the folder stands and
            // the sweep says which one it did not take.
            swept.Swept.ShouldBeEmpty();
            swept.Left.ShouldHaveSingleItem().ShouldContain(pressed.Spool.Name);

            using var started = corpus.Open();
            started.Meetings.Any(meeting => meeting.Id == pressed.MeetingId).ShouldBeTrue();
            Directory.Exists(pressed.Spool.FullName).ShouldBeTrue();
        }
    }

    /// <summary>
    /// A press somebody walked away from is swept like any other: letting the claim go is what says
    /// nothing came of it.
    /// </summary>
    /// <remarks>
    /// The other half of the one above, and the half that keeps the fix from being worse than the
    /// defect. A claim that is never released turns every abandoned press into a phantom meeting no
    /// start can ever take away, which is a crash making a row permanent.
    /// </remarks>
    [Fact]
    public void A_press_that_let_its_folder_go_without_recording_is_swept_like_any_other()
    {
        PreparedRecording pressed;

        using (var recording = corpus.OpenMigrated())
        {
            pressed = MeetingRecordings.Open(recording, "es", pressedAt);
        }

        pressed.Dispose();

        MeetingsNobodyRecorded.SweepIn(corpus.Root).Swept.ShouldBe([pressed.MeetingId]);

        using var started = corpus.Open();
        started.Meetings.ShouldBeEmpty();
        Directory.Exists(pressed.Spool.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// Two starts in a row: what the first swept stays swept, and what it left is left again for
    /// the same reason. Nothing here is done by time.
    /// </summary>
    [Fact]
    public void A_second_start_sweeps_nothing_new_and_leaves_the_same_recording()
    {
        Guid pressed;
        Guid recorded;

        using (var recording = corpus.OpenMigrated())
        {
            using var abandoned = MeetingRecordings.Open(recording, "es", pressedAt);
            pressed = abandoned.MeetingId;

            using var kept = MeetingRecordings.Open(recording, "es", openedAgainAt);
            recorded = kept.MeetingId;
            Fabricated.Spools(kept.Spool, seconds: 0.05);
        }

        MeetingsNobodyRecorded.SweepIn(corpus.Root).Swept.ShouldBe([pressed]);
        MeetingsNobodyRecorded.SweepIn(corpus.Root).Swept.ShouldBeEmpty();

        using var started = corpus.Open();
        started.Meetings.Select(meeting => meeting.Id).ToList().ShouldBe([recorded]);
    }

    public void Dispose() => corpus.Dispose();

    /// <summary>
    /// Every file in this folder and what it holds, as something two moments apart can be compared
    /// on. A name and a length would miss a rewrite of the same size.
    /// </summary>
    private static string[] OnDisk(DirectoryInfo folder) =>
    [
        .. folder.GetFiles()
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .Select(file => $"{file.Name} {CorpusFiles.Sha256Of(file)}"),
    ];
}
