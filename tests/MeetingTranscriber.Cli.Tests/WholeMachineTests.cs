using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// The gate ISC-139 hangs off: nothing moves a recording from the application it is following to
/// the whole machine's audio without somebody choosing it.
/// </summary>
/// <remarks>
/// <para>
/// No device anywhere in here, and none is needed. What <see cref="WholeMachine"/> decides from is
/// two things — whether the rule says channel 0 has heard nothing, and what somebody typed — and
/// what it decides is whether to run the move. Both are arguments, so the whole of the consent is
/// on this side of the hardware and the move itself is on the other, which is where the run
/// recorded in <c>ISA.md</c> reaches it.
/// </para>
/// <para>
/// The cost of getting this wrong is not a lost recording. It is a file holding every notification
/// and every other application on the machine, sent to a transcription service by somebody who
/// asked to record one program — so what is held here is mostly the ways the offer must <em>not</em>
/// be taken.
/// </para>
/// </remarks>
public class WholeMachineTests
{
    /// <summary>What the offer says, enough of it to tell it from any other line.</summary>
    private const string Offer = "Press w to record the whole machine";

    [Fact]
    public void The_offer_is_made_when_channel_0_has_heard_nothing_from_the_program()
    {
        var run = new Prompt();

        run.Gate.Consider(heardNothing: true, run.Output);

        run.Said.ShouldContain(Offer);
        run.Said.ShouldContain("notifications and every other application");
        run.Moves.ShouldBe(0);
    }

    /// <summary>
    /// Once, and then never again for the rest of the meeting. The rule goes on being true for
    /// every second channel 0 stays quiet, and a warning repeated every second is one somebody
    /// scrolls past — and it would be repeated over the top of the levels it is about.
    /// </summary>
    [Fact]
    public void The_offer_is_said_once_however_long_the_program_stays_silent()
    {
        var run = new Prompt();

        for (var second = 0; second < 30; second++)
        {
            run.Gate.Consider(heardNothing: true, run.Output);
        }

        run.Times(Offer).ShouldBe(1);
    }

    [Fact]
    public void A_program_that_has_played_something_is_offered_nothing()
    {
        var run = new Prompt();

        for (var second = 0; second < 30; second++)
        {
            run.Gate.Consider(heardNothing: false, run.Output);
        }

        run.Said.ShouldBeEmpty();
        run.Gate.Offered.ShouldBeFalse();
        run.Moves.ShouldBe(0);
    }

    /// <summary>
    /// The claim itself: the rule saying the offer is worth making moves nothing on its own.
    /// </summary>
    [Fact]
    public void Nothing_moves_the_channel_while_nobody_answers_the_offer()
    {
        var run = new Prompt();

        for (var second = 0; second < 30; second++)
        {
            run.Gate.Consider(heardNothing: true, run.Output);
        }

        run.Moves.ShouldBe(0);
    }

