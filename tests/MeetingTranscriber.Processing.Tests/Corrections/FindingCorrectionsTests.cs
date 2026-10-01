using System.Text.Json;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Corrections;
using MeetingTranscriber.Processing.Intake;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Tests.Corrections;

/// <summary>
/// Finding what a person may want to correct in a corpus that holds real responses, and finding it
/// without anything that summarises.
/// </summary>
public class FindingCorrectionsTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp Later =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_word_typed_against_a_filed_meeting_comes_back_as_the_turns_wrote_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Receive(context, DeepgramFixtures.TwoChannelShort, When);

        var texts = context.Utterances.AsNoTracking().Select(turn => turn.Text).ToList();

        // A word the turns write both capitalised and not: counting forms case-sensitively would
        // split it in two.
        var word = texts
            .SelectMany(Spellings.WordsOf)
            .Where(candidate => candidate.Length >= 6 && candidate.All(char.IsAsciiLetter))
            .GroupBy(candidate => candidate.ToLowerInvariant())
            .First(group => group.Distinct().Count() > 1)
            .Key;
        var typed = word[..^1] + (word[^1] is 'z' ? 'q' : 'z');

        var expected = texts
            .SelectMany(Spellings.WordsOf)
            .Count(candidate => candidate.Equals(word, StringComparison.OrdinalIgnoreCase));

        var forms = FindingCorrections.LikeTyped(context, typed);

        forms.Single(form => form.Text == word).Times.ShouldBe(expected);
    }

    [Fact]
    public void A_meeting_on_its_way_out_is_not_read()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var received = Receive(context, DeepgramFixtures.TwoChannelShort, When);
        var word = context.Utterances.AsNoTracking().Select(turn => turn.Text).ToList()
            .SelectMany(Spellings.WordsOf)
            .First(candidate => candidate.Length >= 6 && candidate.All(char.IsAsciiLetter));
        var typed = word[..^1] + (word[^1] is 'z' ? 'q' : 'z');
        FindingCorrections.LikeTyped(context, typed).ShouldNotBeEmpty();

        // Both, because a CHECK holds the state and the instant together.
        var onItsWayOut = context.Meetings.Single(meeting => meeting.Id == received.MeetingId);
        onItsWayOut.LifecycleState = LifecycleState.Deleting;
        onItsWayOut.DeletedAt = Later;
        context.SaveChanges();

        FindingCorrections.LikeTyped(context, typed).ShouldBeEmpty();
        FindingCorrections.Heard(context).Words.ShouldBeEmpty();
    }

    [Fact]
    public void Every_word_the_response_the_turns_came_from_holds_is_heard()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
        var first = MeetingIntake.ReceiveInto(
            context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
        MeetingIntake.ReceiveAgainInto(
            context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelLong)), Later);

        // The second filing renders, which moves turn_sources to it. This stands for the turns
        // rendered from version 1 while version 2 waits on its render: the row names the response
        // the turns on disk came from, and that is the one that has to be read.
        context.TurnSources.Single().ResponseArtifactId = first.Response.Id;
        context.SaveChanges();

        var heard = FindingCorrections.Heard(context);

        heard.Unread.ShouldBeEmpty();
        heard.Words.Sum(word => word.Times).ShouldBe(PiecesIn(DeepgramFixtures.TwoChannelShort));
    }

    [Fact]
    public void One_meeting_is_heard_from_its_own_response_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Receive(context, DeepgramFixtures.TwoChannelShort, When);
        Receive(context, DeepgramFixtures.TwoChannelLong, Later);

        FindingCorrections.HeardIn(context, first.MeetingId).Sum(word => word.Times)
            .ShouldBe(PiecesIn(DeepgramFixtures.TwoChannelShort));
    }

    [Fact]
    public void A_meeting_whose_response_cannot_be_read_is_named_and_the_rest_are_still_read()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var lost = Receive(context, DeepgramFixtures.TwoChannelShort, When);
        Receive(context, DeepgramFixtures.TwoChannelLong, Later);
        CorpusFiles.Locate(corpus.Root, lost.Response.RelativePath).Delete();

        var heard = FindingCorrections.Heard(context);

        heard.Unread.ShouldBe([lost.MeetingId]);
        heard.Words.Sum(word => word.Times).ShouldBe(PiecesIn(DeepgramFixtures.TwoChannelLong));
        FindingCorrections.UnpromptedIn(context, lost.MeetingId).Unread.ShouldBe([lost.MeetingId]);
    }

    [Fact]
    public void A_meeting_the_corpus_does_not_hold_is_refused_by_name()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var missing = Guid.NewGuid();

        Should.Throw<MeetingStageException>(() => FindingCorrections.UnpromptedIn(context, missing))
            .Message.ShouldContain(missing.ToString());
    }

    [Fact]
    public void Finding_candidates_reaches_nothing_that_summarises()
    {
        var files = RepositoryTree.SourceUnder(new DirectoryInfo(
                Path.Combine(RepositoryTree.Src.FullName, "MeetingTranscriber.Processing", "Corrections")))
            .Append(RepositoryTree.At("src/MeetingTranscriber.Domain/Knowledge/Spellings.cs"))
            .Append(RepositoryTree.At("src/MeetingTranscriber.Domain/Knowledge/MisspelledWords.cs"))
            .ToList();

        files.Count.ShouldBeGreaterThanOrEqualTo(3);
        foreach (var file in files)
        {
            var code = SourceText.WithoutProse(file);
            code.ShouldNotContain("Summaries", customMessage: file.Name);
            code.ShouldNotContain("ClaudeCode", customMessage: file.Name);
        }
    }

    /// <summary>How many pieces a fixture's words come to, counted straight from the JSON.</summary>
    private static int PiecesIn(string fixture)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DeepgramFixtures.PathOf(fixture)));
        return document.RootElement.GetProperty("results").GetProperty("utterances")
            .EnumerateArray()
            .SelectMany(utterance => utterance.GetProperty("words").EnumerateArray())
            .Sum(word => Spellings.WordsOf(
                (word.TryGetProperty("punctuated_word", out var written) ? written : word.GetProperty("word"))
                    .GetString()!).Count);
    }

    private static ReceivedMeeting Receive(CorpusDbContext context, string fixture, UtcTimestamp when) =>
        MeetingIntake.Receive(
            context,
            new FileInfo(DeepgramFixtures.PathOf(fixture)),
            new MeetingDetails(when, DeepgramFixtures.ProfileOf(fixture), "es", fixture),
            when);
}
