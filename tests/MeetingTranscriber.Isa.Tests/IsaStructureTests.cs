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
    /// What a stub's pointers may come to on top of <see cref="LongestStubProse"/>. The two added
    /// together are the longest evidence a stub may carry, whatever it does with its backticks.
    /// </summary>
    /// <remarks>
    /// The prose measure prices a backticked span at everything past `LongestPointer`, so it
    /// bounds one span and never the number of them: fourteen ticked spans of 140 discount 1960
    /// between them and leave 13 characters to measure. That is not a dodge somebody plots — the
    /// section already backticks whole sentences as evidence — but a check that can be walked past
    /// by pressing Enter is not a check, and the alternative on offer was to write the walk down
    /// and leave it open.
    ///
    /// It is not derived from a gap the way <see cref="LongestStubProse"/> was, because there is
    /// no population of flagged stubs to sit above: nobody has ever been told they named too many
    /// probes, and the format spec says naming them costs nothing. Measured over the 131 stubs on
    /// 2026-09-01, what a stub's pointers come to runs to a median of 115 and a top of ISC-110.1
    /// at 751 across 13 spans, then ISC-126 at 684 and ISC-34 at 545. So this sits above the
    /// highest with room for two more fully-qualified test names — 115 apiece with their ticks —
    /// on the densest stub in the file.
    ///
    /// What the sum bounds is the whole stub and not the pointers on their own, and that is
    /// deliberate: a stub of nothing but test names is exactly what the format asks for, so a
    /// stub spending its prose budget on pointers too is inside the rule rather than around it.
    /// The evidence ceiling it comes to is 316 characters above the longest evidence in the
    /// section, which is ISC-110.1's at 1259.
    /// </remarks>
    private const int MostFreePointers = 1000;

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
        // still parse. A claim closes on one line or the retelling has just moved.
        //
        // Cutting it in half under an ID no claim carries is the check above. What neither check
        // reaches, and what is written down rather than built against: the splitter can write the
        // claim. Adding `- [x] ISC-N.1` beside ISC-N, hanging the second half off it and bumping
        // `progress:` leaves every gate here green, and buys a whole second budget for three lines
        // — proved by running it, not argued. Nothing mechanical tells a claim that was always two
        // claims from one bought to carry an overrun, because the difference is what the sentences
        // say. That one is a reviewer's to see, and it is here so they know to look.
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
        var overlong = isa.Stubs
            .Where(Retells)
            .Select(stub => $"{stub.Id} (prose {stub.Prose.Length}, evidence {stub.Evidence.Length})")
            .ToArray();

        overlong.ShouldBeEmpty(
            $"a Verification stub points at the evidence; past {LongestStubProse} characters of "
            + "prose it is retelling it, and past that plus another "
            + $"{MostFreePointers} of pointers it is retelling it inside backticks. Which number "
            + "the stub is over says which to cut: test names, commands and paths are free "
            + "against the first and not against the second. What explains one file is a comment "
            + "in that file, what argues the design is `arquitectura.md`, and how this session "
            + "got there is the commit message and the PR, which is where anybody following the "
            + "merge back is already standing.");
    }

    /// <summary>
    /// Its own fact rather than the size gate's preamble. The evidence half of that gate holds
    /// whether the ticks pair the way they look or not, so nothing has to be checked before
    /// anything — and while the two shared a fact, a stub that dropped a tick *and* ran a
    /// thousand characters over reported only the tick, sending its author back for a second run
    /// to find out the rest.
    /// </summary>
    [Fact]
    public void A_stub_that_drops_a_backtick_is_not_measured_as_shorter_than_it_is()
    {
        isa.Stubs.Where(stub => !stub.PointersClose).Select(stub => stub.Id).ShouldBeEmpty(
            "a stub with an odd number of backticks does not measure what it looks like it "
            + "measures, so the prose half of the size gate reads it as shorter than it is.");
    }

    /// <summary>
    /// The size gate over stubs written to break it, because over `ISA.md` it has nothing to bite
    /// on: the longest span there is 135 against a `LongestPointer` of 150 and the heaviest
    /// pointers come to 751 against a `MostFreePointers` of 1000, so both prices are dead in CI
    /// and every proof they work has been a hand run over a temporarily edited file. That is how
    /// the per-span price shipped with the split form open — the arithmetic was argued in a
    /// comment and never run.
    /// </summary>
    [Fact]
    public void A_paragraph_is_a_paragraph_however_its_backticks_fall()
    {
        var name = new string('n', 113);
        var sevenNames = string.Concat(Enumerable.Repeat($"`{name}`", 7));
        var ceiling = new string('x', LongestStubProse);

        Retells(OneStub(new string('x', 600))).ShouldBeTrue("600 characters of plain prose.");
        Retells(OneStub($"`{new string('x', 798)}`")).ShouldBeTrue(
            "the same paragraph inside one pair of backticks, which the per-span price catches.");
        Retells(OneStub(string.Join(" ", Enumerable.Repeat($"`{new string('x', 138)}`", 14))))
            .ShouldBeTrue(
                "the same paragraph in fourteen ticked spans of 140, which the per-span price "
                + "does not catch — it discounts all 1960 of them and leaves 13 to measure.");

        Retells(OneStub(sevenNames)).ShouldBeFalse(
            "seven fully-qualified test names and nothing else is the stub the format asks for.");
        Retells(OneStub(ceiling + sevenNames)).ShouldBeFalse(
            "prose at its ceiling with 805 characters of pointers under it is inside both.");
        Retells(OneStub(ceiling + sevenNames + string.Concat(Enumerable.Repeat($"`{name}`", 2))))
            .ShouldBeTrue(
                "two more names take the pointers to 1035 over prose already at its ceiling, so "
                + "the evidence bound is the only one that moved.");
    }

    /// <summary>
    /// The size gate, so the fact over `ISA.md` and the fact over stubs written to break it read
    /// one rule. Two numbers rather than one: what a stub says in the open, and what it carries in
    /// total however it arranges its backticks.
    /// </summary>
    private static bool Retells(IsaDocument.Stub stub) =>
        stub.Prose.Length > LongestStubProse
        || stub.Evidence.Length > LongestStubProse + MostFreePointers;

    private static IsaDocument.Stub OneStub(string evidence) =>
        IsaDocument.Of("## Verification", $"- ISC-1 — {evidence}").Stubs.Single();

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
