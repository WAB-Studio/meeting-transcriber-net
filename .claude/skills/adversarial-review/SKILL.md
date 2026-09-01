---
name: adversarial-review
description: >-
  Adversarial code review. Spawns one reviewer agent per critical lens to challenge work from a
  distinct angle. Produces a synthesized verdict with findings and lead judgment.
  Triggers: "adversarial review".
---

# Adversarial Review

Spawn one reviewer per lens to challenge work. Reviewers attack from distinct lenses grounded in
the principles under `references/`. The deliverable is a synthesized verdict — do NOT make changes.

`CLAUDE.md` says when it runs here, and that is the trigger: once, over the whole diff, above 50
non-comment lines, before the four commands that then have to prove what the verdict confirmed.
Not once a phase and not once an audit round — the size table below sizes a review, it never
triggers one.

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

Reviewer output goes in a scratch directory, and there is exactly one requirement on it: **it must
not show up as untracked files in the very diff under review.** The session scratchpad the
environment names is outside the tree, so it satisfies that by construction. **One folder per
review.**

```sh
REVIEW_DIR="<scratchpad>/reviews/$$"
mkdir -p "$REVIEW_DIR"
```

Name each output file after the lens: `skeptic.md`, `architect.md`, `minimalist.md`.

Build each reviewer's prompt using the template in `references/reviewer-prompt.md`.

## Step 4 — Verify and Synthesize Verdict

Confirm the output files exist before reading them:

```sh
ls "$REVIEW_DIR"/*.md
```

If any output file is missing or empty, note the failure in the verdict — do not silently skip
a reviewer.

Read each reviewer's output file from `$REVIEW_DIR/`. Deduplicate overlapping findings.
Produce a single verdict using the format in `references/verdict-format.md`.

## Step 5 — Render Judgment

After synthesizing the reviewers, apply your own judgment. Using the stated intent and the
principles as your frame, state which findings you would accept and which you would reject —
and why. Reviewers are adversarial by design; not every finding warrants action. Call out
false positives, overreach, and findings that mistake style for substance.

Append the Lead Judgment section to the verdict (see `references/verdict-format.md`).

---

Vendored from [poteto/noodle](https://github.com/poteto/noodle)
`.agents/skills/adversarial-review/`. Changes from upstream: the `brain/principles.md` dependency
is vendored into `references/principles/`, the scratch directory moved from `/tmp` to the session
scratchpad, the `schedule:` frontmatter key became prose, the size thresholds count only
non-comment lines and no longer trigger the review themselves, and reviewers are agents rather than
a second model's CLI.
