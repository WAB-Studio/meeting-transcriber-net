using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// A transcription arriving turns into the two files a person reads, with nobody asking for them:
/// the corpus is opened by path the way the application opens it, and what comes back is read off
/// the disk afterwards rather than out of the context that wrote it.
/// </summary>
public class OwedRendersTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_meeting_whose_transcription_arrived_gets_its_files_without_being_asked()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = Transcribed(corpus);

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBe([meeting]);
        caught.CouldNotRender.ShouldBeEmpty();

        using var context = corpus.Open();
        Files(context, meeting).ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances], ignoreOrder: true);
        context.Utterances.Count(turn => turn.MeetingId == meeting).ShouldBeGreaterThan(0);

        foreach (var file in context.Artifacts.Where(artifact => artifact.MeetingId == meeting).ToArray())
        {
            CorpusFiles.Locate(corpus.Root, file.RelativePath).Exists.ShouldBeTrue();
        }
    }

    /// <summary>
    /// The other half of the done condition: catching up is safe to run on every launch because a
    /// meeting already holding its files is not owed a render at all, so the second pass writes
    /// nothing rather than writing the same bytes again.
    /// </summary>
    [Fact]
    public void Catching_up_again_renders_nothing_and_leaves_the_same_files()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = Transcribed(corpus);

        OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);
        var first = Rendered(corpus, meeting);

        var again = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        again.Rendered.ShouldBeEmpty();
        again.CouldNotRender.ShouldBeEmpty();
        Rendered(corpus, meeting).ShouldBe(first);
    }

    [Fact]
    public void A_meeting_whose_transcription_has_not_arrived_is_not_owed_a_render()
    {
        using var corpus = new TemporaryCorpus();

        using (var context = corpus.OpenMigrated())
        {
            Meeting(context, SourceProfile.Multichannel);
        }

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBeEmpty();
        caught.CouldNotRender.ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting somebody asked the application to get rid of is owed nothing, so nothing is
    /// written into a folder on its way out.
    /// </summary>
    [Fact]
    public void A_meeting_on_its_way_out_is_left_alone()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = Transcribed(corpus);

        using (var context = corpus.Open())
        {
            var leaving = context.Meetings.Single(row => row.Id == meeting);
            leaving.LifecycleState = LifecycleState.Deleting;
            leaving.DeletedAt = When;
            context.SaveChanges();
        }

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBeEmpty();

        // Left alone and not attempted: a refusal here would be the meeting being reached for.
        caught.CouldNotRender.ShouldBeEmpty();

        using var reopened = corpus.Open();
        Files(reopened, meeting).ShouldNotContain(ArtifactKind.Transcript);
    }

    /// <summary>
    /// The isolation the whole thing rests on. The sweep runs oldest first and remembers nothing
    /// between launches, so a meeting whose files can never be written has to cost that meeting
    /// and no other — otherwise one of them silently starves every meeting recorded after it, on
    /// every launch, which is the failure this exists to prevent.
    /// </summary>
    [Fact]
    public void A_meeting_whose_files_cannot_be_written_does_not_starve_a_newer_one()
    {
        using var corpus = new TemporaryCorpus();
        var blocked = Transcribed(corpus, DeepgramFixtures.TwoChannelShort, When);
        var newer = Transcribed(
            corpus, DeepgramFixtures.TwoChannelOneVoiceMe, When + Duration.FromMilliseconds(3_600_000));

        // Something standing where the transcript goes, which is what a folder half-synced from
        // elsewhere or a path another program is holding comes to. It is not a RenderException,
        // and that is the point: the isolation is around the meeting, not around one exception.
        Directory.CreateDirectory(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(blocked, "transcript.md")).FullName);

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBe([newer]);
        caught.CouldNotRender.ShouldHaveSingleItem().ShouldContain(blocked.ToString());

        using var context = corpus.Open();
        Files(context, newer).ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances], ignoreOrder: true);

        // And the blocked meeting kept nothing of a render that did not happen: its turns went
        // back with its files, so the next launch starts from where this one found it.
        Files(context, blocked).ShouldBeEmpty();
        context.Utterances.Count(turn => turn.MeetingId == blocked).ShouldBe(0);
    }

    /// <summary>
    /// One meeting is one render. A response that has gone missing costs that meeting its files
    /// and costs the others nothing, which is why each is rendered on its own.
    /// </summary>
    [Fact]
    public void A_meeting_whose_response_is_gone_is_named_and_stops_nothing_else()
    {
        using var corpus = new TemporaryCorpus();
        var lost = Transcribed(corpus, DeepgramFixtures.TwoChannelShort, When);
        var whole = Transcribed(corpus, DeepgramFixtures.TwoChannelOneVoiceMe, When + Duration.FromMilliseconds(3_600_000));

        using (var context = corpus.Open())
        {
            var response = context.Artifacts.Single(artifact =>
                artifact.MeetingId == lost && artifact.Kind == ArtifactKind.DeepgramResponse);
            CorpusFiles.Locate(corpus.Root, response.RelativePath).Delete();
        }

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBe([whole]);
        caught.CouldNotRender.ShouldHaveSingleItem().ShouldContain(lost.ToString());
    }

    /// <summary>
    /// A folder holding no corpus is read and left as it was found. Making one here would put an
    /// empty corpus where somebody's is supposed to be, which is the failure that looks like
    /// nothing being wrong.
    /// </summary>
    [Fact]
    public void A_folder_with_no_corpus_in_it_does_not_get_one()
    {
        using var corpus = new TemporaryCorpus();

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBeEmpty();
        caught.CouldNotRender.ShouldBeEmpty();
        File.Exists(corpus.DatabasePath).ShouldBeFalse();
    }

    /// <summary>
    /// The bytes of both rendered files, read off the disk. Both, because the pair is what a
    /// meeting is read from and a transcript naming turns the jsonl does not have is two different
    /// meetings depending on which file was opened.
    /// </summary>
    private static string[] Rendered(TemporaryCorpus corpus, Guid meeting)
    {
        using var context = corpus.Open();
        var paths = context.Artifacts
            .Where(artifact => artifact.MeetingId == meeting
                && (artifact.Kind == ArtifactKind.Transcript || artifact.Kind == ArtifactKind.Utterances))
            .OrderBy(artifact => artifact.Kind)
            .Select(artifact => artifact.RelativePath)
            .ToArray();

        paths.Length.ShouldBe(2);
        return [.. paths.Select(path => File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName))];
    }

    private static ArtifactKind[] Files(CorpusDbContext context, Guid meeting) => context.Artifacts
        .Where(artifact => artifact.MeetingId == meeting && artifact.Kind != ArtifactKind.DeepgramResponse)
        .Select(artifact => artifact.Kind)
        .ToArray();

    /// <summary>
    /// A meeting whose transcription has arrived and which has nothing rendered from it — the
    /// state the application finds at launch. The context is let go of before the catch-up runs,
    /// so what it reads is what is on disk rather than what a live context is holding.
    /// </summary>
    private static Guid Transcribed(
        TemporaryCorpus corpus,
        string fixture = DeepgramFixtures.TwoChannelOneVoiceMe,
        UtcTimestamp? startedAt = null)
    {
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, DeepgramFixtures.ProfileOf(fixture), startedAt ?? When);

        DurableArtifact.Write(
            context,
            meeting,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting, "deepgram.json"),
            When,
            stream =>
            {
                using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
                response.CopyTo(stream);
            });

        return meeting;
    }

    private static Guid Meeting(CorpusDbContext context, SourceProfile profile, UtcTimestamp? startedAt = null)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAt ?? When,
            SourceProfile = profile,
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();
        return meeting.Id;
    }
}
