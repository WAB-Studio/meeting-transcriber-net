using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Letting go of a device, driven by releases doing what no handle on a build agent can be asked to
/// do: refuse to come back, and refuse to close. Those two are the whole subject — a handle that
/// closes when it is told is every other test in this suite.
/// </summary>
public class DeviceReleaseTests
{
    /// <summary>How long a release that does answer is given before the test itself is the failure.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(1);

    /// <summary>
    /// ISC-136. The release never comes back, which is a driver that drained fine and then wedged
    /// on being let go of — the failure one line below the one ISC-128 bounded. Waiting for it ends
    /// at the deadline and says it was given up on, so a recording already on disk is not held by a
    /// device nobody is listening to any more.
    /// </summary>
    /// <remarks>
    /// The five seconds are the deadline itself, for the reason the draining loop's test gives: a
    /// release built with a shorter one for the test's sake would leave the number the product
    /// really waits proved by nothing.
    /// </remarks>
    [Fact]
    public void A_release_that_never_answers_is_given_up_on_rather_than_waited_on()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);
        var letGo = false;

        var release = DeviceRelease.Of("will not let go", () =>
        {
            // Uncancellable on purpose: this stands in for a thread inside a driver, and one that
            // could be asked to come back would stand in for nothing.
            stuck.Wait(Timeout.Infinite);
            letGo = true;
        });

        try
        {
            var waited = Time(release.Dispose);

            waited.ShouldBeGreaterThanOrEqualTo(CaptureLoop.StopsWithin);

            // Generous on purpose: what is being told apart is a deadline from no deadline at all,
            // so a loaded agent taking half as long again is not what this should go red over.
            waited.ShouldBeLessThan(CaptureLoop.StopsWithin * 2);
            release.Abandoned.ShouldBeTrue();

            // Still inside the handles, so nothing about them became anybody's to close.
            letGo.ShouldBeFalse();

            // And the second attempt at letting go — a source is asked by the path that failed and
            // by the path that finished — waits none of it again.
            Time(release.Dispose).ShouldBeLessThan(Promptly);
            release.Abandoned.ShouldBeTrue();
        }
        finally
        {
            stuck.Set();
        }
    }

    /// <summary>
    /// The ordinary release, and the two things it has to be: the handles really are let go of, and
    /// nobody waited a deadline to find that out. Asked twice, it lets go once — a source is let go
    /// of by both paths out of a session, and a second thread over handles the first already closed
    /// is a double release rather than a tidy one.
    /// </summary>
    [Fact]
    public void A_release_that_answers_lets_go_once_and_promptly()
    {
        var released = 0;
        var release = DeviceRelease.Of("lets go", () => Interlocked.Increment(ref released));

        Time(release.Dispose).ShouldBeLessThan(Promptly);
        release.Abandoned.ShouldBeFalse();
        released.ShouldBe(1);

        Time(release.Dispose).ShouldBeLessThan(Promptly);
        release.Abandoned.ShouldBeFalse();
        released.ShouldBe(1);
    }

    /// <summary>
    /// A handle that refuses to close is not the end of the process. This is the one thing this
    /// type does not share with the draining loop, and the difference is not a preference: a
    /// release runs after a recording is already on disk, and the session's guarantee is that every
    /// source is let go of whatever the one before it did — which an exception off a thread with
    /// nothing catching would end, taking the other source's release with it.
    /// </summary>
    /// <remarks>
    /// The first three are the ones a source has always swallowed on the way out, so what moved is
    /// where they arrive and not which of them are answers. The fourth is any other, and it is here
    /// because this thread has no boundary above it: a defect in cleanup ending the process would
    /// take the other source's release and the session's own way of finishing with it, after a
    /// meeting that is already on disk.
    /// </remarks>
    [Theory]
    [InlineData("io")]
    [InlineData("denied")]
    [InlineData("com")]
    [InlineData("nothing anybody expected")]
    public void A_handle_that_refuses_to_close_does_not_take_the_process_with_it(string refusal)
    {
        var reached = false;

        var release = DeviceRelease.Of($"refuses to close, {refusal}", () =>
        {
            reached = true;

            throw refusal switch
            {
                "io" => new IOException("the disk would not let go"),
                "denied" => new UnauthorizedAccessException("not yours to close"),
                "com" => new COMException("the device would not let go"),
                _ => new InvalidOperationException("a defect in this code, and still not the end"),
            };
        });

        Time(release.Dispose).ShouldBeLessThan(Promptly);

        // Over rather than wedged: a handle that refused is a handle that answered, so nothing is
        // held and the next holder is free to carry on.
        release.Abandoned.ShouldBeFalse();
        reached.ShouldBeTrue();
    }

    private static TimeSpan Time(Action step)
    {
        var clock = Stopwatch.StartNew();
        step();
        return clock.Elapsed;
    }
}
