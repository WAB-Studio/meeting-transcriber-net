---
name: run-day
description: >-
  Run a day of unattended work: cycle after cycle, spawn each stage as a subagent, act on what it
  returns, and report what happened. Triggers: "trabajá el día", "work the day", "arrancá el día",
  "seguí el board todo el día".
---

# run-day — you are the orchestrator

You spawn the stages, route between them and do the acting. You are the only thing that knows this
table exists.

| Stage | `subagent_type` | Give it | It returns |
| --- | --- | --- | --- |
| pick | `picker` | a ceiling | the candidates, in the order they are to be planned |
| recover | `recoverer` | card id, its card dir, PR number | a briefing on what was already done |
| plan | `planner` | the batch dir, the cards, the base, how many workers may run at once | a plan per card, and the split saying which worker builds what and on which model |
| validate | `validator` | the batch dir, the cards, the base | `pass`, `revise` or `ask` per card, and collisions |
| work | `worker` | its share, its card dir, the base, a followup where there is one | a record, and a pushed branch |
| audit | `auditor` | the base, the batch dir, the branches, the PR number when one exists | one PR carrying the batch, a verdict per card and one for the PR |

Pass each agent what its column says and nothing more: never who produced it, never what happens to
it next, never where the day stands.

**Route on the object; never retell the content.** Each stage writes what it knows to a file and the
next stage reads that file whole. What reaches you is the structured object, and it is what you act
on and what you report. Never paraphrase one stage's work into another's input.

## 1 · The run directory

```
private/runs/<yyyy-mm-dd-hhmm>/<task_id>/
```

Absolute paths, under the primary checkout, ignored by git. The batch dir is the dated folder; a
card dir is the folder named for a card. Create each before the stage that writes into it, and pass
the right one — a stage given the wrong depth reads nothing and says nothing about it.

The batch dir holds `base`, the commit every stage in this batch is given, and `split.md`, what the
planner divided the work into. A card dir holds `briefing.md`, `plan.md`, `review.md`, `pr.md` and
`record.json` — each written by one stage and read by the next. A stage whose answer has a reader on
GitHub writes no file: the verdict is the comment on the PR, and a second copy on disk is a copy
nobody reads.

It is scratch. What survives a day is the issue's comments and labels, the branch and its commits,
the PR, the handoff, and three files under `private/`: `owed.md`, what merged wrong and has to be
built next; `asked.md`, every question waiting on the user; `proposed-issues.md`, what has been proposed and not
yet decided. Nothing else, and you write no parallel record of what a cycle did. Pick a
dead day up by starting a new one; §3 gets the context back.

### The handoff

`private/handoff.md` — one file, replaced whole at every close and never appended to.

Read it before the first pick. It is the only thing a day inherits.

Write it when the day ends. Fifteen lines at the outside, and only what the next day would otherwise
work out again — what waits on a person, what a card turned out to need, a card that moved without
landing. Never what happened: the issues, the PRs and the commits carry that already.

## 2 · The cycle

**Pin the base first.** `git rev-parse origin/main` once, into `<batch dir>/base`, and hand that
sha to every stage. Nothing in the batch resolves `origin/main` to decide what it is building
against; the audit floor is still read at the trunk.

1. **Pick.** Spawn `picker` with a ceiling of candidates — six unless this machine has less room.
   - `no_tasks` or `blocked` → end the day. Say why.
   - Say the cards and the `why` in one line before you spawn anything else.
2. **Recover.** A card already being worked, or carrying an open PR, gets a `recoverer` into its
   card dir before anything is planned.
3. **Plan.** Spawn **one** `planner` over every candidate, with the batch dir, the base, and how
   many workers may run at once — four unless this machine has less room. It plans each card and
   divides the work between the workers it wants, naming for each share what it builds and which
   model builds it. What it drops is said and goes back to the pool unbuilt.
   - A card it returns `needs_grill` or `blocked` → §4, and the rest of the batch goes on.
   - `already_done` → that card closed itself.
4. **Validate, or don't.** Spawn `validator` once over the split when it holds more than one card,
   when any plan returned `floor_paths`, or when a plan carries a decision that holds up other parts
   of the application for months. One card, no floor path, nothing structural → skip it and say so.
   - `revise` → spawn `planner` again over that card alone, on the same card dir. It reads
     `review.md` there and answers every finding. **Once.** A second `revise` drops the card.
   - `ask` → §4. Drop the card.
   - A collision → drop the card that waits; it is still in the pool and the next pick finds it.

   A card dropped here does not stop the rest.
5. **Work.** Spawn one `worker` per share, in parallel, each in its own worktree under
   `C:\Users\pc\Documents\GitHub\Personal\worktrees`, never inside the checkout, deleted when the
   share is done and never reused. Give each its card dir by absolute path, its share and the base.
   Tell each which paths it owns and which belong to another share — to read and not to write, never
   to stay out of. Say other shares are running and never which. Anything you hand a worker past its
   plan, tell it to declare, so the audit reads the reason rather than working it out.
   - Anything but `built` drops that share. A worker that wanted a path another share owns is not
     one of those: it finishes, and what it wanted is a `followups_proposed` entry for the audit.
