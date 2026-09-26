using System.Text.Json.Nodes;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Summaries;

using static MeetingTranscriber.Processing.Tests.Summaries.JsonNodeMutations;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// Reading an extraction's output against schema version <c>"1"</c>: every field, every way the
/// shape can be broken, and nothing beyond the shape.
/// </summary>
public class ExtractionReaderTests
{
    [Fact]
    public void A_document_in_the_extraction_s_shape_is_read_whole()
    {
        var meetingId = Guid.NewGuid();
        var read = ExtractionReader.Read(Utf8(Valid(meetingId)));

        read.Refusals.ShouldBeEmpty();
        var document = read.Document.ShouldNotBeNull();
        document.SchemaVersion.ShouldBe("1");
        document.MeetingId.ShouldBe(meetingId);
        document.Abstract.ShouldBe("We decided the launch date.");
        document.Summary.ShouldBe(string.Empty);
        document.Participants.ShouldBe(["ch1:speaker_0"]);

        document.Decisions.Count.ShouldBe(1);
        document.Decisions[0].Statement.ShouldBe("Ship on Friday.");
        var decisionEvidence = document.Decisions[0].Evidence.ShouldNotBeNull();
        decisionEvidence.UtteranceOrdinal.ShouldBe(0);
        decisionEvidence.Start.ShouldBe(Duration.FromMilliseconds(0));
        decisionEvidence.End.ShouldBe(Duration.FromMilliseconds(500));
        decisionEvidence.SpeakerLabel.ShouldBe("ch1:speaker_0");
        decisionEvidence.QuotedText.ShouldBe("Let's ship on Friday.");

        document.Actions.Count.ShouldBe(1);
        document.Actions[0].Statement.ShouldBe("Write the release notes.");
        document.Actions[0].DueDate.ShouldBe(new DateOnly(2026, 3, 10));
        document.Actions[0].Evidence.ShouldNotBeNull();

        document.OpenQuestions.Count.ShouldBe(1);
        document.OpenQuestions[0].Question.ShouldBe("Who owns the rollback plan?");
        document.OpenQuestions[0].Evidence.ShouldBeNull();
    }

