using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// What a whole corpus was left with, as opposed to one meeting: every decision, every action,
/// every open question, out of the one extraction of each meeting that counts.
/// </summary>
/// <remarks>
/// The corpus-wide plural of what <c>MeetingReadingTests</c> holds for one meeting, and the facts
/// here are the ones only a second meeting can show: which run answers when a corpus holds several,
/// what a window over time does and does not take in, and that a meeting on its way out says
/// nothing.
/// </remarks>
public class CorpusStatementsTests
{
    private static readonly UtcTimestamp August =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp July =
        UtcTimestamp.From(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Only the extraction a person accepted last answers.
    /// </summary>
    /// <remarks>
    /// Red the moment <c>CorpusSearch.TheRunThatCounts</c> is dropped for a plain join, which does
    /// not fail — it answers, twice, with the same decision worded two ways and nothing on either
    /// to say which is current. That is the failure this narrowing exists against, and it is the
    /// only reason this type is in <c>Infrastructure</c> rather than beside its caller.
    /// </remarks>
    [Fact]
    public void Only_the_extraction_a_person_accepted_last_answers()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "the first go");
        MeetingRows.Extracted(
            context,
            meeting,
            August,
            accepted: UtcTimestamp.From(August.Value.AddHours(1)),
            "the second go");

        var settled = CorpusStatements.Of(context, LeftKind.Decision, null, null, 20);

        settled.Count.ShouldBe(1);
        settled[0].Says.ShouldBe("the second go");
        settled[0].MeetingId.ShouldBe(meeting);
        settled[0].StartedAt.ShouldBe(August);
        settled[0].TurnOrdinal.ShouldBe(0);
        settled[0].At.ShouldBe(MeetingRows.At(0));
        settled[0].SpeakerLabel.ShouldBe(MeetingRows.SpeakerLabel);
        settled[0].Quoted.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Every statement carries the artifact its quotation was read out of, off the citation's own
    /// column and never off the meeting.
    /// </summary>
    /// <remarks>
    /// A meeting can hold more than one paid response — re-transcribing files a new one and never
    /// replaces the old — so <em>the meeting's response</em> is not an answer to <em>what was this
    /// sentence quoted out of</em>. The column exists for that, and this is the reader that hands
    /// it to somebody who will check the quotation against it.
    /// </remarks>
    [Fact]
    public void A_statement_says_what_its_quotation_was_read_out_of()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Single()
            .SourceSha256
            .ShouldBe(MeetingRows.QuotedFromSha256);
    }

    /// <summary>
    /// A run nobody accepted is not read at all, because acceptance is what says a person looked at
    /// what the model wrote and let it into the corpus.
    /// </summary>
    [Fact]
    public void An_extraction_nobody_accepted_says_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: null, "never accepted");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting on its way out says nothing, for the reason every search branch gives: it is being
    /// deleted, and what it decided will not be there when somebody opens it.
    /// </summary>
    [Fact]
    public void A_meeting_on_its_way_out_says_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, August);

        MeetingRows.Extracted(context, meeting, August, accepted: August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).Count.ShouldBe(1);

        var onItsWayOut = context.Meetings.Single(row => row.Id == meeting);
        onItsWayOut.LifecycleState = LifecycleState.Deleting;

        // Both, because the corpus refuses one without the other: a CHECK holds the state and the
        // instant together, so a meeting on its way out always says when it started going.
        onItsWayOut.DeletedAt = August;
        context.SaveChanges();

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20).ShouldBeEmpty();
    }

    /// <summary>
    /// The window is on the meeting's own start, closed at the bottom and open at the top.
    /// </summary>
    /// <remarks>
    /// Two calls over adjoining stretches answer exactly what one call over both would have, which
    /// is what makes a listing pageable by time. It is on the meeting and not on when a model wrote
    /// the sentence, because what somebody asking for <em>the decisions of August</em> means is the
    /// meetings of August — a meeting re-summarised in September is still August's.
    /// </remarks>
    [Fact]
    public void The_window_is_the_meetings_own_start_and_takes_neither_end_twice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, July, "july");
        Extracted(context, August, "august");

        var wholeOfAugust = CorpusStatements.Of(
            context,
            LeftKind.Decision,
            UtcTimestamp.From(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            UtcTimestamp.From(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
            20);

        wholeOfAugust.Select(statement => statement.Says).ShouldBe(["august"]);

        // The meeting's own instant as the top of the window leaves it out, and as the bottom takes
        // it in — which is the pair that makes two adjoining calls one answer.
        CorpusStatements.Of(context, LeftKind.Decision, null, August, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["july"]);

        CorpusStatements.Of(context, LeftKind.Decision, August, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["august"]);
    }

    /// <summary>
    /// Newest meeting first, and bounded by what the caller asked for.
    /// </summary>
    [Fact]
    public void The_newest_meeting_answers_first_and_the_limit_is_kept()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, July, "july");
        Extracted(context, August, "august");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["august", "july"]);

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 1)
            .Select(statement => statement.Says)
            .ShouldBe(["august"]);
    }

    /// <summary>
    /// Each of the three sections answers about itself and never about another, which is the whole
    /// of what the table name decides.
    /// </summary>
    /// <remarks>
    /// An open question is stored under a column of a different name, so a listing that reached for
    /// <c>statement</c> on every table would fail on exactly one of the three — and it is the one
    /// nothing else in this repository lists.
    /// </remarks>
    [Fact]
    public void Every_section_answers_about_itself()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Extracted(context, August, "what was settled");

        CorpusStatements.Of(context, LeftKind.Decision, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled"]);

        CorpusStatements.Of(context, LeftKind.Action, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled, to do"]);

        CorpusStatements.Of(context, LeftKind.Question, null, null, 20)
            .Select(statement => statement.Says)
            .ShouldBe(["what was settled, unresolved"]);
    }

    /// <summary>One meeting with one turn to cite, which is all any fact here reads.</summary>
    private static Guid Recorded(CorpusDbContext context, UtcTimestamp when) =>
        MeetingRows.Recorded(context, when, ["lo que se dijo"]);

    /// <summary>A meeting recorded and summarised in one go, for the facts about two of them.</summary>
    private static void Extracted(CorpusDbContext context, UtcTimestamp when, string saying) =>
        MeetingRows.Extracted(context, Recorded(context, when), when, accepted: when, saying);
}
