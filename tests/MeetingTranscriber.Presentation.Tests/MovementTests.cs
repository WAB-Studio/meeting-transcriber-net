namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-173.3, on the half that can be run: with Windows asked for no animation, every duration is
/// zero — and every move still has somewhere to arrive, so nothing is lost for standing still.
/// </summary>
/// <remarks>
/// Both answers, not the one the machine running this happens to give. What Windows was asked for
/// arrives as a constructor argument precisely so that a build agent with animations on proves the
/// machine that has them off, and the other way round — which is the difference between obeying
/// that setting and reasoning about obeying it.
/// </remarks>
public class MovementTests
{
    /// <summary>
    /// Everything that moves for a length of time, so a move added to <see cref="Move"/> and not to
    /// <c>docs/design.md</c> §What moves shows up as a failure here rather than as a duration
    /// somebody picked.
    /// </summary>
    public static TheoryData<Move> EveryMove() => [.. Enum.GetValues<Move>()];

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void Every_move_takes_no_time_at_all_when_the_platform_asks_for_none(Move move)
    {
        Asked(false).Milliseconds(move).ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void Every_move_takes_time_when_the_platform_allows_it(Move move)
    {
        // The other half of the pair, and the one that would be missed. A Movement that answered
        // zero to everything would pass the check above with nothing moving on any machine, which
        // is the failure that reads exactly like obeying the setting.
        Asked(true).Milliseconds(move).ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void What_a_move_is_worth_depends_on_nothing_but_the_platform(Move move)
    {
        // Said rather than assumed, because the whole point of taking the answer as an argument is
        // that it is the only thing this reads. A Movement that consulted the machine it was
        // running on would agree with one of these two and disagree with the other, and the suite
        // would go green or red depending on the settings of whoever ran it.
        Asked(true).Milliseconds(move).ShouldNotBe(Asked(false).Milliseconds(move));
    }

    /// <summary>A machine that was asked for animation, or asked for none.</summary>
    private static Movement Asked(bool allowed) => new(ThePlatformAllowsAnimation: allowed);

    [Fact]
    public void The_three_lengths_are_the_ones_the_design_names()
    {
        // `docs/design.md` §What moves. Written out rather than derived, because these are the
        // numbers that page fixes and a test that computed them from the same table they came from
        // would prove only that arithmetic works.
        Asked(true).Milliseconds(Move.AnsweringAPress).ShouldBe(150);
        Asked(true).Milliseconds(Move.EnteringOrLeaving).ShouldBe(250);
        Asked(true).Milliseconds(Move.Travelling).ShouldBe(300);
    }

    [Fact]
    public void A_move_that_is_not_one_of_the_four_is_refused()
    {
        // The table and the enum are one statement. A fifth kind of movement is a decision somebody
        // takes and writes on `docs/design.md`, so a value that reached here without one stops
        // rather than falling through to a length nobody chose.
        Should.Throw<ArgumentOutOfRangeException>(
            () => Asked(true).Milliseconds((Move)(-1)));
    }
}
