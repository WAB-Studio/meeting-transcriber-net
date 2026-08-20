using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Audio somebody brought becoming a meeting: what the file and the folder it arrived in decide it
/// is, and what the corpus holds afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The one thing every test here is really about is that nothing anywhere in it is asked what a
/// channel carries. The only inputs that decide are the audio's own shape and the folder around
/// it, so a build that grew a way to declare a profile would have nowhere in these to be told one.
/// </para>
/// <para>
/// Every foreign fixture is written by <see cref="ForeignWav"/> at a rate and a width no recording
/// of this application has, because that is what a foreign file is. The one place a fixture is
/// built the other way is <see cref="Recorded"/>, which records a real meeting into a corpus of its
/// own and copies the folder out — a card built by hand there would only prove that this reads what
/// this test writes.
/// </para>
/// </remarks>
public sealed class AudioIntakeTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly DirectoryInfo elsewhere = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    private readonly UtcTimestamp now = UtcTimestamp.Parse("2026-08-20T11:00:00.000Z");
    private readonly BroughtDetails details = new(
        UtcTimestamp.Parse("2026-05-04T14:00:00.000Z"), "es", "Kickoff con el cliente");

    public AudioIntakeTests() => elsewhere.Create();

    /// <summary>
    /// ISC-83. A file with one track becomes a meeting: a row, its audio filed as a source, and a
    /// recovery card beside it — the same shape a recording that was stopped leaves.
    /// </summary>
    [Fact]
    public void A_single_track_from_disk_becomes_a_meeting()
    {
        using var context = corpus.OpenMigrated();

        var brought = AudioIntake.Bring(context, Foreign("phone.wav", 44_100, 0.5f), details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeFalse();
        brought.WasAlreadyThere.ShouldBeFalse();
        brought.Length.Milliseconds.ShouldBe(1_000);
        brought.Audio.Kind.ShouldBe(ArtifactKind.Audio);
        brought.Audio.RelativePath.ShouldBe($"meetings/{brought.MeetingId}/{MeetingAudio.FileName}");

        // Read back through a second connection, so what is asserted is the database rather than
        // the objects still in this context's tracker.
        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();

        meeting.Id.ShouldBe(brought.MeetingId);
        meeting.StartedAt.ShouldBe(details.StartedAt);
        meeting.Title.ShouldBe("Kickoff con el cliente");
        meeting.Language.ShouldBe("es");
        meeting.SourceProfile.ShouldBe(SourceProfile.Diarize);
        meeting.Duration.ShouldBe(Duration.FromMilliseconds(1_000));
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);

        // It lands on the rung a stopped recording lands on, so what happens to it next is a press
        // somebody makes rather than a charge this put in motion.
        var kinds = reopened.Artifacts.Where(row => row.MeetingId == meeting.Id)
            .Select(row => row.Kind).ToList();
        kinds.ShouldContain(ArtifactKind.Audio);
        kinds.ShouldContain(ArtifactKind.Manifest);
        MeetingStages.Of(kinds, Array.Empty<JobKind>()).ShouldBe(MeetingStage.Recorded);
    }

    /// <summary>
    /// ISC-83, and ISC-151's ordinary case. A stereo file this application did not record enters as
    /// one track, and the audio the corpus ends up holding really has one channel in it.
    /// </summary>
    /// <remarks>
    /// Both halves, because either alone would pass over the failure. A row saying <c>diarize</c>
    /// over a two-channel file is a meeting nothing can transcribe, and a mixed down file under a
    /// row saying <c>multichannel</c> is a meeting whose every turn is about to be attributed to a
    /// device.
    /// </remarks>
    [Fact]
    public void A_stereo_file_this_application_did_not_record_is_never_two_channels_of_one_meeting()
    {
        using var context = corpus.OpenMigrated();

        var brought = AudioIntake.Bring(
            context, Foreign("call.wav", 44_100, 0.5f, 0.25f), details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeTrue();
        brought.Length.Milliseconds.ShouldBe(1_000);

        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();
        meeting.SourceProfile.ShouldBe(SourceProfile.Diarize);
        meeting.Duration.ShouldBe(Duration.FromMilliseconds(1_000));
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// ISC-83. How long the meeting is comes off the audio the corpus holds and never off the
    /// length the file's header declares.
    /// </summary>
    /// <remarks>
    /// A copy interrupted half way declares an hour and gives up a minute, because a WAV's length
    /// is written before its audio and never corrected. The declared number is the one
    /// <c>meetings.duration</c> would carry and the one every citation into this meeting is checked
    /// against — and nothing downstream could ever notice, because the file hashes to exactly what
    /// its row says it does.
    /// </remarks>
    [Fact]
    public void How_long_the_meeting_is_comes_off_the_audio_and_never_off_a_header()
    {
        // A second of mono at 16 kHz is what this says it is, and 12.5 ms is what it holds.
        var file = ForeignWav.Truncated(
            new FileInfo(Path.Combine(elsewhere.FullName, "cut-off.wav")),
            rate: 16_000, channels: 1, declared: 32_000, present: 400);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, file, details, now);

        brought.Length.Milliseconds.ShouldBe(13);

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Duration.ShouldBe(Duration.FromMilliseconds(13));
    }

    /// <summary>
    /// ISC-151. Nor is one carrying a card that says it is. A card is five keys of plain JSON that
    /// anybody can write; the audio being the exact shape this application records is the half that
    /// cannot be typed, and it is asked first.
    /// </summary>
    [Fact]
    public void A_card_beside_audio_this_application_could_not_have_made_decides_nothing()
    {
        var folder = Recorded();

        // A stereo export at 48 kHz, which no recording of this application is — dropped into the
        // folder under the name the card describes, so the card is about it and says multichannel.
        ForeignWav.Steady(MeetingAudio.In(folder), 48_000, 48_000, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, MeetingAudio.In(folder), details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// ISC-151. And nor is one saved under a name the card is not about. A meeting's folder holds
    /// one recording and one card describing it, so a file dropped in beside them is somebody
    /// else's however the card reads.
    /// </summary>
    [Fact]
    public void A_card_says_nothing_about_a_file_it_is_not_about()
    {
        var folder = Recorded();
        var alongside = new FileInfo(Path.Combine(folder.FullName, "from-the-phone.wav"));
        ForeignWav.Steady(alongside, 44_100, 16_000, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, alongside, details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeTrue();
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// The other side of the same rule: this application's own recording, arriving in the folder it
    /// was filed in, is its two sources — and it goes in byte for byte rather than being mixed
    /// down.
    /// </summary>
    /// <remarks>
    /// The folder is a real one this product wrote: a meeting recorded into a corpus of its own and
    /// finished, which is what a backup restored onto another machine holds.
    /// </remarks>
    [Fact]
    public void This_applications_own_recording_arrives_as_its_two_sources()
    {
        var folder = Recorded();
        var audio = MeetingAudio.In(folder);
        var arrived = CorpusFiles.Sha256Of(audio);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, audio, details, now);

        brought.Profile.ShouldBe(SourceProfile.Multichannel);
        brought.MixedDown.ShouldBeFalse();
        brought.Audio.Sha256.ShouldBe(arrived);
        Filed(brought).ShouldBe(MeetingAudio.Interchange);

        using var reopened = corpus.Open();
        reopened.Meetings.Single().SourceProfile.ShouldBe(SourceProfile.Multichannel);

        // A new meeting of this corpus, and never the id the card carries. The card is read for
        // what the audio is and for nothing else, so a folder handed over twice cannot land on top
        // of a meeting already here.
        brought.MeetingId.ShouldNotBe(MeetingManifest.Read(
            new FileInfo(Path.Combine(folder.FullName, MeetingManifest.FileName))).MeetingId);
    }

    /// <summary>
    /// A file that is exactly what this application records, with nothing beside it saying whether
    /// it is one, is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// It is the case that used to be answered silently and wrongly in both directions: taken as
    /// two sources it puts somebody's name on a stranger's words, and averaged to mono it destroys
    /// the split between what the machine played and what the microphone heard on a recording this
    /// application made. The refusal says which folder to bring it in from, which is not the same
    /// as asking what a channel carries.
    /// </remarks>
    [Fact]
    public void Audio_shaped_like_this_applications_own_with_nothing_saying_so_is_refused()
    {
        var orphan = new FileInfo(Path.Combine(elsewhere.FullName, MeetingAudio.FileName));
        ForeignWav.Steady(orphan, MeetingAudio.Interchange.SampleRate, 16_000, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();

        Should.Throw<RecordingException>(() => AudioIntake.Bring(context, orphan, details, now))
            .Message.ShouldContain(MeetingManifest.FileName);

        NothingWasFiled();
    }

    /// <summary>
    /// ISC-151. A folder saying two sources over a single track is still one track. The audio is
    /// what is asked first, and a card cannot widen what is actually in the file.
    /// </summary>
    [Fact]
    public void A_folder_saying_two_sources_over_a_single_track_is_still_one_track()
    {
        var folder = Recorded();
        var audio = MeetingAudio.In(folder);

        // One channel at the rate and width a recording of this application has, so everything
        // about it but the channel count agrees with the card beside it.
        ForeignWav.Steady(audio, MeetingAudio.Interchange.SampleRate, 16_000, 0.5f);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, audio, details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeFalse();
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// A card that is there and cannot be read stops the import instead of quietly being treated as
    /// audio from somewhere else. A recording of this application's own, mixed down to mono because
    /// the file saying what it was had been torn in half, is a loss nobody would notice.
    /// </summary>
    [Fact]
    public void A_card_that_cannot_be_read_stops_the_import()
    {
        var folder = Recorded();
        File.WriteAllText(
            Path.Combine(folder.FullName, MeetingManifest.FileName), "{\"meeting\": \"not-an-id\"}");

        using var context = corpus.OpenMigrated();

        Should.Throw<ManifestException>(
            () => AudioIntake.Bring(context, MeetingAudio.In(folder), details, now));

        NothingWasFiled();
    }

    /// <summary>
    /// ISC-83, whatever its channel count. A room's microphone array hands over six, and six
    /// average into one the same way two do.
    /// </summary>
    [Fact]
    public void More_channels_than_a_pair_still_become_a_meeting()
    {
        using var context = corpus.OpenMigrated();

        var brought = AudioIntake.Bring(
            context, Foreign("room.wav", 48_000, 0.6f, 0f, 0.6f, 0f, 0.6f, 0f), details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeTrue();
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// ISC-34. The same bytes handed over twice are the meeting that is already here, which is what
    /// somebody re-running a command that half worked is doing.
    /// </summary>
    /// <remarks>
    /// Both shapes, because they reach the answer differently: a single track is compared as it
    /// arrived, and a stereo file is compared after the mix down — which only works because the mix
    /// down is the same code over the same bytes every time.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_same_audio_brought_in_twice_is_one_meeting(int channels)
    {
        var levels = Enumerable.Range(0, channels).Select(index => 0.5f - (0.2f * index)).ToArray();
        var file = Foreign("call.wav", 44_100, levels);

        using var context = corpus.OpenMigrated();

        var first = AudioIntake.Bring(context, file, details, now);
        var again = AudioIntake.Bring(context, file, details, now);

        again.WasAlreadyThere.ShouldBeTrue();
        again.MeetingId.ShouldBe(first.MeetingId);

        using var reopened = corpus.Open();
        reopened.Meetings.Select(meeting => meeting.Id).ShouldBe([first.MeetingId]);
        reopened.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);
    }

    /// <summary>
    /// A meeting's own audio is not something to bring in. It is already here, and bringing it in
    /// would be the same meeting a second time under an id of its own — with the first meeting's
    /// card read as the origin evidence for the copy.
    /// </summary>
    [Fact]
    public void A_file_already_in_this_corpus_is_refused()
    {
        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(
            context, Foreign("call.wav", 44_100, 0.5f, 0.25f), details, now);

        var filed = CorpusFiles.Locate(corpus.Root, brought.Audio.RelativePath);

        Should.Throw<RecordingException>(() => AudioIntake.Bring(context, filed, details, now))
            .Message.ShouldContain("already a file of this corpus");

        using var reopened = corpus.Open();
        reopened.Meetings.Count().ShouldBe(1);
    }

    /// <summary>Audio that cannot be read at all leaves the corpus exactly as it was.</summary>
    [Fact]
    public void Audio_that_is_not_a_wav_leaves_the_corpus_untouched()
    {
        var file = new FileInfo(Path.Combine(elsewhere.FullName, "meeting.m4a"));
        File.WriteAllBytes(file.FullName, [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]);

        using var context = corpus.OpenMigrated();

        Should.Throw<AudioCaptureException>(() => AudioIntake.Bring(context, file, details, now));

        NothingWasFiled();
    }

    /// <summary>
    /// A width this build has no reader for stops before anything is filed, rather than half way
    /// through a mix down with a meeting already in the corpus.
    /// </summary>
    [Fact]
    public void A_width_this_build_cannot_read_leaves_the_corpus_untouched()
    {
        var file = new FileInfo(Path.Combine(elsewhere.FullName, "studio.wav"));
        ForeignWav.Wide(file, rate: 48_000, channels: 2, frames: 16_000);

        using var context = corpus.OpenMigrated();

        Should.Throw<AudioCaptureException>(() => AudioIntake.Bring(context, file, details, now));

        NothingWasFiled();
    }

    /// <summary>
    /// Bringing audio in queues nothing, for the same reason stopping a recording does: what comes
    /// next spends the user's own Deepgram credit, and importing a file is not somebody agreeing to
    /// pay for it.
    /// </summary>
    [Fact]
    public void Bringing_audio_in_queues_no_work_on_the_meeting()
    {
        using var context = corpus.OpenMigrated();

        AudioIntake.Bring(context, Foreign("call.wav", 44_100, 0.5f, 0.25f), details, now);

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.ShouldBeEmpty();
    }

    /// <summary>
    /// Nothing is left beside the meeting's audio afterwards. The mix down is a working file, and a
    /// second copy of a meeting's audio under a name nothing looks for is exactly what the
    /// reconciler would later find and be unable to explain.
    /// </summary>
    [Fact]
    public void The_mix_down_is_not_left_in_the_meetings_folder()
    {
        using var context = corpus.OpenMigrated();

        var brought = AudioIntake.Bring(
            context, Foreign("call.wav", 44_100, 0.5f, 0.25f), details, now);

        Folder(brought.MeetingId).GetFiles().Select(file => file.Name)
            .ShouldBe([MeetingAudio.FileName, MeetingManifest.FileName], ignoreOrder: true);
    }

    /// <summary>
    /// And no folder is left behind by a filing that never happened, which the reconciler walks
    /// files rather than folders to find and so would never mention.
    /// </summary>
    [Fact]
    public void A_filing_that_did_not_happen_leaves_no_folder_behind()
    {
        var file = new FileInfo(Path.Combine(elsewhere.FullName, "studio.wav"));
        ForeignWav.Wide(file, rate: 48_000, channels: 2, frames: 16_000);

        using var context = corpus.OpenMigrated();

        Should.Throw<AudioCaptureException>(() => AudioIntake.Bring(context, file, details, now));

        var meetings = new DirectoryInfo(Path.Combine(corpus.Root.FullName, CorpusFiles.Meetings));
        meetings.Refresh();
        if (meetings.Exists)
        {
            meetings.GetDirectories().ShouldBeEmpty();
        }
    }

    public void Dispose()
    {
        corpus.Dispose();

        try
        {
            elsewhere.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a green test over.
        }
    }

    /// <summary>
    /// A second of audio that arrived from somewhere else, in a folder with nothing else in it and
    /// at a rate no recording of this application has.
    /// </summary>
    private FileInfo Foreign(string name, int rate, params float[] levels) => ForeignWav.Steady(
        new FileInfo(Path.Combine(elsewhere.FullName, name)), rate, rate, levels);

    /// <summary>
    /// A meeting folder this product really wrote: recorded into a corpus of its own, finished, and
    /// left with its audio and the card beside it — which is what a backup holds.
    /// </summary>
    private DirectoryInfo Recorded()
    {
        using var other = new TemporaryCorpus();
        using var context = other.OpenMigrated();

        var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 1);
        MeetingRecordings.Finish(context, prepared.MeetingId, now);

        var filed = new DirectoryInfo(Path.Combine(
            other.Root.FullName, CorpusFiles.Meetings, prepared.MeetingId.ToString()));

        // Copied out before the corpus it was made in goes away, because that is what somebody
        // holding a restored folder actually has: the files, and no database behind them.
        var restored = new DirectoryInfo(Path.Combine(elsewhere.FullName, "restored"));
        restored.Create();
        foreach (var file in filed.GetFiles())
        {
            file.CopyTo(Path.Combine(restored.FullName, file.Name), overwrite: true);
        }

        return restored;
    }

    private DirectoryInfo Folder(Guid meetingId) => new(Path.Combine(
        corpus.Root.FullName, CorpusFiles.Meetings, meetingId.ToString()));

    /// <summary>What the corpus ended up holding, read back off the disk.</summary>
    private StreamFormat Filed(BroughtMeeting brought) =>
        AudioFiles.Read(CorpusFiles.Locate(corpus.Root, brought.Audio.RelativePath)).Format;

    /// <summary>The corpus as it was before anything was brought in: no meeting, and no file.</summary>
    private void NothingWasFiled()
    {
        using var reopened = corpus.Open();
        reopened.Meetings.ShouldBeEmpty();
        reopened.Artifacts.ShouldBeEmpty();

        var meetings = new DirectoryInfo(Path.Combine(corpus.Root.FullName, CorpusFiles.Meetings));
        meetings.Refresh();
        if (meetings.Exists)
        {
            meetings.GetFiles("*", SearchOption.AllDirectories).ShouldBeEmpty();
        }
    }
}
