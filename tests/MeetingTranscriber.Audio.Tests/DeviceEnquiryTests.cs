using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Asking this machine what it can record from, driven by bodies doing what no build agent's audio
/// service can be asked to do: stop answering while somebody is choosing a microphone. That silence
/// is the whole subject — a machine that answers is every other test in this suite.
/// </summary>
/// <remarks>
/// One class and not two, because what these share is a static: a question the machine was given up
/// on is remembered across callers by design, so two classes over it would run in parallel and each
/// would see the other's wedge. Every test that leaves one behind ends by letting its bodies go and
/// waiting until that question is answered again, in a <c>finally</c>, since a test that failed part
/// way through would otherwise take every test after it with it.
/// </remarks>
public class DeviceEnquiryTests
{
    /// <summary>What the two callers this application has ask about, in their own words.</summary>
    /// <remarks>
    /// Spelled here as constants because the memory is keyed on them: a test that reworded one
    /// would be testing two questions where the product asks one, and would pass for the wrong
    /// reason. That these really are the words <c>AudioDevices</c> passes is
    /// <see cref="Both_questions_this_application_asks_about_devices_go_through_the_deadline"/>.
    /// </remarks>
    private const string TheMicrophones = "the microphones on this machine";
    private const string ThePlaybackDevice = "the device this machine plays through";

