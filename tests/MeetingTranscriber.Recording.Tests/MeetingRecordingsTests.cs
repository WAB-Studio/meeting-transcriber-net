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
    /// ISC-156. The identity, the row and the folder are all there before a device is opened, so a
    /// recording is something the corpus already knows about by the time it holds a sample.
    /// </summary>
    [Fact]
    public void A_meeting_and_its_folder_exist_before_any_of_it_is_captured()
    {
        using var context = corpus.OpenMigrated();

        var prepared = MeetingRecordings.Open(context, "es", now);

        prepared.MeetingId.ShouldNotBe(Guid.Empty);
        prepared.Spool.Exists.ShouldBeTrue();

        // Nothing has been recorded into it. This is the whole of "before the first sample": the
        // folder is there and empty, so anything that arrives next has somewhere that already
        // belongs to a meeting to arrive in.
        prepared.Spool.GetFiles().ShouldBeEmpty();

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
    /// The identity is the application's own and comes from nothing else — not a title, not a file
    /// name, not anything a provider says. Two meetings started with everything else identical are
    /// two meetings.
    /// </summary>
    [Fact]
    public void A_meeting_is_identified_without_a_title_or_anything_a_provider_says()
    {
        using var context = corpus.OpenMigrated();

        var first = MeetingRecordings.Open(context, "es", now);
        var second = MeetingRecordings.Open(context, "es", now);

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
        var prepared = MeetingRecordings.Open(context, "en", now);

        var card = new SpoolCard(
            prepared.MeetingId,
            Guid.NewGuid(),
            now,
            CapturedAudio.Profile,
            [
                new SpooledSource(
                    AudioChannel.Loopback,
                    followedAProgram ? "Teams (4120)" : "Speakers",
                    followedAProgram ? null : "{0.0.0.00000000}.{loopback}"),
                new SpooledSource(AudioChannel.Microphone, "Headset", "{0.0.1.00000000}.{mic}"),
            ]);

        var run = MeetingRecordings.Began(context, card);

        run.Id.ShouldBe(card.CaptureRunId);
        run.MeetingId.ShouldBe(prepared.MeetingId);
        run.StartedAt.ShouldBe(now);
        run.MeDeviceName.ShouldBe("Headset");
        run.SampleRate.ShouldBe(CapturedAudio.SampleRate);
        run.ChannelCount.ShouldBe(CapturedAudio.ChannelCount);

        if (followedAProgram)
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.ProcessLoopback);
            run.OthersProcess.ShouldBe("Teams (4120)");
            run.OthersDeviceName.ShouldBeNull();
            run.OthersDeviceId.ShouldBeNull();
        }
        else
        {
            run.OthersCaptureMode.ShouldBe(CaptureMode.FullLoopback);
            run.OthersProcess.ShouldBeNull();
            run.OthersDeviceName.ShouldBe("Speakers");
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
        var prepared = MeetingRecordings.Open(context, "es", now);
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
    /// ISC-157. Stopping is the end of the recording and the start of nothing: transcribing spends
    /// the user's own credit, so it waits for somebody to ask for it.
    /// </summary>
    [Fact]
    public void Stopping_a_recording_queues_no_work_on_the_meeting()
    {
        using var context = corpus.OpenMigrated();
        var prepared = MeetingRecordings.Open(context, "es", now);
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
            var prepared = MeetingRecordings.Open(context, "es", now);
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
        var mine = MeetingRecordings.Open(context, "es", now);
        var yours = MeetingRecordings.Open(context, "es", now);

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
