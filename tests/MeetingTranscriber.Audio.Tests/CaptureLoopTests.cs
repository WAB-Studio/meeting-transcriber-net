using System.Diagnostics;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The thread that drains a source, driven by loop bodies doing what no device on a build agent
/// can be asked to do: refuse to come back. That refusal is the whole subject — a device that
/// stops when it is told is the case every other test in this suite already records.
/// </summary>
public class CaptureLoopTests
{
    /// <summary>How long a loop that does end is given before the test itself is the failure.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The ordinary way out, and the baseline the rest of this class is measured against: a body
    /// that reads what it was asked and returns, waited for and not given up on.
    /// </summary>
    [Fact]
    public void A_loop_that_stops_when_it_is_asked_is_waited_for()
    {
        using var draining = new ManualResetEventSlim(initialState: false);
        var loop = CaptureLoop.Draining("stops when asked", running =>
        {
            while (running.Running)
            {
                draining.Set();
                Thread.Sleep(1);
            }
        });

        // Waited for rather than assumed, and the wait is the assertion: a loop disposed before its
        // thread took a pass would end promptly whatever the body read, so the baseline would pass
        // over a loop that never looked at what it was asked.
        draining.Wait(Promptly, TestContext.Current.CancellationToken).ShouldBeTrue();

        var waited = Time(loop.Dispose);

        loop.Abandoned.ShouldBeFalse();
        loop.Running.ShouldBeFalse();
        waited.ShouldBeLessThan(Promptly);
    }

    /// <summary>
    /// ISC-128 and ISC-131. The loop ignores what it was asked, which is what a driver wedged
    /// inside WASAPI looks like from out here. Waiting comes back anyway, near the deadline rather
    /// than at some multiple of it, and says the loop was given up on — and the loop is still
    /// running when it says so, which is the whole content of the word: what it holds is being used
    /// by a live thread and is not anybody else's to close.
    /// </summary>
    /// <remarks>
    /// The five seconds this costs are the deadline itself. A loop built with a shorter one for the
    /// test's sake would leave the number the product actually waits proved by nothing.
    /// </remarks>
    [Fact]
    public void A_loop_that_will_not_come_back_is_given_up_on_rather_than_waited_on()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);
        var cameBack = false;

        var loop = CaptureLoop.Draining("will not come back", _ =>
        {
            // Uncancellable on purpose: this is standing in for a thread inside a driver, and one
            // that could be asked to come back would be standing in for nothing.
            stuck.Wait(Timeout.Infinite);
            cameBack = true;
        });

        try
        {
            var waited = Time(loop.Dispose);

            waited.ShouldBeGreaterThanOrEqualTo(CaptureLoop.StopsWithin);
            waited.ShouldBeLessThan(CaptureLoop.StopsWithin + Promptly);
            loop.Abandoned.ShouldBeTrue();

            // Still in there. Nothing it touches has become free to close because waiting gave up.
            cameBack.ShouldBeFalse();

            // And nobody waits for it again. Three holders let go in sequence on the way out of a
            // recording, so a second deadline each would turn one wedged device into a shutdown
            // nobody sits through.
            Time(loop.Dispose).ShouldBeLessThan(Promptly);
            loop.Abandoned.ShouldBeTrue();
        }
        finally
        {
            stuck.Set();
        }
    }

    private static TimeSpan Time(Action step)
    {
        var clock = Stopwatch.StartNew();
        step();
        return clock.Elapsed;
    }
}
