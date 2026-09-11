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
| pick | `picker` | a ceiling | the candidates, as `priority` and `secondary` |
| recover | `recoverer` | card id, its card dir, PR number | a briefing on what was already done |
| plan | `planner` | the batch dir, both candidate lists, the base, last batch's `consequence` count, how many workers may run at once | a plan per card, and the split saying which worker builds what |
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
the PR, the handoff, and four files under `private/`: `owed.md`, every defect and every repair still
owed — everything that is not a feature, every entry open and carrying an id, a severity and an
origin; `owed-closed.md`, what left that queue; `asked.md`, every question waiting on the user; and
`proposed-issues.md`, what has been proposed and not yet decided. Nothing else, and you write no
parallel record of what a cycle did. Pick a dead day up by starting a new one; §3 gets the context
back.

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

1. **Pick.** Spawn `picker` with a ceiling of eight candidates.
   - `no_tasks` or `blocked` → end the day. Say why.
   - Say the cards and the `why` in one line before you spawn anything else. Hand the planner both
     lists whole; which of them becomes this batch is its call, not yours.
2. **Recover.** A card already being worked, or carrying an open PR, gets a `recoverer` into its
   card dir before anything is planned.
3. **Plan.** Spawn **one** `planner` over both lists, with the batch dir, the base, how many
   `consequence` entries the last batch created, and how many workers may run at once — four; five
   is what the memory on this machine will not carry. It plans each card and divides the work
   between the workers it wants, naming for each share what it builds. What it drops is said and
   goes back to the pool unbuilt.
   - A card it returns `needs_grill` or `blocked` → §4, and the rest of the batch goes on.
   - `already_done` → that card closed itself.
4. **Validate, or don't.** Spawn `validator` once over the split when it holds more than one card,
   when any plan returned `floor_paths`, or when a plan carries a decision that holds up other parts
   of the application for months. One card, no floor path, nothing structural → skip it and say so.
   - `revise` → spawn `planner` again over that card alone, on the same card dir. It reads
     `review.md` there and answers every finding. **Once.** A second `revise` drops the card.
   - `ask` → §4. Drop the card.
   - A collision → act on the `remedy` the validator returned, which is the only stage that read
     both plans. `one_share` or `after` → spawn `planner` again over the batch, **once**, to re-split
     it; a collision surviving that drops the card that waits. `postpone` → drop it now; it is still
     in the pool and the next pick finds it.

   A card dropped here does not stop the rest.
5. **Work.** A share carrying `after` runs in a second wave, once that share has built and pushed,
   and its base is that share's pushed tip and not `base`; everything else runs now. Spawn one
   `worker` per share of the wave, in parallel, each in its own worktree under
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
   - **The ledger has one writer and it is you.** Count the open entries first. Then, in the primary
     checkout, in one pass: every `owed` the audit returned that `owed-closed.md` does not already
     hold becomes an entry under a fresh `O-<yyyymmdd>-<nn>` — fresh against both files, because an
     id is never reissued — at `passed 0` with its severity and origin. Every `owed_built` from a
     share that **built**, and every `owed_settled` the audit returned, leaves for
     `private/owed-closed.md` under the commit that closed it; an `owed_built` carrying no commit
     closed nothing. Every entry still open that was open when you counted has its `passed` raised
     by one, whatever became of the share that held it.
   - **Then say the six, and check they balance**: open at the start, closed, created by integration,
     created as a known consequence, preexisting found, open at the end. `end = start − closed +
     integration + consequence + preexisting`. It not balancing means an entry was lost or counted
     twice, and that is worth more than the batch. **A `consequence` is a planning defect** — it
     belongs at zero, and it is what the next planner's closure rule is judged by.
   - `decisions_owed`, or a `blocked` on a claim `ISA.md` does not carry → §4, and the merge happens
     anyway. Write no claim yourself.
   - **`followups_proposed` is a proposal and you decide. You open no issue, ever.**
     - `fits_this_branch`, the PR still open, and no path `.claude/audit-floor.md` names → spawn
       that card's `worker` again with the followup, its card dir and that PR number.
     - Somebody has to decide it → §4.
     - **A defect, a probe, a guard, a cleanup — anything that is not a feature → `private/owed.md`,
       and never the board.** The next planning pass builds it out of that file, and it is the one
       you take when you are unsure.
     - A feature the product cannot do yet → a proposal in `private/proposed-issues.md`, quoting the
       followup's own words. Whether it becomes a card is settled by the `curate` skill at the
       close, not here.
8. **Curate.** At the close of the day, before the handoff, read the `curate` skill and do what it
   says over `private/proposed-issues.md`. Yourself, never a subagent. It opens cards and closes
   none.
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

Edit no file, make no commit, switch no branch between cycles. Five exceptions, all in §2: the
merge, the one-line fix on the PR's own branch, the proposal you add to
`private/proposed-issues.md`, the ledger, which no other stage may write, and the curate, which
rewrites that file and is the one place you decide rather than route.
