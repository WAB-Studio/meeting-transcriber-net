using System.Text.Json.Nodes;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Summaries;

using static MeetingTranscriber.Processing.Tests.Summaries.JsonNodeMutations;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// Whether the meeting behind a <see cref="MeetingInput"/> supports every word of an extraction:
/// the input, the meeting, each participant, and each statement's own citation.
/// </summary>
public class ExtractionCheckTests
{
    private static readonly Guid MeetingId = Guid.NewGuid();

    private static readonly Turn Turn0 = new(
        0, Duration.FromMilliseconds(0), Duration.FromMilliseconds(1000),
        AudioChannel.Loopback, "ch0:speaker_0", "Vamos a lanzar el viernes.");

    private static readonly Turn Turn1 = new(
        1, Duration.FromMilliseconds(1000), Duration.FromMilliseconds(2000),
        AudioChannel.Microphone, "ch1:speaker_0", "Yo escribo las notas de la version.");

    private static readonly Turn Turn2 = new(
        2, Duration.FromMilliseconds(2000), Duration.FromMilliseconds(3000),
        AudioChannel.Microphone, "ch1:speaker_0", "Quien se encarga del plan de reversion?");

    private static MeetingInput Prepared() => new(MeetingId, new string('a', 64), [Turn0, Turn1, Turn2]);

    [Fact]
    public void An_extraction_the_meeting_supports_word_for_word_is_accepted()
    {
        var prepared = Prepared();

        var verdict = ExtractionCheck.Of(Utf8(Valid()), prepared.Hash, prepared);

        verdict.IsAccepted.ShouldBeTrue();
        verdict.Refusals.ShouldBeEmpty();
        verdict.Accepted.ShouldNotBeNull().Abstract.ShouldBe("Se decidio la fecha de lanzamiento.");
    }

