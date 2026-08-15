using System.Globalization;

namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// The structural gate on ISA.md. Every check here is one a person cannot argue with, which is
/// the only kind worth blocking a build over.
/// </summary>
/// <remarks>
/// What is deliberately absent is the count-shaped judgement — "enough claims", "one anti-claim
/// per feature". A count that blocks just gets manufactured, and a claim written to satisfy a
/// counter is worse than a missing one because it reads as coverage. Those live in the skill's
/// CheckCompleteness workflow, which reports and does not block.
/// </remarks>
public class IsaStructureTests
{
    /// <summary>
    /// The eight lists of the MeetingTranscriber space, plus the em dash F0 carries because
    /// cross-cutting work belongs to no single list.
    /// </summary>
    /// <remarks>
    /// Hardcoded, and it has to be: tests never touch the network, so the board cannot be asked.
    /// This catches a `Board:` line invented or mistyped in the ISA, which is the half inside this
    /// repo's control. A list renamed in ClickUp goes unnoticed until somebody edits the ISA — the
    /// rename is the moment to update this array, and the skill says so.
    /// </remarks>
    private static readonly string[] BoardLists =
    [
        "—",
        "0 · Contratos y caracterización",
        "1 · Núcleo .NET desde artefactos",
        "2 · Spike y motor de audio",
        "3 · Grabador WinUI",
        "4 · Deepgram BYOK",
        "5 · Summaries",
        "6 · Conocimiento local",
        "7 · Distribución y backup",
    ];

    private readonly IsaDocument isa = IsaDocument.Read();

    [Fact]
    public void The_progress_count_is_the_claims_and_not_an_opinion()
    {
        var closed = isa.Claims.Count(claim => claim.Closed);

        isa.Frontmatter.ShouldContainKey("progress");
        isa.Frontmatter["progress"].ShouldBe(
            $"{closed}/{isa.Claims.Count}",
            "progress is a count of closed claims over total, recomputed here. Someone edited a "
            + "claim and left the number, or wrote the number they wanted.");
    }

    [Fact]
    public void A_complete_ISA_has_every_claim_closed_and_no_fog_left()
    {
        if (isa.Frontmatter.GetValueOrDefault("phase") != "complete")
        {
            return;
        }

        isa.Claims.ShouldAllBe(claim => claim.Closed);
        isa.Fog.ShouldBeEmpty(
            "fog graduates to a claim or is dropped; it cannot be carried past the close.");
    }

    [Fact]
    public void Every_feature_says_why_it_exists_and_which_list_it_is_worked_from()
    {
        isa.Features.ShouldNotBeEmpty();

        foreach (var feature in isa.Features)
        {
            feature.Why.ShouldNotBeNullOrWhiteSpace(
                $"{feature.Id} · {feature.Name}: the Why line states what the name and the claims "
                + "do not. Without it the block is a folder.");
            BoardLists.ShouldContain(feature.Board, $"{feature.Id} · {feature.Name}: "
                + $"'{feature.Board}' is not a list in the MeetingTranscriber space.");
        }
    }

    [Fact]
    public void No_claim_id_is_used_twice()
    {
        // IDs never renumber, so a duplicate means a split reused a number rather than nesting
        // under it — and Verification and the board both key on the ID.
        isa.Claims.Select(claim => claim.Id).ShouldBeUnique();
    }

