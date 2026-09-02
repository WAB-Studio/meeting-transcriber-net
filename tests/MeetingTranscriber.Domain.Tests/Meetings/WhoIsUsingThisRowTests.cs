using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// The row that asks who is using the application, as the three things it decides. It is here and
/// not beside the window because a window is what no build agent can run: the sentence appearing
/// exactly while nobody has answered is the whole of the asking, and nothing else could catch it
/// going away.
/// </summary>
/// <remarks>
/// In this suite rather than <c>Presentation.Tests</c>, following the record itself — the row is
/// about the person the corpus flags as me, and <c>docs/layout.md</c> is where that placement is
/// argued. ISC-166 closed on these same facts while both sat one project over.
/// </remarks>
public class WhoIsUsingThisRowTests
{
    private static readonly WhoIsUsingThisRow Answered = new(
        CorpusIsReachable: true, SomebodyHasSaid: true, Typed: "Ada", BeingKept: false);

    private static readonly WhoIsUsingThisRow Asking = new(
        CorpusIsReachable: true, SomebodyHasSaid: false, Typed: "", BeingKept: false);

    /// <summary>
    /// The question is put once and stops being put. It is the difference between a field with a
    /// label on it and a question, and there is nothing else on the row that says which it is.
    /// </summary>
    [Fact]
    public void The_question_is_on_screen_exactly_while_nobody_has_answered()
    {
        Asking.IsAsking.ShouldBeTrue();
        Answered.IsAsking.ShouldBeFalse();
    }

    /// <summary>
    /// A corpus that was refused leaves nothing on this row live, including the question: asking
    /// somebody for an answer there is nowhere to keep is asking them to press something that
    /// cannot work, and the refusal naming the folder is already on the same screen.
    /// </summary>
    [Fact]
    public void A_corpus_that_cannot_be_reached_leaves_the_whole_row_dead()
    {
        var refused = Asking with { CorpusIsReachable = false, Typed = "Ada" };

        refused.IsAsking.ShouldBeFalse();
        refused.FieldIsLive.ShouldBeFalse();
        refused.MayBeKept.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_answer_is_nothing_to_keep(string blank) =>
        (Asking with { Typed = blank }).MayBeKept.ShouldBeFalse();

    [Fact]
    public void A_name_that_has_been_typed_may_be_kept() =>
        (Asking with { Typed = "Ada" }).MayBeKept.ShouldBeTrue();

    /// <summary>
    /// What is kept is what a citation reads, so the spaces around it go before anybody sees them
    /// in a transcript heading — and the same trim is what decides there is anything to keep, so
    /// the two cannot disagree about a field holding one space.
    /// </summary>
    [Fact]
    public void What_would_be_kept_is_the_name_without_the_spaces_around_it()
    {
        var typed = Asking with { Typed = "  Ada Lovelace  " };

        typed.Name.ShouldBe("Ada Lovelace");
        typed.MayBeKept.ShouldBeTrue();
    }

    /// <summary>
    /// The state the row would not otherwise have. Without it the field stays live under a press
    /// that is still running, a keystroke arms the press again, and two answers race: each finds
    /// nobody has answered and each writes somebody who has, leaving a corpus that cannot say
    /// which of two people is using it.
    /// </summary>
    [Fact]
    public void Nothing_on_the_row_can_be_touched_while_an_answer_is_on_its_way()
    {
        var keeping = Answered with { BeingKept = true };

        keeping.FieldIsLive.ShouldBeFalse();
        keeping.MayBeKept.ShouldBeFalse();
    }

    /// <summary>
    /// The question stays on screen through the press that answers it, and goes when the answer
    /// is in the corpus. A row that stopped asking the moment somebody pressed would be saying the
    /// answer is kept while it is still being written, and a press that failed would leave it
    /// saying so.
    /// </summary>
    [Fact]
    public void A_press_in_flight_has_not_answered_anything_yet() =>
        (Asking with { Typed = "Ada", BeingKept = true }).IsAsking.ShouldBeTrue();

    /// <summary>Before any corpus has been read, there is nothing to press and nothing to ask.</summary>
    [Fact]
    public void A_row_nothing_has_been_read_into_is_dead()
    {
        WhoIsUsingThisRow.Unread.IsAsking.ShouldBeFalse();
        WhoIsUsingThisRow.Unread.FieldIsLive.ShouldBeFalse();
        WhoIsUsingThisRow.Unread.MayBeKept.ShouldBeFalse();
    }
}
