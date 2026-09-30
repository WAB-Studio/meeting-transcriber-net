using System.Collections.Frozen;
using System.Text.RegularExpressions;

using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// Whether a refused extraction can be handed back for one correction, what the correction is
/// asked to fix, and whether what came back still brings back a statement it was asked to remove.
/// </summary>
/// <remarks>
/// Three questions and not one type per question, because all three read the same two lists: the
/// refusals a first attempt earned, and — for the third — what a second attempt did about them.
/// Nothing here files anything; the door that does is <c>ExtractionIntake</c>, which calls each of
/// these in turn and writes what they answer.
/// </remarks>
public static class ExtractionCorrection
{
    /// <summary>
    /// The refusals that name a shape problem or something the meeting does not support — every
    /// one of them fixable by asking again with the same input.
    /// </summary>
    private static readonly FrozenSet<ExtractionCondition> Correctable = new[]
    {
        ExtractionCondition.NotTheSchema,
        ExtractionCondition.SpeakerNotInTheMeeting,
        ExtractionCondition.NoEvidence,
        ExtractionCondition.NoSuchTurn,
        ExtractionCondition.NotTheTurnCited,
        ExtractionCondition.QuoteNotInTheTurn,
    }.ToFrozenSet();

    /// <summary>A statement's or question's own path, with or without its <c>.evidence</c> suffix.</summary>
    private static readonly Regex StatementPath =
        new(@"^(?:decisions|actions|open_questions)\[\d+\](?:\.evidence(?:\..+)?)?$", RegexOptions.Compiled);

    /// <summary>The section and index off the front of a statement's or question's own path.</summary>
    private static readonly Regex SectionAndIndex =
        new(@"^(decisions|actions|open_questions)\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Whether a refused run may be handed back once for correction, rather than failing its job.
    /// </summary>
    /// <remarks>
    /// Every refusal has to be one this can be corrected without a new attempt at the whole
    /// meeting: <see cref="ExtractionCondition.InputNotAsPrepared"/> is not, because the meeting has
    /// moved and correcting an answer about the meeting as it stood is not the fix.
    /// <see cref="ExtractionCondition.AnotherMeeting"/> is not, because a <c>meeting_id</c> that
    /// names something else says the whole answer was made about another meeting, and nothing in it
    /// can be trusted to belong to this one. <see cref="ExtractionCondition.CitedAgainElsewhere"/> is
    /// not, because it exists only on a correction and a correction is never corrected again.
    /// <para>
    /// This asks only about the refusals it is handed, and cannot itself tell a first run's
    /// refusals from a correction's: the caller that files a correction — <c>ExtractionIntake</c> —
    /// is what must never call this a second time over one job, the way it must never file a second
    /// correction at all. The corpus holds that promise structurally, at
    /// <c>ux_extraction_runs_one_correction_per_job</c> (Decides 9): a second correction is refused
    /// by the write, whatever this method answered about it.
    /// </para>
    /// </remarks>
    public static bool MayBeHandedBack(IReadOnlyList<ExtractionRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        return refusals.Count > 0 && refusals.All(refusal => Correctable.Contains(refusal.Condition));
    }

    /// <summary>
    /// What <c>what-was-wrong.md</c> says: one line per refusal, in order, in English — the
    /// language the workspace's other files are in, and never the meeting's own.
    /// </summary>
    public static string WhatWasWrong(IReadOnlyList<ExtractionRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        return string.Join('\n', refusals.Select(Line));
    }

    /// <summary>
    /// Whether a corrected answer still brings back a statement it was asked to remove, citing
    /// something else. Adds <see cref="ExtractionCondition.CitedAgainElsewhere"/> where it does,
    /// and returns <paramref name="found"/> unchanged where it does not.
    /// </summary>
    /// <param name="handedBack">The refusals the first attempt earned — what was asked to be fixed.</param>
    /// <param name="corrected">
    /// The second attempt's document, once it read as the shape and nothing else, or <c>null</c>
    /// when it did not: there is nothing of its own to search in that case, and this judges nothing.
    /// </param>
    /// <param name="found">What <c>ExtractionCheck.Of</c> found wrong with the second attempt.</param>
    /// <remarks>
    /// <para>
    /// Only the first attempt's refusals that name one statement or question — a path of
    /// <c>decisions[i]</c>, <c>actions[i]</c> or <c>open_questions[i]</c>, with or without an
    /// <c>.evidence…</c> suffix — are asked about here. A shape refusal about the document as a
    /// whole names nothing to have been brought back.
    /// </para>
    /// <para>
    /// Texts are compared after collapsing whitespace, the way <c>ExtractionCheck</c> does, and
    /// ignoring case. A decision brought back as an action is still the same unsupported statement
    /// with a new citation, which is why every one of the three sections is searched for each one.
    /// </para>
    /// <para>
    /// A match <c>ExtractionCheck</c> already refused at its own path gains nothing: a statement
    /// records only its first failure, and that failure already says the citation does not hold up.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ExtractionRefusal> Judge(
        IReadOnlyList<ExtractionRefusal> handedBack, ExtractionDocument? corrected, IReadOnlyList<ExtractionRefusal> found)
    {
        ArgumentNullException.ThrowIfNull(handedBack);
        ArgumentNullException.ThrowIfNull(found);

        if (corrected is null)
        {
            return found;
        }

        var alreadyRefused = found.Select(refusal => refusal.Path).ToHashSet(StringComparer.Ordinal);
        List<ExtractionRefusal>? broughtBack = null;

        foreach (var refusal in handedBack)
        {
            if (refusal.Statement is not { } removed || !StatementPath.IsMatch(refusal.Path))
            {
                continue;
            }

            foreach (var (path, text) in Matching(corrected, removed))
            {
                if (!alreadyRefused.Add(path))
                {
                    continue;
                }

                (broughtBack ??= []).Add(new ExtractionRefusal(ExtractionCondition.CitedAgainElsewhere, path, text));
            }
        }

        return broughtBack is null ? found : [.. found, .. broughtBack];
    }

