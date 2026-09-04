using MeetingTranscriber.Domain.Jobs;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// What a press on the meetings list is called, which is what a redraw finds it again by and what
/// a tool driving the window presses it by.
/// </summary>
/// <remarks>
/// No window here, and none needed: what can go wrong with an id is that two presses share one,
/// that one is not the same twice, or that it does not say what the press buys — and all three are
/// answerable off the strings themselves. Whether the keyboard really comes back to the button is
/// the by-hand probe's, like everything else on this screen that needs a packaged host.
/// </remarks>
public sealed class RowPressesTests
{
    private static readonly Guid AMeeting = Guid.Parse("0a1cf7d4-4c1f-4c69-9b0e-7f1a2b3c4d5e");
    private static readonly Guid AnotherMeeting = Guid.Parse("9e8d7c6b-5a49-4382-b1c0-fedcba987654");

    /// <summary>
    /// Twelve presses one list can draw at once, and twelve ids between them.
    /// </summary>
    /// <remarks>
    /// The meetings' ids and the recordings' are checked together because one list holds both, and
    /// a redraw looking a press up cannot ask which half it came from.
    /// </remarks>
    [Fact]
    public void No_two_presses_on_this_list_share_an_id()
    {
        string[] ids =
        [
            RowPresses.ToOpen(AMeeting),
            RowPresses.ToLeave(AMeeting),
            RowPresses.ToTake(AMeeting, JobKind.Transcribe),
            RowPresses.ToTake(AMeeting, JobKind.Extract),
            RowPresses.ToOpen(AnotherMeeting),
            RowPresses.ToLeave(AnotherMeeting),
            RowPresses.ToTake(AnotherMeeting, JobKind.Transcribe),
            RowPresses.ToTake(AnotherMeeting, JobKind.Extract),
            RowPresses.ToAnswer("a-folder", WaitingAnswer.Keep),
            RowPresses.ToAnswer("a-folder", WaitingAnswer.Discard),
            RowPresses.ToAnswer("another-folder", WaitingAnswer.Keep),
            RowPresses.ToAnswer("another-folder", WaitingAnswer.Discard),
        ];

        ids.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(
            ids.Length,
            "two presses this list can draw at the same time answer to one id, so a redraw would "
            + "give the keyboard back to whichever of them it happened to remember last.");
    }

    /// <summary>
    /// An id is built from what the caller handed over and from nothing else.
    /// </summary>
    /// <remarks>
    /// The mutation this whole card turns on. An id off a counter, a fresh Guid, a timestamp or the
    /// row's position in the list is one the next draw cannot match — and a positional one is
    /// worse than useless, because when a row above leaves it sends the keyboard to another
    /// meeting's button and the next Enter spends money on a meeting nobody chose.
    /// </remarks>
    [Fact]
    public void A_press_has_the_same_id_every_time_the_list_is_drawn()
    {
        RowPresses.ToOpen(AMeeting).ShouldBe(RowPresses.ToOpen(AMeeting));
        RowPresses.ToLeave(AMeeting).ShouldBe(RowPresses.ToLeave(AMeeting));
        RowPresses.ToTake(AMeeting, JobKind.Transcribe)
            .ShouldBe(RowPresses.ToTake(AMeeting, JobKind.Transcribe));
        RowPresses.ToTake(AMeeting, JobKind.Extract)
            .ShouldBe(RowPresses.ToTake(AMeeting, JobKind.Extract));
        RowPresses.ToAnswer("a-folder", WaitingAnswer.Keep)
            .ShouldBe(RowPresses.ToAnswer("a-folder", WaitingAnswer.Keep));
        RowPresses.ToAnswer("a-folder", WaitingAnswer.Discard)
            .ShouldBe(RowPresses.ToAnswer("a-folder", WaitingAnswer.Discard));
    }

