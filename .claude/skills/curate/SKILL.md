---
name: curate
description: >-
  Decide where each proposal goes: an issue only if it is a feature, `private/owed.md` if it is
  anything else real, and nowhere at all if it is one of the three. Reach for it at the close of a
  day, before the handoff. Triggers: "curá las propuestas", "curate", "qué propuestas abrimos".
---

# Curate

Every proposal has exactly one of four destinations. Give each one a destination and never leave a
proposal where it was.

Reach for this at the close of a day, over `private/proposed-issues.md`.

## A feature — open an issue

**A feature is something the product cannot do yet and a person would ask for by name.** Not a
defect in something it already does, however serious. Not a check, a probe, a guard, a sweep or a
test. Not a rename, a cleanup or a comment that lies.

If you are weighing whether something is a feature, it is not one. Send it to `private/owed.md`.

## Anything else that is real — write it into `private/owed.md`

A defect, a probe, a guard, a cleanup, a comment contradicting its code, a rename, a repair anybody
owes. It goes into `private/owed.md` under a heading naming what it belongs to, newest last, and
**never onto the board** — however small, however certain, however long it has been true.

It carries the header line every entry there carries — a fresh id, its severity, `preexisting`, and
`passed 0` — in the shape that file's own header defines.

Write it so the next planning pass can build it without finding anything out again: the file, the
line, what it is today, what it should be, and what breaks while it is not.

**A bug is never an issue.** No exception exists for one that is one line, one that is embarrassing,
one that blocks something, or one you have just proved.

## One of the three — throw it away

Confirm each against the tree before acting on it.

- **Already there.** Under another name, in another file, or inside a stage that already runs.
- **Not this application.** Machine state, a checkout, a worktree, a build agent's housekeeping.
- **Invented.** A problem only the code suggested: an edge case no recording reaches, a fallback for
  input nothing produces, a guard for a caller that does not exist.

## Turns on the user's taste — leave it

What a screen offers, what somebody is told, what the application decides for them. Only that.
Never anything that turns on engineering, however large.

Rewrite `private/proposed-issues.md` to hold what you left and nothing else, in the words it arrived
in, keeping the headings that were already there.

## Writing a card

The `github` skill has the template and the labels. Without `**Claim:**`, `**Delivers**`,
`**Screen:**` and `**Proof:**` it is not a card you opened.

Say what the proposal was answering. Link the issue or pull request it came from.

## Bounds

Open nothing already on `main`, and nothing the user has refused.

Close nothing, reopen nothing, relabel nothing.

Write no claim. Edit no `ISA.md`. Touch no branch. Merge nothing. Change no file but
`private/proposed-issues.md` and `private/owed.md`.

## Commands

```powershell
gh issue list --state open --limit 300 --json number,title,labels,body
gh issue view <n> --json number,title,body,labels,state,comments
gh pr list --state open --json number,title,body
gh issue create --title "<title>" --body-file <path> --label enhancement --label "<F-label>"
```

## What you say

Every issue you opened. Everything you sent to `private/owed.md`. Everything you threw away, which
of the three it was, and what you read that settles it. What you left, and the taste it turns on.
