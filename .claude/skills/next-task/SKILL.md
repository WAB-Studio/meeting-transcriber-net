---
name: next-task
description: >-
  Carry one named board task to an open PR with nobody in the loop. Given a card, it works it,
  proves it, opens the PR and hands off a structured record. Triggers: "next task",
  "próxima tarea", "keep going on the board".
---

# next-task — one unattended working session

A run is **one board task carried to an open PR**, and nothing else. `CLAUDE.md` governs the work
itself; what is here is only what changes when nobody is beside you.

**You are given two paths, in this order:**

1. **Where the handoff goes.** You do not write it — §5, your last message, is the handoff, and the
   caller writes that file itself. The path is passed so it can be named in what it writes.
2. **The card.** A JSON file that exists and is yours to read: `task_id`, and `pr_number` — a number
   when that card already has work in flight, `null` when it does not.

**Read the second one.** The first does not exist yet, and a session that opens it, finds nothing
and goes looking for work of its own has quietly become something else; the day stops when it comes
back holding a card other than the one it was given.

Everything you do to the board is done to that one card — read it, move it, comment on it — so the
CLI is only ever addressed by id:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <id>
```

**`python` is the first word of the command and the path goes whole**, and `PYTHONIOENCODING` is
already exported. A refused call is a missing rule in `.claude/orchestrator/settings.json`: **say so
in `left_out` and stop trying to spell your way around it.**

**Long prose goes in a file, never on the command line.** `--text` and `--desc` take `@path`.
Write it to `.scratch/` — the only place you may write outside the source tree,
gitignored at the root, and the reason `/tmp` and the session scratchpad are both refused is that
they are outside the working directory.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --text @.scratch/note.md
```

## 0 · Before touching anything

Clean tree, standing on a current `main`. If it is not, fix nothing: `outcome: "blocked"`, write
the handoff, stop. An unattended session that tidies up what another left half-done is the fastest
way to lose work.

## 1 · The card

**The card is an input, not a choice.** Read the file you were given, then read the card, then
start:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <task_id>
```

**Which card is the right one is not a question this session has open**, and going to the board to
satisfy yourself about it is the one detour worth naming. It costs a quarter of the session, and it
re-decides from one card what was decided from the board — so it is slower and worse at once. Which
list is which and what each state means is `arquitectura.md` §13, and you should not need any of it.

**What the card was grilled into is the `**Grilled.**` comment**, and it is the first thing to read
after the description. It carries the SHA of `main` it was decided against: if the code has moved
under one of those decisions since, that decision is an open fork again, not a settled one — say so
in `decisions_deferred` and use your judgement, which is what `CLAUDE.md` asks for anyway.

Three things are yours to refuse with, and all three are about the work rather than the choice:

- **The work is already in `main`** → `outcome: "already_done"`, §1b. Check this before anything else:
  it is the cheapest answer there is and the only one that costs a whole session when it is missed.
- **The card holds a decision the grill did not settle** → `outcome: "needs_grill"`, §2b.
- **You cannot start at all** — the tree is dirty, the branch you were given is gone, the card
  describes something no session can finish without hardware nobody plugged in → `outcome:
  "blocked"` with `blocked_reason`, and leave the comment on the card yourself.

### 1b · The card that was already finished

A card reaches you finished when the session before yours landed the work and died before the
bookkeeping: the PR merged, and nothing moved the card off `in progress`. **You are not the first
session to be handed this card, and building anything is the wrong answer.** The board says the work
is owed; `main` says it is done, and `main` is the one that cannot be wrong.

Ask it in one step, before reading the card for what to build:

```powershell
gh pr list --search "<task_id>" --state merged --json number,mergedAt,headRefName
git merge-base --is-ancestor <head_sha of that PR> origin/main   # exit 0 means it landed
```

What proves it is the commit, not the PR's state: a PR can be closed without merging, and a merged
PR can have been reverted since. **The claim is that the work is in `main` now**, so say which
commit, and re-run the four commands over `main` if the card names ISCs — an ISC marked `[x]` by a
branch that never landed is a claim closed on nothing.

Then: `outcome: "already_done"`, `pr_number` naming the PR that landed it, `blocked_reason` empty.
**Move the card to `in review` yourself and leave the comment saying what you checked** — the same
as §blocked, and for the same reason: what you found is read on the board tomorrow, not in a
transcript. Do not close the card; closing is the user's.

The day does not stop over one of these. It is one card that turned out to cost nothing, and the
next cycle takes the next one.

### When a PR number comes with it

`pr_number` set means the card already has work in flight: a PR that was read and not let through,
or one parked on a decision that has since been answered. **The card's own comments say which** —
a `**Not merged.**` comment naming the defect, and where it was parked, a `**Grilled.**` one
carrying the answer the branch now has to be brought in line with. Read which before you assume you
are patching: a hold expects the same approach to land properly, while an answered `ask` may take
the diff down to the studs.

Continue on that PR's branch rather than cutting a new one — a second branch means a second PR
against one card:

```powershell
git fetch origin
git checkout <branch-from-the-PR>
```

Then hand off **that** PR number and the new tip. If the answer means the branch is worth nothing,
say so in `decisions_deferred`, close the PR and start it properly — that is a judgement, and it is
yours.

## 2 · What cannot be done alone

**A task cannot be finished when it needs something that is not on this side of the CLI** — a real
meeting, two sound cards, a device unplugged mid recording, two hours of drift measured on hardware.
Nearly all of phase 2 is this, and most of it is caught before a card ever reaches here.

When it is not, and you find out from inside the card: `outcome: "blocked"` with `blocked_reason`
saying what is missing, and the comment on the card yourself, in terms of what somebody has to
bring. Comment, and leave the card where it is — a card moved to `pending` by a session that then
ends is a card out of every pool, and putting it back there is not this session's call:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --text "Needs <what exactly> — <what a person has to do or bring>."
```