    /// <summary>
    /// ISC-163. The body never comes back, which is the audio service stuck inside the enumerator,
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
                TheMicrophones,
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
            wedged.Message.ShouldContain(TheMicrophones);
        }
        finally
        {
            TheMachineComesBack(stuck, TheMicrophones);
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

        Deadlines.Time(() => listed = DeviceEnquiry.Answering(TheMicrophones, () => devices))
            .ShouldHaveComeBackAtOnce();

        listed.ShouldBeSameAs(devices);
    }

    /// <summary>
    /// A machine that says no has answered, and the answer is Windows' own: thrown again as it was
    /// thrown, so what it said is still there for whoever turns it into a sentence. A refusal
    /// wrapped in something of this type's own would be a machine that said no reported as one that
    /// said nothing — and it would be remembered as a question still out there, which would stop
    /// this application asking that one thing for as long as it ran.
    /// </summary>
    [Fact]
    public void A_machine_that_refuses_says_so_in_its_own_words_and_at_once()
    {
        var refusal = new COMException("the audio service is not running");
        var clock = Stopwatch.StartNew();

        var thrown = Should.Throw<COMException>(() =>
            DeviceEnquiry.Answering<object>(TheMicrophones, () => throw refusal));

        clock.Elapsed.ShouldHaveComeBackAtOnce();
        thrown.ShouldBeSameAs(refusal);

        // And it left nothing behind: a refusal is an answer, so the next question is asked.
        DeviceEnquiry.Answering(TheMicrophones, () => true).ShouldBeTrue();
    }

    /// <summary>
    /// ISC-162. A screen redrawing its meters asks once a second, so a deadline on its own would be
    /// a freeze with pauses in it — five seconds out of every six, and an abandoned thread for each.
    /// The same question is refused at once instead, and it is never put to the machine at all.
    /// </summary>
    /// <remarks>
    /// The sentence names what was asked, which is what a person can act on: it is the machine and
    /// this question they are being told about, not a deadline the application decided to skip.
    /// </remarks>
    [Fact]
    public void A_question_that_has_not_come_back_is_not_put_to_the_machine_again()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering(TheMicrophones, Wedging(stuck)));

            var asked = false;
            var clock = Stopwatch.StartNew();

            var refused = Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                TheMicrophones,
                () =>
                {
                    asked = true;
                    return new object();
                }));

            clock.Elapsed.ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();
            refused.Message.ShouldContain(TheMicrophones);
        }
        finally
        {
            TheMachineComesBack(stuck, TheMicrophones);
        }
    }

    /// <summary>
    /// ISC-164, and the reason the memory is scoped to what was asked rather than kept for the
    /// machine as a whole. The screen looks at what this machine plays through once a second to say
    /// whether the room is hearing the other side twice; the watcher lists the microphones every
    /// two seconds to move a channel whose device was unplugged mid-meeting. One memory over both
    /// means the first of those wedging stops the second — for as long as a body nothing can stop
    /// is out there, which for a body that never comes back is the rest of the meeting.
    /// </summary>
    /// <remarks>
    /// Written the way round that costs a meeting: the cosmetic question is the one wedged and the
    /// recovery is the one that has to get through. Reversed it would pass on code that couples
    /// them, since the wedge would be on the question being asked anyway.
    /// </remarks>
    [Fact]
    public void A_question_still_out_there_leaves_a_different_one_asked()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering(ThePlaybackDevice, Wedging(stuck)));

            var listed = false;

            Deadlines.Time(() =>
                    DeviceEnquiry.Answering(TheMicrophones, () => listed = true).ShouldBeTrue())
                .ShouldHaveComeBackAtOnce();

            // Not only that it came back, but that the machine was the one that answered: a refusal
            // handed back without asking would be this test's own default and would read the same
            // from the clock.
            listed.ShouldBeTrue();

            // And the wedged one is still wedged, so what is being measured is the scope of the
            // memory and not the memory having quietly emptied itself.
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering(ThePlaybackDevice, () => new object()));
        }
        finally
        {
            TheMachineComesBack(stuck, ThePlaybackDevice);
        }
    }

    /// <summary>
    /// ISC-162 with two callers asking one question at once, which the product does have: the
    /// window lists the microphones as it opens while the watcher lists them on a thread of its
    /// own. Each pays the deadline once, because neither is stopped by what the other has not yet
    /// learnt — and from the moment either of them gives up, that question is not asked again.
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
                        TheMicrophones,
                        Wedging(stuckSecond, cameBack: secondCameBack))));
            })
            {
                IsBackground = true,
                Name = "the window opening",
            };

            second.Start();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(TheMicrophones, Wedging(stuckFirst))))
                .ShouldHaveWaitedTheDeadline();

            // Joined before it is read, which is also what makes what that thread measured visible
            // here at all.
            second.Join();
            secondWaited.ShouldHaveWaitedTheDeadline();

            var asked = false;
            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(TheMicrophones, () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();

            // The later of the two comes back and the earlier does not, which is one thread out of
            // the audio service and one still inside it.
            stuckSecond.Set();
            secondCameBack.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
                .ShouldBeTrue();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(TheMicrophones, () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();
        }
        finally
        {
            stuckSecond.Set();
            TheMachineComesBack(stuckFirst, TheMicrophones);
        }
    }

    /// <summary>
    /// ISC-163, from the side no test can reach through a real audio service: both questions this
    /// application puts to the machine on nobody else's behalf are behind the deadline rather than
    /// beside it, and each is behind its own. With its question still out there, each call comes
    /// back at once saying so — which it could only do by having gone through the bounded ask under
    /// that name, since the audio stack itself would answer either with devices or with a refusal
    /// of its own.
    /// </summary>
    /// <remarks>
    /// One wedge per question and never one for both, which is also the only way this can be run at
    /// all now that the memory is scoped: a call whose question is not the wedged one would reach
    /// the machine, and a build agent's answer to that is nobody's evidence. It notices either of
    /// these two being taken back out from behind the ask, and either being renamed. What it cannot
    /// notice is a third question added beside them, and it cannot reach that every call inside
    /// these two is inside the ask — the enumerator, the default endpoint and each driver's
    /// property store. Both of those are read off <c>AudioDevices</c>, where the whole of each body
    /// is the lambda handed over.
    /// </remarks>
    [Fact]
    public void Both_questions_this_application_asks_about_devices_go_through_the_deadline()
    {
        ItsOwnCallerIsRefused(TheMicrophones, () => AudioDevices.Microphones());
        ItsOwnCallerIsRefused(ThePlaybackDevice, () => AudioDevices.Playback());
    }

    /// <summary>
    /// Wedges one question and asserts that the method which asks it comes back at once refusing,
    /// rather than reaching the machine.
    /// </summary>
    private static void ItsOwnCallerIsRefused(string asked, Action caller)
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(asked, Wedging(stuck)));

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(caller))
                .ShouldHaveComeBackAtOnce();
        }
        finally
        {
            TheMachineComesBack(stuck, asked);
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
    /// Lets the wedged bodies go and waits until <paramref name="asked"/> is answered again, which
    /// is the one thing every test that leaves a question out there owes the next one — and, in
    /// passing, the half of ISC-162 that keeps it from being this application having given up on
    /// Windows for good.
    /// </summary>
    /// <remarks>
    /// That same question and never another, now that the memory is scoped to what was asked: any
    /// other one is answered whether or not the wedged body ever came back, so a teardown asking it
    /// would return at once and hand the next test a static with a thread still in it.
    /// <para>
    /// Asked in a loop rather than waited for, because what says the question came back is a thread
    /// nothing here can join — the same thread the product gave up on. The last ask is outside the
    /// catch, so a machine that never comes back fails here saying so rather than timing out.
    /// </para>
    /// </remarks>
    private static void TheMachineComesBack(ManualResetEventSlim stuck, string asked)
    {
        stuck.Set();

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                DeviceEnquiry.Answering(asked, () => true).ShouldBeTrue();
                return;
            }
            catch (AudioDeviceWedgedException)
            {
                Thread.Sleep(millisecondsTimeout: 1);
            }
        }

        DeviceEnquiry.Answering(asked, () => true).ShouldBeTrue();
    }
}