    [Fact]
    public void A_key_pressed_after_the_offer_moves_the_channel()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(1);
    }

    [Fact]
    public void The_key_is_the_same_key_with_shift_held()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("W");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(1);
    }

    [Fact]
    public void A_key_that_is_not_the_one_offered_answers_nothing()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("q");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(0);
    }

    /// <summary>
    /// Typed while the meeting was recording normally, before there was anything to answer. A
    /// keyboard left with a key in it would have that key read as consent the second the words
    /// appeared, which is a person's notifications in the file over something they typed at
    /// something else.
    /// </summary>
    [Fact]
    public void A_key_typed_before_the_offer_appeared_is_not_an_answer_to_it()
    {
        var run = new Prompt();
        run.Types("w");

        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Said.ShouldContain(Offer);
        run.Moves.ShouldBe(0);
    }

    /// <summary>
    /// And every key of it, not merely as far as the first. Somebody leaning on a key, or typing
    /// into the window a meeting is being recorded from, leaves more than one behind — and one
    /// left waiting is read the following second as an answer to an offer it came before.
    /// </summary>
    [Fact]
    public void Every_key_typed_before_the_offer_is_discarded_and_not_only_the_first()
    {
        var run = new Prompt();
        run.Types("www");

        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(0);
        run.Waiting.ShouldBe(0);
    }

    /// <summary>
    /// The narrowest version of the same thing, and the one an emptied keyboard alone does not
    /// close: somebody typing while the line is being written to the screen. Emptying before
    /// saying it leaves that key waiting, and it is read the next second as an answer to words
    /// that were not there when it was typed.
    /// </summary>
    [Fact]
    public void A_key_typed_while_the_offer_was_being_written_is_not_an_answer_to_it()
    {
        var run = new Prompt();
        run.TypesWhileTheNextLineIsWritten("w");

        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Said.ShouldContain(Offer);
        run.Moves.ShouldBe(0);
    }

    /// <summary>
    /// The keyboard is emptied every second, offer or no offer, which is what makes what is read
    /// after it an answer rather than whatever was in the buffer.
    /// </summary>
    [Fact]
    public void What_was_typed_while_the_program_was_playing_is_never_read_as_an_answer()
    {
        var run = new Prompt();
        run.Types("w");

        run.Gate.Consider(heardNothing: false, run.Output);
        run.Waiting.ShouldBe(0);

        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(0);
    }

    [Fact]
    public void Answering_the_offer_twice_moves_the_channel_once()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);
        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(1);
    }

    [Fact]
    public void A_channel_already_moved_is_not_moved_again_by_the_named_second()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);
        run.Gate.Take(run.Output);

        run.Moves.ShouldBe(1);
    }

    /// <summary>
    /// A refusal is the machine saying no to the move — a device that will not open, a folder that
    /// cannot be written. The meeting is still being recorded either way, and ending the run over
    /// it would cost the recording it was reporting on.
    /// </summary>
    [Fact]
    public void A_move_that_was_refused_is_reported_and_the_meeting_carries_on()
    {
        var run = new Prompt(refusing: "The playback device did not answer.");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Said.ShouldContain("The playback device did not answer.");
        run.Moves.ShouldBe(1);
    }

    /// <summary>
    /// And leaves the offer there to be taken again: nothing moved, so what the recording is is
    /// what it was before somebody pressed, and a device that refused once may open on the second
    /// ask.
    /// </summary>
    [Fact]
    public void A_move_that_was_refused_can_be_asked_for_again()
    {
        var run = new Prompt(refusing: "The playback device did not answer.");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);
        run.Types("w");
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Moves.ShouldBe(2);
    }

    /// <summary>
    /// <c>--whole-machine-at</c>, which is how the cost of a move gets measured without somebody
    /// holding a key down for two hours. It takes the offer and does not stand in for one: a
    /// second that falls before the rule has said anything has nothing to take.
    /// </summary>
    [Fact]
    public void A_named_second_before_the_offer_was_made_takes_nothing_and_says_so()
    {
        var run = new Prompt();

        run.Gate.Take(run.Output);

        run.Moves.ShouldBe(0);
        run.Said.ShouldContain("has not been offered");
        run.Said.ShouldContain("Nothing moved.");
    }

    [Fact]
    public void A_named_second_after_the_offer_was_made_takes_it()
    {
        var run = new Prompt();
        run.Gate.Consider(heardNothing: true, run.Output);

        run.Gate.Take(run.Output);

        run.Moves.ShouldBe(1);
    }

    /// <summary>
    /// A run nobody is sitting at — a script, a redirected prompt, a scheduled measurement — sees
    /// the offer and never takes it, which is the same answer somebody who reads it and does
    /// nothing gives. This one runs the real keyboard, which is the one thing the rest of the
    /// class stands in for, and a test host has no console to read.
    /// </summary>
    [Fact]
    public void The_prompts_own_keyboard_takes_nothing_when_there_is_nobody_at_it()
    {
        var moves = 0;
        var gate = WholeMachine.AtThePrompt(_ => moves++);
        using var output = new StringWriter();

        for (var second = 0; second < 30; second++)
        {
            gate.Consider(heardNothing: true, output);
        }

        moves.ShouldBe(0);
        output.ToString().ShouldContain(Offer);
    }

    /// <summary>
    /// One metering loop's worth of the offer: the gate, what somebody typed at it, what it wrote
    /// and how many times it asked for the channel to be moved.
    /// </summary>
    private sealed class Prompt
    {
        private readonly Queue<char> waiting = new();
        private readonly Screen said;
        private readonly string? refusing;

        public Prompt(string? refusing = null)
        {
            this.refusing = refusing;
            said = new Screen(Types);
            Gate = new WholeMachine(_ => Move(), () => waiting.Count == 0 ? null : waiting.Dequeue());
        }

        public WholeMachine Gate { get; }

        public TextWriter Output => said;

        /// <summary>How many times the channel was asked to move, refused or not.</summary>
        public int Moves { get; private set; }

        /// <summary>What is still waiting at the keyboard, which nothing should ever leave.</summary>
        public int Waiting => waiting.Count;

        public string Said => said.ToString();

        public void Types(string keys)
        {
            foreach (var key in keys)
            {
                waiting.Enqueue(key);
            }
        }

        /// <summary>
        /// Somebody typing in the moment a line is being put on the screen, which is the one thing
        /// a keyboard read between calls cannot stand for.
        /// </summary>
        public void TypesWhileTheNextLineIsWritten(string keys) => said.WhileWriting(keys);

        public int Times(string words) =>
            Said.Split(words, StringSplitOptions.None).Length - 1;

        private void Move()
        {
            Moves++;

            if (refusing is not null)
            {
                throw new AudioCaptureException(refusing);
            }
        }
    }

    /// <summary>
    /// What the run writes to, with room for somebody to type while it is being written. A key
    /// arriving there is the one moment the keyboard is not read between two calls, and it is
    /// where a drain done before the words go up loses it.
    /// </summary>
    private sealed class Screen(Action<string> typing) : StringWriter
    {
        private string? typed;

        public void WhileWriting(string keys) => typed = keys;

        public override void Write(string? value)
        {
            Typing();
            base.Write(value);
        }

        public override void Write(char value)
        {
            Typing();
            base.Write(value);
        }

        private void Typing()
        {
            if (typed is not { } keys)
            {
                return;
            }

            typed = null;
            typing(keys);
        }
    }
}
