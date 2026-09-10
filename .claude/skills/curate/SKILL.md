---
name: curate
description: >-
  Decide which proposals become cards at all, so the board only grows for something real. Reach for
  it at the close of a day, before the handoff. Triggers: "curá las propuestas", "curate",
  "qué propuestas abrimos", "abrí lo que valga".
---

# Curate

Opening a card is cheap and its cost is paid every day after, by everybody choosing out of the pool.
You are the one gate before that happens. Nothing here closes a card: an issue that exists is
somebody's, and getting rid of it is not this.

Reach for this at the close of a day, over `private/proposed-issues.md`.

## What is not a card

Three kinds. Confirm each against the tree — open the file, run the grep, read the agent that
supposedly already does it — before acting on it. A proposal describes what it believes is there,
and believing it is how the board fills with work nobody needed.

- **Already there.** Under another name, in another file, or inside a stage that already runs.
- **Not this application.** Machine state, a checkout, a worktree, a build agent's housekeeping.
- **Invented.** A problem only the code suggested: an edge case no recording reaches, a fallback for
  input nothing produces, a guard for a caller that does not exist.

Anything else is a card, whatever anybody thinks it is worth. What gets built first is the picker's.

## Decide the proposals

Open what is a card. Throw away what is one of the three, saying what you read that settles it.

Open what the user has already approved, and throw none of it away.

Leave only what turns on the user's taste about the product — what a screen offers, what somebody is
told, what the application decides for them. Never leave anything that turns on engineering.

Several proposals that are one problem become one card, not several.

Then rewrite `private/proposed-issues.md` to hold what you left and nothing else, in the words it
arrived in, keeping the headings that were already there.

## Writing a card

The `github` skill has the template and the labels. Without `**Claim:**`, `**Delivers**`,
`**Screen:**` and `**Proof:**` it is not a card you opened — a card nobody can pick up is worse than
no card, because it sits in the pool being read.

Say what the proposal was answering. Link the issue or pull request it came from.

## Bounds

Open nothing already on `main`, and nothing the user has refused.

Close nothing, reopen nothing, and relabel nothing.

Write no claim. Edit no `ISA.md`. Touch no branch. Merge nothing. Change no file but the proposals.

## What you say

Every card you opened. Everything you threw away, which of the three it was, and what you read that
settles it. What you left, and the taste it turns on.
