using System.Globalization;

using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The one consent every spend at this prompt rests on: how many minutes of audio a call is
/// about, and somebody typing that number back rather than answering yes.
/// </summary>
/// <remarks>
/// <para>
/// Both <see cref="LiveCheck"/> and a meeting transcribed again ask this, and both spend real
/// money, so the sentence and the comparison are written once here rather than twice with a chance
/// to drift. A stray keystroke on a prompt that was waiting for one cannot then spend somebody's
/// money, and somebody who read the wrong line types the wrong number.
/// </para>
/// </remarks>
public static class TypedBack
{
    /// <summary>
    /// What is said before the confirmation is asked for, every run, whether or not it goes ahead.
    /// This product deliberately keeps one answer to <em>which key does this machine use</em>, so
    /// what it can do is say where the money lands before anybody agrees to spend it.
    /// </summary>
    public const string WhereTheSpendLands =
        "the spend lands on whatever Deepgram account this machine's key belongs to. Point that "
        + "key at a test project first if that is not what you want charged: "
        + "'meeting-transcriber key --set'.";

    /// <summary>
    /// <paramref name="length"/> in whole minutes, rounded up — the one number the report, the
    /// ceiling and the confirmation all talk about, so that nobody has to work out whether eight
    /// minutes and twenty-three seconds is eight minutes or nine. Up rather than to the nearest,
    /// because that is the direction that cannot understate a bill.
    /// </summary>
    public static int MinutesOf(Duration length) => (int)Math.Ceiling(length.Milliseconds / 60_000.0);

    /// <summary>
    /// Asks once, and answers whether somebody agreed to spend on <paramref name="minutes"/> of
    /// audio.
    /// </summary>
    /// <remarks>
    /// The number typed back, and not <c>y</c>. What somebody is agreeing to is a quantity, so
    /// agreeing to it means saying it.
    /// </remarks>
    /// <param name="minutes">How many minutes this call is about.</param>
    /// <param name="output">Where the question goes.</param>
    /// <param name="typed">What somebody typed, or nothing at all when nobody is there.</param>
    public static bool Confirmed(int minutes, TextWriter output, Func<string?> typed)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(typed);

        Report.Line(
            output,
            "confirm",
            $"type {minutes} and press Enter to send {minutes} minute(s) of audio to Deepgram. "
            + "Anything else sends nothing.");

        return typed()?.Trim() == minutes.ToString(CultureInfo.InvariantCulture);
    }
}
