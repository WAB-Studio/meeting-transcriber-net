using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Tests.Intake;

/// <summary>
/// A paid response on disk becoming a meeting of the corpus: the row, the source filed as one,
/// and everything derived from it.
/// </summary>
public class MeetingIntakeTests
{
    private const string Fixture = DeepgramFixtures.TwoChannelShort;

    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    /// <summary>Deliberately not what any fixture says it transcribed.</summary>
    private static readonly Duration AnHour = Duration.FromMilliseconds(3_600_000);

    /// <summary>A second instant, so a timestamp that moved can be told from one that did not.</summary>
    private static readonly UtcTimestamp Later =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_response_becomes_a_meeting_with_its_source_and_its_derivatives()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var received = Receive(context, corpus.Root);

        var meeting = context.Meetings.Single();
        meeting.Id.ShouldBe(received.MeetingId);
        meeting.Title.ShouldBe("la del presupuesto");
        meeting.StartedAt.ShouldBe(When);
        meeting.Language.ShouldBe("es");
        meeting.SourceProfile.ShouldBe(DeepgramFixtures.ProfileOf(Fixture));

        // The length comes off the response, which is the only thing that knows it.
        meeting.Duration!.Value.Milliseconds.ShouldBeGreaterThan(0);

        received.WasAlreadyThere.ShouldBeFalse();
        received.Response.Kind.ShouldBe(ArtifactKind.DeepgramResponse);
        received.Response.Origin.ShouldBe(ArtifactOrigin.Source);
        received.Response.Sha256.ShouldBe(CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(Fixture))));
        received.Turns.ShouldBe(context.Utterances.Count(turn => turn.MeetingId == received.MeetingId));

        CorpusFiles.Locate(corpus.Root, received.Response.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, received.Transcript.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, received.Utterances.RelativePath).Exists.ShouldBeTrue();

        // Where it came from, in the table provenance belongs in.
        context.AuditEvents.Single().MeetingId.ShouldBe(received.MeetingId);
    }

    /// <summary>
    /// The response is what identifies the meeting, so the same bytes are the same meeting however
    /// they arrive — under another file name, from another folder, a second time by somebody who
    /// was not sure the first one worked.
    /// </summary>
    [Fact]
    public void The_same_response_under_another_name_is_the_same_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, corpus.Root);

        var elsewhere = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.json");
        File.Copy(DeepgramFixtures.PathOf(Fixture), elsewhere);
        try
        {
            var second = Receive(context, corpus.Root, new FileInfo(elsewhere));

            second.MeetingId.ShouldBe(first.MeetingId);
            second.WasAlreadyThere.ShouldBeTrue();
            second.Turns.ShouldBe(first.Turns);
            second.Response.Id.ShouldBe(first.Response.Id);
            context.Meetings.Count().ShouldBe(1);
            context.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.DeepgramResponse).ShouldBe(1);
        }
        finally
        {
            File.Delete(elsewhere);
        }
    }

    /// <summary>
    /// The half that makes the meeting worth filing again: a corpus whose derivatives are gone —
    /// a render that failed, a folder somebody emptied — gets them back from the response it
    /// already holds, and does not gain a second meeting on the way.
    /// </summary>
    [Fact]
    public void A_meeting_whose_derivatives_are_gone_gets_them_back_from_the_response_it_has()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, corpus.Root);

        foreach (var derived in context.Artifacts.Where(artifact => artifact.Origin == ArtifactOrigin.Derived).ToArray())
        {
            CorpusFiles.Locate(corpus.Root, derived.RelativePath).Delete();
            context.Artifacts.Remove(derived);
        }

        context.Utterances.Where(turn => turn.MeetingId == first.MeetingId).ExecuteDelete();
        context.SaveChanges();

        var again = Receive(context, corpus.Root);

        again.MeetingId.ShouldBe(first.MeetingId);
        again.Turns.ShouldBe(first.Turns);
        context.Meetings.Count().ShouldBe(1);
        CorpusFiles.Locate(corpus.Root, again.Transcript.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, again.Utterances.RelativePath).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// The other half of it, one step further back: the paid file itself is gone. Handing the
    /// original over again is what somebody does when the check says the corpus claims a file it
    /// does not have, and before this it went straight to a render that failed on that same file —
    /// with the bytes that would have fixed it open in the method. They go back first, and only
    /// because they are the ones the row already records, which is what found the meeting at all.
    /// </summary>
    [Fact]
    public void A_meeting_whose_response_is_gone_gets_it_back_when_the_same_bytes_are_filed_again()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, corpus.Root);
        var response = CorpusFiles.Locate(corpus.Root, first.Response.RelativePath);

        response.Delete();

        var again = Receive(context, corpus.Root);

        again.MeetingId.ShouldBe(first.MeetingId);
        again.WasAlreadyThere.ShouldBeTrue();
        again.Turns.ShouldBe(first.Turns);
        context.Meetings.Count().ShouldBe(1);
        context.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.DeepgramResponse).ShouldBe(1);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// The contract's refusal, and the state the corpus is left in by it. Nothing is filed: a
    /// meeting whose response cannot be read is a meeting nothing can ever render, and the paid
    /// file would be one that may never be written again.
    /// </summary>
    [Fact]
    public void A_response_that_disagrees_with_its_profile_is_refused_with_nothing_filed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<AudioContractException>(
            () => Receive(context, corpus.Root, profile: SourceProfile.Diarize));

        context.Meetings.ShouldBeEmpty();
        context.Artifacts.ShouldBeEmpty();
        Directory.Exists(Path.Combine(corpus.Root.FullName, CorpusFiles.Meetings)).ShouldBeFalse();
    }

    [Fact]
    public void A_response_that_is_not_there_says_which_file()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var missing = new FileInfo(Path.Combine(corpus.Root.FullName, "nothing.json"));

        var refused = Should.Throw<IntakeException>(() => Receive(context, corpus.Root, missing));

        refused.Message.ShouldContain(missing.FullName);
    }

    /// <summary>
    /// Filing a paid response, whole, into a corpus where somebody has said who is using the
    /// application: the meeting comes out of the one command with their own turns under their
    /// name, and nobody named a voice for that to happen.
    /// </summary>
    /// <remarks>
    /// What this adds over <c>MeetingRendererTests</c>, which is where the rule lives, is that
    /// filing reaches it at all — a response arrives and the file on disk already reads the name,
    /// rather than reading labels until something renders it a second time.
    /// </remarks>
    [Fact]
    public void A_filed_response_already_names_whoever_said_they_are_using_this()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var ada = new HumanLayer(context, When).ThisIsMe("Ada");

        var received = Receive(
            context,
            corpus.Root,
            new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe)),
            DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe));

        context.SpeakerAssignments.Single().PersonId.ShouldBe(ada.Id);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, received.Transcript.RelativePath).FullName)
            .ShouldContain("## Ada — ");
    }

    /// <summary>
    /// What a meeting's lifecycle starts as, asserted at the door that never says it. The audio
    /// door has had this assertion since it was written; this one leans on <c>Meeting</c>'s own
    /// initializer, which after #94 is the only place a new meeting's lifecycle is decided at all.
    /// </summary>
    [Fact]
    public void An_imported_meeting_is_active()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingId;

        using (var context = corpus.OpenMigrated())
        {
            meetingId = Receive(context, corpus.Root).MeetingId;
        }

        using var reopened = corpus.Open();
        var meeting = reopened.Meetings.Single(row => row.Id == meetingId);

        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.DeletedAt.ShouldBeNull();
    }

    /// <summary>
    /// ISC-34 through the third door, and the card's first half: a meeting this corpus recorded
    /// gains a transcript in its own row rather than a second meeting beside it.
    /// </summary>
    [Fact]
    public void A_response_for_a_meeting_this_corpus_recorded_leaves_one_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetingId = Recorded(context, SourceProfile.Multichannel);

        var received = ReceiveInto(context, meetingId);

        context.Meetings.Count().ShouldBe(1);
        received.MeetingId.ShouldBe(meetingId);
        received.WasAlreadyThere.ShouldBeFalse();

        context.Artifacts.Where(row => row.MeetingId == meetingId)
            .Select(row => row.Kind)
            .ToList()
            .ShouldBe(
                [
                    ArtifactKind.Audio,
                    ArtifactKind.DeepgramResponse,
                    ArtifactKind.Manifest,
                    ArtifactKind.Transcript,
                    ArtifactKind.Utterances,
                ],
                ignoreOrder: true);

        var turns = context.Utterances.Count(turn => turn.MeetingId == meetingId);
        turns.ShouldBeGreaterThan(0);
        received.Turns.ShouldBe(turns);

        CorpusFiles.Locate(corpus.Root, received.Response.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, received.Transcript.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, received.Utterances.RelativePath).Exists.ShouldBeTrue();

        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// A third verb, because it is a third thing to find later. One word shared with
    /// <c>"imported"</c> would make the audit unable to say whether a meeting's response arrived
    /// with the meeting or afterwards, which is the distinction this door creates.
    /// </summary>
    [Fact]
    public void A_recorded_meeting_a_response_arrives_for_is_a_third_thing_in_the_audit()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetingId = Recorded(context, SourceProfile.Multichannel);

        ReceiveInto(context, meetingId);

        context.AuditEvents.Single(row => row.MeetingId == meetingId).Action.ShouldBe("response filed");
    }

    /// <summary>
    /// The length was counted off the audio the corpus holds. What the provider says it transcribed
    /// is a second number about the same file, and one meeting is never given two lengths.
    /// </summary>
    [Fact]
    public void A_response_filed_onto_a_recorded_meeting_never_changes_its_length()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingId;

        using (var context = corpus.OpenMigrated())
        {
            meetingId = Recorded(context, SourceProfile.Multichannel, AnHour);
            ReceiveInto(context, meetingId);
        }

        using var reopened = corpus.Open();
        reopened.Meetings.Single(row => row.Id == meetingId).Duration!.Value.ShouldBe(AnHour);
    }

    /// <summary>
    /// The profile is the meeting's row and is never a caller's, in both directions: a recorded
    /// two-channel meeting handed a one-track response, and a meeting brought in through
    /// <c>import-audio</c> — which is always <c>Diarize</c> — handed a two-channel one.
    /// </summary>
    [Theory]
    [InlineData(SourceProfile.Multichannel, DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(SourceProfile.Diarize, DeepgramFixtures.TwoChannelShort)]
    public void A_response_is_read_under_the_profile_the_meeting_was_recorded_as(
        SourceProfile recordedAs, string response)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetingId = Recorded(context, recordedAs);

        Should.Throw<AudioContractException>(() => ReceiveInto(context, meetingId, response));

        // Nothing was written: the refusal happens before the first row and no paid file moved.
        context.Artifacts.Count(row => row.MeetingId == meetingId).ShouldBe(1);
        context.AuditEvents.ShouldBeEmpty();

        var folder = new DirectoryInfo(
            Path.Combine(corpus.Root.FullName, CorpusFiles.Meetings, meetingId.ToString()));
        folder.EnumerateFiles(MeetingIntake.ResponseFileName).ShouldBeEmpty();
        folder.EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    /// <summary>
    /// A response is one meeting's. Filing the same bytes onto a second would put one conversation
    /// under two meetings with nothing afterwards able to tell which is which.
    /// </summary>
    [Fact]
    public void A_response_already_filed_onto_another_meeting_is_refused()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var itsOwn = Receive(context, corpus.Root);
        var meetingId = Recorded(context, SourceProfile.Multichannel);

        var refused = Should.Throw<IntakeException>(() => ReceiveInto(context, meetingId));

        refused.Message.ShouldContain(itsOwn.MeetingId.ToString());
        refused.Message.ShouldContain(meetingId.ToString());
        context.Meetings.Count().ShouldBe(2);
        context.Artifacts.Count(row => row.MeetingId == meetingId).ShouldBe(1);
    }

    /// <summary>
    /// A paid response is never written over, so a second and different one onto the same meeting
    /// is refused and nothing it touched stayed touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is this door's own and not <c>StagedArtifact</c>'s, which matters for where it
    /// lands as well as for what it says: it is asked before the meeting row is touched, so nothing
    /// was written to be undone. <c>StagedArtifact</c> would also refuse here — but only because the
    /// file is still on disk, which is the half that stops being true the moment somebody loses it.
    /// </para>
    /// <para>
    /// Asserted through a second context all the same. Refusals that <em>do</em> arrive after
    /// <c>UpdatedAt</c> is set are still reachable — a durable write the disk refuses, a card
    /// nothing can replace — and rolling the archive's transaction back does not roll EF's change
    /// tracker back with it, so the first context is not a thing to read the corpus off.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_second_response_is_never_written_over_the_first()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingId;

        using (var context = corpus.OpenMigrated())
        {
            meetingId = Recorded(context, SourceProfile.Multichannel);
            ReceiveInto(context, meetingId);

            var refused = Should.Throw<IntakeException>(() => MeetingIntake.ReceiveInto(
                context,
                meetingId,
                new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe)),
                Later));

            refused.Message.ShouldContain(meetingId.ToString());
            refused.Message.ShouldContain("never written over");
        }

        using var reopened = corpus.Open();
        var filed = reopened.Artifacts.Single(row =>
            row.MeetingId == meetingId && row.Kind == ArtifactKind.DeepgramResponse);

        var first = CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(Fixture)));
        filed.Sha256.ShouldBe(first);
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, filed.RelativePath)).ShouldBe(first);

        reopened.AuditEvents.Single(row => row.MeetingId == meetingId).Action.ShouldBe("response filed");
        reopened.Meetings.Single(row => row.Id == meetingId).UpdatedAt.ShouldBe(When);
    }

    /// <summary>
    /// And the refusal holds when the stored file is the thing that is gone, which is the state
    /// this door could reach and the other could not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Receive</c> picks the meeting <em>by</em> the hash, so bytes the corpus has never seen are
    /// always a meeting of their own and it can never be pointed at an occupied row. Here the
    /// meeting comes off the name and the response off the bytes, so a second response and a
    /// meeting that already has one is a state that exists — and with <c>deepgram.json</c> deleted,
    /// the durable write has no file to find and would have landed the new bytes on the old row:
    /// same path, new hash, one conversation's transcript filed under another meeting, and
    /// <c>check</c> calling the corpus sound afterwards.
    /// </para>
    /// <para>
    /// Missing is not an exotic state. It is what <c>ArtifactReconciler.Check</c> reports after a
    /// partial restore, a sync client, or somebody clearing a folder — and handing the original
    /// over again is exactly what a person does about it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_second_response_is_refused_even_when_the_first_is_missing_from_the_disk()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingId;
        string sha256;

        using (var context = corpus.OpenMigrated())
        {
            meetingId = Recorded(context, SourceProfile.Multichannel);
            var first = ReceiveInto(context, meetingId);
            sha256 = first.Response.Sha256;

            CorpusFiles.Locate(corpus.Root, first.Response.RelativePath).Delete();

            var refused = Should.Throw<IntakeException>(() => MeetingIntake.ReceiveInto(
                context,
                meetingId,
                new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe)),
                Later));

            refused.Message.ShouldContain(meetingId.ToString());
            refused.Message.ShouldContain(sha256);
        }

        using var reopened = corpus.Open();
        var filed = reopened.Artifacts.Single(row =>
            row.MeetingId == meetingId && row.Kind == ArtifactKind.DeepgramResponse);

        // The row still describes the response that was paid for, and the refusal reached it before
        // anything was written — no second audit line, no moved timestamp.
        filed.Sha256.ShouldBe(sha256);
        reopened.AuditEvents.Count(row => row.MeetingId == meetingId).ShouldBe(1);
        reopened.Meetings.Single(row => row.Id == meetingId).UpdatedAt.ShouldBe(When);
    }

    /// <summary>
    /// ISC-34's sentence through this door: the same response filed twice is one filing, and the
    /// third time puts back a paid file the disk has lost.
    /// </summary>
    [Fact]
    public void The_same_response_filed_onto_a_meeting_twice_is_one_filing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetingId = Recorded(context, SourceProfile.Multichannel);

        var first = ReceiveInto(context, meetingId);
        var confirmed = first.Response.ConfirmedAt;

        var second = ReceiveInto(context, meetingId);

        second.WasAlreadyThere.ShouldBeTrue();
        second.MeetingId.ShouldBe(meetingId);
        second.Turns.ShouldBe(first.Turns);
        second.Response.Id.ShouldBe(first.Response.Id);
        second.Response.ConfirmedAt.ShouldBe(confirmed);
        context.Artifacts.Count(row =>
            row.MeetingId == meetingId && row.Kind == ArtifactKind.DeepgramResponse).ShouldBe(1);

        CorpusFiles.Locate(corpus.Root, first.Response.RelativePath).Delete();

        var third = ReceiveInto(context, meetingId);

        third.PutBack.ShouldContain(first.Response.RelativePath);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// The state a meeting still being recorded is in, the state one whose stop never finished is
    /// in, and the state <c>MeetingRecordings.Open</c> leaves behind when nothing follows it. All
    /// three are one row with no audio under it, and all three are refused here for the one reason
    /// that is true of all three.
    /// </summary>
    [Fact]
    public void A_meeting_with_no_audio_is_not_something_a_response_is_of()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetingId = Guid.NewGuid();

        context.Meetings.Add(new Meeting
        {
            Id = meetingId,
            StartedAt = When,
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        });
        context.SaveChanges();

        var refused = Should.Throw<IntakeException>(() => ReceiveInto(context, meetingId));

        refused.Message.ShouldContain(meetingId.ToString());
        refused.Message.ShouldContain("recovery");
        context.Artifacts.ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting nothing knows about is a sentence rather than a stack trace, and it says what
    /// happens instead: a response for a meeting the corpus has never held becomes one of its own.
    /// </summary>
    [Fact]
    public void A_meeting_this_corpus_does_not_hold_is_refused_by_name()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var nothing = Guid.NewGuid();

        var refused = Should.Throw<IntakeException>(() => ReceiveInto(context, nothing));

        refused.Message.ShouldContain(nothing.ToString());
        context.Meetings.ShouldBeEmpty();
    }

    private static ReceivedMeeting Receive(
        CorpusDbContext context,
        DirectoryInfo root,
        FileInfo? response = null,
        SourceProfile? profile = null) => MeetingIntake.Receive(
            context,
            response ?? new FileInfo(DeepgramFixtures.PathOf(Fixture)),
            new MeetingDetails(
                When,
                profile ?? DeepgramFixtures.ProfileOf(Fixture),
                "es",
                "la del presupuesto"),
            When);

    private static ReceivedMeeting ReceiveInto(
        CorpusDbContext context, Guid meetingId, string response = Fixture) =>
        MeetingIntake.ReceiveInto(
            context, meetingId, new FileInfo(DeepgramFixtures.PathOf(response)), When);

    /// <summary>
    /// A meeting this corpus recorded, built out of exactly what <c>ReceiveInto</c> reads: a row
    /// with a profile and a length, and one <c>audio</c> artifact under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By hand rather than through <c>AudioIntake.Bring</c>, and not by choice: this project targets
    /// <c>net10.0</c> and <c>MeetingTranscriber.Recording</c> targets <c>net10.0-windows…</c>, so
    /// there is no reference to make. It is also the only way to get a <c>Multichannel</c> meeting
    /// with no response, which is what a real recording is and what the audio door never produces —
    /// it mixes anything it is not sure about down to one track and files it as <c>Diarize</c>.
    /// </para>
    /// <para>
    /// The bytes under the audio row are not audio. Nothing on this path opens that file: the
    /// response is what is parsed and the length is the row's. What the file has to be is present
    /// and hashed, which is what makes <c>ArtifactReconciler.Check</c> sound afterwards.
    /// </para>
    /// </remarks>
    private static Guid Recorded(
        CorpusDbContext context, SourceProfile profile, Duration? length = null)
    {
        var meetingId = Guid.NewGuid();

        context.Meetings.Add(new Meeting
        {
            Id = meetingId,
            Title = "la de los jueves",
            StartedAt = When,
            Duration = length ?? AnHour,
            SourceProfile = profile,
            Language = "es",
            LifecycleState = LifecycleState.Active,
            CreatedAt = When,
            UpdatedAt = When,
        });
        context.SaveChanges();

        DurableArtifact.Write(
            context,
            meetingId,
            ArtifactKind.Audio,
            CorpusFiles.PathFor(meetingId, RecordingFiles.Recording),
            When,
            into => into.Write("stands in for the recording"u8));

        return meetingId;
    }
}