    [Fact]
    public void Output_that_is_not_json_is_refused_as_not_the_schema_at_the_top()
    {
        var read = ExtractionReader.Read("not json at all"u8);

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "$", null)]);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("meeting_id")]
    [InlineData("abstract")]
    [InlineData("summary")]
    [InlineData("participants")]
    [InlineData("decisions")]
    [InlineData("actions")]
    [InlineData("open_questions")]
    [InlineData("decisions[0].statement")]
    [InlineData("decisions[0].evidence.utterance_ordinal")]
    [InlineData("decisions[0].evidence.start_ms")]
    [InlineData("decisions[0].evidence.end_ms")]
    [InlineData("decisions[0].evidence.speaker_label")]
    [InlineData("decisions[0].evidence.quoted_text")]
    [InlineData("open_questions[0].question")]
    public void A_field_the_shape_requires_and_the_output_leaves_out_is_refused_naming_where(string path)
    {
        var node = Valid(Guid.NewGuid());
        Remove(node, path);

        var read = ExtractionReader.Read(Utf8(node));

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, path, null)]);
    }

    [Theory]
    [InlineData("abstract", "   ")]
    [InlineData("decisions[0].statement", "")]
    [InlineData("open_questions[0].question", "   \t  ")]
    public void A_field_the_shape_requires_to_say_something_is_refused_when_it_is_blank(string path, string blank)
    {
        var node = Valid(Guid.NewGuid());
        Set(node, path, JsonValue.Create(blank));

        var read = ExtractionReader.Read(Utf8(node));

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, path, null)]);
    }

    [Theory]
    [InlineData("topics")]
    [InlineData("decisions[0].confidence")]
    [InlineData("decisions[0].evidence.utterance_id")]
    public void A_field_nobody_asked_for_is_refused_naming_where(string path)
    {
        var node = Valid(Guid.NewGuid());
        Set(node, path, JsonValue.Create("unexpected"));

        var read = ExtractionReader.Read(Utf8(node));

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, path, null)]);
    }

    [Theory]
    [InlineData("decisions[0].evidence.start_ms", "\"soon\"")]
    [InlineData("participants", "\"ch1:speaker_0\"")]
    [InlineData("actions[0].due_date", "\"next week\"")]
    [InlineData("decisions[0].evidence.utterance_ordinal", "1.5")]
    public void A_field_of_the_wrong_kind_is_refused_naming_where(string path, string replacementJson)
    {
        var node = Valid(Guid.NewGuid());
        Set(node, path, JsonNode.Parse(replacementJson));

        var read = ExtractionReader.Read(Utf8(node));

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, path, null)]);
    }

    [Fact]
    public void A_schema_version_this_build_does_not_read_is_refused()
    {
        var node = Valid(Guid.NewGuid());
        Set(node, "schema_version", JsonValue.Create("2"));

        var read = ExtractionReader.Read(Utf8(node));

        read.Document.ShouldBeNull();
        read.Refusals.ShouldBe([new ExtractionRefusal(ExtractionCondition.NotTheSchema, "schema_version", null)]);
    }

    [Fact]
    public void An_offset_that_is_negative_or_ends_before_it_starts_is_refused()
    {
        var negative = Valid(Guid.NewGuid());
        Set(negative, "decisions[0].evidence.start_ms", JsonValue.Create(-1));
        ExtractionReader.Read(Utf8(negative)).Refusals.ShouldBe(
            [new ExtractionRefusal(ExtractionCondition.NotTheSchema, "decisions[0].evidence.start_ms", null)]);

        var backwards = Valid(Guid.NewGuid());
        Set(backwards, "decisions[0].evidence.start_ms", JsonValue.Create(500));
        Set(backwards, "decisions[0].evidence.end_ms", JsonValue.Create(100));
        ExtractionReader.Read(Utf8(backwards)).Refusals.ShouldBe(
            [new ExtractionRefusal(ExtractionCondition.NotTheSchema, "decisions[0].evidence.end_ms", null)]);
    }

    [Fact]
    public void Evidence_left_out_or_null_is_read_as_none_and_not_as_a_shape_problem()
    {
        var absent = Valid(Guid.NewGuid());
        Remove(absent, "decisions[0].evidence");
        var readAbsent = ExtractionReader.Read(Utf8(absent));
        readAbsent.Refusals.ShouldBeEmpty();
        readAbsent.Document.ShouldNotBeNull().Decisions[0].Evidence.ShouldBeNull();

        var isNull = Valid(Guid.NewGuid());
        Set(isNull, "decisions[0].evidence", null);
        var readNull = ExtractionReader.Read(Utf8(isNull));
        readNull.Refusals.ShouldBeEmpty();
        readNull.Document.ShouldNotBeNull().Decisions[0].Evidence.ShouldBeNull();
    }

    [Fact]
    public void Every_shape_problem_in_one_output_is_reported_in_document_order()
    {
        var node = Valid(Guid.NewGuid());
        Remove(node, "abstract");
        Remove(node, "decisions");

        var read = ExtractionReader.Read(Utf8(node));

        read.Refusals.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.NotTheSchema, "abstract", null),
            new ExtractionRefusal(ExtractionCondition.NotTheSchema, "decisions", null),
        ]);
    }

    /// <summary>One of everything the shape names, valid, so a theory can break one field at a time.</summary>
    private static JsonNode Valid(Guid meetingId) => JsonNode.Parse($$"""
        {
          "schema_version": "1",
          "meeting_id": "{{meetingId}}",
          "abstract": "We decided the launch date.",
          "summary": "",
          "participants": ["ch1:speaker_0"],
          "decisions": [
            {
              "statement": "Ship on Friday.",
              "evidence": {
                "utterance_ordinal": 0,
                "start_ms": 0,
                "end_ms": 500,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "Let's ship on Friday."
              }
            }
          ],
          "actions": [
            {
              "statement": "Write the release notes.",
              "due_date": "2026-03-10",
              "evidence": {
                "utterance_ordinal": 1,
                "start_ms": 1000,
                "end_ms": 1500,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "I will write the release notes."
              }
            }
          ],
          "open_questions": [
            { "question": "Who owns the rollback plan?", "evidence": null }
          ]
        }
        """)!;
}
