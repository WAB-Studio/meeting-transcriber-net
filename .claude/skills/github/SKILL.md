---
name: github
description: >-
  The shape of a pull request here: what the title says, what the body says, and what has no place
  in it. Use when opening or editing a PR. Triggers: "gh pr create", "open the PR", "PR body",
  "PR description", "abrí el PR".
---

# The pull request

**Title** — one line, in English, saying what the change does.

**Body** — three things, in this order, and nothing else:

1. **The claims it closes**, as `ISC-N`. `None` when it closed none, and never a claim that is not
   in `ISA.md`.
2. **What changed**, said explicitly: the behaviour that is different now, and what it was before.
   Somebody opening this cold reads it instead of the diff, not before it.
3. **What to look at**, when one part carries more risk than the rest. Leave it out when none does.

```text
Closes ISC-118, ISC-119.

`Turns.Group` ends a turn when the channel changes, not only on the gap. Two speakers answering
across each other used to come out as one turn with one label on it.

Worth a look: the gap constant moved into `Turns`, and two callers read it from there now.
```

If it does not fit on a screen it is carrying something that belongs somewhere else.

## What has no place in it

- **The review.** What `/adversarial-review` said, and what it made you fix, is between you and the
  diff. It is in the commits already.
- **The record.** What was suspected, what was tried, what turned out not to be the cause: that is
  the commit message and the journal.
- **The evidence.** CI says whether the commands are green, and `CLAUDE.md` sends the rest to a
  comment on the card.
