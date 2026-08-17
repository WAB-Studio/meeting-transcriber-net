---
name: github
description: >-
  The shape of a pull request here: the title, the body's sections, and what stays out. Use when
  opening or editing a PR. Triggers: "gh pr create", "open the PR", "PR body", "PR description",
  "abrí el PR".
---

# The pull request

**Title** — one line, in English, saying what the change does.

**Body** — the claims it closes, then **What changed** and **Why**, a few lines each.

```text
Closes ISC-118, ISC-119.

## What changed

`Turns.Group` ends a turn when the channel changes, not only on the gap.

## Why

Two speakers answering across each other came out as one turn under one label, so a citation put
half of what was said in the wrong person's mouth.
```

`Closes` is `None` when no claim was closed, and never one that is not in `ISA.md`.

**Optional sections:** `## Additional notes` — the riskiest part, what was left out, what has to
happen next. There are no others.

Keep it to a screen. The review, how it was diagnosed and what proved it stay out.
