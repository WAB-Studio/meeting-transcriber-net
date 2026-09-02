using System.Diagnostics;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The one question every test that wedges a device asks about what it timed: was that the
/// deadline, or no deadline at all? A loop that will not come back, a loop that will not start, a
/// device that will not answer, a device that refuses and will not be let go of, handles that will
/// not close, a machine that will not say what it has — all of them time a wait against
/// <see cref="CaptureLoop.StopsWithin"/> and all of them mean the same thing by it.
/// </summary>
/// <remarks>
/// <para>
/// Answered here rather than in each of those, because neither end of the bound is about the thing
/// under test: the floor is a judgement about two clocks and the ceiling is a judgement about a
/// loaded build agent. Written out at each site, it was written four ways — three of them with no
/// room at the floor at all — and the one written differently is the one that goes red for nothing.
/// </para>
/// <para>
/// Both answers split the same axis at the same point, and that point is the deadline: what these
/// mechanisms do is come back with an answer or come back at <see cref="CaptureLoop.StopsWithin"/>.
/// So the bound is read off that number rather than picked. A number chosen for how instant it
/// feels is not a bound on any of this code — it is a guess at how quick a build agent is, and it
/// goes red when the guess is wrong rather than when the code is.
/// </para>
/// <para>
/// What a clock is for here is the one thing the outcome cannot say. A wedge and a deadline are
/// answered with the same exception, so which of them happened is only in the timing; and any of
/// these could regress into coming back with the right answer having waited for it anyway, which
/// nothing but a clock notices. What none of it may be pointed at is a stretch that is Windows
/// working rather than this application waiting: a real endpoint enumeration takes as long as the
/// machine takes, so what a bound over one reads is the agent. Every wedge these tests drive is a
/// body the test wrote, and a call that would reach the audio stack for real is either given a
/// question already wedged — refused before Windows is touched — or not made.
/// </para>
/// </remarks>
internal static class Deadlines
{
    /// <summary>
    /// How far under <see cref="CaptureLoop.StopsWithin"/> a bounded wait may measure and still be
    /// that deadline. A wait counts its timeout on the operating system's tick while
    /// <see cref="Stopwatch"/> counts the same stretch on the performance counter, so a wait can
    /// come back a fraction of a millisecond short on the second one.
    /// </summary>
    /// <remarks>
    /// Both kinds of wait do it, and on CI under the full suite rather than only in theory:
    /// <c>Monitor.Wait</c> on the loop's gate measured 4.9993669 s against five seconds, and
    /// <c>Thread.Join</c> on the loop's thread measured 4.9999999 s and then 4.9995301 s. Fifty
    /// milliseconds is far more room than any of those needed, deliberately — the difference
    /// between the two clocks is not what a single one of these tests is about, and it is only ever
    /// a fraction of a millisecond, while what the floor is there to tell apart is five seconds
    /// wide. Room that costs the probe a hundredth of its margin buys a suite that does not go red
    /// for nothing.
    /// <para>
    /// One number and not two, because both answers ask the same question of the same point: which
    /// side of the deadline did this land on. The uncertainty about that point is the skew above,
    /// so a run cannot be told it both waited the deadline and did not.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long a test waits for something it expects to happen anyway, before deciding that the
    /// test itself is what failed.
    /// </summary>
    /// <remarks>
    /// The one number here that asserts nothing. It exists so a signal that never arrives fails
    /// saying so rather than hanging the run, which makes more of it strictly better and no test
    /// poorer for it. Everything waited on with it is a body a test released a line earlier, so
    /// what it really allows is three orders of magnitude over the microseconds any of them needs;
    /// it is <see cref="CaptureLoop.StopsWithin"/> only so that a test is never less patient than
    /// the application it drives. Written out at each site it was a second, which reads as a bound
    /// on the product when it is nothing of the kind.
    /// </remarks>
    internal static readonly TimeSpan Patience = CaptureLoop.StopsWithin;

    /// <summary>
    /// Asserts that a wait somebody timed was the deadline rather than no deadline at all — that
    /// something bounded the wait, and that what bounded it was this number.
    /// </summary>
    /// <remarks>
    /// The ceiling is generous on purpose and the floor is not: a wedged device on a loaded agent
    /// can be answered late, so half as long again is not what this should go red over, but nothing
    /// makes a wait come back early except not having waited.
    /// </remarks>
    /// <param name="waited">What a <see cref="Stopwatch"/> measured across the wait.</param>
    internal static void ShouldHaveWaitedTheDeadline(this TimeSpan waited)
    {
        waited.ShouldBeGreaterThanOrEqualTo(CaptureLoop.StopsWithin - Slack);
        waited.ShouldBeLessThan(CaptureLoop.StopsWithin * 2);
    }

    /// <summary>
    /// Asserts the other half of the same question: that a wait was not the deadline, which is what
    /// every one of those tests measures its wedge against.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="ShouldHaveWaitedTheDeadline"/>'s floor from below, so the two together say
    /// which side of the deadline a measurement fell and nothing finer. That is the whole of what
    /// this can be asked: it separates an answer from a deadline, and it does not separate a quick
    /// answer from a slow one — a bound that did would be a number about the build agent.
    /// </remarks>
    /// <param name="waited">What a <see cref="Stopwatch"/> measured across the wait.</param>
    internal static void ShouldNotHaveSpentTheDeadline(this TimeSpan waited) =>
        waited.ShouldBeLessThan(CaptureLoop.StopsWithin - Slack);

    /// <summary>How long <paramref name="step"/> took, for the tests that assert one of the two.</summary>
    internal static TimeSpan Time(Action step)
    {
        var clock = Stopwatch.StartNew();
        step();
        return clock.Elapsed;
    }
}
