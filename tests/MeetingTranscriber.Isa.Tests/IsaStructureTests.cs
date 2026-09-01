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
    /// The most prose a `## Verification` stub may carry once its pointers are taken out.
    /// </summary>
    /// <remarks>
    /// Measured over all 127 stubs on `main` at 3551c12 on 2026-08-26, and it is prose rather
    /// than line length because the two disagree about the honest cases. ISC-50 is a 668-character
    /// line of almost nothing but fully-qualified test names — seven of them, and this repo names
    /// a test in a whole sentence — which is exactly what the format spec asks a stub to be. A
    /// limit over the line would have to fail it to catch anything, while still passing a tighter
    /// essay. With the backticked spans out ISC-50 is 133, and the file turns out bimodal: 91
    /// stubs at 541 or below, the other 36 at 606 or above, and nothing in between. This number
    /// sits in that 65-character gap, 34 above the highest of the 91 and 31 below the lowest of
    /// the 36 — near enough the middle, and it is the gap between the two populations rather than
    /// the widest gap in the file, since there are wider ones higher up inside the essays.
    ///
    /// The 91 are the population that matters, because nobody had ever flagged one of them: their
    /// median is 22 and their largest is ISC-114.2 at 541. So this limit is the one thing a hard
    /// gate has to be — above every stub the people writing them thought was fine — and it is not
    /// the target. The target is the median. A stub written up against this number is a stub that
    /// read the ceiling as the standard, which is the habit the gate exists to break.
    ///
    /// The headroom clears the one exception the format spec named when it argued this could not
    /// be a character count: a claim closed on a hand run has no test to name and carries numbers
    /// CI will never produce again. Those two stubs are ISC-73.1 at 385 and ISC-118 at 353 — the
    /// only two in the file with no backticked span at all, which is what that exception looks
    /// like from here, and both well inside.
    ///
    /// Every number above is 3551c12's file and not this one. The pass that brought the 36 under
    /// the limit filled the gap it was derived from in: the file is now continuous from the low
    /// 300s to 541, because a stub collapsed to fit lands nearer the ceiling than one that was
    /// never over it. So the derivation reproduces only against that commit, and the number
    /// cannot be re-derived from the file it now runs over — which is an argument for leaving it
    /// where it is rather than for moving it, since re-deriving would ratchet it upward off the
    /// stubs the ratchet produced.
    /// </remarks>
    private const int LongestStubProse = 575;

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
        var stubs = isa.Stubs.Select(stub => stub.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var claim in isa.Claims.Where(claim => claim.Closed))
        {
            stubs.ShouldContain(claim.Id, $"{claim.Id} is marked closed with no Verification stub. "
                + "Assertion without evidence is not closure.");
        }
    }

    /// <summary>
    /// Stated as what a stub's ID must be rather than as what it must not be. The negative form
    /// this replaces — no stub sits on an open claim — read the same on every real file and let
    /// through the one that matters: an ID belonging to no claim at all is in neither the open set
    /// nor the closed one, so `- ISC-149.9 — …` parked beside ISC-149's own stub was evidence for
    /// nothing, and it reads to anybody skimming as a sub-claim that exists.
    /// </summary>
    [Fact]
    public void Every_stub_names_a_claim_that_is_closed()
    {
        var closed = isa.Claims.Where(claim => claim.Closed).Select(claim => claim.Id)
            .ToHashSet(StringComparer.Ordinal);

        isa.Stubs.Select(stub => stub.Id).Where(id => !closed.Contains(id)).ShouldBeEmpty(
            "a Verification stub is what closed a claim, so its ID is a claim above and that "
            + "claim is marked closed. An ID no claim carries is evidence for nothing; an ID on "
            + "an open claim is evidence for something the file says did not happen.");
    }

    [Fact]
    public void One_claim_closes_on_one_stub()
    {
        // The dodge the size gate below creates, and the reason it is written down the moment the
        // gate is: a stub too long for check 11 passes it by being cut in half under the same ID,
        // and every other check here is happy — the claim still has its evidence, and both halves
        // still parse. A claim closes on one line or the retelling has just moved. Cutting it in
        // half under two IDs instead is the check above: the second half has to name a claim.
        isa.Stubs.Select(stub => stub.Id).ShouldBeUnique();
    }

    /// <summary>
    /// `## Verification` is append-only, and until this check nothing bounded what got appended.
    /// The rule that a stub is a pointer lived only in the format spec and the skill, which is
    /// the one place whoever is appending a single line never opens — so each writer read the
    /// neighbours instead, and the neighbours only ever got longer. On 2026-08-26 the eight
    /// largest lines ran from 2355 to 3333 characters against a spec whose example is 40.
    /// </summary>
    [Fact]
    public void A_stub_points_at_its_evidence_instead_of_retelling_it()
    {
        // Checked first because the gate below is measured through it: the one way that measure
        // fails open is a dropped backtick welding two pointers into a single span and taking the
        // prose between them out of the count.
        isa.Stubs.Where(stub => !stub.PointersClose).Select(stub => stub.Id).ShouldBeEmpty(
            "a stub with an odd number of backticks does not measure what it looks like it "
            + "measures, so the size gate reads it as shorter than it is.");

        var overlong = isa.Stubs
            .Where(stub => stub.Prose.Length > LongestStubProse)
            .Select(stub => $"{stub.Id} ({stub.Prose.Length})")
            .ToArray();

        overlong.ShouldBeEmpty(
            $"a Verification stub points at the evidence; past {LongestStubProse} characters of "
            + "prose it is retelling it. Test names, commands and paths are free here, so name "
            + "every probe precisely — it is the sentences around them that have somewhere "
            + "better to be. What explains one file is a comment in that file, what argues the "
            + "design is `arquitectura.md`, and how this session got there is the commit message "
            + "and the PR, which is where anybody following the merge back is already standing.");
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
