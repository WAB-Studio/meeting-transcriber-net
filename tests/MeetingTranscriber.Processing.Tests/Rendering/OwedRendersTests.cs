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
        // elsewhere or a path another program is holding comes to. This one the disk says, and the
        // two below are the ones a response says: together they are why the boundary is the
        // meeting rather than a list of what a render is thought to throw.
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
    /// A response that stops early, which the render path reaches through the parser and no list
    /// of what a render throws had in it. Nothing files a response through the parser — the legacy
    /// importer copies a <c>deepgram.json</c> and hashes it — and the sweep runs oldest first,
    /// which is exactly where an imported meeting sits, so the whole corpus parks behind this one.
    /// </summary>
    [Fact]
    public void A_response_the_parser_cannot_read_does_not_starve_a_newer_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var truncated = Filed(corpus, SourceProfile.Multichannel, When, stream =>
        {
            using var response = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            var head = new byte[4096];
            response.ReadExactly(head);
            stream.Write(head);
        });
        var newer = Transcribed(
            corpus, DeepgramFixtures.TwoChannelOneVoiceMe, When + Duration.FromMilliseconds(3_600_000));

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBe([newer]);

        // The refusal and not only that one came back: the line has to be the parser saying it
        // could not read the response, or the probe would still pass if this arrived as something
        // a list of six would have carried anyway.
        var line = caught.CouldNotRender.ShouldHaveSingleItem();
        line.ShouldContain(truncated.ToString());
        line.ShouldContain("stops early");

        using var context = corpus.Open();
        Files(context, newer).ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances], ignoreOrder: true);
        Files(context, truncated).ShouldBeEmpty();
    }

    /// <summary>
    /// The other one the response says: a single track filed against a meeting recorded on two
    /// channels. It is the audio contract refusing — thrown by the domain, reached through the
    /// parser — and it costs its own meeting the files and the next meeting nothing.
    /// </summary>
    [Fact]
    public void A_response_that_disagrees_with_its_profile_does_not_starve_a_newer_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var mismatched = Filed(corpus, SourceProfile.Multichannel, When, stream =>
        {
            using var response = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.SingleTrackDiarized));
            response.CopyTo(stream);
        });
        var newer = Transcribed(
            corpus, DeepgramFixtures.TwoChannelOneVoiceMe, When + Duration.FromMilliseconds(3_600_000));

        var caught = OwedRenders.CatchUpOn(corpus.Root, TimeProvider.System);

        caught.Rendered.ShouldBe([newer]);

        var line = caught.CouldNotRender.ShouldHaveSingleItem();
        line.ShouldContain(mismatched.ToString());
        line.ShouldContain("needs 2 channel(s), got 1");

        using var context = corpus.Open();
        Files(context, newer).ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances], ignoreOrder: true);
        Files(context, mismatched).ShouldBeEmpty();
    }

    /// <summary>
    /// The rule itself, as against the two live paths above, which only pin the two refusals the
    /// old list happened to miss.
    /// </summary>
    /// <remarks>
    /// A catch-up absorbs what it was never going to be able to name in advance, so the probe has
    /// to arrive as a type nothing in this system throws and no list would ever have carried. The
    /// clock is the way in because it is the one collaborator a caller hands over, and it stands
    /// for every refusal the render path has not learnt yet: narrow the boundary back to a list —
    /// six entries or eight — and this goes red where the two above would stay green.
    /// </remarks>
    [Fact]
    public void A_refusal_no_list_would_have_carried_comes_back_as_an_answer()
    {
        using var corpus = new TemporaryCorpus();
        Transcribed(corpus);

        var caught = OwedRenders.CatchUpOn(corpus.Root, new BrokenClock());

        caught.Rendered.ShouldBeEmpty();
        caught.CouldNotRender.ShouldHaveSingleItem().ShouldContain("no clock here");
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
        UtcTimestamp? startedAt = null) =>
        Filed(corpus, DeepgramFixtures.ProfileOf(fixture), startedAt ?? When, stream =>
        {
            using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
            response.CopyTo(stream);
        });

    /// <summary>
    /// A meeting with a response filed against it — whatever bytes the caller writes, under
    /// whatever profile the meeting was recorded on.
    /// </summary>
    /// <remarks>
    /// The two are separate arguments and not one fixture on purpose. Nothing checks that a filed
    /// response can be read or that it agrees with the meeting it is filed against, so a real
    /// corpus holds pairs that do not: <c>tools/MeetingTranscriber.CorpusImport</c> files a
    /// <c>deepgram.json</c> on its sha256 and never opens it. A fixture-only helper could not put
    /// the corpus into the state the sweep actually meets.
    /// </remarks>
    private static Guid Filed(
        TemporaryCorpus corpus,
        SourceProfile profile,
        UtcTimestamp startedAt,
        Action<Stream> response)
    {
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, profile, startedAt);

        DurableArtifact.Write(
            context,
            meeting,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting, "deepgram.json"),
            When,
            response);

        return meeting;
    }

    /// <summary>
    /// A clock that refuses, with a refusal of a type nothing on the render path throws. It is not
    /// a scenario anybody meets — it is the only way in from outside to a boundary whose whole
    /// point is that what crosses it cannot be listed.
    /// </summary>
    private sealed class BrokenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            throw new NotSupportedException("There is no clock here.");
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
