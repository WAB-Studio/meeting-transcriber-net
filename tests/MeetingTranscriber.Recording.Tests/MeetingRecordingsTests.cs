using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Recording a meeting into a corpus, with no device anywhere: what pressing record leaves behind
/// before there is any audio, and what pressing stop makes of the audio there turned out to be.
/// </summary>
/// <remarks>
/// The spools these finish are written the way a capture writes them — fabricated packets through
/// <see cref="SpoolWriter"/> — so everything here is true on a build agent with no sound card, and
/// what is left needing a machine with two devices on it is the ordering of the calls, which is
/// what <see cref="MeetingRecording"/> is and why it holds nothing else.
/// </remarks>
public sealed class MeetingRecordingsTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp now = UtcTimestamp.Parse("2026-08-18T09:30:00.000Z");

    /// <summary>
    /// ISC-156. The identity, the row, the folder and the claim over it are all there before a
    /// device is opened, so a recording is something the corpus already knows about by the time it
    /// holds a sample.
    /// </summary>
    [Fact]
    public void A_meeting_and_its_folder_exist_before_any_of_it_is_captured()
    {
        using var context = corpus.OpenMigrated();

        using var prepared = MeetingRecordings.Open(context, "es", now);

        prepared.MeetingId.ShouldNotBe(Guid.Empty);
        prepared.Spool.Exists.ShouldBeTrue();

        // Nothing has been *recorded* into it. This is the whole of "before the first sample": the
        // folder is there and holds one file, which is the claim this press has over it, so
        // anything that arrives next has somewhere that already belongs to a meeting to arrive in.
        // Named rather than counted, so a build that wrote a spool or a card at the press fails
        // here.
        prepared.Spool.GetFiles().Select(file => file.Name).ShouldBe([CaptureMark.FileName]);

        // Read back through a second connection, so what is asserted is the database and not the
        // object still sitting in the context's tracker.
        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();

        meeting.Id.ShouldBe(prepared.MeetingId);
        meeting.StartedAt.ShouldBe(now);
        meeting.CreatedAt.ShouldBe(now);
        meeting.SourceProfile.ShouldBe(CapturedAudio.Profile);
        meeting.Language.ShouldBe("es");
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.Duration.ShouldBeNull();
    }

    /// <summary>
    /// The press holds the folder it made, from one statement after making it until it hands the
    /// claim on or lets it go.
    /// </summary>
    /// <remarks>
    /// The unit statement of the ordering; the sweep tests in <c>MeetingsNobodyRecordedTests</c>
    /// are its consequence. A start's sweep of the meetings nobody recorded runs through the
    /// folders under <c>spool/</c> at every launch, so a press holding nothing is a press whose
    /// meeting a sweep can take out from under it. A claim taken and released inside <c>Open</c>,
    /// or taken lazily on first use, fails here.
    /// </remarks>
    [Fact]
    public void A_press_holds_the_folder_it_just_made()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);

        CaptureMark.IsHeldIn(prepared.Spool).ShouldBeTrue();
    }

    /// <summary>
    /// The claim outlives the press once it has been handed on: what holds the folder from then on
    /// is whatever is recording into it.
    /// </summary>
    /// <remarks>
    /// One owner at every instant is the whole of it. A <c>HandTheClaimOn</c> that handed the mark
    /// out without letting go of it here leaves a later tidy-up — a <c>using</c> on the press, in
    /// production or in a test — unclaiming the folder of a meeting that is being recorded, which
    /// puts the sweep straight back where the card found it.
    /// </remarks>
    [Fact]
    public void A_claim_handed_on_outlives_the_press_that_made_it()
    {
        using var context = corpus.OpenMigrated();
        var prepared = MeetingRecordings.Open(context, "es", now);

        using var claim = prepared.HandTheClaimOn();
        prepared.Dispose();

        CaptureMark.IsHeldIn(prepared.Spool).ShouldBeTrue();
    }

    /// <summary>
    /// A claim is handed on once. Asking twice is a defect in the caller and is said so, loudly.
    /// </summary>
    /// <remarks>
    /// Two owners of one handle, where the first to let go unclaims the folder underneath the
    /// second — and the second is the one still recording into it.
    /// <see cref="InvalidOperationException"/> and not <see cref="RecordingException"/>, because
    /// <c>ScreenFailures.Reportable</c> names the second and leaves the first out: a defect has to
    /// reach the dispatcher rather than be shown to somebody as a recording that would not start.
    /// </remarks>
    [Fact]
    public void A_claim_is_handed_on_once_and_never_twice()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);

        using var claim = prepared.HandTheClaimOn();

        Should.Throw<InvalidOperationException>(() => prepared.HandTheClaimOn());
    }

    /// <summary>
    /// The identity is the application's own and comes from nothing else — not a title, not a file
    /// name, not anything a provider says. Two meetings started with everything else identical are
    /// two meetings.
    /// </summary>
    [Fact]
    public void A_meeting_is_identified_without_a_title_or_anything_a_provider_says()
    {
        using var context = corpus.OpenMigrated();

        using var first = MeetingRecordings.Open(context, "es", now);
        using var second = MeetingRecordings.Open(context, "es", now);

        second.MeetingId.ShouldNotBe(first.MeetingId);
        second.Spool.FullName.ShouldNotBe(first.Spool.FullName);
        context.Meetings.ShouldAllBe(meeting => meeting.Title == null);
    }

    /// <summary>
    /// The run is written from the card the recording wrote about itself, so what the corpus says
    /// fed each channel is what actually opened rather than what was asked for.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_run_says_what_the_recording_said_fed_each_channel(bool followedAProgram)
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "en", now);

        var card = new SpoolCard(
            prepared.MeetingId,
            Guid.NewGuid(),
            now,
            CapturedAudio.Profile,
            followedAProgram ? CaptureMode.ProcessLoopback : CaptureMode.FullLoopback,
            [
                new SpooledSource(
                    AudioChannel.Loopback,
                    followedAProgram ? "Teams (4120)" : "everything this machine plays",
                    null),
                new SpooledSource(AudioChannel.Microphone, "Headset", "{0.0.1.00000000}.{mic}"),
            ]);

        var run = MeetingRecordings.Began(context, card);

        run.Id.ShouldBe(card.CaptureRunId);
        run.MeetingId.ShouldBe(prepared.MeetingId);
        run.StartedAt.ShouldBe(now);
        run.MeDeviceName.ShouldBe("Headset");
        run.SampleRate.ShouldBe(CapturedAudio.SampleRate);
        run.ChannelCount.ShouldBe(CapturedAudio.ChannelCount);

        // Channel 0 is not a device either way, so the run names none of one — what it says
        // instead is which of the two it was, and the program's name only when there was one.
        run.OthersDeviceName.ShouldBeNull();
        run.OthersDeviceId.ShouldBeNull();

        if (followedAProgram)
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.ProcessLoopback);
            run.OthersProcess.ShouldBe("Teams (4120)");
        }
        else
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.FullLoopback);
            run.OthersProcess.ShouldBeNull();
        }
    }

    /// <summary>
    /// Stopping makes the meeting's audio and says how long the meeting was, and the row describing
    /// the audio carries the hash of what was actually written.
    /// </summary>
    [Fact]
    public void Stopping_leaves_the_meeting_with_its_audio_and_its_length()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 3);

        // The card beside the blocks and the row describing the run, the way a capture leaves them
        // - so what closes the run off is the run the recording named and not the only one there.
        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        var run = MeetingRecordings.Began(context, card);

        var stoppedAt = now + Duration.FromSeconds(3);
        var finished = MeetingRecordings.Finish(context, prepared.MeetingId, stoppedAt);

        finished.Length.Milliseconds.ShouldBeInRange(2_950, 3_050);
        finished.Audio.Kind.ShouldBe(ArtifactKind.Audio);
        finished.Audio.Origin.ShouldBe(ArtifactOrigin.Source);
        finished.Audio.RelativePath.ShouldBe($"meetings/{prepared.MeetingId}/audio.wav");

        var written = CorpusFiles.Locate(corpus.Root, finished.Audio.RelativePath);
        written.Exists.ShouldBeTrue();
        finished.Audio.Sha256.ShouldBe(CorpusFiles.Sha256Of(written));
        finished.Audio.ByteSize.ShouldBe(written.Length);

        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();
        meeting.Duration.ShouldBe(finished.Length);

        // The run is closed off too, so nothing is left looking like a recording still going on.
        reopened.CaptureRuns.Single().Id.ShouldBe(run.Id);
        reopened.CaptureRuns.Single().FinishedAt.ShouldBe(stoppedAt);
    }

    /// <summary>
    /// A finish commits the row describing the meeting's audio and the meeting's length together, so
    /// no reader looking while it runs is ever handed a meeting that was recorded and has no length —
    /// and nothing irreversible has happened yet when the length goes down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two used to be two commits, which was latent until the meetings list started looking every
    /// two seconds (#205 / PR #281) — every read of that list used to be a moment the window chose.
    /// What is asserted is the shape of every state a reader could be handed rather than one state at
    /// one moment, which is why the assertion is a loop over readings and an equality rather than a
    /// one-way implication: a length over no audio is the other half of the same wrongness.
    /// </para>
    /// <para>
    /// The second assertion is about the order of the two saves inside the transaction, which no
    /// reader can see — both orders are invisible until the commit, and both leave the same corpus.
    /// What tells them apart is the disk: the audio row's save is the one that renames
    /// <c>audio.wav</c> into place, so if it ran first the file would already be there when the
    /// length went down, and a throw from that save would roll a row back out from under a file the
    /// corpus may never write over again. Asserting the file is absent at that moment is what makes
    /// the order a rule rather than the way the lines happen to sit.
    /// </para>
    /// <para>
    /// <c>SavingChanges</c> and <c>SavedChanges</c> are plain CLR events raised immediately before the
    /// batch and at the end of <c>SaveChanges</c>, after any implicit transaction has committed, so a
    /// read taken in either one is the state an outside reader is handed at that moment. Both are
    /// hooked and not only the second: committing a transaction is not a <c>SaveChanges</c> and raises
    /// nothing, so a gap opened between the commit and the next save is visible only from the near
    /// side of that next save. The last reading is taken here rather than left to the card's save, so
    /// what proves the window spans the change is the finished meeting and not a save that happens to
    /// come after the commit. Nothing is unsubscribed because the context is disposed here.
    /// </para>
    /// <para>
    /// Each reading is taken through <see cref="CorpusDatabase.OpenReadOnly"/> — a second connection,
    /// opened the way the watch opens its own — so what is read is the database rather than this
    /// context's tracker, and the arrangement is the one the product runs every two seconds. Reading
    /// while a writer is live is the point here, so <c>CorpusSchemaTests</c>'s warning about clearing
    /// the pools and closing the writer first does not reach this: that one is about a reader arriving
    /// after the writer has gone. The precedent for this arrangement is
    /// <c>MeetingsWatchTests.A_stage_that_moved_behind_the_application_is_told_about</c>, which runs a
    /// live watch — and so <see cref="CorpusDatabase.OpenReadOnly"/> on a timer — while a migrated
    /// context is open and a second one writes.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_reader_is_ever_handed_a_meeting_recorded_with_no_length()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var audioFile = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(prepared.MeetingId, MeetingAudio.FileName));

        // Attached after the setup saves, so what is collected is the finish and nothing before it.
        var handed = new List<(bool Audio, Duration? Length)>();
        var lengthWentDownOverTheFile = new List<bool>();

        context.SavingChanges += (_, _) =>
        {
            handed.Add(WhatAnOutsideReaderSees());

            // Asked of the save itself rather than counted off, so the assertion below names the
            // save that writes `meetings.duration_ms` and not the third one along.
            if (context.ChangeTracker.Entries<Meeting>().Any(entry =>
                    entry.State is EntityState.Modified
                    && entry.Property(row => row.Duration).IsModified))
            {
                audioFile.Refresh();
                lengthWentDownOverTheFile.Add(audioFile.Exists);
            }
        };

        context.SavedChanges += (_, _) => handed.Add(WhatAnOutsideReaderSees());

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        handed.Add(WhatAnOutsideReaderSees());

        // The window really spans the change, so an assertion over an empty list or over one taken
        // entirely after the finish had landed cannot pass by saying nothing. `HasValue` and not
        // `is null`: Shouldly takes these as expression trees, which have no patterns in them.
        handed.ShouldContain(seen => !seen.Audio && !seen.Length.HasValue);
        handed.ShouldContain(seen => seen.Audio && seen.Length.HasValue);

        foreach (var (audio, length) in handed)
        {
            // The half the card is about is a meeting recorded with no length, and the other half is
            // a length over no recording. Either both or neither, at every one of these moments.
            audio.ShouldBe(length is not null);
        }

        // One save writes the length, and when it does the rename that puts `audio.wav` in the
        // meeting's folder has not happened — so a throw from it rolls back over a folder nothing was
        // moved into, and the recording is still on the waiting list for a clean second attempt.
        lengthWentDownOverTheFile.ShouldHaveSingleItem().ShouldBeFalse();

        (bool Audio, Duration? Length) WhatAnOutsideReaderSees()
        {
            using var reading = CorpusDatabase.OpenReadOnly(corpus.Root);

            // One query and not two. Two would be two statements, each in an autocommit read of its
            // own on a pooled connection, so the pair would be two moments stitched together rather
            // than the single state a reader is handed — which is the whole of what this asserts.
            var seen = reading.Meetings
                .Where(row => row.Id == prepared.MeetingId)
                .Select(row => new
                {
                    Audio = reading.Artifacts.Any(artifact =>
                        artifact.MeetingId == row.Id && artifact.Kind == ArtifactKind.Audio),
                    row.Duration,
                })
                .Single();

            return (seen.Audio, seen.Duration);
        }
    }

    /// <summary>
    /// ISC-165.1. A meeting somebody recorded and never named comes out of the whole path with no
    /// name at all, so what a screen has to show is that nobody named it.
    /// </summary>
    /// <remarks>
    /// Asserted after stopping and not only after starting, because stopping is where a name would
    /// be invented if it were going to be: the audio has a file, the folder has a path and the run
    /// has two device names on it, and every one of those is a plausible thing to fill a blank
    /// title from. The one true answer is that there is no name, and the screen says so in words a
    /// person can tell from a title — <c>UiTexts.AMeetingNobodyHasNamed</c>.
    /// <para>
    /// Nothing here reaches the two doors that do carry a title, and neither is a counter-example:
    /// bringing audio in and filing a transcribed meeting both take one from whoever typed it,
    /// which is somebody naming a meeting rather than the application inventing one.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meeting_nobody_named_comes_out_of_recording_with_no_name()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);
        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Title.ShouldBeNull();
    }

    /// <summary>
    /// ISC-157. Stopping starts no work nobody asked for beforehand: transcribing spends the user's
    /// own credit, so it waits for somebody to ask for it.
    /// </summary>
    /// <remarks>
    /// What is asserted is stronger than the claim, and can be while nothing lets anybody ask
    /// beforehand: <see cref="WhatStoppingStarts"/> answers the same empty list for every meeting,
    /// and the preference that will make it answer otherwise is ISC-157.1, open. So nobody asked
    /// and nothing was queued is the whole of the sentence today. The case to write beside this one
    /// is a meeting somebody did ask about, and it arrives with the preference and not before.
    /// </remarks>
    [Fact]
    public void Stopping_a_recording_queues_no_work_on_the_meeting()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var finished = MeetingRecordings.Finish(context, prepared.MeetingId, now);

        finished.Queued.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.ShouldBeEmpty();
    }

    /// <summary>
    /// The card's own finishing condition: with the database gone, what is left on disk is still
    /// enough to say which meeting this folder is.
    /// </summary>
    [Fact]
    public void The_meeting_is_recognisable_with_the_database_deleted()
    {
        Guid recorded;
        using (var context = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(context, "es", now);
            recorded = prepared.MeetingId;
            Fabricated.Spools(prepared.Spool, seconds: 2);
            MeetingRecordings.Finish(context, prepared.MeetingId, now);
        }

        CorpusDatabaseGone();

        var card = MeetingManifest.Read(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(recorded, "manifest.json")));

        card.MeetingId.ShouldBe(recorded);
        card.StartedAt.ShouldBe(now);
        card.Profile.ShouldBe(CapturedAudio.Profile);
        card.Language.ShouldBe("es");
    }

    /// <summary>
    /// Finishing a folder whose meeting the corpus has never heard of says so, rather than writing
    /// an artifact hanging off a meeting that does not exist.
    /// </summary>
    [Fact]
    public void A_recording_of_no_meeting_this_corpus_knows_is_refused()
    {
        using var context = corpus.OpenMigrated();

        Should.Throw<RecordingException>(
            () => MeetingRecordings.Finish(context, Guid.NewGuid(), now));
    }

    /// <summary>
    /// A folder holding a recording of another meeting is refused rather than filed. The card
    /// beside the blocks says which meeting they are, and a build that trusted the folder instead
    /// would write one conversation into another meeting's audio, hash it, and put a card on it
    /// confidently naming the wrong one - with nothing afterwards able to tell.
    /// </summary>
    [Fact]
    public void A_folder_holding_another_meetings_recording_is_refused()
    {
        using var context = corpus.OpenMigrated();
        using var mine = MeetingRecordings.Open(context, "es", now);
        using var yours = MeetingRecordings.Open(context, "es", now);

        // Somebody else's recording, blocks and card, sitting where mine would be.
        Fabricated.Spools(mine.Spool, seconds: 2);
        SpoolManifest.Write(mine.Spool, Fabricated.CardFor(yours.MeetingId, now));

        var refused = Should.Throw<RecordingException>(
            () => MeetingRecordings.Finish(context, mine.MeetingId, now));

        refused.Message.ShouldContain(yours.MeetingId.ToString());

        using var reopened = corpus.Open();
        reopened.Artifacts.ShouldBeEmpty();
    }

    public void Dispose() => corpus.Dispose();

    /// <summary>Takes the database away, leaving everything the corpus wrote to disk.</summary>
    private void CorpusDatabaseGone()
    {
        CorpusDatabase.ClearPoolsFor(corpus.Root);
        foreach (var file in corpus.Root.GetFiles("corpus.db*"))
        {
            file.Delete();
        }
    }
}
