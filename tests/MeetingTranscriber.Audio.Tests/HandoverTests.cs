namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// A replacement device that dies in the window between being started and being handed the
/// channel, driven every way round — and the move that replaced it, which reports nothing.
/// </summary>
/// <remarks>
/// The window is a few milliseconds wide on a real move and nothing here can make a driver die
/// inside it, which is exactly why the ordering is a type of its own: what has to be true is that
/// the end is reported once whichever thread got there first, and never by a move the channel has
/// already left. That is calls and no device. What it is worth is in <see cref="Handover"/> — a
/// channel that says it ended rather than one silent with every reading green, and on the other
/// side a healthy channel that is not moved onto a new device every two seconds for the rest of
/// the meeting.
/// </remarks>
public class HandoverTests
{
    private static readonly InvalidOperationException Died = new("the replacement stopped answering");

    /// <summary>
    /// ISC-78. The ordinary shape of the defect: the replacement's loop ends while the thread
    /// handing over is still writing the folder's changes, so nothing is listening yet. The end
    /// waits, and the thread that hands over reports it the moment the channel is really on that
    /// stream.
    /// </summary>
    [Fact]
    public void An_end_that_arrives_before_the_channel_is_reported_when_it_takes_over()
    {
        var reported = new List<Exception?>();
        var handover = new Handover(reported.Add);

        handover.Ended(Died);

        // Nobody has been told yet, and that is right: the channel is still on the old stream, so
        // the source has not ended — the old device is still the one being recorded. A move that
        // then throws leaves it here, which is the folder having refused the line and the
        // replacement having been let go of.
        reported.ShouldBeEmpty();

        handover.TookOver();

        reported.ShouldHaveSingleItem().ShouldBeSameAs(Died);
    }

    /// <summary>
    /// The other way round, which is the same instant read from the other thread: the channel is
    /// handed over and the loop's end lands after it. Then the capture thread is the second
    /// arrival and reports it itself, rather than storing it for a handover that is already done.
    /// </summary>
    [Fact]
    public void An_end_that_arrives_after_the_channel_took_over_is_reported_there_and_then()
    {
        var reported = new List<Exception?>();
        var handover = new Handover(reported.Add);

        handover.TookOver();

        // A move that worked and whose device is recording: nothing to report, and reporting here
        // would stop a meeting that is running.
        reported.ShouldBeEmpty();

        handover.Ended(Died);

        reported.ShouldHaveSingleItem().ShouldBeSameAs(Died);
    }

    /// <summary>
    /// The second device change on one channel. The stream the first move brought in is stopped by
    /// the second move, so its loop ends and its callback runs — with the first move's handover,
    /// which took over long ago. Reporting there would say a healthy channel had ended, and on the
    /// microphone that is a channel followed onto another device every two seconds until the
    /// meeting stops.
    /// </summary>
    [Fact]
    public void A_move_the_channel_has_already_left_reports_nothing()
    {
        var reported = new List<Exception?>();
        var first = new Handover(reported.Add);

        first.TookOver();

        // What the move after it does, before it starts anything of its own.
        first.Retire();

        first.Ended(stopped: null);

        reported.ShouldBeEmpty();
    }

    /// <summary>
    /// Retiring is not a way to lose an end that was already waiting: a replacement that died
    /// before the channel reached it, on a move that then never completed, was never this source
    /// ending in the first place — and one that did complete has already been reported.
    /// </summary>
    [Fact]
    public void Retiring_a_move_that_took_over_and_then_reported_changes_nothing()
    {
        var reported = new List<Exception?>();
        var handover = new Handover(reported.Add);

        handover.TookOver();
        handover.Ended(Died);
        handover.Retire();

        reported.ShouldHaveSingleItem().ShouldBeSameAs(Died);
    }

    /// <summary>
    /// Once, whichever way round. A stream given up on and answering late reaches the callback
    /// again, and a second end reported over a channel that has moved on is a recording stopped
    /// twice for one device.
    /// </summary>
    [Fact]
    public void The_end_is_reported_exactly_once()
    {
        var reported = new List<Exception?>();
        var handover = new Handover(reported.Add);

        handover.Ended(Died);
        handover.Ended(new InvalidOperationException("and again"));
        handover.TookOver();
        handover.TookOver();
        handover.Ended(new InvalidOperationException("late"));

        reported.ShouldHaveSingleItem().ShouldBeSameAs(Died);
    }

    /// <summary>
    /// A stream asked to stop ends without a reason, and that is still an end. The nothing has to
    /// survive the wait: a replacement that stopped cleanly before the channel reached it leaves a
    /// channel on a stream that will hand over no more blocks, which is the same silence.
    /// </summary>
    [Fact]
    public void An_end_with_no_reason_is_still_reported()
    {
        var reported = new List<Exception?>();
        var handover = new Handover(reported.Add);

        handover.Ended(stopped: null);
        handover.TookOver();

        reported.ShouldHaveSingleItem().ShouldBeNull();
    }

    /// <summary>
    /// Both threads at once, which is the case the lock is there for. Neither ordering is chosen:
    /// the two are started together and whichever the machine runs first has to leave the other
    /// reporting exactly one end, and the right one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two threads for the whole run and a barrier per go, rather than a pair of fresh threads each
    /// time. That is what makes it a probe rather than a gesture: creating a thread costs more than
    /// the window being raced, so the arrivals in that shape are far apart and the flaw walks
    /// through. Measured on this machine over 200 000 goes of a copy of <see cref="Handover"/> with
    /// its lock taken out — 1138 where nobody reported and 12 where two did, against 0 and 0 with
    /// the lock. At the 5 000 below the same copy loses between 34 and 93 over five runs.
    /// </para>
    /// <para>
    /// Nobody reporting is the production symptom exactly: a channel left on a dead stream with
    /// every reading green. Reporting twice is the other end of the same hole and is rarer, which
    /// is why the count and not only the "at least one" is asserted.
    /// </para>
    /// </remarks>
    [Fact]
    public void Racing_the_two_arrivals_still_reports_one_end()
    {
        const int Goes = 5_000;

        var reported = 0;
        Exception? why = null;
        Handover? racing = null;

        // On every wait, including the two threads' own: a run given up on has to break all three
        // of them, or the two here would be left standing at a barrier nobody else reaches.
        var stopping = TestContext.Current.CancellationToken;

        // Three: the two arrivals and the thread setting up the next go. Every go is a handover of
        // its own, because what is being raced is one move and a move happens once.
        using var line = new Barrier(3);

        var capture = Racing(() => racing!.Ended(Died));
        var moving = Racing(() => racing!.TookOver());

        capture.Start();
        moving.Start();

        for (var go = 0; go < Goes; go++)
        {
            reported = 0;
            why = null;

            // The reason as well as the count. A handover that reported the nothing it was built
            // with instead of the failure it was handed passes a count and loses why a channel
            // stopped, which is the sentence somebody diagnosing the meeting reads.
            racing = new Handover(stopped =>
            {
                Interlocked.Increment(ref reported);
                Interlocked.CompareExchange(ref why, stopped, null);
            });

            line.SignalAndWait(stopping);
            line.SignalAndWait(stopping);

            Volatile.Read(ref reported).ShouldBe(1, $"go {go}");
            Volatile.Read(ref why).ShouldBeSameAs(Died, $"go {go}");
        }

        capture.Join();
        moving.Join();

        Thread Racing(Action arriving) => new(() =>
        {
            for (var go = 0; go < Goes; go++)
            {
                line.SignalAndWait(stopping);
                arriving();
                line.SignalAndWait(stopping);
            }
        });
    }
}
