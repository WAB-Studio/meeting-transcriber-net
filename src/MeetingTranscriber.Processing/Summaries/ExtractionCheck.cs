using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// Whether a meeting supports every word of an extraction: read it, then hold it against the input
/// it was supposed to be made from and the turns that input carries.
/// </summary>
/// <remarks>
/// <para>
/// Shape first, and alone: <see cref="ExtractionReader"/> reports every way an output fails to be
/// the shape, and when it does nothing else about it is asked — a document that is not readable has
/// nothing left on it worth checking against a meeting.
/// </para>
/// <para>
/// Then the input, by <see cref="MeetingInput.Hash"/>: an extraction whose hash does not match the
/// bytes prepared now was made from a transcript that has since changed underneath it, and nothing
/// past that is worth asking either — the citations it carries answer to turns that may not be
/// there any more.
/// </para>
/// <para>
/// Then the meeting itself, its participants, and finally each statement: every decision, then
/// every action, then every open question, in that order and then by position. A statement records
/// only the first of its own failures — no evidence at all; a turn this meeting does not have; a
/// voice this meeting does not have at all; a turn that exists but is not the one this citation
/// describes, on its start or its voice, whichever disagrees first; or a quote that turn does not
/// say — because each of those already explains why the one after it could not be asked. Reporting
/// four ways in which the same made-up citation is wrong would not tell anybody anything the first
/// one did not.
/// </para>
/// <para>
/// A participant from another meeting is not a ninth condition of its own. It arrives as a speaker
/// this meeting does not have, which <see cref="ExtractionCondition.SpeakerNotInTheMeeting"/>
/// already names — adding a second condition for the same observation would be two answers to one
/// question, told apart only by which field happened to carry it.
/// </para>
/// </remarks>
public static class ExtractionCheck
{
    /// <summary>What checking one extraction against its meeting found.</summary>
    public sealed record ExtractionVerdict(ExtractionDocument? Accepted, IReadOnlyList<ExtractionRefusal> Refusals)
    {
        public bool IsAccepted => Accepted is not null;
    }

    public static ExtractionVerdict Of(ReadOnlySpan<byte> output, string inputHashSent, MeetingInput prepared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHashSent);
        ArgumentNullException.ThrowIfNull(prepared);

        var read = ExtractionReader.Read(output);
        if (read.Refusals.Count > 0)
        {
            return new ExtractionVerdict(null, read.Refusals);
        }

        var document = read.Document!;
        var refusals = new List<ExtractionRefusal>();

        if (!string.Equals(inputHashSent, prepared.Hash, StringComparison.Ordinal))
        {
            refusals.Add(new ExtractionRefusal(ExtractionCondition.InputNotAsPrepared, "$", null));
        }

        if (document.MeetingId != prepared.MeetingId)
        {
            refusals.Add(new ExtractionRefusal(ExtractionCondition.AnotherMeeting, "meeting_id", null));
        }

        var speakers = prepared.Turns.Select(turn => turn.SpeakerLabel).ToHashSet(StringComparer.Ordinal);

        // Never throws on a real meeting's turns: (MeetingId, Ordinal) is an alternate key on
        // utterances, and Turns.Group assigns ordinals from the list's own count as it builds it.
        var turnsByOrdinal = prepared.Turns.ToDictionary(turn => turn.Ordinal);

        for (var index = 0; index < document.Participants.Count; index++)
        {
            if (!speakers.Contains(document.Participants[index]))
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.SpeakerNotInTheMeeting, $"participants[{index}]", null));
            }
        }

        CheckStatements(
            document.Decisions, "decisions", decision => decision.Statement, decision => decision.Evidence,
            speakers, turnsByOrdinal, refusals);
        CheckStatements(
            document.Actions, "actions", action => action.Statement, action => action.Evidence,
            speakers, turnsByOrdinal, refusals);
        CheckStatements(
            document.OpenQuestions, "open_questions", question => question.Question, question => question.Evidence,
            speakers, turnsByOrdinal, refusals);

        return refusals.Count == 0
            ? new ExtractionVerdict(document, [])
            : new ExtractionVerdict(null, refusals);
    }

    private static void CheckStatements<T>(
        IReadOnlyList<T> items,
        string section,
        Func<T, string> textOf,
        Func<T, ExtractedEvidence?> evidenceOf,
        IReadOnlySet<string> speakers,
        IReadOnlyDictionary<int, Turn> turnsByOrdinal,
        List<ExtractionRefusal> refusals)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var text = textOf(item);
            var evidence = evidenceOf(item);
            var itemPath = $"{section}[{index}]";

            if (evidence is null)
            {
                refusals.Add(new ExtractionRefusal(ExtractionCondition.NoEvidence, itemPath, text));
                continue;
            }

            if (!turnsByOrdinal.TryGetValue(evidence.UtteranceOrdinal, out var turn))
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.NoSuchTurn, $"{itemPath}.evidence.utterance_ordinal", text));
                continue;
            }

            if (!speakers.Contains(evidence.SpeakerLabel))
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.SpeakerNotInTheMeeting, $"{itemPath}.evidence.speaker_label", text));
                continue;
            }

            if (evidence.Start != turn.Start)
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.NotTheTurnCited, $"{itemPath}.evidence.start_ms", text));
                continue;
            }

            if (!string.Equals(evidence.SpeakerLabel, turn.SpeakerLabel, StringComparison.Ordinal))
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.NotTheTurnCited, $"{itemPath}.evidence.speaker_label", text));
                continue;
            }

            if (!QuoteBelongsToTurn(evidence.QuotedText, turn.Text))
            {
                refusals.Add(new ExtractionRefusal(
                    ExtractionCondition.QuoteNotInTheTurn, $"{itemPath}.evidence.quoted_text", text));
            }
        }
    }

    /// <summary>
    /// A quote belongs to its turn when, after both are evened out, the turn's text contains it,
    /// compared ordinally and case-sensitively. Case-insensitive or accent-folded matching is
    /// rejected on purpose: a quotation that is not what was said is exactly what this exists to
    /// catch.
    /// </summary>
    private static bool QuoteBelongsToTurn(string quote, string turnText)
    {
        var evened = EvenOut(quote);
        return evened.Length > 0 && EvenOut(turnText).Contains(evened, StringComparison.Ordinal);
    }

    /// <summary>Collapses every run of whitespace to one space and trims both ends.</summary>
    private static string EvenOut(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
