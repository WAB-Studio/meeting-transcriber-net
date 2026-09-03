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

    /// <summary>
    /// The gate on a claim born ticked, and the only one here whose input is not the file. What it
    /// refuses is a claim written into `ISA.md` already closed, inside the diff that delivers the
    /// work it was supposed to judge — a claim that never stood as a bet the work had to clear, so
    /// the closure it records is of nothing and the work is scored against itself.
    /// </summary>
    /// <remarks>
    /// Why this could not be a check on the file: a claim marked `[x]` reads the same either way.
    /// Why it could not be a person: it was one, four times in two days, and each was caught on the
    /// way past rather than refused. `IsaHistory` is the seam and says what the comparison is.
    /// </remarks>
    [Fact]
    public void No_claim_is_written_into_the_file_already_closed()
    {
        IsaHistory.BornTicked(IsaHistory.Baseline(), isa).ShouldBeEmpty(
            "a claim marked `[x]` that the trunk does not carry at all was written and closed in "
            + "the same change, so nothing was ever at stake in it. The claim goes in open and "
            + "reaches `main` before the work that closes it starts — this gate is the one place "
            + "that says so out loud, and a card whose claim does not exist yet needs the claim "
            + "landed first. If the trunk does carry it, `git fetch`: the fork point is read "
            + "through `origin/main`, and a stale one has not seen the claim you are closing.");
    }

    /// <summary>
    /// The rule over documents written to break it, because over a green branch the fact above has
    /// nothing to bite on and passes without proving the rule holds.
    /// </summary>
    [Fact]
    public void A_claim_stands_open_before_it_closes_and_the_file_moving_under_it_is_not_that()
    {
        var none = Claims();
        var open = Claims("- [ ] ISC-1: A meeting is recorded.");
        var closed = Claims("- [x] ISC-1: A meeting is recorded.");

        IsaHistory.BornTicked(none, closed).ShouldBe(
            ["ISC-1"], "written and closed in one change is the defect, whole.");
        IsaHistory.BornTicked(none, open).ShouldBeEmpty("added open is what the rule asks for.");
        IsaHistory.BornTicked(open, closed).ShouldBeEmpty(
            "a claim that stood open and is now closed is what closing one looks like.");
        IsaHistory.BornTicked(closed, Claims("- [ ] ISC-1: [DROPPED 2026-09-02: nobody asked.]"))
            .ShouldBeEmpty("withdrawing a claim opens it; a tombstone closes nothing.");
        IsaHistory.BornTicked(
            open, Claims("- [ ] ISC-2: [DROPPED 2026-09-02: issued and withdrawn unbuilt.]", "- [ ] ISC-1: A meeting is recorded."))
            .ShouldBeEmpty(
                "a tombstone is written open, so one arriving whole in a single change is fine — "
                + "which is how ISC-160 and ISC-161 both landed. Reordering is nothing either.");
        IsaHistory.BornTicked(closed, Claims("- [x] ISC-1: A meeting somebody sat through is kept."))
            .ShouldBeEmpty("ids are what it compares; what a claim says is check 16's.");
    }

    /// <summary>
    /// The gate on a claim written beside the work it scores. The one above refuses a claim written
    /// and ticked together; this refuses the same act with the tick left for the next change, which
    /// is what it looks like once somebody has read that message.
    /// </summary>
    /// <remarks>
    /// Why it is a gate and not a reviewer: what a claim is worth is that it was written before
    /// anybody knew what would be built, and there is no way to see from the file that it was not.
    /// The residue is the range — a change that issues an id and writes code in the same breath is
    /// one where the bet and the thing it judges were drafted by the same hand on the same day.
    ///
    /// Splitting is the shape that matters most and is why <see cref="IsaHistory.Introduced"/>
    /// counts open claims too: a bet nobody could clear becomes two smaller ones, both green, and
    /// the count in the frontmatter goes up. Nothing else in this file reaches that.
    ///
    /// It is not asked on the trunk, and <see cref="IsaHistory.OnTheTrunk"/> says why.
    /// </remarks>
    [Fact]
    public void A_claim_arrives_in_a_change_that_writes_no_code()
    {
        if (IsaHistory.OnTheTrunk())
        {
            return;
        }

        var introduced = IsaHistory.Introduced(IsaHistory.Baseline(), isa);

        if (introduced.Count == 0)
        {
            return;
        }

        IsaHistory.CodeTouchedSince(IsaHistory.BaselineCommit()).ShouldBeEmpty(
            $"this change issues {string.Join(", ", introduced)} and writes code in the same "
            + "breath. A claim is what work is scored against, so it lands on `main` in a change "
            + "of its own before the work that closes it starts — CLAUDE.md's 'How work starts and "
            + "ends' says so, and the reason is that a bet drafted beside the thing it judges is "
            + "drafted to fit it. Land the claim open through `.claude/skills/isa/SKILL.md`, then "
            + "cut the branch. Splitting one counts: the leaves are ids the trunk never issued.");
    }

    /// <summary>
    /// The rule over documents written to break it, because over a branch that issues no claim the
    /// fact above returns early and proves nothing about what it would have refused.
    /// </summary>
    [Fact]
    public void A_split_issues_ids_the_trunk_never_carried()
    {
        var one = Claims("- [ ] ISC-1: A meeting is recorded.");

        IsaHistory.Introduced(one, one).ShouldBeEmpty("nothing arrived.");
        IsaHistory.Introduced(one, Claims(
            "- [ ] ISC-1.1: The audio is kept.",
            "- [ ] ISC-1.2: The transcript is kept."))
            .ShouldBe(["ISC-1.1", "ISC-1.2"], "a split issues leaves the trunk never carried, and "
                + "that is the whole defect: the bet got smaller and the count got bigger.");
        IsaHistory.Introduced(one, Claims(
            "- [ ] ISC-1: A meeting is recorded.",
            "- [x] ISC-2: A meeting is transcribed."))
            .ShouldBe(["ISC-2"], "open or closed, an id the trunk had not issued is one arriving.");
        IsaHistory.Introduced(one, Claims("- [ ] ISC-1: [DROPPED 2026-09-03: nobody asked.]"))
            .ShouldBeEmpty("a tombstone over an id the trunk carried issues nothing.");
    }

    /// <summary>
    /// The gate on a claim reworded into its own closure: an open claim rewritten to describe what
    /// the change just built, then ticked. The id carried a bet, and the bet it carried is not the
    /// one being scored — which reads as an ordinary closure to the gate above and to anybody who
    /// does not go and look up what the claim used to say.
    /// </summary>
    /// <remarks>
    /// It reaches a claim the trunk had open and nothing else. Rewording a claim already closed
    /// closes nothing, and the two times this repo did it — `ISC-121` in PR #58 and `ISC-120` in
    /// PR #74 — the product had moved under a standing closure and the `## Verification` stub was
    /// rewritten in the same commit to say so. Refusing those would have sent the honest act down
    /// a route with no reviewer on it, to buy a rule ISC-176 does not make.
    /// </remarks>
    [Fact]
    public void No_claim_is_closed_in_words_the_file_did_not_already_carry()
    {
        IsaHistory.RewordedIntoClosure(IsaHistory.Baseline(), isa).ShouldBeEmpty(
            "a claim marked `[x]` whose words are not the words it stood open in on the trunk was "
            + "rewritten by the very change that closes it, so the bet it is scored against was "
            + "written to fit. A narrowing goes in a change that does not tick it. If the words do "
            + "match, the fork point is behind: `git fetch`, then merge `main` in.");
    }

    /// <summary>
    /// The rule over documents written to break it, and over the shapes that look like it and are
    /// not: a claim left open, a withdrawal, and a rewrite of a claim this change does not close.
    /// </summary>
    [Fact]
    public void A_claim_closes_in_the_words_it_stood_open_in()
    {
        var open = Claims("- [ ] ISC-1: A meeting is recorded.");
        var closed = Claims("- [x] ISC-1: A meeting is recorded.");
        var narrowed = Claims("- [x] ISC-1: A meeting somebody sat through is recorded.");

        IsaHistory.RewordedIntoClosure(open, narrowed).ShouldBe(
            ["ISC-1"], "reworded to fit what was built, then ticked, is the defect this closes.");
        IsaHistory.RewordedIntoClosure(open, closed).ShouldBeEmpty(
            "the words it stood open in are the words it closes in.");
        IsaHistory.RewordedIntoClosure(
            open, Claims("- [ ] ISC-1: A meeting somebody sat through is recorded."))
            .ShouldBeEmpty("a claim this change does not close is free to be sharpened.");
        IsaHistory.RewordedIntoClosure(closed, narrowed).ShouldBeEmpty(
            "a claim the trunk already closed is not being closed here, whatever happens to its "
            + "words — that is a stub standing over a sentence, and a different rule.");
        IsaHistory.RewordedIntoClosure(
            closed, Claims("- [ ] ISC-1: [DROPPED 2026-09-02: nobody asked.]"))
            .ShouldBeEmpty("a tombstone is written open, and rewrites the line by definition.");
        IsaHistory.RewordedIntoClosure(Claims(), closed).ShouldBeEmpty(
            "a claim the trunk does not carry at all is check 15's, and is named once.");
    }

    /// <summary>
    /// The gate on a stub left standing over a sentence it was not written against: a claim already
    /// closed, reworded, and its `## Verification` line untouched. Nothing is being closed, so
    /// check 16 does not reach it — the tick is old and honest — but what it now reads as covering
    /// is a claim no probe was ever run against.
    /// </summary>
    /// <remarks>
    /// Rewording a standing closure is a thing the repo does and should keep doing, because the
    /// product moves under claims that stay true; `IsaHistory.RewordedIntoClosure` names the two
    /// times it was done right. This is that habit made mechanical, and the only act it refuses is
    /// doing half of it. What it cannot see is whether the probe was run again — a bumped date over
    /// a run nobody made passes it, which is `references/format.md`'s residue and not this gate's.
    /// </remarks>
    [Fact]
    public void No_closed_claim_is_reworded_and_left_on_the_evidence_for_the_old_words()
    {
        IsaHistory.StubLeftBehind(IsaHistory.Baseline(), isa).ShouldBeEmpty(
            "a claim marked `[x]` whose words moved while its `## Verification` stub stayed where "
            + "it was reads as probed against a sentence nobody ran anything against. Re-run the "
            + "probe against what the claim says now and write down what came back, or put the "
            + "words back to the ones the evidence covers. Moving a closed claim is allowed — "
            + "leaving its evidence behind is what this refuses.");
    }

    /// <summary>
    /// The rule over documents written to break it, and over the shapes that look like it: a stub
    /// that moved with its claim, a re-run under a claim that did not move, a closure this change is
    /// making rather than carrying, a claim reopened, and a closed claim with no stub at all.
    /// </summary>
    /// <remarks>
    /// A space is a rewording here, and deliberately: what a claim says is compared as bytes by
    /// every one of these gates, so a claim edited only in punctuation owes its stub a line saying
    /// the probe still holds. That is the cheapest red this gate produces and the one most likely to
    /// be answered with a bumped date, which is why the case is written down rather than left to be
    /// met for the first time in anger.
    /// </remarks>
    [Fact]
    public void Evidence_moves_with_the_claim_it_was_written_against()
    {
        const string Recorded = "- [x] ISC-1: A meeting is recorded.";
        const string SatThrough = "- [x] ISC-1: A meeting somebody sat through is recorded.";
        const string Green = "- ISC-1 — `RecordingTests` green 2026-09-02";
        const string GreenAgain = "- ISC-1 — `RecordingTests` green 2026-09-03";

        var closed = Closing([Recorded], Green);

        IsaHistory.StubLeftBehind(closed, Closing([SatThrough], Green)).ShouldBe(
            ["ISC-1"], "the words moved and the evidence under them did not, which is the defect.");
        IsaHistory.StubLeftBehind(closed, Closing(["- [x] ISC-1: A meeting is recorded"], Green))
            .ShouldBe(["ISC-1"], "a full stop is a rewording; the gates read a claim as bytes.");
        IsaHistory.StubLeftBehind(closed, Closing([SatThrough], GreenAgain)).ShouldBeEmpty(
            "the probe was re-run against the new words and the stub says so.");
        IsaHistory.StubLeftBehind(closed, Closing([Recorded], GreenAgain)).ShouldBeEmpty(
            "a stub is rewritten whenever the probe is re-run, and a claim that has not moved has "
            + "nothing to say about that. The rule runs one way.");
        IsaHistory.StubLeftBehind(
            Closing(["- [ ] ISC-1: A meeting is recorded."]), Closing([SatThrough], Green))
            .ShouldBeEmpty("a claim the trunk had open is being closed here, which is check 16's.");
        IsaHistory.StubLeftBehind(
            closed, Closing(["- [ ] ISC-1: [DROPPED 2026-09-02: nobody asked.]"]))
            .ShouldBeEmpty("a claim withdrawn is closing nothing, so it has no evidence to leave.");
        IsaHistory.StubLeftBehind(Closing([Recorded]), Closing([SatThrough], Green)).ShouldBeEmpty(
            "nothing is left standing when there was no stub to leave — a closed claim carrying no "
            + "evidence at all is check 5's, and is named there.");
    }

    /// <summary>
    /// The seam, over a merge that really did it. Without this the fact above proves the rule and
    /// nothing proves the reading — that git is asked at all, that a commit which is not the
    /// working tree comes back, and that what comes back parses as claims.
    /// </summary>
    /// <remarks>
    /// What it still cannot reach, said rather than left for somebody to find: that `Trunk` names
    /// the trunk. Point it at `HEAD` and the baseline becomes the working tree, the gate says yes
    /// to everything, and every assertion in this file stays green — because the two only differ
    /// over a tree that changed `ISA.md`, which no test can conjure without editing the file the
    /// gates run over. A constant cannot be checked from inside the suite that reads it; what
    /// stands there instead is that it is one line, named, with this paragraph under it.
    /// </remarks>
    /// <remarks>
    /// PR #63 is the merge chosen because it is the file's worst case rather than its plainest.
    /// It split the closed `ISC-139` into two leaves marked closed, and in the same breath issued
    /// `ISC-158` with nine children and ticked two of them the day they were written. The gate
    /// names all four, and that a split of a closed claim is among them is the rule and not an
    /// oversight: `ISC-139.2` is about a screen that same pull request built, closed on a stub
    /// written that day naming tests written that day. A split into new closed ids is a second
    /// closure however it reads, so it goes red and the person says why.
    ///
    /// Checks 16 and 17 get no pinned pair of their own. Nothing in this repo's history breaks check
    /// 16, and the one commit that breaks check 17 — `eb2a692b`, which widened `ISC-157` and left
    /// its stub naming a run against the narrower sentence — is a pair this test already covers the
    /// reading of. Pinning it would prove the seam a second time and the rule not at all, since
    /// `Evidence_moves_with_the_claim_it_was_written_against` proves that over seven shapes, and
    /// each pinned commit is another old blob a clone has to hold — `ISC-134` and `ISC-135`.
    /// </remarks>
    [Fact]
    public void The_gate_reads_history_and_not_the_tree_it_is_standing_on()
    {
        const string TheRecordingWindow = "0f63462def717e9278640979964e2d155f713e3a";
        const string WhatItWasMergedOnto = "34bc0b461ef3f6eb6c08c40ada2838936abb42a3";

        IsaHistory.BornTicked(
            IsaHistory.At(WhatItWasMergedOnto),
            IsaHistory.At(TheRecordingWindow)).ShouldBe(
            ["ISC-139.1", "ISC-139.2", "ISC-158.4", "ISC-158.5"],
            ignoreOrder: true,
            customMessage: "the four claims PR #63 marked closed on the day it wrote them.");
    }

    /// <summary>
    /// Which two documents the three rules above are handed, which is the one part of the gates that
    /// decides every verdict without being a rule itself.
    /// </summary>
    /// <remarks>
    /// The value comes in as an argument rather than off the environment so this can ask, and
    /// because a test that set a process-wide variable would be deciding the answer for whatever
    /// else was running beside it. `Baseline()` is the one line that reads the environment.
    ///
    /// What no assertion here reaches is that `Trunk` names the trunk. Point it at `HEAD` and the
    /// baseline becomes the working tree, the gates say yes to everything, and every assertion in
    /// this file stays green — because the two only differ over a tree that changed `ISA.md`, which
    /// no test can conjure without editing the file the gates run over. A constant cannot be checked
    /// from inside the suite that reads it; what stands there instead is that it is one line, named,
    /// with this paragraph under it.
    /// </remarks>
    [Fact]
    public void What_a_change_is_judged_against_is_said_and_never_guessed()
    {
        const string AClaimWasIssued = "aa070b6964e1d291248340cd9fceb0756bcc6127";
        var fork = IsaHistory.TrunkBefore(null);

        IsaHistory.TrunkBefore(AClaimWasIssued).ShouldBe(
            AClaimWasIssued, "a push says where it began, and that is what it is judged against.");
        IsaHistory.TrunkBefore($"  {AClaimWasIssued}  ").ShouldBe(
            AClaimWasIssued, "an environment variable arrives with whatever whitespace it arrives.");
        IsaHistory.TrunkBefore(string.Empty).ShouldBe(
            fork, "nothing said means every route but the push, which wants the fork point.");
        Should.Throw<InvalidOperationException>(() => IsaHistory.TrunkBefore("HEAD"))
            .Message.ShouldContain(
                "HEAD",
                customMessage: "a name resolves and is an ancestor of itself, so it would turn all "
                + "three gates off from a shell. Only a full commit id is taken.");
        Should.Throw<InvalidOperationException>(
            () => IsaHistory.TrunkBefore("0123456789012345678901234567890123456789"))
            .Message.ShouldContain(
                "clone",
                customMessage: "a commit this clone cannot reach, or one HEAD does not descend "
                + "from, is a push nothing can be judged against — red, and never the fork point, "
                + "which on the trunk is HEAD and would be the silence this exists to remove.");
    }

    /// <summary>
    /// The gate on a pointer that stopped resolving. A stub cites the probe that closed a claim by
    /// path, and until this nothing held those paths to the tree — so a file moving took the
    /// evidence for a closed claim out of anybody's reach and every gate here stayed green.
    /// </summary>
    /// <remarks>
    /// It has found nothing yet, spelling included: seventeen distinct paths over eighty-one
    /// citations on `main` at c4046fa on 2026-09-02, and every one of them resolves and is spelled
    /// the way the tree spells it. So it repairs nothing and is worth only what it refuses next
    /// time, which makes a false red the one thing that could put it behind where the file started
    /// — hence four conditions rather than "looks like a path", and hence
    /// <see cref="A_path_is_told_from_a_name_a_command_and_a_glob"/> being written over the spans
    /// this file actually holds. `## Verification` backticks type names, member names, command
    /// lines, enum values and whole English sentences: 593 spans that day against the 81 this
    /// reads, and a gate reddening on `Turns.Group` is one somebody deletes.
    /// <para>
    /// What it reaches is thinner than it sounds and the measurement says so: eleven of the
    /// seventeen are `tests/` and the ten test project directories, and not one span in the file is
    /// a path under `src/`. A stub cites its probe as a backticked type name with the project
    /// directory beside it, so what this holds is mostly the directories, which go stale only when
    /// a project is renamed.
    /// </para>
    /// <para>
    /// Which is also what it cannot see: a pointer that resolves and is wrong. `ISC-166`'s stub
    /// names a suite and the project it sits in; PR #246 moved the suite between two projects that
    /// both exist, repaired that stub by hand, and named this gate — so the gate it named would
    /// have been green over it. Holding a cited suite to the project cited beside it is the gate
    /// that catches that one. Measured over this file the pairing appears twenty times and every
    /// one of them is sound today, so it is a card and not a line here.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_path_the_file_points_at_is_one_this_repository_has()
    {
        // Without this the check below passes by finding nothing, which is how a rule that stopped
        // recognising a path reads exactly like a file whose pointers are all sound.
        isa.Paths.ShouldNotBeEmpty(
            "no backticked span in `ISA.md` was read as a path, so this check reads nothing.");

        isa.Paths.Where(path => !Resolves(path)).ShouldBeEmpty(
            "a claim's evidence is cited by path, and this repository has nothing at this path "
            + "under this spelling — which on Windows can mean the pointer opens on the machine "
            + "you are reading it on and nowhere else. Whatever moved takes its pointer with it, "
            + "so point the stub at where the probe lives now, spelled the way the tree spells it. "
            + "If the probe is gone rather than moved, say so on the claim — a closed claim whose "
            + "evidence no longer exists is a claim nobody can follow, and deleting the reference "
            + "would leave it reading as closed on something.");
    }

    /// <summary>
    /// The rule over the spans it has to tell apart, because over `ISA.md` the fact above only
    /// proves that nothing is broken today — it cannot show which of the file's backticked spans
    /// were read as paths, and a rule that had quietly stopped recognising one would pass it. Every
    /// line here is a span this file carries or carried.
    /// </summary>
    [Fact]
    public void A_path_is_told_from_a_name_a_command_and_a_glob()
    {
        Paths("`tests/MeetingTranscriber.Isa.Tests`").ShouldBe(["tests/MeetingTranscriber.Isa.Tests"]);
        Paths("`docs/ui-probe.md`").ShouldBe(["docs/ui-probe.md"]);
        Paths("`.claude/agents/auditor.md`").ShouldBe(
            [".claude/agents/auditor.md"], "a root whose name opens with a dot is a root.");
        Paths("`tests/`").ShouldBe(["tests/"], "a directory is a pointer like any other.");
        Paths("`tests/fixtures/deepgram/two-channel-one-voice-me.json`").ShouldBe(
            ["tests/fixtures/deepgram/two-channel-one-voice-me.json"]);
        Paths("`src/MeetingTranscriber.Gone/Moved.cs`").ShouldBe(
            ["src/MeetingTranscriber.Gone/Moved.cs"],
            "a span is read as a path on its shape alone, whether or not it resolves. Which of the "
            + "two it is, is the next assertion, and no file `ISA.md` cites is used for either — "
            + "pinning a real path as absent would go red the day somebody creates it.");

        Resolves("src/MeetingTranscriber.Gone/Moved.cs").ShouldBeFalse(
            "and this is the red half: a pointer read as a path that the tree has not got.");

        Paths("`TESTS/MeetingTranscriber.Isa.Tests`").ShouldBe(
            ["TESTS/MeetingTranscriber.Isa.Tests"],
            "a root is matched without regard to case on purpose, so a pointer spelled wrong is "
            + "still read as a path and still answered. Matching the root exactly would make this "
            + "span stop being a path at all, which is the one answer worse than either.");

        Resolves("TESTS/MeetingTranscriber.Isa.Tests").ShouldBeFalse(
            "and this is what Windows will not say: it opens that directory happily, git has it "
            + "under one spelling only, and a reader on a case-sensitive checkout finds nothing.");

        Paths("`Turns.Group`").ShouldBeEmpty(
            "a member name is not a path, and reddening on this one is how a gate gets deleted by "
            + "the first person it stops.");
        Paths("`ISA.md`").ShouldBeEmpty(
            "a bare file name says which file and never which one of them, so it is a name.");
        Paths("`ch1:speaker_0`").ShouldBeEmpty("a speaker label.");
        Paths("`multichannel`").ShouldBeEmpty("a Deepgram option.");
        Paths("`git grep -l \"class TemporaryCorpus\" -- tests/`").ShouldBeEmpty(
            "a recorded run holding a path is evidence of the run, not a pointer at the path.");
        Paths("`mklink /J`").ShouldBeEmpty("a command whose switch reads as a path.");
        Paths("`tests/**/*.cs`").ShouldBeEmpty(
            "a glob names a set, and whether a set has members is a different question.");
        Paths("`meetings/<id>/manifest.json`").ShouldBeEmpty(
            "a shape standing for a folder in somebody's corpus, which no checkout has.");
        Paths("`tests/MeetingTranscriber.Isa.Tests/IsaStructureTests.cs:120`").ShouldBeEmpty(
            "a line number points inside a file, so the span names a place and not a file — and "
            + "reddening on it would stop somebody who cited their evidence more precisely.");
        Paths("`docs/layout.md#a-heading`").ShouldBeEmpty("an anchor points inside a document.");
        Paths("`docs/[a-z]+`").ShouldBeEmpty(
            "a character class names a set, which is the glob above under another spelling.");
        Paths("`MeetingTranscriber.Testing/DeepgramFixtures.cs`").ShouldBeEmpty(
            "what a `git grep` inside `tests/` printed, so resolving it would mean guessing the "
            + "directory it was run from.");
        Paths("`https://api.deepgram.com/v1/listen`").ShouldBeEmpty("a URL is not this tree.");
        Paths("`press <the meeting> wait BackButton`").ShouldBeEmpty("a UI probe walk.");
    }

    /// <summary>
    /// Whether one of the file's pointers is a file or a folder this repository has, spelled the
    /// way this repository spells it.
    /// </summary>
    /// <remarks>
    /// Segment by segment against what each directory actually holds, rather than
    /// <c>File.Exists</c> over the joined path. This runs on <c>windows-latest</c>, where the file
    /// system answers without regard to case, so <c>TESTS/MeetingTranscriber.Audio.Tests</c> is
    /// there as far as this machine is concerned and nowhere else: GitHub serves the directory
    /// under one spelling, and a checkout on a case-sensitive volume has nothing at the other. A
    /// pointer only the machine that wrote it can follow is the same defect as one that resolves
    /// nowhere, and the whole of what this gate is for is that a claim's evidence can be found.
    /// <para>
    /// What is compared is the disk and not the index. A working tree whose spelling has drifted
    /// from what git recorded — a case-only rename made by hand, which `core.ignorecase` lets pass
    /// uncommitted — reds here and would be green on a fresh clone. That is the right way round:
    /// the local tree is where somebody is about to follow the pointer.
    /// </para>
    /// </remarks>
    private static bool Resolves(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var walked = IsaDocument.Root().FullName;

        foreach (var segment in segments)
        {
            // What the directory hands back is the name on disk, so comparing it to the segment is
            // what the file system itself will not do. The pattern is the segment verbatim, which
            // is safe because a span holding a wildcard was never read as a path.
            var spelled = Directory.Exists(walked)
                && Directory.EnumerateFileSystemEntries(walked, segment).Any(
                    entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));

            if (!spelled)
            {
                return false;
            }

            walked = Path.Combine(walked, segment);
        }

        return segments.Length > 0;
    }

    /// <summary>What the path rule reads out of one line, so a case is one line.</summary>
    private static IReadOnlyList<string> Paths(string line) => IsaDocument.Of(line).Paths;

    /// <summary>
    /// A document holding nothing but claims, in one feature block so they parse as claims. The
    /// gates above read claim lines and nothing else, so a case is claim lines and nothing else.
    /// </summary>
    private static IsaDocument Claims(params string[] claims) => IsaDocument.Of(
    [
        "## Features",
        "### F1 · Recording",
        "Why: it is where the lines below have to sit to be read as claims.",
        "Board: —",
        .. claims,
    ]);

    /// <summary>
    /// The same, with a `## Verification` section under it: check 17 is the one gate that reads a
    /// claim and its stub together, so a case for it is both.
    /// </summary>
    private static IsaDocument Closing(string[] claims, params string[] stubs) => IsaDocument.Of(
    [
        "## Features",
        "### F1 · Recording",
        "Why: it is where the lines below have to sit to be read as claims.",
        "Board: —",
        .. claims,
        "## Verification",
        .. stubs,
    ]);
}
