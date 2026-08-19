using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The offer of the whole machine's audio, at a prompt: said once when channel 0 has heard nothing
/// from the program it is following, and taken only by somebody answering it.
/// </summary>
/// <remarks>
/// <para>
/// The rule about when it is worth saying is <c>SilentProgram</c>'s and the move itself is the
/// capture session's. What is here is the shape the offer takes when there is no window: a line
/// beside the levels, and a key. Both commands that record show it, so it is one type rather than
/// the same eight lines in each of their metering loops.
/// </para>
/// <para>
/// Public, and its keyboard is a constructor argument, because this is the whole of the consent
/// ISC-139 is about — nothing else decides whether a recording moves to the whole machine's audio
/// — and a gate nothing in CI can stand over is a claim resting on somebody having read it. The
/// keys come from <see cref="AtThePrompt"/> everywhere this program runs; what a test hands it
/// instead is what somebody typed and when, which is the only input this makes a decision from.
/// </para>
/// <para>
/// A prompt with nothing typing at it — a script, a redirected run, a scheduled measurement —
/// still sees the line and never takes the offer, which is the same answer a person who reads it
/// and does nothing gives. Nothing here presses it on anybody's behalf.
/// </para>
/// </remarks>
public sealed class WholeMachine
{
    /// <summary>What somebody presses to take it.</summary>
    private const char Key = 'w';

    private readonly Action<TextWriter> take;
    private readonly Func<char?> typed;

    private bool offered;
    private bool taken;

    /// <param name="take">What moving channel 0 to the whole machine's audio is.</param>
    /// <param name="typed">
    /// The next key waiting to be read, or nothing when none is. Asked until it says nothing,
    /// which is what empties whatever was typed rather than leaving it to answer a later offer.
    /// </param>
    public WholeMachine(Action<TextWriter> take, Func<char?> typed)
    {
        ArgumentNullException.ThrowIfNull(take);
        ArgumentNullException.ThrowIfNull(typed);

        this.take = take;
        this.typed = typed;
    }

    /// <summary>Whether the offer has been said, so that there is something to answer.</summary>
    public bool Offered => offered;

    /// <summary>The offer read off the keyboard of the prompt this is running at.</summary>
    public static WholeMachine AtThePrompt(Action<TextWriter> take) => new(take, FromTheKeyboard);

    /// <summary>
    /// Says the offer if it is worth saying and has not been said, and takes it if somebody has
    /// pressed for it. Called once a second by a metering loop, whatever it is showing.
    /// </summary>
    /// <param name="heardNothing">Whether channel 0 has heard nothing from the program.</param>
    /// <param name="output">Where the offer is written.</param>
    public void Consider(bool heardNothing, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (heardNothing && !offered)
        {
            offered = true;
            Report.Line(
                output,
                "no audio",
                $"nothing at all has come from that program. Press {Key} to record the whole "
                + "machine instead, which puts notifications and every other application in the "
                + "recording. The meeting keeps running either way.");

            // Emptied after the words are on screen, and that order is the whole of it. Draining
            // first leaves everything typed while the line was being written waiting to be read
            // next second as an answer — and a key typed before the offer was visible answers
            // nothing, whether it came a second before or a microsecond.
            Pressed();
            return;
        }

        // Read every second, offer or no offer, and that is what makes it an answer to the offer
        // rather than a key somebody happened to have typed. Anything left waiting would otherwise
        // still be there when the warning appeared and would move the recording on the spot — a
        // choice made before there was anything to choose, and read as informed consent to put
        // every other application on the machine in the file.
        var pressed = Pressed();

        if (offered && pressed)
        {
            Take(output);
        }
    }

    /// <summary>
    /// Somebody taking it. The key is one way of saying so and an argument naming the second is
    /// the other — a run nobody is sitting at is still a run somebody started, and a measurement
    /// of what moving costs is not something a person repeats by holding down a key.
    /// </summary>
    /// <remarks>
    /// An offer that was never made is nothing to take, whichever way it is asked for. The named
    /// second answers a question the rule has not put yet, and moving on it would be the one thing
    /// this type exists to stop: the whole machine's audio in the file with nothing having said it
    /// was going there. It is reported rather than thrown, for the reason a refusal is.
    /// </remarks>
    public void Take(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (taken)
        {
            return;
        }

        if (!offered)
        {
            Report.Line(
                output,
                "channel 0",
                "the whole machine's audio has not been offered, so there is nothing to take: "
                + "channel 0 is following its program and has not been silent long enough for the "
                + "offer to be worth making. Nothing moved.");
            return;
        }

        try
        {
            take(output);
            taken = true;
        }
        catch (AudioCaptureException refused)
        {
            // Reported and not thrown: the meeting is still being recorded either way, and ending
            // a run over this would cost the recording it was reporting on.
            //
            // Under the channel's own name rather than under a word like "not moved", because what
            // this line is for is the sentence saying which device the meeting is on now — and
            // every way of reaching here leaves the channel where it was, so each message says so
            // itself and in its own terms. Nothing hands over until the new device is running and
            // the folder has said so.
            Report.Line(output, "channel 0", refused.Message);
        }
    }

    /// <summary>
    /// Whether the key was among what has been typed since this last asked. Everything waiting is
    /// read, and not only as far as the first press: a second key left behind would be read next
    /// second as an answer to an offer it came before, which is the very thing draining prevents.
    /// </summary>
    private bool Pressed()
    {
        var pressed = false;

        while (typed() is { } key)
        {
            if (char.ToLowerInvariant(key) == Key)
            {
                pressed = true;
            }
        }

        return pressed;
    }

    /// <summary>
    /// The next key waiting at the prompt, without ever waiting for one: this runs inside a
    /// meeting being recorded, and a read that blocked would hold the levels and the pause up on a
    /// keyboard.
    /// </summary>
    private static char? FromTheKeyboard()
    {
        try
        {
            return Console.KeyAvailable ? Console.ReadKey(intercept: true).KeyChar : null;
        }
        catch (Exception nobodyThere) when (nobodyThere is InvalidOperationException or IOException)
        {
            // No console to read from, which is every redirected run, and a handle that has gone
            // away under one, which is the same answer: nobody is there to take the offer. Not an
            // error and not worth reporting — and never something that ends the run, because what
            // is on the other side of this line is a meeting still being recorded.
            return null;
        }
    }
}
