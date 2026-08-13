---
name: adversarial-review
description: >-
  Adversarial code review using cross-model approach. Spawns reviewers on the opposing model
  (Claude uses Codex, Codex uses Claude) to challenge work from distinct critical lenses.
  Produces a synthesized verdict with findings and lead judgment. Triggers: "adversarial review".
---

# Adversarial Review

Spawn reviewers on the **opposite model** to challenge work. Reviewers attack from distinct
lenses grounded in the principles under `references/`. The deliverable is a synthesized verdict —
do NOT make changes.

Worth running after a session that produced a large diff (200+ lines), after finishing a phase of
an implementation plan, or after a planning session.

**Hard constraint:** Reviewers MUST run via the opposite model's CLI (`codex exec` or
`claude -p`). Do NOT use subagents, the Agent tool, or any internal delegation mechanism as
reviewers — those run on *your own* model, which defeats the purpose. If the opposite model's CLI
is not on `PATH`, stop and say so; do not substitute same-model reviewers and call it an
adversarial review.

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

## Step 3 — Detect Model and Spawn Reviewers

Reviewer output goes in a scratch directory outside the repo — never in the working tree, where
it would show up as untracked files in the very diff under review. Use the session scratchpad
directory if the environment names one; otherwise:

```sh
REVIEW_DIR=$(mktemp -d)
```

Determine which model you are, then spawn reviewers on the opposite:

**If you are Claude** — spawn Codex reviewers via `codex exec`:

```sh
codex exec --skip-git-repo-check -o "$REVIEW_DIR/skeptic.md" "prompt" 2>/dev/null
```

Default to the read-only sandbox, which is what `codex exec` already uses. Pass
`-s workspace-write` only if the reviewer needs to run tests — a reviewer that can write is a
reviewer that can change what it is reviewing.

Run with `run_in_background: true`, monitor via `TaskOutput` with `block: true, timeout: 600000`.

**If you are Codex** — spawn Claude reviewers via `claude` CLI:

```sh
claude -p "prompt" > "$REVIEW_DIR/skeptic.md" 2>/dev/null
```

Run with `run_in_background: true`.

Name each output file after the lens: `skeptic.md`, `architect.md`, `minimalist.md`.

Build each reviewer's prompt using the template in `references/reviewer-prompt.md`.

## Step 4 — Verify and Synthesize Verdict

Before reading reviewer output, log which CLI was used and confirm the output files exist:

```sh
echo "reviewer_cli=codex|claude"
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
is vendored into `references/principles/`, the scratch directory is not hardcoded to `/tmp`, the
`schedule:` frontmatter key became prose, and the size thresholds count only non-comment lines.
