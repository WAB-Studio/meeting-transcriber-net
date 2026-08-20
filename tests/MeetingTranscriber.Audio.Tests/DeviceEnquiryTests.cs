using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Asking this machine what it can record from, driven by bodies doing what no build agent's audio
/// service can be asked to do: stop answering while somebody is choosing a microphone. That silence
/// is the whole subject — a machine that answers is every other test in this suite.
/// </summary>
/// <remarks>
/// One class and not two, because what these share is a static: the question the machine was given
/// up on is remembered across callers by design, so two classes over it would run in parallel and
/// each would see the other's wedge. Every test that leaves one behind ends by letting the body go
/// and waiting for the machine to answer again.
/// </remarks>
public class DeviceEnquiryTests
{
    /// <summary>How long an ask that does answer is given before the test itself is the failure.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(1);

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

        Time(() => listed = DeviceEnquiry.Answering("the microphones on this machine", () => devices))
            .ShouldBeLessThan(Promptly);

        listed.ShouldBeSameAs(devices);
    }

    /// <summary>
    /// A machine that says no has answered, and the answer is Windows' own: thrown again as it was
    /// thrown, so what it said is still there for whoever turns it into a sentence. A refusal
    /// wrapped in something of this type's own would be a machine that said no reported as one that
    /// said nothing — and it would be remembered as a question still out there, which would stop
    /// this application asking anything else about audio for as long as it ran.
    /// </summary>
    [Fact]
    public void A_machine_that_refuses_says_so_in_its_own_words_and_at_once()
    {
        var refusal = new COMException("the audio service is not running");
        var clock = Stopwatch.StartNew();

        var thrown = Should.Throw<COMException>(() =>
            DeviceEnquiry.Answering<object>("the microphones on this machine", () => throw refusal));

        clock.Elapsed.ShouldBeLessThan(Promptly);
        thrown.ShouldBeSameAs(refusal);

        // And it left nothing behind: a refusal is an answer, so the next question is asked.
        DeviceEnquiry.Answering("the microphones on this machine", () => true).ShouldBeTrue();
    }

    /// <summary>
    /// ISC-162. A screen redrawing its meters asks once a second and the watcher looking for a
    /// device that went away asks every two, so a deadline on its own would be a freeze with pauses
    /// in it — five seconds out of every six, and an abandoned thread for each. The second question
    /// is refused at once instead, and it is never put to the machine at all.
    /// </summary>
    /// <remarks>
    /// It names the question still out there rather than the one just asked, which on this machine
    /// are two different things: what a person can act on is what Windows has stopped answering.
    /// </remarks>
    [Fact]
    public void A_machine_that_has_not_come_back_is_asked_nothing_else()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                "the microphones on this machine",
                () =>
                {
                    stuck.Wait(Timeout.Infinite);
                    return new object();
                }));

            var asked = false;
            var clock = Stopwatch.StartNew();

            var refused = Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                "the device this machine plays through",
                () =>
                {
                    asked = true;
                    return new object();
                }));

            clock.Elapsed.ShouldBeLessThan(Promptly);
            asked.ShouldBeFalse();
            refused.Message.ShouldContain("the microphones on this machine");
        }
        finally
        {
            TheMachineComesBack(stuck);
        }
    }

    /// <summary>
    /// ISC-161, from the side no test can reach through a real audio service: both questions this
    /// application puts to the machine are behind the deadline rather than beside it. With a
    /// question still out there, each comes back at once saying so — which it could only do by
    /// having gone through the bounded ask, since the audio stack itself would answer either with
    /// devices or with a refusal of its own.
    /// </summary>
    /// <remarks>
    /// The one probe here that would notice a third question being added to that file and left
    /// unbounded, or one of these two being taken back out. What it cannot reach is that every call
    /// inside those questions is inside the ask — that is read off `AudioDevices`, where the whole
    /// of each body is the lambda handed over.
    /// </remarks>
    [Fact]
    public void Both_questions_this_application_asks_about_devices_go_through_the_deadline()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        try
        {
            Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
                "the microphones on this machine",
                () =>
                {
                    stuck.Wait(Timeout.Infinite);
                    return new object();
                }));

            var clock = Stopwatch.StartNew();

            Should.Throw<AudioDeviceWedgedException>(() => AudioDevices.Microphones());
            Should.Throw<AudioDeviceWedgedException>(() => AudioDevices.Playback());

            clock.Elapsed.ShouldBeLessThan(Promptly);
        }
        finally
        {
            TheMachineComesBack(stuck);
        }
    }

    /// <summary>
    /// ISC-162, the half that keeps it from being "this application gave up on Windows". A machine
    /// that comes back — the audio service restarted, and the thread nobody is waiting for finishes
    /// what it was asked — is asked again, so a meeting recorded after that is a meeting like any
    /// other rather than one needing the application closed first.
    /// </summary>
    [Fact]
    public void A_machine_that_comes_back_is_asked_again()
    {
        using var stuck = new ManualResetEventSlim(initialState: false);

        Should.Throw<AudioDeviceWedgedException>(() => DeviceEnquiry.Answering(
            "the microphones on this machine",
            () =>
            {
                stuck.Wait(Timeout.Infinite);
                return new object();
            }));

        TheMachineComesBack(stuck);
    }

    /// <summary>
    /// Lets the wedged body go and waits until the machine answers again, which is the one thing
    /// every test that leaves a question out there owes the next one.
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
        while (clock.Elapsed < Promptly)
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

    private static TimeSpan Time(Action step)
    {
        var clock = Stopwatch.StartNew();
        step();
        return clock.Elapsed;
    }
}