6. **Audit.** Spawn `auditor` with the base, the batch dir, the branches that built, and the PR
   number when one already carries this batch. It carries the branches onto one, proves the whole
   diff once, opens the PR, and then judges what it built — every card against its plan through its
   own commit, and the PR through the worst of them.
   - **One audit per batch.** It does not run again after a fix.
7. **Act.** Yours, with no subagent. **Merging is the rule.** Work that runs goes into `main` and
   what is wrong with it is carried forward; a batch held over a bad decision costs a day, and the
   same defect carried costs one entry in a file.
   - Bring the branch up to the `main` you are merging onto before you read its checks.
   - `pass`, `pass_with_followup` or `ask` → `gh pr merge <n> --merge --delete-branch`, whatever is
     owed. A verdict is not a reason to keep a branch alive.
   - **Nothing this batch cut is left on the remote.** `--delete-branch` reaches the batch's branch
     and no other, and a worker's branch was carried by cherry-pick, so its commits are on `main`
     under different shas and nothing will ever call it merged. The auditor deletes what it carried;
     check that it did, and delete what it says it dropped once you have settled that card.
   - `hold` → the code does not run, or the fix is roughly fifteen lines. Under a line, make it
     yourself on the PR's own branch and merge on green. Otherwise spawn `worker` once with the
     verdict as its followup, then merge. A `hold` for anything else is one you read as
     `pass_with_followup` — say so on the PR and merge.
   - **What is owed is not lost.** The auditor writes it into `private/owed.md`, line by line; the
     next planner reads that file and builds it. Check it landed before you merge, and say what went
     into it.
   - `decisions_owed`, or a `blocked` on a claim `ISA.md` does not carry → §4, and the merge happens
     anyway. Write no claim yourself.
   - **`followups_proposed` is a proposal and you decide. You open no issue, ever.**
     - `fits_this_branch`, the PR still open, and no path `.claude/audit-floor.md` names → spawn
       that card's `worker` again with the followup, its card dir and that PR number.
     - Somebody has to decide it → §4.
     - `product` → a proposal in `private/proposed-issues.md`, quoting the followup's own words.
       Whether it becomes a card is settled by the `curate` skill at the close, not here.
     - The rest → an `owed` entry, or a line on the standing machinery card the `github` skill names.
8. **Curate.** At the close of the day, before the handoff, read the `curate` skill and do what it
   says over `private/proposed-issues.md`. It is yours and not a subagent's: whether a proposal is
   real turns on what happened today, which you have and nothing spawned cold does. It opens cards
   and closes none.
9. **Leave nothing open.** A PR this day opened is merged this day. One you cannot merge is a
   question you put to the user under §4, named as what is waiting and on whom — never a thing left
   standing for somebody to notice.

Then pick again. Nothing paces this.

## 3 · A stage that returns nothing

A stage returns prose instead of its object, dies, or answers about the wrong card. Spawn
`recoverer` for that card, then a fresh agent of the stage that failed.

Twice on one stage in one cycle → stop. Say what was lost, drop that card, take the rest.

Do not protect work already done at any price.

## 4 · Ask, and keep going

A decision no stage may make is a question **you** put to the user, in your own words, naming what
changes with each answer. Nobody hunts through twenty issues looking for the one that needs them,
so a card labelled and left is a question nobody answers.

Three things, and none of them stops anything:

1. **Ask it**, here, while the rest of the batch runs.
2. **Append it to `private/asked.md`** — the card, the question, the options, the date. One file, so
   the user reads one thing and not the board. Strike an entry when it is answered.
3. **Label the card `question`** so the pool skips it, and go on with everything else.

The batch does not wait, the day does not end, and the merge does not stop. The stage that met the
decision already wrote it on the card; do not write it again.

## 5 · A decision the user hands you

Say before you write it down and before you act on it when the framework or the platform will not
take it, naming what refuses it. Taste fires nothing. Then take whatever they answer second.

Never write down an answer you had reason to question and keep the reason.

## 6 · What you say

Speak when a batch is picked, when a cycle closes, when a rule fires, and when the day ends.

- **A pick** — the cards, their names, the `why`, before you spawn the next stage.
- **A cycle closing** — the cards, the PR, the verdict, what happened to it.
- **A permission denied to an agent** — quote it exactly, naming the tool and what it tried.
- **The curate** — every card opened, and everything thrown away with what settled it.

Quote a stage's own words for what it found. Summarise only what you did.

Say once at the start that you report only while this conversation is alive. Open every comment you
leave with `[Day]`, and leave one only where §2 says to.

## 7 · Do not touch the repo

Edit no file, make no commit, switch no branch between cycles. Four exceptions, all in §2: the
merge, the one-line fix on the PR's own branch, the proposal you add to
`private/proposed-issues.md`, and the curate, which rewrites that file and is the one place you
decide rather than route.
