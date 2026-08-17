---
name: github
description: >-
  The shape of a pull request here: what the title says, the three sections of the body, and what
  has no place in it. Use when opening or editing a PR. Triggers: "gh pr create", "open the PR",
  "PR body", "PR description", "abrí el PR".
---

# The pull request

**Title** — one line, in English, saying what the change does.

**Body** — a lead line naming the claims, then three sections of a few lines each:

```text
Closes ISC-118, ISC-119.

## What changed

`Turns.Group` ends a turn when the channel changes, not only on the gap. The constant that decides
the gap moved into `Turns`, and its two callers read it from there.

## Why

Two speakers answering across each other came out as one turn carrying one label, so a citation
anchored on it put half of what was said in the wrong person's mouth.

## Additional notes

The old behaviour is gone rather than flagged: nothing has shipped, so no recording carries it.
```

- **Closes** — `ISC-N` for every claim the work closes, or `None`. Never a claim that is not in
  `ISA.md`.
- **What changed** — the behaviour that is different now, and what it was before. Name files when
  naming them saves a reader a search.
- **Why** — what was wrong, said as what it cost somebody. Not how it was found.
- **Additional notes** — the part carrying more risk than the rest, what was left out, what has to
  happen next. Leave the section out entirely when there is none.

If it does not fit on a screen it is carrying something that belongs somewhere else.

## What has no place in it

- **The review.** What `/adversarial-review` said, and what it made you fix, is between you and the
  diff. It is in the commits already.
- **The record.** What was suspected, what was tried, what turned out not to be the cause: that is
  the commit message and the journal.
- **The evidence.** CI says whether the commands are green, and `CLAUDE.md` sends the rest to a
  comment on the card.
