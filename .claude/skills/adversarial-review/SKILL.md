---
name: adversarial-review
description: >-
  Adversarial code review. Spawns one reviewer per critical lens, each blind to the others, to
  challenge work from a distinct angle. Produces a synthesized verdict with findings and lead
  judgment. Triggers: "adversarial review".
---

# Adversarial Review

Spawn one reviewer per lens to challenge work. Reviewers attack from distinct lenses grounded in
the principles under `references/`. The deliverable is a synthesized verdict — do NOT make changes.

Worth running after a session that produced a large diff (200+ lines), after finishing a phase of
an implementation plan, or after a planning session.

**In this repo the reviewers run on Claude.** Upstream crosses models — Codex reviewing Claude and
back — and that is not what happens here. Two things follow, and neither is optional.

**Isolation replaces the model gap, and it is the hard constraint.** One subagent per lens, each
seeing only its own lens, none of them seeing another's output, and none carrying the reasoning
that produced the diff. A reviewer told what the author was thinking reviews the explanation
instead of the code.

**Convergence is what this costs, and a verdict may not spend what it does not have.** Two lenses
agreeing are siblings agreeing: same model, same training, same blind spots. A finding stands on
its own evidence — a line, a case, a run that failed — or it does not stand. **"Two reviewers found
it" is not evidence here** and never appears in a verdict.

## Step 1 — Load Principles

Read `references/principles.md`. Follow every `[[wikilink]]` and read each linked principle file
under `references/principles/`. These govern reviewer judgments.

## Step 2 — Determine Scope and Intent

Identify what to review from context (recent diffs, referenced plans, user message).

Determine the **intent** — what the author is trying to achieve. This is critical: reviewers
challenge whether the work *achieves the intent well*, not whether the intent is correct.
State the intent explicitly before proceeding.

Assess change size. **Only lines that are not comments count** — a comment-only line, added or
removed, is not a line of change for the purposes of this table, and neither is the comment half
of a line that also carries code. A diff that is mostly documentation or commentary sizes as the
code it changes, not as the prose around it.

For C# — where comment lines dominate — the count is:

```sh
git diff -U0 -- '*.cs' | grep -E '^[+-]' | grep -Ev '^(\+\+\+|---)' \
  | sed -E 's/^[+-][[:space:]]*//' | grep -Ev '^(//|/\*|\*)' | wc -l
```

Add the changed lines in every other file to that. The command is a helper, not the rule: it does
not see a trailing comment on a code line, and it would read a markdown bullet as a block-comment
continuation, so where the two disagree the rule wins.

| Size | Threshold | Reviewers |
|------|-----------|-----------|
| Small | < 50 lines, 1-2 files | 1 (Skeptic) |
| Medium | 50-200 lines, 3-5 files | 2 (Skeptic + Architect) |
| Large | 200+ lines or 5+ files | 3 (Skeptic + Architect + Minimalist) |

Read `references/reviewer-lenses.md` for lens definitions.

## Step 3 — Spawn Reviewers

One subagent per lens, all in a single message so they run concurrently. Build each prompt from
the template in `references/reviewer-prompt.md` and give it exactly one lens.

**A reviewer's final message is its review.** There is no scratch directory and no output file:
the harness hands you what the agent returned, and an agent that died returns nothing, which is
the same signal an empty file used to carry.

Each prompt carries the diff, the stated intent and its own lens, and nothing else. Specifically
not: what the other reviewers were asked, what any of them said, why the author made a choice, or
which findings you expect. Every one of those turns a review into a confirmation.

Reviewers read. A reviewer that runs tests is reviewing a tree it can change, so give none of them
write access — and if a finding turns on whether something actually runs, that is yours to check in
Step 5, on the record, not theirs to assert.

## Step 4 — Verify and Synthesize Verdict

Confirm every reviewer you spawned came back. **A reviewer that returned nothing is named in the
verdict** — never silently dropped, and never quietly replaced with a rerun that says something
different.

Read each review. Deduplicate overlapping findings, and when two lenses raise the same thing, keep
the evidence rather than the count: merge them into the one claim, carrying whichever line, case or
run makes it checkable. Produce a single verdict using the format in `references/verdict-format.md`.

## Step 5 — Render Judgment

After synthesizing the reviewers, apply your own judgment. Using the stated intent and the
principles as your frame, state which findings you would accept and which you would reject —
and why. Reviewers are adversarial by design; not every finding warrants action. Call out
false positives, overreach, and findings that mistake style for substance.

Sharing a model with the reviewers cuts both ways here: agreeing with a finding costs you nothing
and proves nothing. What settles one is the code, and where a finding can be checked by running
something, run it and say what came back.

Append the Lead Judgment section to the verdict (see `references/verdict-format.md`).

---

Vendored from [poteto/noodle](https://github.com/poteto/noodle)
`.agents/skills/adversarial-review/`. Changes from upstream: the `brain/principles.md` dependency
is vendored into `references/principles/`, the `schedule:` frontmatter key became prose, and the
size thresholds count only non-comment lines. **The cross-model design is gone** — upstream runs
reviewers on the opposing model's CLI, and here they are subagents on Claude, which trades
convergence for isolation and says so in the verdict rather than in a fallback note.
