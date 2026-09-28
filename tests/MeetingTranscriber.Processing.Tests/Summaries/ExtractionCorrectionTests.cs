using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Processing.Summaries;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// Whether a refusal may be handed back once, what the correction is asked for, and whether it
/// still brings back a statement it was asked to remove.
/// </summary>
public class ExtractionCorrectionTests
{
    public static TheoryData<ExtractionCondition, bool> EveryConditionAloneAgainstWhetherItMayBeHandedBack()
    {
        var data = new TheoryData<ExtractionCondition, bool>();
        foreach (var condition in Enum.GetValues<ExtractionCondition>())
        {
            data.Add(condition, condition is not (
                ExtractionCondition.InputNotAsPrepared
                or ExtractionCondition.AnotherMeeting
                or ExtractionCondition.CitedAgainElsewhere));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryConditionAloneAgainstWhetherItMayBeHandedBack))]
    public void Only_a_refusal_of_the_shape_or_of_what_the_meeting_supports_is_handed_back(
        ExtractionCondition condition, bool mayBeHandedBack)
    {
        ExtractionCorrection.MayBeHandedBack([new ExtractionRefusal(condition, "$", null)])
            .ShouldBe(mayBeHandedBack);
    }

    [Fact]
    public void One_refusal_that_cannot_be_corrected_rules_out_the_whole_hand_back()
    {
        ExtractionCorrection.MayBeHandedBack([
                new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
                new ExtractionRefusal(ExtractionCondition.AnotherMeeting, "meeting_id", null),
            ])
            .ShouldBeFalse();
    }

    [Fact]
    public void What_was_wrong_asks_for_the_shape_to_be_fixed_and_an_unsupported_statement_removed()
    {
        var text = ExtractionCorrection.WhatWasWrong([
            new ExtractionRefusal(ExtractionCondition.NotTheSchema, "actions[0].due_date", null),
            new ExtractionRefusal(ExtractionCondition.SpeakerNotInTheMeeting, "participants[1]", null),
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
            new ExtractionRefusal(
                ExtractionCondition.NoSuchTurn, "actions[0].evidence.utterance_ordinal", "Mandar el correo."),
            new ExtractionRefusal(
                ExtractionCondition.SpeakerNotInTheMeeting,
                "open_questions[0].evidence.speaker_label",
                "Quien paga la publicidad?"),
            new ExtractionRefusal(
                ExtractionCondition.NotTheTurnCited, "decisions[1].evidence.start_ms", "Otra decision."),
            new ExtractionRefusal(
                ExtractionCondition.QuoteNotInTheTurn, "decisions[2].evidence.quoted_text", "Tercera decision."),
        ]);

        text.ShouldBe(string.Join('\n', [
            "- At actions[0].due_date: this is not in the shape schema.md describes. Fix it.",
            "- At participants[1]: this voice is not in meeting.json. Remove it from participants.",
            "- decisions[0] \"Lanzar el viernes.\": it cites nothing. Remove this statement.",
            "- actions[0] \"Mandar el correo.\": it cites a turn meeting.json does not have. Remove this statement.",
            "- open_questions[0] \"Quien paga la publicidad?\": it names a voice meeting.json does not have. "
                + "Remove this statement.",
            "- decisions[1] \"Otra decision.\": its start_ms or speaker_label is not the cited turn's own. "
                + "Remove this statement.",
            "- decisions[2] \"Tercera decision.\": the quoted words are not in the cited turn. "
                + "Remove this statement.",
        ]));
    }

    [Fact]
    public void A_statement_handed_back_to_be_removed_that_comes_back_citing_something_else_is_refused()
    {
        var handedBack = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
        };

        // Brought back as an action, with different whitespace and case, and a different ordinal
        // than the refusal named — none of which is what "the same statement" means here.
        var corrected = Document(
            decisions: [],
            actions: ["  LANZAR   el viernes.  "],
            questions: []);

        var judged = ExtractionCorrection.Judge(handedBack, corrected, found: []);

        judged.ShouldBe([
            new ExtractionRefusal(ExtractionCondition.CitedAgainElsewhere, "actions[0]", "  LANZAR   el viernes.  "),
        ]);
    }

    [Fact]
    public void A_match_already_refused_for_its_own_reason_gains_no_second_refusal()
    {
        var handedBack = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
        };

        var corrected = Document(decisions: ["Lanzar el viernes."], actions: [], questions: []);
        var found = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
        };

        ExtractionCorrection.Judge(handedBack, corrected, found).ShouldBe(found);
    }

    [Fact]
    public void A_statement_not_brought_back_at_all_judges_clean()
    {
        var handedBack = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
        };

        var corrected = Document(decisions: [], actions: [], questions: []);

        ExtractionCorrection.Judge(handedBack, corrected, found: []).ShouldBeEmpty();
    }

    [Fact]
    public void A_refusal_about_the_document_as_a_whole_names_nothing_to_judge()
    {
        var handedBack = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NotTheSchema, "$", null),
        };

        var corrected = Document(decisions: ["anything"], actions: [], questions: []);

        ExtractionCorrection.Judge(handedBack, corrected, found: []).ShouldBeEmpty();
    }

    [Fact]
    public void With_no_document_to_search_the_second_verdict_is_returned_as_it_stood()
    {
        var handedBack = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes."),
        };

        var found = new[]
        {
            new ExtractionRefusal(ExtractionCondition.NotTheSchema, "$", null),
        };

        ExtractionCorrection.Judge(handedBack, corrected: null, found).ShouldBe(found);
    }

    private static ExtractionDocument Document(
        IReadOnlyList<string> decisions, IReadOnlyList<string> actions, IReadOnlyList<string> questions) => new(
        SchemaVersion: "1",
        MeetingId: Guid.NewGuid(),
        Abstract: "what it was about",
        Summary: string.Empty,
        Participants: [],
        Decisions: [.. decisions.Select(statement => new ExtractedDecision(statement, Evidence: null))],
        Actions: [.. actions.Select(statement => new ExtractedAction(statement, DueDate: null, Evidence: null))],
        OpenQuestions: [.. questions.Select(question => new ExtractedQuestion(question, Evidence: null))]);
}
