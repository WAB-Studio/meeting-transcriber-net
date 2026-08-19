using System.Diagnostics;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The thread that works a source — draining it, or letting go of it — driven by bodies doing what
/// no device on a build agent can be asked to do: refuse to answer. That refusal is the whole
/// subject, at each of the three moments it can happen — the device will not start, will not stop,
/// or will not be let go of. A device that answers is the case every other test in this suite
/// already records.
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
            running.Underway();

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
    /// ISC-128. The loop ignores what it was asked, which is what a driver wedged inside WASAPI
    /// looks like from out here. Waiting comes back anyway, at the deadline rather than never, and
    /// says the loop was given up on — and the loop is still running when it says so, which is the
    /// whole content of the word: what it holds is being used by a live thread and is not anybody
    /// else's to close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The five seconds this costs are the deadline itself. A loop built with a shorter one for the
    /// test's sake would leave the number the product actually waits proved by nothing.
    /// </para>
    /// <para>
    /// What it does not reach is what the stream and the source then keep hold of, which is ISC-131
    /// and is open: that decision is two conditionals over a device, and forcing it needs a real one
    /// wedged on demand.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_loop_that_will_not_come_back_is_given_up_on_rather_than_waited_on()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);
        var cameBack = false;

        var loop = CaptureLoop.Draining("will not come back", running =>
        {
            // Underway first, so what this measures is still the deadline on the way out. A device
            // that wedges without ever having started is the other failure and the test below it.
            running.Underway();

            // Uncancellable on purpose: this is standing in for a thread inside a driver, and one
            // that could be asked to come back would be standing in for nothing.
            stuck.Wait(Timeout.Infinite);
            cameBack = true;
        });

        try
        {
            var waited = Time(loop.Dispose);

            // It still fails the unbounded wait this replaced, which never came back at all.
            waited.ShouldHaveWaitedTheDeadline();
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

    /// <summary>
    /// ISC-137. The body never says it is underway, which is what a driver wedged inside the call
    /// that starts a device looks like from out here — the thread that starts it is the thread that
    /// would then drain it. Starting comes back anyway, at the deadline, and says it was given up
    /// on rather than handing back a stream whose device may or may not be running.
    /// </summary>
    /// <remarks>
    /// The other half of the claim is that nothing is released over it, and it is held by the loop
    /// being abandoned rather than by a second assertion: abandoned is the one word every holder
    /// reads before letting go of anything, and the test below it holds that a second wait over the
    /// same thread costs nothing.
    /// </remarks>
    [Fact]
    public void A_loop_that_never_gets_underway_is_given_up_on_rather_than_waited_on()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);
        var underway = false;

        try
        {
            var clock = Stopwatch.StartNew();
            var loop = CaptureLoop.Draining("will not start", running =>
            {
                // Wedged before saying anything, and never asked to come back — a driver inside
                // the call that starts a device is not something that reads a flag.
                stuck.Wait(Timeout.Infinite);
                running.Underway();
                underway = true;
            });
            var waited = clock.Elapsed;

            waited.ShouldHaveWaitedTheDeadline();
            loop.Abandoned.ShouldBeTrue();

            // Still in there, so nothing it holds became free because starting gave up on it. And
            // whoever lets go of it next does not spend the deadline over again.
            underway.ShouldBeFalse();
            Time(loop.Dispose).ShouldBeLessThan(Promptly);
            loop.Abandoned.ShouldBeTrue();
        }
        finally
        {
            stuck.Set();
        }
    }

    /// <summary>
    /// The baseline for the gate above: a body that says it is underway is waited for and no
    /// longer, so what the deadline tells apart is a device that started from one that never did —
    /// and not every recording from a fast one.
    /// </summary>
    [Fact]
    public void A_loop_that_says_it_is_underway_is_waited_for_and_no_longer()
    {
        var loop = CaptureLoop.Draining("starts", running =>
        {
            running.Underway();

            while (running.Running)
            {
                Thread.Sleep(1);
            }
        });

        try
        {
            loop.Abandoned.ShouldBeFalse();
        }
        finally
        {
            loop.Dispose();
        }
    }

    /// <summary>
    /// A loop given up on and then answering carries on into the rest of its body, which is the
    /// half of the word that decides what every holder may close: abandoned says a thread is still
    /// in there, never that it has stopped being able to reach what it holds. A cleanup that read
    /// it the other way closed a spool under this thread and deleted its file.
    /// </summary>
    [Fact]
    public void A_loop_given_up_on_still_runs_its_body_when_the_device_answers_late()
    {
        using var late = new ManualResetEventSlim(initialState: false);
        using var drained = new ManualResetEventSlim(initialState: false);

        var loop = CaptureLoop.Draining("starts late", running =>
        {
            late.Wait(Timeout.Infinite);
            running.Underway();

            // What the real body does next, and the whole point: it drains through the callback it
            // was handed, whatever anybody decided about it while it was not answering.
            drained.Set();
        });

        try
        {
            loop.Abandoned.ShouldBeTrue();

            late.Set();

            drained.Wait(Promptly, TestContext.Current.CancellationToken).ShouldBeTrue(
                "a device that answers after the deadline runs the rest of its loop, so nothing it "
                + "touches was ever anybody else's to close");
        }
        finally
        {
            late.Set();
            loop.Dispose();
        }
    }

    private static TimeSpan Time(Action step)
    {
        var clock = Stopwatch.StartNew();
        step();
        return clock.Elapsed;
    }
}
