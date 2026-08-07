---
name: isa
description: >-
  Write, sharpen, score and close ISA.md — the repo's Ideal State Articulation, where done is
  stated as falsifiable claims (ISCs) that close on recorded evidence. Use when adding or
  splitting a claim, closing one on evidence, auditing whether the articulation covers the work,
  seeding claims from code, or starting a board task that has no claim yet. Triggers: "ISA",
  "claim", "ISC", "what does done mean", "check the ISA", "close ISC-N".
---

# ISA — Ideal State Articulation

`ISA.md` at the repo root is the claims surface: what *done* means for this product, stated so
that each statement names the probe that would prove it false, plus a mechanical count of how
many hold. It is the system of record for the ideal state and outlives every session.

It is **not** the design document and **not** the work queue. Three artifacts, one job each:

| Artifact | Its one job |
| --- | --- |
| `arquitectura.md` | The design and its reasoning — decisions, model, flows, risks. Prose, Spanish. |
| `ISA.md` | What done means as falsifiable claims, how each is probed, and the count. |
| ClickUp | The work queue — what to do next, who has it, what state it is in. |

When this file and `references/format.md` disagree, the format spec wins — it is the shape the
gate test parses.

## The one rule everything else serves

**A claim closes on tool evidence of the right modality, or it stays open.** Not on a task
moving to `in review`, not on a build succeeding nearby, not on "should work". A closed claim
carries a one-line provenance stub in `## Verification` — a test name, a commit, a probe ref.

When a probe fails, the question is always *is the code wrong, or is the claim wrong?* Both
answers are progress. A claim that mis-stated done gets fixed in the ISA and logged in
`## Decisions`; that is not cheating, that is the articulation improving.

## The board and the ISA

The board is where work is queued; the ISA is where done is defined. They are not one-to-one —
a task may close several claims, and a claim may need several tasks.

**The task points at the claim. The claim never points at the task.** ISC IDs are stable
forever by rule; ClickUp IDs churn as the board is groomed, so storing one inside the ISA would
rot the artifact every time a task is renamed, split or deleted. A task carries `Cierra: ISC-12,
ISC-13` in its description — the ClickUp skill's own rule is that a description is the present
and gets rewritten, which is exactly where a volatile pointer belongs. To find the work behind a
claim, search the board for the ID.

Each `### F<n>` feature block carries a `Board:` line naming its list. That is the only
structural link, and the gate test checks the list exists.

The flow itself — when to write a claim, when to close it, when to move the task — lives in
`CLAUDE.md`, because it governs every task and not only ISA work.

## Workflows

Five. LifeOS's sixth, Reconcile, merges ephemeral per-feature copies back into master; it needs
parallel-agent infrastructure this repo does not have, and without that infrastructure the safe
rule is that `ISA.md` has exactly one copy.

### Seed — draft claims from what exists

Read the code, the tests and `arquitectura.md`; write the claims the product already holds and
the ones it does not. A seeded claim is closed only when its probe was run in this session and
seen to pass — a `[x]` inferred from a test class existing is worse than an empty ISA, because
it reads as verified.

### Scaffold — a new claim

Write the claim, then check the Splitting Test:

- Contains "and" / "with" / "including" joining two verifiable things → split.
- Part A can pass while part B fails independently → split.
- Contains "all" / "every" / "complete" → enumerate what that means.
- Crosses a boundary (domain / storage / processing / UI) → one per boundary.

Splits preserve the parent: `ISC-7` becomes the container, leaves become `ISC-7.1`, `ISC-7.2`.
Never renumber.

**Run the probe before building.** If it passes with no work done, either the claim was already
true — delete it — or the probe cannot fail, which means it is not a probe. Deterministic types
only; never for `manual`.

### Interview — fill a thin section

Ask about what is dim, one question at a time, and write the answers in. Stop when the next
question would be inventing detail rather than eliciting it — what is genuinely unknown goes to
`## Not yet specified` as fog, not into a speculative claim.

The graduation test for fog: *can you state the question precisely now — not answer it, state
it?* Statable with a nameable falsifier → a claim. Statable but not probe-able → fog. Beyond the
vision → `## Out of Scope` in `arquitectura.md` §16.

### CheckCompleteness — score the articulation

Structural checks are the gate test's job; run `dotnet test --filter "FullyQualifiedName~IsaStructureTests"`
rather than re-checking them by eye. What only a reading catches:

- A subsystem named in the Goal with no claim covering it — the coverage gate, assessed at close.
- A claim with no anti-claim anywhere near it. A goal with zero failure modes worth naming is
  under-specified.
- A claim stated as an example where a universal is available. "One fixture parses" holds while
  the parser is broken; "every fixture in `tests/fixtures/deepgram/` parses" does not.
- A claim that says what was built rather than what must be true.
- A closed claim whose `## Verification` stub names a test that no longer exists, or names
  nothing runnable. This is the one the removal of `## Test Strategy` made worth looking for.

Report gaps; do not silently fix them.

### Append — the three append-only sections

`## Decisions`, `## Learning` and `## Verification` are written through here so their shape does
not drift into free prose.

- **Decisions** — dated, including dead ends. `refined:` prefix when a Goal or claim changed
  shape rather than status.
- **Learning** — only when understanding changed, and only complete: conjecture / refuted-by /
  learned / criterion-now. A partial entry does not get written. This is not a changelog; `git
  log -- ISA.md` is that.
- **Verification** — one line per closed claim. The proof lives in git and CI; the ISA points at
  it. An entry that grows into a paragraph gets collapsed back.

## Probes

There is no probe table — `## Test Strategy` was dropped on 2026-08-07 because sixty-five rows
of `dotnet test … --filter-class` boilerplate cost more to keep true than they bought. What a
claim closed on lives in its `## Verification` stub instead.

Two forms are load-bearing enough to get wrong quietly, and both are in
`references/format.md`: a `dotnet test` probe must name its project, because
`--filter "FullyQualifiedName~X"` is silently ignored by the MTP runner and passes without
running anything; and a paid Deepgram run is never CI evidence, so its stub carries the run's
date and the budget that was approved.
