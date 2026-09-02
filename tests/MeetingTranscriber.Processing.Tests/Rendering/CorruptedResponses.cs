using System.Text.RegularExpressions;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// A committed response edited into one the corpus will not store, which is what a probe about a
/// render or a rebuild refused partway needs to stand a meeting in front of.
/// </summary>
/// <remarks>
/// One owner rather than one per test class. The edit is a regex over the shape a provider actually
/// sent, so a reordered field in <c>tests/fixtures/deepgram/</c> has to be found in one place, and
/// the argument for why it is every utterance rather than one is written once.
/// </remarks>
internal static class CorruptedResponses
{
    /// <summary>
    /// The confidence of an utterance, and not of a word or of a channel's transcript: only an
    /// utterance carries the channel it was on, and only its confidence is read.
    /// </summary>
    private static readonly Regex Reported = new(@"""confidence"":[0-9.]+,""channel"":");

    /// <summary>
    /// A response whose confidences the corpus will not store. Every timing, channel, speaker and
    /// word it was sent with is left alone, and the one edit is the only number on the render path
    /// that nothing checks until SQLite does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parser carries a confidence exactly as sent and refuses nothing over it, deliberately —
    /// it is the provider's own number about one stretch of speech, and a whole meeting is not
    /// worth refusing over it. <c>ck_utterances_confidence</c> disagrees: the column it lands in is
    /// bounded to zero through one. So a response the parser reads happily is a row the corpus
    /// refuses, and the refusal arrives from inside <c>MeetingRenderer</c>'s own <c>SaveChanges</c>
    /// with the meeting's turns already staged — the state no fixture edited any other way can
    /// reach, because every one of those is refused before a row is built at all.
    /// </para>
    /// <para>
    /// Every utterance rather than one: <c>Turns.Group</c> averages a turn's confidence over the
    /// segments it merges, weighted by their lengths, so a single out-of-range segment could come
    /// back inside the bound and prove nothing.
    /// </para>
    /// </remarks>
    internal static string WithConfidenceOffTheScale(string response)
    {
        Reported.IsMatch(response).ShouldBeTrue(
            "the fixture no longer carries an utterance's confidence in the shape this edits, so "
            + "what it hands back is the response unchanged and every probe over it proves nothing");

        return Reported.Replace(response, @"""confidence"":1.5,""channel"":");
    }
}
