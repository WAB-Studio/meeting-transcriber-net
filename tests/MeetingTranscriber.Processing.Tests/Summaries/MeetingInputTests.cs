using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Summaries;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// The bytes a provider is given to summarise a meeting: the same meeting prepares the same bytes,
/// a changed word changes the hash, and a meeting with nothing to summarise refuses to prepare.
/// </summary>
public class MeetingInputTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_same_meeting_prepares_the_same_bytes_and_the_same_hash()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, When, ["Hola", "Chau"], responseSha256: new string('a', 64));

        var first = MeetingInput.Prepare(context, meeting);
        var second = MeetingInput.Prepare(context, meeting);

        first.Bytes().ShouldBe(second.Bytes());
        first.Hash.ShouldBe(second.Hash);
    }

    [Fact]
    public void A_turn_whose_words_changed_changes_the_hash()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, When, ["Hola"], responseSha256: new string('a', 64));

        var before = MeetingInput.Prepare(context, meeting).Hash;

        var turn = context.Utterances.Single(row => row.MeetingId == meeting);
        turn.Text = "Adios";
        context.SaveChanges();

        var after = MeetingInput.Prepare(context, meeting).Hash;

        after.ShouldNotBe(before);
    }

    [Fact]
    public void A_meeting_is_prepared_from_its_stored_turns_and_the_response_they_came_from()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var responseSha256 = new string('b', 64);
        var meeting = MeetingRows.Recorded(
            context, When, ["Hola", "Que tal", "Chau"], responseSha256: responseSha256);

        var prepared = MeetingInput.Prepare(context, meeting);

        prepared.MeetingId.ShouldBe(meeting);
        prepared.TranscribedFrom.ShouldBe(responseSha256);
        prepared.Turns.Select(turn => turn.Ordinal).ShouldBe([0, 1, 2]);
        prepared.Turns.Select(turn => turn.Text).ShouldBe(["Hola", "Que tal", "Chau"]);
    }

    [Fact]
    public void A_meeting_with_no_turns_is_not_prepared()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, When, []);

        Should.Throw<InvalidOperationException>(() => MeetingInput.Prepare(context, meeting));
    }
}