    /// <summary>Every statement or question in the corrected document that reads as the same as <paramref name="text"/>.</summary>
    private static IEnumerable<(string Path, string Text)> Matching(ExtractionDocument corrected, string text)
    {
        for (var index = 0; index < corrected.Decisions.Count; index++)
        {
            var statement = corrected.Decisions[index].Statement;
            if (SameStatement(statement, text))
            {
                yield return ($"decisions[{index}]", statement);
            }
        }

        for (var index = 0; index < corrected.Actions.Count; index++)
        {
            var statement = corrected.Actions[index].Statement;
            if (SameStatement(statement, text))
            {
                yield return ($"actions[{index}]", statement);
            }
        }

        for (var index = 0; index < corrected.OpenQuestions.Count; index++)
        {
            var question = corrected.OpenQuestions[index].Question;
            if (SameStatement(question, text))
            {
                yield return ($"open_questions[{index}]", question);
            }
        }
    }

    private static bool SameStatement(string one, string other) =>
        string.Equals(ExtractionCheck.EvenOut(one), ExtractionCheck.EvenOut(other), StringComparison.OrdinalIgnoreCase);

    private static string Line(ExtractionRefusal refusal)
    {
        if (refusal.Condition is ExtractionCondition.NotTheSchema)
        {
            return $"- At {refusal.Path}: this is not in the shape schema.md describes. Fix it.";
        }

        if (refusal.Condition is ExtractionCondition.SpeakerNotInTheMeeting
            && refusal.Path.StartsWith("participants[", StringComparison.Ordinal))
        {
            return $"- At {refusal.Path}: this voice is not in meeting.json. Remove it from participants.";
        }

        return StatementLine(refusal);
    }

    private static string StatementLine(ExtractionRefusal refusal)
    {
        var match = SectionAndIndex.Match(refusal.Path);
        var sectionAndIndex = match.Success ? $"{match.Groups[1].Value}[{match.Groups[2].Value}]" : refusal.Path;

        return $"- {sectionAndIndex} \"{refusal.Statement}\": {Why(refusal.Condition)}. Remove this statement.";
    }

    private static string Why(ExtractionCondition condition) => condition switch
    {
        ExtractionCondition.NoEvidence => "it cites nothing",
        ExtractionCondition.NoSuchTurn => "it cites a turn meeting.json does not have",
        ExtractionCondition.SpeakerNotInTheMeeting => "it names a voice meeting.json does not have",
        ExtractionCondition.NotTheTurnCited => "its start_ms or speaker_label is not the cited turn's own",
        ExtractionCondition.QuoteNotInTheTurn => "the quoted words are not in the cited turn",
        _ => throw new ArgumentOutOfRangeException(
            nameof(condition), condition, "No line for a condition that is never handed back."),
    };
}
