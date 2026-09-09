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
    /// ISC-151 and ISC-159. A file that is exactly what this application records, with nothing
    /// beside it saying whether it is one, is a single track like every other file nothing vouches
    /// for — and it enters saying the channels were averaged.
    /// </summary>
    /// <remarks>
    /// There are two outcomes here and not three. Nothing in this build can tell this application's
    /// own recording, dragged out of its folder, from somebody's 16 kHz stereo export, so a refusal
    /// aimed at the first would land on the second and turn a meeting somebody has into one they
    /// cannot import. The cost it takes instead is that a recording of this application arriving
    /// without its folder loses the split between the two sources — which is why the losing is what
    /// this asserts, and not just the profile.
    /// </remarks>
    [Fact]
    public void Audio_shaped_like_this_applications_own_with_nothing_saying_so_is_one_track()
    {
        var orphan = new FileInfo(Path.Combine(elsewhere.FullName, MeetingAudio.FileName));
        ForeignWav.Steady(orphan, MeetingAudio.Interchange.SampleRate, 16_000, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, orphan, details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeTrue();
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);

        // What landed is what the row says landed. The mix down is the one step on this path that
        // writes bytes the caller never saw, so a row describing a file the corpus does not hold
        // is the failure it could produce and nothing downstream would ever notice.
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, brought.Audio.RelativePath))
            .ShouldBe(brought.Audio.Sha256);

        using var reopened = corpus.Open();
        reopened.Meetings.Single().SourceProfile.ShouldBe(SourceProfile.Diarize);
    }

    /// <summary>
    /// The corpus itself remembers that the channels were averaged, so the loss outlives the
    /// console line that reported it. No claim requires this one: ISC-160 said it and was
    /// tombstoned on 2026-08-20 because nobody had decided the corpus is obliged to disclose a mix
    /// down — the claim was written and closed in the same pass as the code it described. The
    /// behaviour and this probe stay because they are right, not because the ISA asks for them, so
    /// whoever finds this test in the way answers that question on the board first rather than
    /// deleting the only durable account a person gets of a meeting having lost the split between
    /// what the machine played and what the microphone heard.
    /// </summary>
    /// <remarks>
    /// The two meetings here are indistinguishable everywhere else the corpus looks — same profile,
    /// same length, same card field for field — because a meeting that says one channel says
    /// nothing about whether there were ever two. Somebody asking in six months why a meeting they
    /// remember recording has one track has the audit trail and nothing else, which is why this is
    /// probed by reading rows back rather than by reading a report.
    /// </remarks>
    [Fact]
    public void What_became_of_the_channels_is_still_in_the_corpus_afterwards()
    {
        using var context = corpus.OpenMigrated();

        var averaged = AudioIntake.Bring(
            context, Foreign("call.wav", 44_100, 0.5f, 0.25f), details, now);
        var untouched = AudioIntake.Bring(
            context, Foreign("phone.wav", 44_100, 0.5f), details, now);

        using var reopened = corpus.Open();

        // Everything else really does agree, so the audit line is carrying the whole difference.
        reopened.Meetings.Select(meeting => meeting.SourceProfile).Distinct()
            .ShouldBe([SourceProfile.Diarize]);

        Said(reopened, averaged.MeetingId).ShouldContain("2 channels averaged into one");
        Said(reopened, untouched.MeetingId).ShouldNotContain("averaged");
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
    /// ISC-159. A card that does not read as a meeting's vouches for nothing, and the file goes in
    /// as the single track everything nothing vouches for goes in as.
    /// </summary>
    /// <remarks>
    /// The file here is somebody else's, and <c>manifest.json</c> is not a name this product owns
    /// — a browser extension, a package and a web app all write one. Refusing over it would mean
    /// rejecting a stranger's audio for a JSON file they never thought about, and would leave the
    /// rule "delete the file you did not know was there and the same import goes through". That is
    /// the same shape as refusing over a missing card, which is why it gets the same answer.
    /// </remarks>
    [Fact]
    public void A_card_that_does_not_read_as_a_meetings_vouches_for_nothing()
    {
        var folder = new DirectoryInfo(Path.Combine(elsewhere.FullName, "downloads"));
        folder.Create();
        File.WriteAllText(
            Path.Combine(folder.FullName, MeetingManifest.FileName),
            "{\"name\": \"Some Extension\", \"version\": \"1.0\"}");

        var audio = new FileInfo(Path.Combine(folder.FullName, MeetingAudio.FileName));
        ForeignWav.Steady(audio, MeetingAudio.Interchange.SampleRate, 16_000, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();
        var brought = AudioIntake.Bring(context, audio, details, now);

        brought.Profile.ShouldBe(SourceProfile.Diarize);
        brought.MixedDown.ShouldBeTrue();
        Filed(brought).Channels.ShouldBe(AudioFiles.OneTrack);
    }

    /// <summary>
    /// A recording this corpus has not stopped yet is not something to bring in, and what refuses
    /// it is where it is rather than anything read out of it.
    /// </summary>
    /// <remarks>
    /// Its blocks are spooled under the corpus's own folder and reach a meeting through recovery,
    /// so bringing their playback in here would be the same meeting twice. That is worth a probe
    /// because it is the one thing the card reader used to be relied on for — a spool's card is
    /// under the same name and answers a different set of questions, so it used to fail to parse
    /// and refuse by accident. The rule that really holds it runs first and does not depend on a
    /// file parsing.
    /// </remarks>
    [Fact]
    public void A_recording_this_corpus_has_not_stopped_yet_is_not_something_to_bring_in()
    {
        using var context = corpus.OpenMigrated();

        using var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 1);
        var playback = MeetingAudio.Materialise(prepared.Spool).File;

        // It really is the shape a recording comes out as, so nothing but where it sits is left to
        // refuse it on.
        AudioFiles.FormatOf(playback).ShouldBe(MeetingAudio.Interchange);

        Should.Throw<RecordingException>(() => AudioIntake.Bring(context, playback, details, now))
            .Message.ShouldContain("already a file of this corpus");
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

        again.PutBack.ShouldBeEmpty();

        using var reopened = corpus.Open();
        reopened.Meetings.Select(meeting => meeting.Id).ShouldBe([first.MeetingId]);
        reopened.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);
    }

    /// <summary>
    /// And the same audio handed over to a corpus that has the row and has lost the file puts the
    /// file back — and says which one, rather than answering "already here" over a write.
    /// </summary>
    /// <remarks>
    /// Naming it is the whole point of the test. Somebody re-running this reads the answer as a
    /// command that did nothing, and a file having come back means their corpus had a hole in it a
    /// moment ago — which is worth going to look at, and is invisible if the only thing said is
    /// that the meeting was already there.
    /// </remarks>
    [Fact]
    public void Audio_a_row_is_missing_comes_back_and_the_answer_says_which_file()
    {
        var file = Foreign("call.wav", 44_100, 0.5f, 0.25f);

        using var context = corpus.OpenMigrated();
        var first = AudioIntake.Bring(context, file, details, now);

        var lost = CorpusFiles.Locate(corpus.Root, first.Audio.RelativePath);
        lost.Delete();

        var again = AudioIntake.Bring(context, file, details, now);

        again.WasAlreadyThere.ShouldBeTrue();
        again.PutBack.ShouldBe([first.Audio.RelativePath]);

        lost.Refresh();
        lost.Exists.ShouldBeTrue(lost.FullName);
        CorpusFiles.Sha256Of(lost).ShouldBe(first.Audio.Sha256);
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

        using var prepared = MeetingRecordings.Open(context, "es", now);
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

    /// <summary>What the corpus recorded about this meeting having been brought in.</summary>
    private static string Said(CorpusDbContext corpus, Guid meetingId) => corpus.AuditEvents
        .Single(row => row.MeetingId == meetingId && row.Action == "audio imported")
        .Detail ?? string.Empty;

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