    [Fact]
    public void No_number_is_missing_between_the_first_claim_and_the_last()
    {
        // The other hand of the check above. A duplicate is a number issued twice; a hole is a
        // claim deleted instead of tombstoned, or a run of them shifted down to close one — and a
        // shift re-points every board task, commit and stub that named the old number at somebody
        // else's claim, silently, because each of those still reads as a valid ID. Between the
        // two checks, the only shape left is the one where an ID means forever what it meant when
        // it was issued.
        var levels = isa.Claims
            .Select(claim => claim.Id["ISC-".Length..].Split('.'))
            .GroupBy(
                parts => string.Join('.', parts[..^1]),
                parts => int.Parse(parts[^1], CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

        foreach (var level in levels)
        {
            var prefix = level.Key.Length == 0 ? "ISC-" : $"ISC-{level.Key}.";
            var missing = Enumerable.Range(1, level.Max()).Except(level)
                .Select(number => prefix + number.ToString(CultureInfo.InvariantCulture));

            missing.ShouldBeEmpty(
                "a claim is gone without leaving a tombstone, or the ones after it were "
                + "renumbered down over the hole. Either way an ID that a board task, a commit or "
                + "a stub points at now means something other than what it meant.");
        }
    }

    [Fact]
    public void Every_closed_claim_points_at_what_closed_it()
    {
        var stubs = isa.VerificationStubs.ToHashSet(StringComparer.Ordinal);

        foreach (var claim in isa.Claims.Where(claim => claim.Closed))
        {
            stubs.ShouldContain(claim.Id, $"{claim.Id} is marked closed with no Verification stub. "
                + "Assertion without evidence is not closure.");
        }
    }

    [Fact]
    public void Nothing_is_verified_that_is_not_closed()
    {
        var open = isa.Claims.Where(claim => !claim.Closed).Select(claim => claim.Id)
            .ToHashSet(StringComparer.Ordinal);

        isa.VerificationStubs.Where(open.Contains).ShouldBeEmpty(
            "a stub under an open claim is evidence for something the file says did not happen; "
            + "one of the two is wrong.");
    }

    /// <summary>
    /// The three checks below all answer one question: did every line of a section survive being
    /// read? A parser that skips what it does not recognise makes a section that is half prose
    /// look exactly like a section that is whole — which is how an append landed on top of the
    /// first line of an existing entry on 2026-08-13, welding two of them together, and passed a
    /// green gate, an adversarial review and a merge into `main`.
    /// </summary>
    [Fact]
    public void Nothing_in_a_feature_block_is_a_bullet_that_is_not_a_claim()
    {
        isa.StrayFeatureBullets.ShouldBeEmpty(
            "a feature block holds claims and nothing else. A bullet here parsed as no claim, so "
            + "it counts towards nothing and closes nothing while reading like it does.");
    }

    [Fact]
    public void Every_line_of_the_evidence_is_a_stub_for_one_claim()
    {
        isa.StrayVerificationLines.ShouldBeEmpty(
            "a line under Verification that is not `- ISC-N — …` is evidence the gate cannot read, "
            + "so no claim is held up by it.");
    }

    [Fact]
    public void A_learning_entry_is_whole_or_it_is_not_written()
    {
        string[] shape = ["conjecture", "refuted-by", "learned", "criterion-now"];

        // The four run in order and repeat, so entry N's labels are positions 4N..4N+3. A missing
        // or spliced bullet shifts everything after it and the first mismatch names the position.
        var expected = Enumerable.Range(0, isa.LearningLabels.Count)
            .Select(position => shape[position % shape.Length]);

        isa.LearningLabels.ShouldBe(
            [.. expected],
            "Learning is conjecture / refuted-by / learned / criterion-now, in that order, and a "
            + "partial entry does not get written — a refutation with no criterion changed nothing.");
        (isa.LearningLabels.Count % shape.Length).ShouldBe(
            0,
            $"{isa.LearningLabels.Count} bullets is not whole entries of {shape.Length}.");
    }

    [Fact]
    public void The_sections_appear_in_the_order_the_format_fixes()
    {
        string[] expected =
        [
            "## Goal",
            "## Features",
            "## Not yet specified",
            "## Learning",
            "## Verification",
        ];

        var present = isa.Lines
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToArray();

        // Optional sections may be absent; what may not happen is two of them swapping places, or
        // a sixth appearing. That second half is what holds `## Decisions` out of this file now
        // that it has been retired — a decision has somewhere better to be read, and a section
        // nothing refuses is one that comes back the next time somebody has one to write down.
        present.ShouldBeSubsetOf(expected);
        present.ShouldBe([.. expected.Where(present.Contains)]);
    }
}
