---
name: curate
description: >-
  Close the open cards that should never have been opened, and decide which proposals become cards
  at all. Reach for it at the close of a day, before the handoff. Triggers: "curá el board",
  "curate", "limpiá las issues", "podá el board", "qué propuestas abrimos".
---

# Curate

You are the only thing that closes a card nobody built, and the only thing that opens a new one.

Reach for this at the close of a day, over `private/proposed-issues.md`.

## What is not a card

Three kinds, and only these three. Read the tree and confirm each before acting on it.

- **Already there.** Under another name, in another file, or inside a stage that already runs.
- **Not this application.** Machine state, a checkout, a worktree, a build agent's housekeeping.
- **Invented.** A problem only the code suggested: an edge case no recording reaches, a fallback for
  input nothing produces, a guard for a caller that does not exist.

Anything else is a card, whatever anybody thinks it is worth. What gets built first is the picker's.

## Close what should never have been opened

Read every open card. Close each one that is one of the three, with a comment saying what it was,
what you read that settles it, and where the thing it asked for already lives. Write it well enough
to be reopened by somebody who was not here.

Several cards that are one problem become one card; close the rest onto it.

**Never close:**

- a card labelled `question` or `grilled`, or one a person has commented on
- a card named by an open card's `**Depends on:**`
- a card whose work is in an open pull request
- a card the `github` skill names by number
- a card whose content is a probe that closes a claim, or one labelled `help wanted`

## Decide the proposals

Open what is a card. Throw away what is one of the three.

Open what the user has already approved, and throw none of it away.

Leave only what turns on the user's taste about the product — what a screen offers, what somebody is
told, what the application decides for them. Never leave anything that turns on engineering.

Several proposals that are one problem become one card.

Then rewrite `private/proposed-issues.md` to hold what you left and nothing else, in the words it
arrived in, keeping the headings that were already there.

## Writing a card

The `github` skill has the template and the labels. Without `**Claim:**`, `**Delivers**`,
`**Screen:**` and `**Proof:**` it is not a card you opened.

Say what the proposal was answering. Link the issue or pull request it came from.

## Bounds

Open nothing already on `main`, and nothing the user has refused.

Write no claim. Edit no `ISA.md`. Touch no branch. Merge nothing. Change no file but the proposals.

## What you say

Every card you closed and which of the three it was. Every card you opened. What you left, and the
taste it turns on. How many cards were open before and after.
