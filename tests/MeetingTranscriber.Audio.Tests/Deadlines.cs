using System.Diagnostics;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The one question every test that wedges a device asks about what it timed: was that the
/// deadline, or no deadline at all? A loop that will not come back, a loop that will not start, a
/// device that will not answer, a device that refuses and will not be let go of, handles that will
/// not close — all of them time a wait against <see cref="CaptureLoop.StopsWithin"/> and all of
/// them mean the same thing by it.
/// </summary>
/// <remarks>
/// Answered here rather than in each of those, because neither end of the bound is about the thing
/// under test: the floor is a judgement about two clocks and the ceiling is a judgement about a
/// loaded build agent. Written out at each site, it was written four ways — three of them with no
/// room at the floor at all — and the one written differently is the one that goes red for nothing.
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
    /// </remarks>
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(50);

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
}
