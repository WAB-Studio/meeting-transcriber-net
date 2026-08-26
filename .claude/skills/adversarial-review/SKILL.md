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
cannot run the review, Step 3b says what to do instead. Never substitute same-model reviewers
quietly and call it an adversarial review.

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

### Is the opposite model available?

Probe for `turn.completed` — `which codex` finds a CLI whose quota is spent, and a 401 still
emits `thread.started`. Read the quota out of the rollout the same run writes:

```sh
PROBE=$(codex exec --json -m gpt-5.6-luna --skip-git-repo-check "reply with the single word OK" < /dev/null 2>/dev/null)
echo "$PROBE" | grep -q '"type":"turn.completed"' || FALLBACK=1

TID=$(echo "$PROBE" | grep -m1 -o '"thread_id":"[^"]*"' | cut -d'"' -f4)
BEFORE=$(grep -ho '"used_percent":[0-9.]*' ~/.codex/sessions/*/*/*/rollout-*-"$TID".jsonl | tail -1 | cut -d: -f2)
awk "BEGIN{exit !(${BEFORE:-100} >= 90)}" && FALLBACK=1
```

Go to Step 3b when `FALLBACK` is set, and say so out loud. Never substitute reviewers quietly.

**Under 10% remaining, Codex does not run the review.** Three lenses cost three turns, and a quota
that runs out mid-review leaves half a review that reads exactly like a whole one.

Run the probe once per review: it costs a turn of the quota it measures. Pin it to
`gpt-5.6-luna` — it only has to prove the CLI answers, and luna is a tenth of what terra or sol
cost per token. The reviewers themselves take no `-m`: they run on the configured default.

Reviewer output goes in a scratch directory, and there is exactly one requirement on it: **it must
not show up as untracked files in the very diff under review.** The session scratchpad the
environment names is outside the tree, so it satisfies that by construction. **One folder per
review.**

```sh
REVIEW_DIR="<scratchpad>/reviews/$$"
mkdir -p "$REVIEW_DIR"
```

Never fall back to capturing reviewer output as stdout. It does produce a review and quietly costs
the two things these files are for: the `.err` that diagnoses a reviewer which produced nothing, and
the empty output file that says one hung.

`-o` is written by the CLI itself and not by the sandboxed command inside it, so the reviewer's own
read-only sandbox does not stand in its way.

Determine which model you are, then spawn reviewers on the opposite:

**If you are Claude** — spawn Codex reviewers via `codex exec`:

```sh
codex exec --skip-git-repo-check -o "$REVIEW_DIR/skeptic.md" "prompt" \
  < /dev/null 2> "$REVIEW_DIR/skeptic.err"
```

**`< /dev/null` is not optional.** `codex exec` reads stdin *in addition to* the prompt argument
— it announces `Reading additional input from stdin...` and waits for EOF. In the background,
where this is meant to run, stdin is a pipe nobody closes, so the reviewer hangs before it reads
a line of the diff. It looks identical to a slow review: the process is alive, the output file
is empty, and it stays that way for as long as you let it.

Send stderr to a file rather than to `/dev/null`. It carries the progress trace and whatever the
CLI would say about auth or a bad flag, and a reviewer that produced nothing is diagnosed there
or not at all.

Default to the read-only sandbox, which is what `codex exec` already uses. Pass
`-s workspace-write` only if the reviewer needs to run tests — a reviewer that can write is a
reviewer that can change what it is reviewing.

Run with `run_in_background: true`, monitor via `TaskOutput` with `block: true, timeout: 600000`.
A reviewer with an empty output file at the timeout has hung, not thought hard: kill it, read its
`.err`, and relaunch. Do not extend the timeout.

**If you are Codex** — spawn Claude reviewers via `claude` CLI:

```sh
claude -p "prompt" > "$REVIEW_DIR/skeptic.md" \
  < /dev/null 2> "$REVIEW_DIR/skeptic.err"
```

Run with `run_in_background: true`. `< /dev/null` for the same reason as above — `claude -p`
also reads a piped stdin, so backgrounding it without closing stdin hangs the reviewer.

Name each output file after the lens: `skeptic.md`, `architect.md`, `minimalist.md`.

Build each reviewer's prompt using the template in `references/reviewer-prompt.md`.

## Step 3b — The opposite model cannot run it

Spawn the same lenses as subagents on your own model, one per lens, each seeing only its own. The
scratch directory, the output files and Step 4 are unchanged.

What changes is what the verdict may claim. Say in it that reviewers ran on your own model and
which of the two reasons it was — the CLI did not answer, or the quota was too low to finish.
Convergence is the thing this costs: two lenses agreeing is agreement between siblings, so a
finding that would have stood on two reviewers meeting stands on its own evidence or not at all.

## Step 4 — Verify and Synthesize Verdict

Before reading reviewer output, log which CLI was used and confirm the output files exist:

```sh
echo "reviewer_cli=codex|claude|same-model"
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
scratchpad, the `schedule:` frontmatter key became prose, and the size thresholds count only
non-comment lines.