The real risk here is not stalling. It is that you produce plausible measurements of a meeting that
never happened. **A number that did not come out of a run does not get written** — not in the code,
not in the ISA, not in a board comment. **Any card you touched at all goes in the handoff**: the
audit re-lists the board and compares, so an undeclared move or comment surfaces anyway, as a
finding against you.

## 2b · A card that still holds a decision

A grilled card has had its product decisions made. If you meet one it did not settle, **stop before
you build anything**: `outcome: "needs_grill"`, the fork in `decisions_owed[]`, and nothing else in
that session.

Nobody is interrupted. What you wrote goes on the card, the card is retagged `regrill` and sent to
`pending`, and a grill settles it before any session touches it again. So `what` is the field that
matters: it is read cold by whoever grills the card, and it is all they get from you. Name the
decision the way somebody who has not read the code would name it, and add `why` and the options
when you can see them.

The bar is `CLAUDE.md`'s and it is narrow: **does the answer change what the person using this app
experiences, or only how the code gets there?** If it does not show from outside, it is yours —
decide it, say so in `decisions_deferred`, and carry on. A decision you could settle by reading the
repo is a session you are spending for nothing.

## 3 · Working

Like any task, with `CLAUDE.md` in charge: the claims in `ISA.md` before building, the four
commands each on its own line, `/adversarial-review` over 50 non-comment lines and whatever the
verdict confirms fixed in the same pass. All the unattended mode adds:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" move <id> --status "in progress"    # on starting, so a crash leaves a trace
```

### The journal

`.scratch/current.md` is already there when you start, with its headings and nothing under them.
**Fill it in as you go, not at the end** — a session that dies has written whatever it had written.

The title line is `# <task-id> · <name>`. Under the headings goes what only you know: where you got
to, **what you tried and threw away**, and what you would do next. The discarded attempts are the
most valuable part and the only part nobody else can reconstruct — the card keeps conclusions, the
PR keeps the diff, and neither keeps the three approaches that did not work.

If `.scratch/parked/<task-id>.md` exists for your card, that is an earlier session on this same
card: read it first and continue from it into `current.md`.

**Write it, and never move it.** Leave it at `.scratch/current.md`; filing it is not yours. A
session that opens a PR and leaves that file empty has not finished.

If you get stuck in a way you cannot resolve: `outcome: "blocked"` with `blocked_reason`, **and
leave the comment on the card yourself** — in the morning that gets read on the board, not in a
log. Do not open a half PR to show progress.

## 4 · Delivering

Branch, commit, `gh pr create`. You do not merge it — the audit's verdict does that, and only if
it passes. Then:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" move <id> --status "in review"
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --text "<probes that ran, review verdict, PR>"
```

Return to `main` with a clean tree, and take the branch tip — `git rev-parse <branch>` — for
`head_sha`.

## 5 · The handoff

**Your last message is the handoff, and nothing else.** One JSON object, no prose around it; the
orchestrator reads it off what you emitted and writes the file itself. You do not write that file
— a session that said the whole handoff and forgot to write it once killed a day with the work
done and the PR open, so the step that could be forgotten was removed rather than repeated.

The shape is in `handoff.schema.json`, next to this file. Four fields are the whole point:

- **`decisions_deferred`** — every fork you resolved without anybody confirming it, and every one
  you left open. `[]` is not silence: **it asserts there were none**, and somebody checks that
  against the diff. If you wrote "left pending" anywhere — the PR, a comment, the code — that is
  an entry here.
- **`left_out`** — what the task asked for and you did not deliver. Cutting scope is the user's.
- **`probes`** — what actually ran. It is taken as your claim, not as evidence: the audit
  corroborates it against CI and by re-running whatever holds a claim up. Writing `passed: true`
  over something that never ran saves you nothing and stops the day. **A step `CLAUDE.md` requires
  and you could not run — the cross-model review over a large diff, most of all — is a probe with
  `passed: false` saying it did not run and why.** Substituting something weaker and not saying so
  is the one failure this whole arrangement exists to catch; the audit holds the PR over it.
- **`head_sha`** — the exact commit you delivered. The audit returns the one it read and the
  orchestrator stops if they disagree.

An honest handoff saying `blocked` is worth more than a tidy one saying `pr_opened` over
half-finished work. The second gets caught and stops the whole day.
