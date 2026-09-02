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
    /// Filing a response whose microphone caught one voice, into a corpus where somebody has said
    /// who is using the application: their own turns come out under their name, and nobody named a
    /// voice for that to happen.
    /// </summary>
    /// <remarks>
    /// The three on the loopback are the control. They are the same meeting through the same
    /// filing and they stay as the labels the provider wrote, because which of them is who is
    /// exactly what the recording cannot know — so a filing that named all four would be one
    /// guessing rather than one settling.
    /// </remarks>
    [Fact]
    public void The_microphones_own_voice_reads_as_whoever_said_they_are_using_this()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var ada = new HumanLayer(context, When).ThisIsMe("Ada");

        var received = Receive(context, corpus.Root, OneVoiceOnTheMicrophone, OneVoiceProfile);

        var settled = context.SpeakerAssignments.Single();
        settled.SpeakerLabel.ShouldBe("ch1:speaker_0");
        settled.PersonId.ShouldBe(ada.Id);
        settled.AssignedBy.ShouldBe(SpeakerAssignmentSource.Channel);

        var transcript = File.ReadAllText(
            CorpusFiles.Locate(corpus.Root, received.Transcript.RelativePath).FullName);

        transcript.ShouldContain("## Ada — ");
        transcript.ShouldNotContain("ch1:speaker_0");
        transcript.ShouldContain("## ch0:speaker_0 — ");
    }

    /// <summary>
    /// The same response into a corpus nobody has answered in. There is no row to settle a voice
    /// onto, so every speaker in the meeting reads as its label — which is what the whole card is
    /// for, and what the screen that asks is worth.
    /// </summary>
    [Fact]
    public void A_meeting_filed_before_anybody_said_who_is_using_this_names_nobody()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var received = Receive(context, corpus.Root, OneVoiceOnTheMicrophone, OneVoiceProfile);

        context.SpeakerAssignments.ShouldBeEmpty();

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, received.Transcript.RelativePath).FullName)
            .ShouldContain("## ch1:speaker_0 — ");
    }

    /// <summary>
    /// Answering afterwards and handing the same paid bytes over again. Re-filing is what
    /// re-deriving everything a response can say means everywhere else here, and who is behind the
    /// microphone is the one thing it could not say the first time.
    /// </summary>
    [Fact]
    public void Filing_a_response_again_after_answering_settles_what_it_could_not_before()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Receive(context, corpus.Root, OneVoiceOnTheMicrophone, OneVoiceProfile);
        context.SpeakerAssignments.ShouldBeEmpty();

        var ada = new HumanLayer(context, When).ThisIsMe("Ada");
        var again = Receive(context, corpus.Root, OneVoiceOnTheMicrophone, OneVoiceProfile);

        again.WasAlreadyThere.ShouldBeTrue();
        context.SpeakerAssignments.Single().PersonId.ShouldBe(ada.Id);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, again.Transcript.RelativePath).FullName)
            .ShouldContain("## Ada — ");
    }

    private static FileInfo OneVoiceOnTheMicrophone =>
        new(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));

    private static SourceProfile OneVoiceProfile =>
        DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe);

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
}
