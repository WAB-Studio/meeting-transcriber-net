using System.Text.Json;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;

namespace MeetingTranscriber.Processing.Tests.Intake;

/// <summary>
/// The card that says which meeting a folder holds when there is no database left to ask.
/// </summary>
public class MeetingManifestTests
{
    private const string Fixture = DeepgramFixtures.TwoChannelShort;

    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Filing_a_response_leaves_a_card_recorded_as_a_source()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var received = Receive(context, corpus.Root);

        received.Manifest.Kind.ShouldBe(ArtifactKind.Manifest);
        received.Manifest.Origin.ShouldBe(ArtifactOrigin.Source);
        received.Manifest.RelativePath.ShouldBe(
            $"{CorpusFiles.Meetings}/{received.MeetingId}/{MeetingManifest.FileName}");
        CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// The claim the card exists for, probed the only way that means anything: the corpus is
    /// closed and deleted first, so nothing but the one file is left to answer from.
    /// </summary>
    [Fact]
    public void A_meeting_is_recognised_from_its_card_with_nothing_else_left()
    {
        var kept = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}-{MeetingManifest.FileName}");
        Guid meetingId;

        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var received = Receive(context, corpus.Root);
            meetingId = received.MeetingId;
            File.Copy(CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath).FullName, kept);
        }

        try
        {
            var card = MeetingManifest.Read(new FileInfo(kept));

            card.MeetingId.ShouldBe(meetingId);
            card.StartedAt.ShouldBe(When);
            card.Profile.ShouldBe(DeepgramFixtures.ProfileOf(Fixture));
            card.Language.ShouldBe("es");
            card.Title.ShouldBe("la del presupuesto");
        }
        finally
        {
            File.Delete(kept);
        }
    }

    /// <summary>
    /// A person is going to open this in a text editor with nothing else to hand, which is the
    /// whole situation it is for. So the names it uses are the names the corpus uses, spelled out
    /// here rather than left to whatever a serializer decided.
    /// </summary>
    [Fact]
    public void The_card_is_written_in_the_names_the_rest_of_the_corpus_uses()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var received = Receive(context, corpus.Root);

        using var card = JsonDocument.Parse(
            File.ReadAllText(CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath).FullName));

        card.RootElement.EnumerateObject().Select(field => field.Name)
            .ShouldBe(["meeting", "started_at", "source_profile", "language", "title"]);
        card.RootElement.GetProperty("meeting").GetString().ShouldBe(received.MeetingId.ToString());
        card.RootElement.GetProperty("started_at").GetString().ShouldBe(When.ToStorage());
        card.RootElement.GetProperty("source_profile").GetString()
            .ShouldBe(WireNames<SourceProfile>.Of(DeepgramFixtures.ProfileOf(Fixture)));
        card.RootElement.GetProperty("language").GetString().ShouldBe("es");
    }

    /// <summary>
    /// Filing again is what a person retrying a command does, and the card is the artifact that
    /// most has to survive it. It comes back saying what the corpus says now — not what it said
    /// the first time, and not an exception about a source that may not be rewritten.
    /// </summary>
    [Fact]
    public void Filing_again_writes_the_card_as_the_corpus_now_says_it_is()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, corpus.Root);

        // The one field of the five a person moves after the fact.
        var meeting = context.Meetings.Single();
        meeting.Title = "la del presupuesto, con Ana";
        context.SaveChanges();

        var again = Receive(context, corpus.Root);

        again.WasAlreadyThere.ShouldBeTrue();
        again.Manifest.Id.ShouldBe(first.Manifest.Id);
        context.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.Manifest).ShouldBe(1);
        Card(corpus, again).Title.ShouldBe("la del presupuesto, con Ana");
    }

    /// <summary>
    /// The other half of being allowed to write it twice: a folder somebody emptied, or a write
    /// that never happened, is not a meeting condemned to be unrecognisable for good.
    /// </summary>
    [Fact]
    public void A_card_that_is_gone_comes_back_when_the_response_is_filed_again()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, corpus.Root);
        CorpusFiles.Locate(corpus.Root, first.Manifest.RelativePath).Delete();

        var again = Receive(context, corpus.Root);

        CorpusFiles.Locate(corpus.Root, again.Manifest.RelativePath).Exists.ShouldBeTrue();
        Card(corpus, again).MeetingId.ShouldBe(first.MeetingId);
    }

    /// <summary>
    /// Nothing is left to check a card against, so one that answers four of the five questions is
    /// refused rather than returned with a gap somebody would act on.
    /// </summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("started_at")]
    [InlineData("source_profile")]
    [InlineData("language")]
    public void A_card_missing_a_field_says_which_one_instead_of_answering(string field)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var received = Receive(context, corpus.Root);
        var file = CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath);

        var card = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file.FullName))!;
        card.Remove(field);
        File.WriteAllText(file.FullName, JsonSerializer.Serialize(card));

        var refused = Should.Throw<ManifestException>(() => MeetingManifest.Read(file));

        refused.Message.ShouldContain(field);
        refused.Message.ShouldContain(file.FullName);
    }

    /// <summary>
    /// The title is the one field a meeting is allowed not to have, so its absence is a card
    /// without a name on it rather than a card that will not be read.
    /// </summary>
    /// <remarks>
    /// The shape is asserted and not only the round trip. An untitled meeting whose card simply
    /// left the key out would be a second shape of this file, and whatever somebody writes to read
    /// these — the point being that it is not this application — would meet it on the one meeting
    /// nobody got round to naming.
    /// </remarks>
    [Fact]
    public void A_meeting_nobody_has_titled_gets_a_card_with_the_same_five_keys()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var received = Receive(context, corpus.Root, title: null);

        Card(corpus, received).Title.ShouldBeNull();

        using var card = JsonDocument.Parse(
            File.ReadAllText(CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath).FullName));
        card.RootElement.EnumerateObject().Select(field => field.Name)
            .ShouldBe(["meeting", "started_at", "source_profile", "language", "title"]);
        card.RootElement.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void A_file_that_is_not_a_card_at_all_says_so_rather_than_throwing_a_parser_error()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var received = Receive(context, corpus.Root);
        var file = CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath);
        File.WriteAllText(file.FullName, "this folder holds the budget meeting, I think");

        var refused = Should.Throw<ManifestException>(() => MeetingManifest.Read(file));

        refused.Message.ShouldContain(file.FullName);
    }

    private static MeetingCard Card(TemporaryCorpus corpus, ReceivedMeeting received) =>
        MeetingManifest.Read(CorpusFiles.Locate(corpus.Root, received.Manifest.RelativePath));

    private static ReceivedMeeting Receive(
        CorpusDbContext context,
        DirectoryInfo root,
        string? title = "la del presupuesto") => MeetingIntake.Receive(
            context,
            root,
            new FileInfo(DeepgramFixtures.PathOf(Fixture)),
            new MeetingDetails(When, DeepgramFixtures.ProfileOf(Fixture), "es", title),
            When);
}
