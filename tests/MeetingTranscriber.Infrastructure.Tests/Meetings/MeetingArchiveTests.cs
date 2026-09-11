using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The one path every door files a meeting down: what lands, in what order, and what survives a
/// refusal halfway through it.
/// </summary>
/// <remarks>
/// The bytes under the two sources here are not a WAV and not a response, and that is deliberate
/// rather than something to fix with a fixture: <see cref="DurableArtifact.Write"/> hashes and
/// stores what it is given and never parses it, and nothing in this suite reads either file back as
/// audio or as JSON. What a real response has to be is `MeetingIntakeTests`' subject; what a real
/// recording has to be is `AudioIntakeTests`'.
/// </remarks>
public sealed class MeetingArchiveTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();

    private static readonly UtcTimestamp Now = UtcTimestamp.Parse("2026-09-10T18:00:00.000Z");
    private static readonly UtcTimestamp Started = UtcTimestamp.Parse("2026-05-04T14:00:00.000Z");

    private static NewMeeting AMeeting(Guid id) => new(
        id,
        Started,
        Duration.FromMilliseconds(61_000),
        SourceProfile.Diarize,
        "es",
        "Kickoff con el cliente",
        "lo que quedó por decidir");

    /// <summary>
    /// A response door: a file that may never be written over, and its own verb.
    /// </summary>
    /// <remarks>
    /// The name is a literal because the constant for it — <c>MeetingIntake.ResponseFileName</c> —
    /// is in <c>Processing</c>, which this suite cannot see. The audio one below uses
    /// <see cref="RecordingFiles.Recording"/>, which it can.
    /// </remarks>
    private static ArrivedOn AResponse() => new(
        Kind: ArtifactKind.DeepgramResponse,
        FileName: "deepgram.json",
        Contents: into => into.Write("{}"u8),
        Verb: "imported",
        Detail: "the response at 'C:\\elsewhere\\paid.json'");

    /// <summary>The audio door, which is the same path and a different file and word.</summary>
    private static ArrivedOn SomeAudio() => new(
        Kind: ArtifactKind.Audio,
        FileName: RecordingFiles.Recording,
        Contents: into => into.Write("RIFF"u8),
        Verb: "audio imported",
        Detail: "the audio at 'C:\\elsewhere\\meeting.wav'");

    /// <summary>
    /// Every field the door said, and none it did not: the lifecycle comes off <c>Meeting</c>'s own
    /// initializer and the archive never restates it.
    /// </summary>
    [Fact]
    public void A_meeting_arriving_carries_what_the_door_said_and_nothing_it_did_not()
    {
        var meetingId = Guid.NewGuid();

        (Artifact Source, Artifact Card) filed;
        using (var context = corpus.OpenMigrated())
        {
            filed = MeetingArchive.New(context, AMeeting(meetingId), AResponse(), Now);
        }

        // Read back through a second connection, so what is asserted is the column rather than the
        // object still in the first context's tracker.
        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single();

        meeting.Id.ShouldBe(meetingId);
        meeting.Title.ShouldBe("Kickoff con el cliente");
        meeting.Context.ShouldBe("lo que quedó por decidir");
        meeting.StartedAt.ShouldBe(Started);
        meeting.Duration.ShouldBe(Duration.FromMilliseconds(61_000));
        meeting.SourceProfile.ShouldBe(SourceProfile.Diarize);
        meeting.Language.ShouldBe("es");
        meeting.CreatedAt.ShouldBe(Now);
        meeting.UpdatedAt.ShouldBe(Now);
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.DeletedAt.ShouldBeNull();
        meeting.TemplateId.ShouldBeNull();

        filed.Source.Kind.ShouldBe(ArtifactKind.DeepgramResponse);
        filed.Source.RelativePath.ShouldEndWith(AResponse().FileName);
        filed.Card.Kind.ShouldBe(ArtifactKind.Manifest);
        filed.Card.RelativePath.ShouldEndWith(MeetingManifest.FileName);

        var stored = reopened.Artifacts.Where(row => row.MeetingId == meetingId).ToList();
        stored.Select(row => row.Id).ShouldContain(filed.Source.Id);
        stored.Select(row => row.Id).ShouldContain(filed.Card.Id);
    }

    /// <summary>
    /// The card is written after the commit that filed the source, so a card that cannot be written
    /// leaves the meeting and its source in the corpus rather than rolling a row back out from
    /// under a file already renamed into place.
    /// </summary>
    /// <remarks>
    /// The folder is not empty when the archive runs, and that is harmless: nothing in
    /// <see cref="MeetingArchive"/> sweeps a folder, and the door that does —
    /// <c>AudioIntake.RemoveIfNothingLanded</c> — removes only an empty one.
    /// </remarks>
    [Fact]
    public void The_recovery_card_is_written_after_the_source_is_committed()
    {
        var meetingId = Guid.NewGuid();

        // A brand-new meeting has no card yet, so there is nothing to hold open until this writes
        // one. The archive takes the id, which is what lets the file be named before anything runs.
        var card = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meetingId, MeetingManifest.FileName));
        card.Directory!.Create();
        File.WriteAllText(card.FullName, "{}");

        using (var context = corpus.OpenMigrated())
        {
            using var held = card.Open(new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.None,
            });

            var refused = Should.Throw<Exception>(
                () => MeetingArchive.New(context, AMeeting(meetingId), SomeAudio(), Now));

            // Which of the two Windows gives is its own business and not this method's contract:
            // a replace over a handle held with no sharing comes back as access denied, and the
            // same thing refused as a sharing violation comes back as an `IOException`. What is
            // being proved is where the throw lands relative to the commit, not its type.
            (refused is IOException or UnauthorizedAccessException).ShouldBeTrue(
                $"the card write should refuse over a held file; it threw {refused.GetType()}");
        }

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Id.ShouldBe(meetingId);

        var audio = reopened.Artifacts.Single(row =>
            row.MeetingId == meetingId && row.Kind == ArtifactKind.Audio);
        CorpusFiles.Locate(corpus.Root, audio.RelativePath).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A file arriving onto a meeting the corpus already holds adds the file and the audit line,
    /// and never a second meeting.
    /// </summary>
    [Fact]
    public void A_file_arriving_onto_a_meeting_adds_no_second_meeting()
    {
        var meetingId = Guid.NewGuid();

        using (var context = corpus.OpenMigrated())
        {
            MeetingArchive.New(context, AMeeting(meetingId), AResponse(), Now);
            MeetingArchive.Onto(context, meetingId, SomeAudio(), Now);
        }

        using var reopened = corpus.Open();
        reopened.Meetings.Count().ShouldBe(1);

        reopened.AuditEvents
            .Where(row => row.MeetingId == meetingId)
            .OrderBy(row => row.Id)
            .Select(row => row.Action)
            .ToList()
            .ShouldBe(["imported", "audio imported"]);

        // One card, because the second write replaced the first rather than adding a row.
        reopened.Artifacts
            .Where(row => row.MeetingId == meetingId)
            .Select(row => row.Kind)
            .ToList()
            .ShouldBe(
                [ArtifactKind.DeepgramResponse, ArtifactKind.Manifest, ArtifactKind.Audio],
                ignoreOrder: true);
    }

    /// <summary>
    /// The verb is the door's and never this method's: one word for two doors would make the audit
    /// unable to say which way a meeting arrived.
    /// </summary>
    [Fact]
    public void Each_door_keeps_its_own_word_in_the_audit()
    {
        var byResponse = Guid.NewGuid();
        var byAudio = Guid.NewGuid();

        using (var context = corpus.OpenMigrated())
        {
            MeetingArchive.New(context, AMeeting(byResponse), AResponse(), Now);
            MeetingArchive.New(context, AMeeting(byAudio), SomeAudio(), Now);
        }

        using var reopened = corpus.Open();

        var imported = reopened.AuditEvents.Single(row => row.MeetingId == byResponse);
        imported.Action.ShouldBe("imported");
        imported.Actor.ShouldBe(AuditActor.App);
        imported.Detail.ShouldBe("the response at 'C:\\elsewhere\\paid.json'");
        imported.OccurredAt.ShouldBe(Now);

        var brought = reopened.AuditEvents.Single(row => row.MeetingId == byAudio);
        brought.Action.ShouldBe("audio imported");
        brought.Actor.ShouldBe(AuditActor.App);
        brought.Detail.ShouldBe("the audio at 'C:\\elsewhere\\meeting.wav'");
    }

    public void Dispose() => corpus.Dispose();
}