    /// <summary>
    /// The press that opens a meeting is still the meeting's own id, exactly.
    /// </summary>
    /// <remarks>
    /// The probe matches an automation id with an ordinal equality and not a contains, so the runs
    /// written down in <c>ISA.md</c> — which press a meeting by this string — go on resolving to
    /// one element now that <c>:leave</c> and <c>:take:…</c> are in the tree beside it. A suffix
    /// here would retarget them silently.
    /// </remarks>
    [Fact]
    public void A_meeting_is_still_opened_by_the_press_the_probe_presses()
    {
        RowPresses.ToOpen(AMeeting).ShouldBe(AMeeting.ToString());
    }

    /// <summary>
    /// The two acts one row can offer are two presses, not one press twice.
    /// </summary>
    /// <remarks>
    /// A meeting whose stage advances between two watch ticks is redrawn with the same row, the
    /// same position and the same standing, and a button whose words have gone from Transcribir to
    /// Resumir. Drop the act from the id and a redraw hands the keyboard to that button, one Enter
    /// away from buying a summary nobody chose.
    /// </remarks>
    [Fact]
    public void The_press_that_buys_the_next_act_is_named_for_the_act_it_buys()
    {
        RowPresses.ToTake(AMeeting, JobKind.Transcribe)
            .ShouldNotBe(RowPresses.ToTake(AMeeting, JobKind.Extract));
    }

    /// <summary>
    /// Both acts a card can offer have an id, and no other kind of work does.
    /// </summary>
    /// <remarks>
    /// The closed set is the one <c>MeetingWords.Action</c> closes over, and the one that must
    /// never join it is <see cref="JobKind.Render"/>: the rendered files cost nothing and can be
    /// made again, so no card offers them and no id names them. An arm that fell through to the
    /// enum member's own spelling would mint one.
    /// </remarks>
    [Fact]
    public void No_act_a_card_can_offer_is_left_out_and_no_other_is_let_in()
    {
        RowPresses.ToTake(AMeeting, JobKind.Transcribe).ShouldNotBeNullOrWhiteSpace();
        RowPresses.ToTake(AMeeting, JobKind.Extract).ShouldNotBeNullOrWhiteSpace();

        foreach (var other in new[]
        {
            JobKind.Capture,
            JobKind.Finalize,
            JobKind.Render,
            JobKind.Backup,
            (JobKind)0,
        })
        {
            Should.Throw<ArgumentOutOfRangeException>(() => RowPresses.ToTake(AMeeting, other));
        }
    }

    /// <summary>
    /// Both answers about a recording have an id, and nothing else does.
    /// </summary>
    [Fact]
    public void An_answer_this_list_does_not_offer_is_not_an_id()
    {
        RowPresses.ToAnswer("a-folder", WaitingAnswer.Keep)
            .ShouldNotBe(RowPresses.ToAnswer("a-folder", WaitingAnswer.Discard));

        RowPresses.ToAnswer("a-folder", WaitingAnswer.Keep).ShouldNotBeNullOrWhiteSpace();
        RowPresses.ToAnswer("a-folder", WaitingAnswer.Discard).ShouldNotBeNullOrWhiteSpace();

        Should.Throw<ArgumentOutOfRangeException>(
            () => RowPresses.ToAnswer("a-folder", (WaitingAnswer)7));
    }

    /// <summary>
    /// A recording with no folder name is refused rather than given an id of almost nothing.
    /// </summary>
    /// <remarks>
    /// An id that is blank, or that is whitespace and a suffix, is one a redraw could match against
    /// a control carrying no id at all — which is every other element in the window.
    /// </remarks>
    [Fact]
    public void A_recording_with_no_folder_is_refused()
    {
        Should.Throw<ArgumentException>(() => RowPresses.ToAnswer(string.Empty, WaitingAnswer.Keep));
        Should.Throw<ArgumentException>(() => RowPresses.ToAnswer("   ", WaitingAnswer.Keep));
        Should.Throw<ArgumentException>(() => RowPresses.ToAnswer(null!, WaitingAnswer.Keep));
    }
}
