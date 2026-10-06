using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;
using MeetingTranscriber.Testing;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The corpus side of the screen that names voices: the voices a meeting's turns carry, everybody
/// who could be put on one, and the one thing that screen writes back.
/// </summary>
public class MeetingVoicesTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Every_voice_the_turns_carry_is_offered_with_everybody_to_choose_from()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(
            context, (AudioChannel.Microphone, 0, "hola"), (AudioChannel.Loopback, 0, "buenas"));
        var somebody = new HumanLayer(context, When).Add("Renata");

        var read = new MeetingVoices(context, TimeProvider.System).Of(meeting);

        read.Voices.Voices.Select(voice => voice.Label).ShouldBe(["ch1:speaker_0", "ch0:speaker_0"]);
        read.Everybody.Select(person => person.Id).ShouldContain(somebody.Id);
    }

    /// <summary>
    /// Who the meeting already names comes first, then whoever the corpus names most, then by name.
    /// </summary>
    /// <remarks>
    /// The one place the order matters is the picker over a voice: the person most likely to be on
    /// it is somebody this meeting already has, and after that somebody who comes up often — and an
    /// alphabetical list puts Zoe, who is on every meeting, under every Aaron who is on none.
    /// </remarks>
    [Fact]
    public void The_people_this_meeting_names_come_first()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, When);
        var aaron = human.Add("Aaron");
        var zoe = human.Add("Zoe");
        var mia = human.Add("Mia");
        var nora = human.Add("Nora");

        var here = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var before = Meeting(context, (AudioChannel.Loopback, 0, "hola"));
        var earlier = Meeting(context, (AudioChannel.Loopback, 0, "chau"));

        human.Name(here, nora, MeetingPersonRole.Attended);
        human.Name(before, zoe, MeetingPersonRole.Attended);
        human.Name(earlier, zoe, MeetingPersonRole.Attended);
        human.Name(earlier, mia, MeetingPersonRole.Attended);

        var read = new MeetingVoices(context, TimeProvider.System).Of(here);

        read.Everybody.Select(person => person.Id).ShouldBe([nora.Id, zoe.Id, mia.Id, aaron.Id]);
    }

    [Fact]
    public void A_meeting_whose_audio_is_there_is_offered_its_file()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.Audio,
            Origin = ArtifactKind.Audio.OriginOf(),
            RelativePath = CorpusFiles.PathFor(meeting, "audio.wav"),
            ByteSize = 4,
            Sha256 = new string('a', 64),
            ConfirmedAt = When,
        });
        var audio = CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "audio.wav"));
        audio.Directory!.Create();
        File.WriteAllBytes(audio.FullName, [0x52, 0x49, 0x46, 0x46]);

        var read = new MeetingVoices(context, TimeProvider.System).Of(meeting);

        read.TheRecording.ShouldBe(RecordedAudio.Playable);
        read.Audio.ShouldNotBeNull();
        read.Audio!.Exists.ShouldBeTrue();
    }

    [Fact]
    public void A_meeting_that_came_in_as_a_paid_response_has_nothing_to_play()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));

        var read = new MeetingVoices(context, TimeProvider.System).Of(meeting);

        read.TheRecording.ShouldBe(RecordedAudio.NoneYet);
        read.Audio.ShouldBeNull();
    }

    [Fact]
    public void A_meeting_whose_recording_is_gone_says_so()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.Audio,
            Origin = ArtifactKind.Audio.OriginOf(),
            RelativePath = CorpusFiles.PathFor(meeting, "audio.wav"),
            ByteSize = 4,
            Sha256 = new string('a', 64),
            ConfirmedAt = When,
        });

        var read = new MeetingVoices(context, TimeProvider.System).Of(meeting);

        read.TheRecording.ShouldBe(RecordedAudio.NotWhereTheCorpusSaysItIs);
        read.Audio.ShouldBeNull();
    }

    [Fact]
    public void Naming_a_voice_puts_the_person_on_its_label_and_leaves_the_turns_as_they_were()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var somebody = new HumanLayer(context, When).Add("Renata");
        var label = SpeakerLabels.For(AudioChannel.Loopback, 0);

        var changed = new MeetingVoices(context, TimeProvider.System).Save(meeting, [new VoiceAnswer(label, somebody.Id)]);

        changed.ShouldBe(1);
        var assignment = context.SpeakerAssignments.Single();
        assignment.PersonId.ShouldBe(somebody.Id);
        assignment.AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
        context.Utterances.Single().Text.ShouldBe("buenas");
    }

    [Fact]
    public void Saving_what_is_already_there_writes_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var somebody = new HumanLayer(context, When).Add("Renata");
        var label = SpeakerLabels.For(AudioChannel.Loopback, 0);
        var clock = new SteppedClock(When.Value);
        var voices = new MeetingVoices(context, clock);

        voices.Save(meeting, [new VoiceAnswer(label, somebody.Id)]);
        var assignedAt = context.SpeakerAssignments.Single().AssignedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        var changed = voices.Save(meeting, [new VoiceAnswer(label, somebody.Id)]);

        changed.ShouldBe(0);
        context.SpeakerAssignments.Single().AssignedAt.ShouldBe(assignedAt);
    }

    [Fact]
    public void Taking_a_name_off_leaves_the_voice_unnamed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var somebody = new HumanLayer(context, When).Add("Renata");
        var label = SpeakerLabels.For(AudioChannel.Loopback, 0);
        var voices = new MeetingVoices(context, TimeProvider.System);
        voices.Save(meeting, [new VoiceAnswer(label, somebody.Id)]);

        var changed = voices.Save(meeting, [new VoiceAnswer(label, null)]);

        changed.ShouldBe(1);
        context.SpeakerAssignments.ShouldBeEmpty();
    }

    [Fact]
    public void A_label_no_turn_carries_is_refused_and_nothing_is_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var somebody = new HumanLayer(context, When).Add("Renata");
        var sound = SpeakerLabels.For(AudioChannel.Loopback, 0);
        var missing = SpeakerLabels.For(AudioChannel.Loopback, 7);

        Should.Throw<ArgumentException>(() => new MeetingVoices(context, TimeProvider.System).Save(
            meeting, [new VoiceAnswer(sound, somebody.Id), new VoiceAnswer(missing, somebody.Id)]));

        context.SpeakerAssignments.ShouldBeEmpty();
    }

    /// <summary>
    /// A label answered twice in one save refuses the whole save, before anything about it is
    /// written — the same rule as a label no turn carries, over a different mistake.
    /// </summary>
    [Fact]
    public void A_label_answered_twice_is_refused_and_nothing_is_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var human = new HumanLayer(context, When);
        var first = human.Add("Renata");
        var second = human.Add("Jo");
        var label = SpeakerLabels.For(AudioChannel.Loopback, 0);

        Should.Throw<ArgumentException>(() => new MeetingVoices(context, TimeProvider.System).Save(
            meeting, [new VoiceAnswer(label, first.Id), new VoiceAnswer(label, second.Id)]));

        context.SpeakerAssignments.ShouldBeEmpty();
    }

    [Fact]
    public void A_person_the_corpus_does_not_hold_is_refused()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, (AudioChannel.Loopback, 0, "buenas"));
        var label = SpeakerLabels.For(AudioChannel.Loopback, 0);

        var refused = Should.Throw<ArgumentException>(() => new MeetingVoices(context, TimeProvider.System).Save(
            meeting, [new VoiceAnswer(label, Guid.NewGuid())]));

        context.SpeakerAssignments.ShouldBeEmpty();
        refused.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_meeting_the_corpus_does_not_hold_is_refused()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<MeetingStageException>(
            () => new MeetingVoices(context, TimeProvider.System).Save(Guid.NewGuid(), []));
    }

    private static Guid Meeting(CorpusDbContext context, params (AudioChannel? Channel, int Speaker, string Text)[] said)
    {
        var meeting = Guid.NewGuid();
        MeetingRows.Add(context, new Meeting
        {
            Id = meeting,
            StartedAt = When,
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        });

        for (var ordinal = 0; ordinal < said.Length; ordinal++)
        {
            var (channel, speaker, text) = said[ordinal];
            MeetingRows.Add(context, new Utterance
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Ordinal = ordinal,
                Start = MeetingRows.At(ordinal),
                End = MeetingRows.At(ordinal) + Duration.FromMilliseconds(500),
                Channel = channel,
                SpeakerLabel = SpeakerLabels.For(channel, speaker),
                Text = text,
            });
        }

        return meeting;
    }
}
