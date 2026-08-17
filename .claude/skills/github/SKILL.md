---
name: github
description: >-
  The shape of a pull request here: the title, the three sections of the body, and what stays out.
  Use when opening or editing a PR. Triggers: "gh pr create", "open the PR", "PR body",
  "PR description", "abrí el PR".
---

# The pull request

**Title** — one line, in English, saying what the change does.

**Body** — the claims it closes, then three sections of a few lines each. **Additional notes** comes
off when there is nothing for it.

```text
Closes ISC-118, ISC-119.

## What changed

`Turns.Group` ends a turn when the channel changes, not only on the gap.

## Why

Two speakers answering across each other came out as one turn under one label, so a citation put
half of what was said in the wrong person's mouth.

## Additional notes

The gap constant moved into `Turns` and its two callers read it from there — the riskiest part.
```

`Closes` is `None` when the work closed no claim, and never a claim that is not in `ISA.md`.

Keep it to a screen. The review, how it was diagnosed and what proved it stay out: the commits, the
journal and CI hold those already.
