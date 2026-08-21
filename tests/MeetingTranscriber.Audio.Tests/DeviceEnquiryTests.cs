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
                DeviceQuestion.Microphones,
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
            wedged.Message.ShouldContain(DeviceQuestion.Microphones.Asked);
        }
        finally
        {
            TheMachineComesBack(stuck, DeviceQuestion.Microphones);
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

        Deadlines.Time(() => listed = DeviceEnquiry.Answering(DeviceQuestion.Microphones, () => devices))
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
            DeviceEnquiry.Answering<object>(DeviceQuestion.Microphones, () => throw refusal));

        clock.Elapsed.ShouldHaveComeBackAtOnce();
        thrown.ShouldBeSameAs(refusal);

        // And it left nothing behind: a refusal is an answer, so the next question is asked.
        DeviceEnquiry.Answering(DeviceQuestion.Microphones, () => true).ShouldBeTrue();
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
                DeviceEnquiry.Answering(DeviceQuestion.Microphones, Wedging(stuck)));

            var asked = false;
            var clock = Stopwatch.StartNew();

            var refused = Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                DeviceQuestion.Microphones,
                () =>
                {
                    asked = true;
                    return new object();
                }));

            clock.Elapsed.ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();
            refused.Message.ShouldContain(DeviceQuestion.Microphones.Asked);
        }
        finally
        {
            TheMachineComesBack(stuck, DeviceQuestion.Microphones);
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
                DeviceEnquiry.Answering(DeviceQuestion.PlaybackDevice, Wedging(stuck)));

            var listed = false;

            Deadlines.Time(() =>
                    DeviceEnquiry.Answering(DeviceQuestion.Microphones, () => listed = true).ShouldBeTrue())
                .ShouldHaveComeBackAtOnce();

            // Not only that it came back, but that the machine was the one that answered: a refusal
            // handed back without asking would be this test's own default and would read the same
            // from the clock.
            listed.ShouldBeTrue();

            // And across the seam the product actually uses, since a lambda handed in here proves
            // the mechanism and not the call the watcher makes. What this machine answers with is
            // its own business — a build agent has no microphone and may refuse outright — so what
            // is asserted is that the refusal, if there is one, is not this memory's, and that no
            // deadline was spent reaching it.
            Deadlines.Time(() =>
            {
                try
                {
                    AudioDevices.Microphones();
                }
                catch (AudioCaptureException itsOwn)
                {
                    itsOwn.ShouldNotBeOfType<AudioDeviceWedgedException>();
                }
            }).ShouldHaveComeBackAtOnce();

            // And the wedged one is still wedged, so what is being measured is the scope of the
            // memory and not the memory having quietly emptied itself.
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering(DeviceQuestion.PlaybackDevice, () => new object()));
        }
        finally
        {
            TheMachineComesBack(stuck, DeviceQuestion.PlaybackDevice);
        }
    }

    /// <summary>
    /// ISC-162 and ISC-164 together, with the two callers that really do overlap: the screen asks
    /// what this machine plays through on its dispatcher while the watcher lists the microphones on
    /// a thread of its own, and both are live for the whole of a meeting. Each pays the deadline
    /// once and neither is stopped by the other, which is the two claims meeting — a deadline paid
    /// once is paid once per question, and one question out there refuses only itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Then what the pruning must not do: the later of the two comes back while the earlier is
    /// still inside the audio stack, and the earlier one stays refused. One thread out of the audio
    /// service is not the service answering, and forgiving the wrong entry would put the deadline
    /// back into every look — the freeze in the shape that is hardest to see. The one that came
    /// back is asked again in the same breath, since forgiving it is the other half of the same
    /// line.
    /// </para>
    /// <para>
    /// A second between them on purpose. It makes which of the two is given up on last a fact
    /// rather than a race, so a run that admits a look after the later one comes back is this test
    /// finding a defect rather than this test being flaky.
    /// </para>
    /// <para>
    /// Two callers of one question at once is not written here, because the product has none: the
    /// window lists the microphones in its constructor, once, before any meeting exists, and the
    /// watcher lists them only while one is running. What holds the memory to more than one entry
    /// is that it is a list, and that is said where the list is.
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
                        DeviceQuestion.PlaybackDevice,
                        Wedging(stuckSecond, cameBack: secondCameBack))));
            })
            {
                IsBackground = true,
                Name = "the screen looking",
            };

            second.Start();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(DeviceQuestion.Microphones, Wedging(stuckFirst))))
                .ShouldHaveWaitedTheDeadline();

            // Joined before it is read, which is also what makes what that thread measured visible
            // here at all. That it waited the deadline is the half that matters: it was inside its
            // own five seconds while the other gave up, and was not refused for it.
            second.Join();
            secondWaited.ShouldHaveWaitedTheDeadline();

            var asked = false;
            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(DeviceQuestion.Microphones, () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();

            // The later of the two comes back and the earlier does not, which is one thread out of
            // the audio service and one still inside it.
            stuckSecond.Set();
            secondCameBack.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
                .ShouldBeTrue();

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(() =>
                    DeviceEnquiry.Answering(DeviceQuestion.Microphones, () => asked = true)))
                .ShouldHaveComeBackAtOnce();
            asked.ShouldBeFalse();

            // And the one that came back is asked again, so the entry was dropped rather than the
            // two of them being forgiven or held together.
            DeviceEnquiry.Answering(DeviceQuestion.PlaybackDevice, () => true).ShouldBeTrue();
        }
        finally
        {
            stuckSecond.Set();
            TheMachineComesBack(stuckFirst, DeviceQuestion.Microphones);
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
        ItsOwnCallerIsRefused(DeviceQuestion.Microphones, () => AudioDevices.Microphones());
        ItsOwnCallerIsRefused(DeviceQuestion.PlaybackDevice, () => AudioDevices.Playback());
    }

    /// <summary>
    /// Wedges one question and asserts that the method which asks it comes back at once refusing,
    /// rather than reaching the machine.
    /// </summary>
    private static void ItsOwnCallerIsRefused(DeviceQuestion question, Action caller)
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() =>
                DeviceEnquiry.Answering(question, Wedging(stuck)));

            Deadlines.Time(() => Should.Throw<AudioDeviceWedgedException>(caller))
                .ShouldHaveComeBackAtOnce();
        }
        finally
        {
            TheMachineComesBack(stuck, question);
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
    /// Lets the wedged bodies go and waits until <paramref name="question"/> is answered again,
    /// which
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
    private static void TheMachineComesBack(ManualResetEventSlim stuck, DeviceQuestion question)
    {
        stuck.Set();

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                DeviceEnquiry.Answering(question, () => true).ShouldBeTrue();
                return;
            }
            catch (AudioDeviceWedgedException)
            {
                Thread.Sleep(millisecondsTimeout: 1);
            }
        }

        DeviceEnquiry.Answering(question, () => true).ShouldBeTrue();
    }
}