    [Fact]
    public void Output_that_is_not_the_schema_is_refused_and_nothing_else_is_asked()
    {
        var prepared = Prepared();
        var node = Valid();
        Remove(node, "abstract");

        // The input hash is wrong too, so an accepting implementation would find two things to
        // refuse. Only the shape problem may come back.
        var verdict = ExtractionCheck.Of(Utf8(node), new string('z', 64), prepared);

        verdict.IsAccepted.ShouldBeFalse();
        verdict.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "abstract", null)]);
    }

    [Fact]
    public void An_extraction_made_from_another_input_is_refused()
    {
        var prepared = Prepared();

        var verdict = ExtractionCheck.Of(Utf8(Valid()), new string('0', 64), prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.InputNotAsPrepared, "$", null)]);
    }

    [Fact]
    public void An_extraction_that_names_another_meeting_is_refused()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "meeting_id", JsonValue.Create(Guid.NewGuid().ToString()));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.AnotherMeeting, "meeting_id", null)]);
    }

    [Fact]
    public void A_participant_this_meeting_does_not_have_is_refused()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "participants", JsonNode.Parse("""["ch0:speaker_0", "ch0:speaker_1"]"""));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.SpeakerNotInTheMeeting, "participants[1]", null),
        ]);
    }

    [Theory]
    [InlineData("decisions[0]", "Lanzar el viernes.")]
    [InlineData("actions[0]", "Escribir las notas de la version.")]
    [InlineData("open_questions[0]", "Quien se encarga del plan de reversion?")]
    public void A_statement_that_cites_nothing_is_refused_naming_it(string itemPath, string text)
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, $"{itemPath}.evidence", null);

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NoEvidence, itemPath, text)]);
    }

    [Fact]
    public void A_citation_of_a_turn_this_meeting_does_not_have_is_refused()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "decisions[0].evidence.utterance_ordinal", JsonValue.Create(3));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(
            ExtractionCondition.NoSuchTurn, "decisions[0].evidence.utterance_ordinal", "Lanzar el viernes.")]);
    }

    [Fact]
    public void A_citation_whose_voice_is_not_in_the_meeting_is_refused_for_that()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "decisions[0].evidence.speaker_label", JsonValue.Create("ch0:speaker_9"));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(
            ExtractionCondition.SpeakerNotInTheMeeting, "decisions[0].evidence.speaker_label", "Lanzar el viernes.")]);
    }

    [Fact]
    public void A_citation_that_does_not_describe_its_turn_is_refused()
    {
        var prepared = Prepared();

        var wrongStart = Valid();
        Set(wrongStart, "decisions[0].evidence.start_ms", JsonValue.Create(1));
        ExtractionCheck.Of(Utf8(wrongStart), prepared.Hash, prepared).Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.NotTheTurnCited, "decisions[0].evidence.start_ms", "Lanzar el viernes."),
        ]);

        // A real speaker of this meeting, but the voice of turn 1 and 2 rather than turn 0's own.
        var wrongLabel = Valid();
        Set(wrongLabel, "decisions[0].evidence.speaker_label", JsonValue.Create("ch1:speaker_0"));
        ExtractionCheck.Of(Utf8(wrongLabel), prepared.Hash, prepared).Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.NotTheTurnCited, "decisions[0].evidence.speaker_label", "Lanzar el viernes."),
        ]);
    }

    [Fact]
    public void A_citation_that_ends_where_its_quote_ends_is_not_refused_for_that()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "decisions[0].evidence.end_ms", JsonValue.Create(400));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void A_quote_its_turn_does_not_say_is_refused()
    {
        var prepared = Prepared();
        var node = Valid();
        Set(node, "decisions[0].evidence.quoted_text", JsonValue.Create("escribo las notas"));

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([new ExtractionRefusal(
            ExtractionCondition.QuoteNotInTheTurn, "decisions[0].evidence.quoted_text", "Lanzar el viernes.")]);
    }

    [Fact]
    public void A_quote_is_matched_with_its_spacing_evened_out_and_nothing_else()
    {
        var prepared = Prepared();

        var spaced = Valid();
        Set(spaced, "decisions[0].evidence.quoted_text", JsonValue.Create("lanzar   el\nviernes."));
        ExtractionCheck.Of(Utf8(spaced), prepared.Hash, prepared).IsAccepted.ShouldBeTrue();

        var wrongCase = Valid();
        Set(wrongCase, "decisions[0].evidence.quoted_text", JsonValue.Create("Lanzar El Viernes."));
        ExtractionCheck.Of(Utf8(wrongCase), prepared.Hash, prepared).Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.QuoteNotInTheTurn, "decisions[0].evidence.quoted_text", "Lanzar el viernes."),
        ]);
    }

    [Fact]
    public void Every_refusal_is_reported_and_not_only_the_first()
    {
        var prepared = Prepared();
        var node = Valid();

        // A second action, cited nowhere, added after the first so it sits at actions[1].
        node["actions"]!.AsArray().Add(JsonNode.Parse(
            """{ "statement": "Revisar el presupuesto.", "evidence": null }"""));
        Set(node, "participants", JsonNode.Parse("""["ch0:speaker_0", "ch0:speaker_1"]"""));
        Remove(node, "decisions[0].evidence");

        var verdict = ExtractionCheck.Of(Utf8(node), prepared.Hash, prepared);

        verdict.Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.SpeakerNotInTheMeeting, "participants[1]", null),
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "actions[1]", "Revisar el presupuesto."),
        ]);
    }

    /// <summary>An extraction this meeting supports word for word, ready to be broken one field at a time.</summary>
    private static JsonNode Valid() => JsonNode.Parse($$"""
        {
          "schema_version": "1",
          "meeting_id": "{{MeetingId}}",
          "abstract": "Se decidio la fecha de lanzamiento.",
          "summary": "",
          "participants": ["ch0:speaker_0", "ch1:speaker_0"],
          "decisions": [
            {
              "statement": "Lanzar el viernes.",
              "evidence": {
                "utterance_ordinal": 0,
                "start_ms": 0,
                "end_ms": 1000,
                "speaker_label": "ch0:speaker_0",
                "quoted_text": "lanzar el viernes."
              }
            }
          ],
          "actions": [
            {
              "statement": "Escribir las notas de la version.",
              "due_date": "2026-03-10",
              "evidence": {
                "utterance_ordinal": 1,
                "start_ms": 1000,
                "end_ms": 2000,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "escribo las notas"
              }
            }
          ],
          "open_questions": [
            {
              "question": "Quien se encarga del plan de reversion?",
              "evidence": {
                "utterance_ordinal": 2,
                "start_ms": 2000,
                "end_ms": 3000,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "plan de reversion"
              }
            }
          ]
        }
        """)!;
}
