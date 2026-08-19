using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The offer of the whole machine's audio, at a prompt: said once when channel 0 has heard nothing
/// from the program it is following, and taken only by somebody pressing the key.
/// </summary>
/// <remarks>
/// <para>
/// The rule about when it is worth saying is <c>SilentProgram</c>'s and the move itself is the
/// capture session's. What is here is the shape the offer takes when there is no window: a line
/// beside the levels, and a key. Both commands that record show it, so it is one type rather than
/// the same eight lines in each of their metering loops.
/// </para>
/// <para>
/// A prompt with nothing typing at it — a script, a redirected run, a scheduled measurement —
/// still sees the line and never takes the offer, which is the same answer a person who reads it
/// and does nothing gives. Nothing here presses it on anybody's behalf.
/// </para>
/// </remarks>
internal sealed class WholeMachine(Action<TextWriter> take)
{
    /// <summary>What somebody presses to take it.</summary>
    private const char Key = 'w';

    private bool offered;
    private bool taken;

    /// <summary>
    /// Says the offer if it is worth saying and has not been said, and takes it if somebody has
    /// pressed for it. Called once a second by a metering loop, whatever it is showing.
    /// </summary>
    /// <param name="heardNothing">Whether channel 0 has heard nothing from the program.</param>
    /// <param name="output">Where the offer is written.</param>
    public void Consider(bool heardNothing, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        // Read every second, offer or no offer, and that is what makes it an answer to the offer
        // rather than a key somebody happened to have typed. Anything left in the buffer would
        // otherwise be waiting there when the warning appeared and would move the recording on the
        // spot — a choice made before there was anything to choose, and read as informed consent to
        // put every other application on the machine in the file.
        var pressed = Pressed();

        if (heardNothing && !offered)
        {
            offered = true;
            Report.Line(
                output,
                "no audio",
                $"nothing at all has come from that program. Press {Key} to record the whole "
                + "machine instead, which puts notifications and every other application in the "
                + "recording. The meeting keeps running either way.");

            // Nothing typed in the second the offer was made answers it either: it was typed before
            // the words were on screen.
            return;
        }

        if (offered && pressed)
        {
            Press(output);
        }
    }

    /// <summary>
    /// Somebody taking it. The key is one way of saying so and an argument naming the second is
    /// the other — a run nobody is sitting at is still a run somebody started, and a measurement
    /// of what moving costs is not something a person repeats by holding down a key.
    /// </summary>
    public void Press(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (taken)
        {
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
            // Under the channel's own name and not under a word like "not moved", because the two
            // things that reach here are not the same thing. A move that was refused leaves the
            // channel where it was; a move that happened and could not be written down leaves it
            // moved, with the folder's account of it short — and each of those messages says which
            // of the two it is. A label asserting one of them would be wrong half the time.
            Report.Line(output, "channel 0", refused.Message);
        }
    }

    /// <summary>
    /// Whether the key is waiting, without ever waiting for it: this runs inside a meeting being
    /// recorded, and a read that blocked would hold the levels and the pause up on a keyboard.
    /// </summary>
    private static bool Pressed()
    {
        try
        {
            while (Console.KeyAvailable)
            {
                if (char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar) == Key)
                {
                    return true;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // No console to read from, which is every redirected run. Not an error and not worth
            // reporting: the offer was still made, and nobody is there to take it.
        }

        return false;
    }
}
