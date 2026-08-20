using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Asking this machine what it can record from, driven by bodies doing what no build agent's audio
/// service can be asked to do: stop answering while somebody is choosing a microphone. That silence
/// is the whole subject — a machine that answers is every other test in this suite.
/// </summary>
/// <remarks>
/// One class and not two, because what these share is a static: the questions the machine was given
/// up on are remembered across callers by design, so two classes over it would run in parallel and
/// each would see the other's wedge. Every test that leaves one behind ends by letting its bodies go
/// and waiting for the machine to answer again, in a <c>finally</c>, since a test that failed part
/// way through would otherwise take every test after it with it.
/// </remarks>
public class DeviceEnquiryTests
{
    /// <summary>
    /// ISC-161. The body never comes back, which is the audio service stuck inside the enumerator,
    /// the default endpoint or a driver's property store — everything listing the microphones
    /// touches. Asking comes back anyway, at the deadline rather than never, and says the machine
    /// did not answer and what it was asked about. The body is still in there when it says so,
    /// which is what being given up on means here as everywhere else.
    /// </summary>
    /// <remarks>
    /// The five seconds this costs are the deadline itself. An ask built with a shorter one for the
    /// test's sake would leave the number the product actually waits proved by nothing.
    /// </remarks>
    [Fact]
    public void A_machine_that_never_says_what_it_has_is_given_up_on_rather_than_waited_on()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);
        var listed = false;

        try
        {
            var clock = Stopwatch.StartNew();

            var wedged = Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                "the microphones on this machine",
                () =>
                {
                    // Uncancellable on purpose: this stands in for a thread inside the audio
                    // service, and one that could be asked to come back would stand in for nothing.
                    stuck.Wait(Timeout.Infinite);
                    listed = true;
                    return new object();
                }));

            clock.Elapsed.ShouldHaveWaitedTheDeadline();

            listed.ShouldBeFalse();
            wedged.Message.ShouldContain("the microphones on this machine");
        }
        finally
        {
            TheMachineComesBack(stuck);
        }
    }

    /// <summary>
    /// The baseline the test above is measured against: a machine that answers is waited for and no
    /// longer, and what it said is what comes back. Otherwise the deadline would tell apart nothing
    /// — every look at the devices would pay it.
    /// </summary>
    [Fact]
    public void A_machine_that_answers_hands_back_what_it_said_and_no_deadline_is_spent()
    {
        var devices = new object();
        object? listed = null;

        Deadlines.Time(() =>
                listed = DeviceEnquiry.Answering("the microphones on this machine", () => devices))
            .ShouldHaveComeBackAtOnce();

        listed.ShouldBeSameAs(devices);
    }

    /// <summary>
    /// A machine that says no has answered, and the answer is Windows' own: thrown again as it was
    /// thrown, so what it said is still there for whoever turns it into a sentence. A refusal
    /// wrapped in something of this type's own would be a machine that said no reported as one that
    /// said nothing — and it would be remembered as a question still out there, which would stop
    /// this application asking anything else about devices for as long as it ran.
    /// </summary>
    [Fact]
    public void A_machine_that_refuses_says_so_in_its_own_words_and_at_once()
    {
        var refusal = new COMException("the audio service is not running");
        var clock = Stopwatch.StartNew();

        var thrown = Should.Throw<COMException>(() =>
            DeviceEnquiry.Answering<object>("the microphones on this machine", () => throw refusal));

        clock.Elapsed.ShouldHaveComeBackAtOnce();
        thrown.ShouldBeSameAs(refusal);

        // And it left nothing behind: a refusal is an answer, so the next question is asked.
        DeviceEnquiry.Answering("the microphones on this machine", () => true).ShouldBeTrue();
    }

    /// <summary>
    /// ISC-162. A screen redrawing its meters asks once a second, so a deadline on its own would be
    /// a freeze with pauses in it — five seconds out of every six, and an abandoned thread for each.
    /// The next question is refused at once instead, and it is never put to the machine at all.
    /// </summary>
    /// <remarks>
    /// It names the question still out there rather than the one just asked, which here are two
    /// different things: what a person can act on is what Windows has stopped answering.
    /// </remarks>
    [Fact]
    public void A_machine_that_has_not_come_back_is_asked_nothing_else()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering("the microphones on this machine", Wedging(stuck)));

            var asked = false;
            var clock = Stopwatch.StartNew();

            var refused = Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                "the device this machine plays through",
                () =>
                {
                    asked = true;
                    return new object();
                }));

            clock.Elapsed.ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();
            refused.Message.ShouldContain("the microphones on this machine");
        }
        finally
        {
            TheMachineComesBack(stuck);
        }
    }

    /// <summary>
    /// ISC-162 with the two callers the product actually has: the screen asks what the machine
    /// plays through on its dispatcher while the watcher lists the microphones on a thread of its
    /// own, and both are live on the machine this is written for. Each pays the deadline once,
    /// because neither is stopped by what the other has not yet learnt — and from the moment either
    /// of them gives up, nothing is asked again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Then the half a single remembered question cannot hold: the machine comes back from the
    /// second and is still inside the first, which is not a machine answering. Asking again on that
    /// evidence would put the deadline back into every look, which is the freeze in the shape that
    /// is hardest to see, so it stays refused until both are back.
    /// </para>
    /// <para>
    /// A second between them on purpose. It makes which of the two is given up on last a fact
    /// rather than a race, so a run that admits a look after the later one comes back is this test
    /// finding a defect rather than this test being flaky.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_lookers_that_arrive_together_pay_the_deadline_once_each_and_no_more()
    {
        using var stuckFirst = new ManualResetEventSlim(initialState: false);
        using var stuckSecond = new ManualResetEventSlim(initialState: false);
        using var secondCameBack = new ManualResetEventSlim(initialState: false);
        var secondWaited = TimeSpan.Zero;

        try
        {
            var second = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                secondWaited = Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(
                        "the device this machine plays through",
                        Wedging(stuckSecond, cameBack: secondCameBack))));
            })
            {
                IsBackground = true,
                Name = "the screen looking",
            };

            second.Start();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering("the microphones on this machine", Wedging(stuckFirst))))
                .ShouldHaveWaitedTheDeadline();

            // Joined before it is read, which is also what makes what that thread measured visible
            // here at all.
            second.Join();
            secondWaited.ShouldHaveWaitedTheDeadline();

            var asked = false;
            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering("the microphones on this machine", () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();

            // The later of the two comes back and the earlier does not, which is one thread out of
            // the audio service and one still inside it.
            stuckSecond.Set();
            secondCameBack.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
                .ShouldBeTrue();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering("the microphones on this machine", () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();
        }
        finally
        {
            stuckSecond.Set();
            TheMachineComesBack(stuckFirst);
        }
    }

    /// <summary>
    /// ISC-161, from the side no test can reach through a real audio service: both questions this
    /// application puts to the machine on nobody else's behalf are behind the deadline rather than
    /// beside it. With a question still out there, each comes back at once saying so — which it
    /// could only do by having gone through the bounded ask, since the audio stack itself would
    /// answer either with devices or with a refusal of its own.
    /// </summary>
    /// <remarks>
    /// It notices either of these two being taken back out from behind the ask. What it cannot
    /// notice is a third question added beside them, and it cannot reach that every call inside
    /// these two is inside the ask — the enumerator, the default endpoint and each driver's
    /// property store. Both of those are read off `AudioDevices`, where the whole of each body is
    /// the lambda handed over.
    /// </remarks>
    [Fact]
    public void Both_questions_this_application_asks_about_devices_go_through_the_deadline()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering("the microphones on this machine", Wedging(stuck)));

            Deadlines.Time(() =>
            {
                Should.Throw<AudioDeviceWedgedException>(() => AudioDevices.Microphones());
                Should.Throw<AudioDeviceWedgedException>(() => AudioDevices.Playback());
            }).ShouldHaveComeBackAtOnce();
        }
        finally
        {
            TheMachineComesBack(stuck);
        }
    }

    /// <summary>
    /// A body that stands in for a thread inside the audio service: it waits on something the test
    /// holds and, where the test asked for it, says when it finally came back out.
    /// </summary>
    private static Func<object> Wedging(ManualResetEventSlim stuck, ManualResetEventSlim? cameBack = null) =>
        () =>
        {
            stuck.Wait(Timeout.Infinite);
            cameBack?.Set();
            return new object();
        };

    /// <summary>
    /// Lets the wedged body go and waits until the machine answers again, which is the one thing
    /// every test that leaves a question out there owes the next one — and, in passing, the half of
    /// ISC-162 that keeps it from being this application having given up on Windows for good.
    /// </summary>
    /// <remarks>
    /// Asked in a loop rather than waited for, because what says the question came back is a thread
    /// nothing here can join — the same thread the product gave up on. The last ask is outside the
    /// catch, so a machine that never comes back fails here saying so rather than timing out.
    /// </remarks>
    private static void TheMachineComesBack(ManualResetEventSlim stuck)
    {
        stuck.Set();

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                DeviceEnquiry.Answering("whether this machine answers again", () => true).ShouldBeTrue();
                return;
            }
            catch (AudioDeviceWedgedException)
            {
                Thread.Sleep(millisecondsTimeout: 1);
            }
        }

        DeviceEnquiry.Answering("whether this machine answers again", () => true).ShouldBeTrue();
    }
}
