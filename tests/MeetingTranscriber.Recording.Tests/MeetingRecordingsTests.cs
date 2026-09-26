using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
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
            followedAProgram ? CaptureMode.OneProgram : CaptureMode.WholeMachine,
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

        // Which of the two channel 0 was, and the program's name only when there was one.
        if (followedAProgram)
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.OneProgram);
            run.OthersProcess.ShouldBe("Teams (4120)");
        }
        else
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.WholeMachine);
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
    /// own credit, so it waits for somebody to have asked for it.
    /// </summary>
    /// <remarks>
    /// The preference is set to <see cref="AfterARecording.DoNothing"/> outright rather than left
    /// unset, so what this asserts is the answer and not the absence of one — a corpus nobody has
    /// answered for reads the same way, and that is <c>CorpusSettingsTests</c>' sentence and not
    /// this one's. The case beside it is the meeting somebody did ask about, below.
    /// </remarks>
    [Fact]
    public void Stopping_a_recording_queues_no_work_on_the_meeting()
    {
        using var context = corpus.OpenMigrated();
        new CorpusSettings(context).WhenARecordingEnds(AfterARecording.DoNothing, now);

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var finished = MeetingRecordings.Finish(context, prepared.MeetingId, now);

        finished.Queued.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-157.1. A recording somebody asked to have transcribed comes out of the stop with exactly
    /// that queued, and the stop runs none of it: what sends it is the runner, which
    /// <c>AfterAStopTests</c> holds.
    /// </summary>
    /// <remarks>
    /// Read back through a second connection, so what is asserted is the database and not a row
    /// still sitting in the context's tracker. The instant on the job is the one the finish was
    /// given and not the machine's clock: a job created because a recording ended is created at the
    /// moment it ended, which is why <c>MeetingWork.Take</c> takes one.
    /// </remarks>
    [Fact]
    public void Stopping_a_recording_somebody_asked_to_transcribe_queues_exactly_that()
    {
        using var context = corpus.OpenMigrated();
        new CorpusSettings(context).WhenARecordingEnds(AfterARecording.Transcribe, now);

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var stopped = now + Duration.FromSeconds(2);
        var finished = MeetingRecordings.Finish(context, prepared.MeetingId, stopped);

        finished.Queued.ShouldBe([JobKind.Transcribe]);

        using var reopened = corpus.Open();
        var queued = reopened.ProcessingJobs.Single();


        queued.MeetingId.ShouldBe(prepared.MeetingId);
        queued.Kind.ShouldBe(JobKind.Transcribe);
        queued.State.ShouldBe(JobState.Pending);
        queued.StartedAt.ShouldBeNull();
        queued.CreatedAt.ShouldBe(stopped);
    }

    /// <summary>
    /// Asking for a summary queues the transcription and not the summary.
    /// </summary>
    /// <remarks>
    /// The meeting has nothing for a summary to be made from, so an <c>Extract</c> row here would
    /// describe a stage this meeting cannot be at. What the rest of that answer means is read by
    /// nothing yet: the runner that finishes the transcription queues no summary.
    /// </remarks>
    [Fact]
    public void Stopping_a_recording_somebody_asked_to_summarise_queues_only_the_transcription()
    {
        using var context = corpus.OpenMigrated();
        new CorpusSettings(context).WhenARecordingEnds(AfterARecording.TranscribeAndSummarise, now);

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        MeetingRecordings.Finish(context, prepared.MeetingId, now);

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Select(job => job.Kind).ShouldBe([JobKind.Transcribe]);
    }

    /// <summary>
    /// A finish run again over a meeting that already carries the queued work queues nothing and
    /// still finishes.
    /// </summary>
    /// <remarks>
    /// This is the recovery path, and it is the one that would have been paid for at the worst
    /// possible moment. <c>MeetingWork.Take</c> throws <c>MeetingStageException</c> when the
    /// standing will not take the stage, and by the time the queueing runs the audio row and the
    /// meeting's length are committed — so a finish that called it blind would die on its last
    /// line over a meeting whose recording is already on disk, and every attempt after it would die
    /// the same way.
    /// </remarks>
    [Fact]
    public void A_finish_run_again_over_a_meeting_already_queued_queues_nothing_and_still_finishes()
    {
        using var context = corpus.OpenMigrated();
        new CorpusSettings(context).WhenARecordingEnds(AfterARecording.Transcribe, now);

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        MeetingRecordings.Finish(context, prepared.MeetingId, now);
        var again = MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(5));

        again.MeetingId.ShouldBe(prepared.MeetingId);

        // And it says so: what comes back is the work this finish wrote, not what the preference
        // decided should be there. Both front ends print this line to a person, and one that
        // announced a charge it did not make would be the application's only sentence about their
        // own money saying the wrong thing.
        again.Queued.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Count().ShouldBe(1);
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

    /// <summary>
    /// ISC-184. The recording said channel 0 stopped following Teams and went to the whole machine,
    /// and the corpus goes on saying so with the folder that said it first deleted.
    /// </summary>
    [Fact]
    public void What_channel_0_stopped_following_and_when_is_in_the_corpus_with_the_folder_gone()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 3);

        var card = new SpoolCard(
            prepared.MeetingId,
            Guid.NewGuid(),
            now,
            CapturedAudio.Profile,
            CaptureMode.OneProgram,
            [
                new SpooledSource(AudioChannel.Loopback, "teams (pid 8124)", null),
                new SpooledSource(AudioChannel.Microphone, "Headset", "{0.0.1.00000000}.{mic}"),
            ]);

        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var moved = now + Duration.FromSeconds(1);
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            moved,
            AudioChannel.Loopback,
            "everything this machine plays",
            "teams (pid 8124)"));

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(3));

        // The one thing that said so until now, gone. The claim over the folder is a held handle
        // and Windows will not unlink a folder under one, so the press lets go of it first.
        prepared.Dispose();
        prepared.Spool.Delete(recursive: true);

        using var reopened = corpus.Open();
        var change = reopened.CaptureSourceChanges.Single();

        change.MeetingId.ShouldBe(prepared.MeetingId);
        change.At.ShouldBe(moved);
        change.Channel.ShouldBe(AudioChannel.Loopback);
        change.WasHearing.ShouldBe("teams (pid 8124)");
        change.Heard.ShouldBe("everything this machine plays");
        change.DeviceId.ShouldBeNull();

        // And the run still says what the recording opened on, which is the other half of the
        // decision: one column never comes to mean two things depending on when it is read.
        var run = reopened.CaptureRuns.Single();
        run.OthersCaptureMode.ShouldBe(CaptureMode.OneProgram);
        run.OthersProcess.ShouldBe("teams (pid 8124)");
    }

    /// <summary>
    /// Most recordings change nothing, and the corpus says nothing about them — a table of moves
    /// that acquired a row per meeting would say a channel moved when none did.
    /// </summary>
    [Fact]
    public void A_recording_that_changed_nothing_leaves_nothing_saying_it_did()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        SpoolChanges.In(prepared.Spool).Exists.ShouldBeFalse();

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        using var reopened = corpus.Open();
        reopened.CaptureSourceChanges.ShouldBeEmpty();
    }

    /// <summary>
    /// A finish run twice over one folder — the path <c>Filed</c> exists for — writes one row per
    /// move rather than failing on the key of the row it wrote the first time.
    /// </summary>
    [Fact]
    public void Finishing_the_same_recording_twice_leaves_one_row_per_move()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var moved = now + Duration.FromSeconds(1);
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            moved, AudioChannel.Loopback, "everything this machine plays", "teams (pid 8124)"));

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));
        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        using var reopened = corpus.Open();
        var change = reopened.CaptureSourceChanges.Single();

        change.At.ShouldBe(moved);
        change.Channel.ShouldBe(AudioChannel.Loopback);
    }

    /// <summary>
    /// A folder that says one channel moved twice at one instant is answered with the later line,
    /// which is what the channel was on afterwards — and with one row, because two on one key would
    /// fail the save that files the meeting.
    /// </summary>
    /// <remarks>
    /// Nothing this application writes produces that file: <c>CaptureSession.Move</c> is the only
    /// writer of it. The lines are appended by hand here for that reason — what is being pinned is
    /// which of the two the corpus keeps, not that the situation arises.
    /// </remarks>
    [Fact]
    public void One_channel_that_moved_twice_at_one_instant_is_what_it_ended_on()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var moved = now + Duration.FromSeconds(1);
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            moved, AudioChannel.Microphone, "Laptop microphone", "Headset", "{0.0.1.0}.{laptop}"));
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            moved, AudioChannel.Microphone, "Desk microphone", "Laptop microphone", "{0.0.1.0}.{desk}"));

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        using var reopened = corpus.Open();
        var change = reopened.CaptureSourceChanges.Single();

        change.Heard.ShouldBe("Desk microphone");
        change.DeviceId.ShouldBe("{0.0.1.0}.{desk}");
    }

    /// <summary>
    /// ISC-120.1. Two headsets sharing one name are still told apart, by the id that reopens the
    /// device the recording ended on and not only by the name Windows gives both of them.
    /// </summary>
    /// <remarks>
    /// Nothing this application writes produces the change line by itself: as
    /// <see cref="One_channel_that_moved_twice_at_one_instant_is_what_it_ended_on"/> says, only
    /// <c>CaptureSession.Move</c> writes it, and it is appended here in the shape that call would
    /// leave.
    /// </remarks>
    [Fact]
    public void Two_microphones_sharing_a_name_are_told_apart_in_the_folder_and_in_the_corpus()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var moved = now + Duration.FromSeconds(1);
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            moved, AudioChannel.Microphone, "Headset", "Headset", "{0.0.1.00000000}.{other-headset}"));

        var onDisk = SpoolManifest.Find(prepared.Spool)!.On(AudioChannel.Microphone);
        onDisk.Heard.ShouldBe("Headset");
        onDisk.DeviceId.ShouldBe("{0.0.1.00000000}.{mic}");

        var changed = SpoolChanges.Find(prepared.Spool).ShouldHaveSingleItem();
        changed.At.ShouldBe(moved);
        changed.Heard.ShouldBe("Headset");
        changed.DeviceId.ShouldBe("{0.0.1.00000000}.{other-headset}");

        onDisk.DeviceId.ShouldNotBe(changed.DeviceId);

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2));

        using var reopened = corpus.Open();
        reopened.CaptureRuns.Single(row => row.MeetingId == prepared.MeetingId).MeDeviceId
            .ShouldBe("{0.0.1.00000000}.{mic}");

        var change = reopened.CaptureSourceChanges.Single();
        change.Heard.ShouldBe("Headset");
        change.WasHearing.ShouldBe("Headset");
        change.DeviceId.ShouldBe("{0.0.1.00000000}.{other-headset}");
    }

    /// <summary>
    /// Two channels that moved are two facts. Channel 1 following Windows to whatever replaced an
    /// unplugged headset carries the endpoint it reopens by; channel 0 has none to carry.
    /// </summary>
    [Fact]
    public void Both_channels_moving_are_two_facts_with_their_own_instants()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 3);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var loopbackMoved = now + Duration.FromSeconds(1);
        var microphoneMoved = now + Duration.FromSeconds(2);

        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            loopbackMoved,
            AudioChannel.Loopback,
            "everything this machine plays",
            "teams (pid 8124)"));
        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            microphoneMoved,
            AudioChannel.Microphone,
            "Laptop microphone",
            "Headset",
            "{0.0.1.00000000}.{laptop}"));

        MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(3));

        using var reopened = corpus.Open();
        var changes = reopened.CaptureSourceChanges.OrderBy(row => row.At).ToList();

        changes.Count.ShouldBe(2);

        changes[0].At.ShouldBe(loopbackMoved);
        changes[0].Channel.ShouldBe(AudioChannel.Loopback);
        changes[0].DeviceId.ShouldBeNull();

        changes[1].At.ShouldBe(microphoneMoved);
        changes[1].Channel.ShouldBe(AudioChannel.Microphone);
        changes[1].Heard.ShouldBe("Laptop microphone");
        changes[1].WasHearing.ShouldBe("Headset");
        changes[1].DeviceId.ShouldBe("{0.0.1.00000000}.{laptop}");
    }

    /// <summary>
    /// A folder whose changes will not read stops the finish where nothing has been paid for yet:
    /// no audio copied, no length written, no row.
    /// </summary>
    /// <remarks>
    /// A complete line that does not read is the one shape <see cref="SpoolChanges.Find"/> refuses
    /// a folder over — a torn last line is dropped there instead — so what this arranges is a
    /// malformed line with a break after it. The refusal is <c>SpoolChanges</c>' own and reaches
    /// here unwrapped, which is what keeps the sentence somebody reads naming the file.
    /// </remarks>
    [Fact]
    public void A_changes_file_that_cannot_be_read_stops_the_finish_before_anything_is_written()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        SpoolChanges.Append(prepared.Spool, new SourceChanged(
            now + Duration.FromSeconds(1),
            AudioChannel.Loopback,
            "everything this machine plays",
            "teams (pid 8124)"));

        File.AppendAllText(
            SpoolChanges.In(prepared.Spool).FullName, "{\"at\": " + Environment.NewLine);

        var refused = Should.Throw<AudioCaptureException>(
            () => MeetingRecordings.Finish(
                context, prepared.MeetingId, now + Duration.FromSeconds(2)));

        refused.Message.ShouldContain(SpoolChanges.FileName);

        using var reopened = corpus.Open();
        reopened.Artifacts.Any(row => row.Kind == ArtifactKind.Audio).ShouldBeFalse();
        reopened.Meetings.Single().Duration.ShouldBeNull();
        reopened.CaptureSourceChanges.ShouldBeEmpty();

        CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(prepared.MeetingId, MeetingAudio.FileName))
            .Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The state a rename that outran its commit leaves — the destination filed, no row naming it —
    /// is exactly what the next attempt adopts, rather than the finish nothing can ever complete
    /// it left before.
    /// </summary>
    [Fact]
    public void A_finish_cut_off_between_the_rename_and_the_commit_completes_on_the_next_attempt()
    {
        Guid recorded;
        string path;
        string hash;
        using (var context = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(context, "es", now);
            recorded = prepared.MeetingId;
            Fabricated.Spools(prepared.Spool, seconds: 2);

            var card = Fabricated.CardFor(prepared.MeetingId, now);
            SpoolManifest.Write(prepared.Spool, card);
            MeetingRecordings.Began(context, card);

            // What a rename that outran its commit leaves: the destination is there, filed with the
            // bytes the spool holds, and nothing in the corpus names it. Built by staging and
            // committing the file directly and then taking the row back, rather than by finishing
            // and interrupting it, because an interrupted finish never reaches the step that would
            // remove this meeting's own spool.
            path = CorpusFiles.PathFor(prepared.MeetingId, MeetingAudio.FileName);
            hash = Fabricated.FileAudioDirectly(context, prepared.MeetingId, prepared.Spool, now);

            context.Artifacts.RemoveRange(context.Artifacts.Where(row => row.Kind == ArtifactKind.Audio));
            context.SaveChanges();
        }

        using var reopened = corpus.Open();
        var again = MeetingRecordings.Finish(reopened, recorded, now + Duration.FromSeconds(5));

        again.Audio.Sha256.ShouldBe(hash);
        again.Length.Milliseconds.ShouldBeInRange(1_950, 2_050);

        using var checked_ = corpus.Open();
        checked_.Meetings.Single().Duration.ShouldBe(again.Length);
        checked_.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);

        var written = CorpusFiles.Locate(corpus.Root, path);
        CorpusFiles.Sha256Of(written).ShouldBe(hash);
    }

    /// <summary>
    /// A destination this corpus has no row for is refused, loudly, when it does not hash to what
    /// this finish just made — one of the two is not this meeting, and neither the row nor the file
    /// is touched.
    /// </summary>
    [Fact]
    public void A_file_standing_where_the_audio_goes_that_is_not_this_recording_is_refused()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        var path = CorpusFiles.PathFor(prepared.MeetingId, MeetingAudio.FileName);
        var destination = CorpusFiles.Locate(corpus.Root, path);
        destination.Directory!.Create();
        File.WriteAllBytes(destination.FullName, [9, 9, 9, 9]);

        Should.Throw<RecordingException>(
            () => MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(2)));

        using var reopened = corpus.Open();
        reopened.Artifacts.Any(row => row.Kind == ArtifactKind.Audio).ShouldBeFalse();
        reopened.Meetings.Single().Duration.ShouldBeNull();
        File.ReadAllBytes(destination.FullName).ShouldBe(new byte[] { 9, 9, 9, 9 });
    }

    /// <summary>
    /// A source guard, because the rule — nothing this finish hashes is hashed under the corpus's
    /// write lock — is about a moment in the run and not a position in the file. It reads the text
    /// between the one <c>BeginTransaction(</c> and the one <c>filing.Commit()</c> and refuses any
    /// <c>Sha256Of(</c> standing in it, rather than asking whether every occurrence in the whole
    /// file precedes the transaction — which would go red the moment a second hasher, declared the
    /// way every other private helper in this file is declared, sits below <c>Finish</c> in the
    /// source rather than above it.
    /// </summary>
    [Fact]
    public void Nothing_this_finish_hashes_is_hashed_under_the_corpus_write_lock()
    {
        var code = SourceText.WithoutProse(
            RepositoryTree.At("src/MeetingTranscriber.Recording/MeetingRecordings.cs"));

        var begin = code.IndexOf("BeginTransaction(", StringComparison.Ordinal);
        var commit = code.IndexOf("filing.Commit()", StringComparison.Ordinal);

        begin.ShouldBeGreaterThanOrEqualTo(0);
        commit.ShouldBeGreaterThan(begin);

        code[begin..commit].ShouldNotContain(
            "Sha256Of(",
            customMessage:
            "MeetingRecordings.cs hashes something between the one BeginTransaction( and the one "
            + "filing.Commit() — everything this finish hashes has to be read outside the corpus's "
            + "write lock, because an hour of two-channel audio is over a gigabyte and hashing it "
            + "under the lock would refuse every other writer for as long as the disk took.");
    }

    /// <summary>
    /// The row an adopted file gets and the length land in one save, so a card that cannot be
    /// written afterwards leaves both rather than a length with no audio row under it.
    /// </summary>
    /// <remarks>
    /// Green on the position this method commits the adopted row at only because the manifest's own
    /// save produced neither — with the adopt moved past <c>filing.Commit()</c> this would be green
    /// for the wrong reason, because the manifest's incidental save would have written the row.
    /// </remarks>
    [Fact]
    public void The_length_and_the_audio_row_of_an_adopted_file_are_one_save()
    {
        using var context = corpus.OpenMigrated();
        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var card = Fabricated.CardFor(prepared.MeetingId, now);
        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        Fabricated.FileAudioDirectly(context, prepared.MeetingId, prepared.Spool, now);

        context.Artifacts.RemoveRange(context.Artifacts.Where(row => row.Kind == ArtifactKind.Audio));
        context.SaveChanges();

        var manifest = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(prepared.MeetingId, MeetingManifest.FileName));
        manifest.Directory!.Create();
        File.WriteAllText(manifest.FullName, "{}");

        using var held = manifest.Open(new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.None,
        });

        var refused = Should.Throw<Exception>(
            () => MeetingRecordings.Finish(context, prepared.MeetingId, now + Duration.FromSeconds(5)));

        (refused is IOException or UnauthorizedAccessException).ShouldBeTrue(
            $"the manifest write should refuse over a held file; it threw {refused.GetType()}");

        held.Dispose();

        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();
        meeting.Duration.ShouldNotBeNull();
        reopened.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);
    }

    /// <summary>
    /// O-20260916-13. A stop that could not write the job row it decided to queue leaves the whole
    /// transaction rolled back — no audio row, no length — because the queueing now runs inside it.
    /// The next attempt over the same blocks completes with the job queued.
    /// </summary>
    [Fact]
    public void A_stop_that_could_not_write_its_job_row_leaves_no_meeting_recorded_and_un_queued()
    {
        Guid recorded;
        DirectoryInfo spool;
        using (var context = corpus.OpenMigrated())
        {
            new CorpusSettings(context).WhenARecordingEnds(AfterARecording.Transcribe, now);

            using var prepared = MeetingRecordings.Open(context, "es", now);
            recorded = prepared.MeetingId;
            spool = prepared.Spool;
            Fabricated.Spools(prepared.Spool, seconds: 2);

            var card = Fabricated.CardFor(prepared.MeetingId, now);
            SpoolManifest.Write(prepared.Spool, card);
            MeetingRecordings.Began(context, card);

            // Trips on the one save that writes the job row this finish decided to queue, and no
            // other — the length and the audio row's saves must complete unhindered so what this
            // proves is the queueing's own save rolling the whole transaction back with it.
            context.SavingChanges += (_, _) =>
            {
                if (context.ChangeTracker.Entries<ProcessingJob>().Any(entry => entry.State == EntityState.Added))
                {
                    throw new InvalidOperationException("the job row could not be written");
                }
            };

            Should.Throw<InvalidOperationException>(
                () => MeetingRecordings.Finish(context, recorded, now + Duration.FromSeconds(2)));

            // ISC-186's first half: refused this late — after the length, the audio row and the
            // queueing's own row were all staged inside the one transaction — still leaves the
            // spool exactly as it was. The removal sits after `filing.Commit()` and never runs.
            spool.Refresh();
            spool.Exists.ShouldBeTrue();
            BlockSpool.FileFor(spool, AudioChannel.Loopback).Exists.ShouldBeTrue();
            BlockSpool.FileFor(spool, AudioChannel.Microphone).Exists.ShouldBeTrue();
            SpoolManifest.In(spool).Exists.ShouldBeTrue();
        }

        using (var reopened = corpus.Open())
        {
            reopened.Meetings.Single().Duration.ShouldBeNull();
            reopened.Artifacts.Any(row => row.Kind == ArtifactKind.Audio).ShouldBeFalse();
            reopened.ProcessingJobs.ShouldBeEmpty();
        }

        using var retry = corpus.Open();
        var again = MeetingRecordings.Finish(retry, recorded, now + Duration.FromSeconds(5));

        again.Queued.ShouldBe([JobKind.Transcribe]);

        using var final = corpus.Open();
        final.ProcessingJobs.Single().Kind.ShouldBe(JobKind.Transcribe);
    }

    /// <summary>
    /// ISC-185. Stopping a recording leaves the corpus holding the audio, verified, and nothing
    /// left in the spool that fed it.
    /// </summary>
    [Fact]
    public void A_stopped_recording_leaves_nothing_in_the_spool()
    {
        using var context = corpus.OpenMigrated();
        Guid meetingId;
        var spool = Recorded(context, out meetingId, seconds: 2);

        var finished = MeetingRecordings.Finish(context, meetingId, now + Duration.FromSeconds(2));

        finished.SpoolLeft.ShouldBeNull();
        spool.Refresh();
        spool.Exists.ShouldBeFalse();

        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();
        meeting.Duration.ShouldBe(finished.Length);

        var audio = reopened.Artifacts.Single(row => row.Kind == ArtifactKind.Audio);
        var written = CorpusFiles.Locate(corpus.Root, audio.RelativePath);
        CorpusFiles.Sha256Of(written).ShouldBe(audio.Sha256);
    }

    /// <summary>
    /// ISC-186's second half, and the one the <c>filed</c> path needs. A row already there and the
    /// destination it names gone from under it — the state <c>Filed</c>'s own remarks name as its
    /// reason for existing — completes the meeting from the row alone, and the spool is the only
    /// remaining copy, so it is left and said so.
    /// </summary>
    [Fact]
    public void A_finish_whose_corpus_copy_is_gone_leaves_the_recording_where_it_is()
    {
        using var context = corpus.OpenMigrated();
        var spool = Recorded(context, out var meetingId, seconds: 2);
        var path = CorpusFiles.PathFor(meetingId, MeetingAudio.FileName);
        Fabricated.FileAudioDirectly(context, meetingId, spool, now);

        CorpusFiles.Locate(corpus.Root, path).Delete();

        var finished = MeetingRecordings.Finish(context, meetingId, now + Duration.FromSeconds(2));

        finished.SpoolLeft.ShouldNotBeNull();
        finished.SpoolLeft.ShouldContain("not on disk");

        spool.Refresh();
        spool.Exists.ShouldBeTrue();
        BlockSpool.FileFor(spool, AudioChannel.Loopback).Exists.ShouldBeTrue();
        BlockSpool.FileFor(spool, AudioChannel.Microphone).Exists.ShouldBeTrue();

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Duration.ShouldBe(finished.Length);
    }

    /// <summary>
    /// Something reading the spool at the instant the mark is let go is not overruled: the meeting
    /// is made all the same, and the folder is left with the engine's own sentence on it rather
    /// than a removal raised as though the meeting had failed.
    /// </summary>
    [Fact]
    public void A_folder_something_is_holding_is_left_and_said_so()
    {
        using var context = corpus.OpenMigrated();
        Guid meetingId;
        var spool = Recorded(context, out meetingId, seconds: 2);

        using var reading = ReadingMark.Take(spool);

        var finished = MeetingRecordings.Finish(context, meetingId, now + Duration.FromSeconds(2));

        finished.SpoolLeft.ShouldNotBeNull();
        finished.SpoolLeft.ShouldContain("reading");

        spool.Refresh();
        spool.Exists.ShouldBeTrue();

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Duration.ShouldBe(finished.Length);
        reopened.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);
    }

    /// <summary>
    /// The mark a save holds is let go however the finish ends, including a throw before the
    /// removal is ever reached — a plain local a throw could walk past would hold it for the rest
    /// of the process, refusing every later decision about that folder.
    /// </summary>
    [Fact]
    public void The_mark_is_let_go_however_the_finish_ends()
    {
        using var context = corpus.OpenMigrated();
        Guid meetingId;
        var spool = Recorded(context, out meetingId, seconds: 2);

        File.AppendAllText(
            SpoolChanges.In(spool).FullName, "{\"at\": " + Environment.NewLine);

        Should.Throw<AudioCaptureException>(
            () => MeetingRecordings.Finish(context, meetingId, now + Duration.FromSeconds(2)));

        SavingMark.IsHeldIn(spool).ShouldBeFalse();

        using var reopened = corpus.Open();
        WaitingRecordings.In(reopened).Select(recording => recording.MeetingId).ShouldContain(meetingId);
    }

    /// <summary>
    /// Builds a recording exactly the way the rest of this file does, but with the press disposed
    /// before it returns — so its claim over the spool is gone before a caller finishes it, and a
    /// removal this finish attempts is never refused by a handle this method itself left open.
    /// </summary>
    private DirectoryInfo Recorded(CorpusDbContext context, out Guid meetingId, double seconds)
    {
        DirectoryInfo spool;
        Guid id;
        using (var prepared = MeetingRecordings.Open(context, "es", now))
        {
            id = prepared.MeetingId;
            spool = prepared.Spool;
            Fabricated.Spools(prepared.Spool, seconds);

            var card = Fabricated.CardFor(prepared.MeetingId, now);
            SpoolManifest.Write(prepared.Spool, card);
            MeetingRecordings.Began(context, card);
        }

        meetingId = id;
        return spool;
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
