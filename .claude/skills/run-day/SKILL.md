---
name: run-day
description: >-
  Run a day of unattended work on the board: cycle after cycle, spawn each stage as a subagent, act
  on what it returns, and report what happened. Triggers: "trabajá el día", "work the day",
  "arrancá el día", "seguí el board todo el día".
---

# run-day — you are the orchestrator

You spawn the stages, route between them and do the acting. You are the only thing that knows this
table exists.

| Stage | `subagent_type` | Give it | It returns |
| --- | --- | --- | --- |
| pick | `picker` | a ceiling | the batch, and which cards may run together |
| recover | `recoverer` | card id, its card dir, PR number | a briefing on what was already done |
| plan | `planner` | card id, its card dir, PR number, the base | a plan in that card dir, its size and its risk |
| validate | `validator` | the batch dir, the cards, the base | `pass`, `revise` or `ask` per card, and collisions |
| work | `worker` | card id, its card dir, PR number, the base | a record, and a pushed branch |
| integrate | `integrator` | the base, the batch dir, the branches | one PR carrying the batch |
| audit | `auditor` | PR number, its card dirs, what carries each | a verdict per card, and one for the PR |

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

The batch dir holds `base`, the commit every stage in this batch is given. A card dir holds
`briefing.md`, `plan.md`, `review.md`, `pr.md` and `record.json` — each written by one stage and
read by the next. A stage whose answer has a reader on GitHub writes no file: the
verdict is the comment on the PR, and a second copy on disk is a copy nobody reads.

It is scratch. What survives a day is the card's status and comments, the branch and its commits,
the PR and the handoff — nothing else, and you write no parallel record of what a cycle did. Pick a
dead day up by starting a new one; §3 gets the context back.

### The handoff

`private/handoff.md` — one file, replaced whole at every close and never appended to.

Read it before the first pick. It is the only thing a day inherits.

Write it when the day ends: the user calling the close, `no_tasks`, `blocked`, or the second park.
Fifteen lines at the outside, and only what the next day would otherwise work out again — what waits
on a person, what a card turned out to need, a card that moved without landing. Never what happened:
the cards, the PRs and the commits carry that already.

## 2 · The cycle

**Pin the base first.** `git rev-parse origin/main` once, into `<batch dir>/base`, and hand that
sha to every stage. Nothing in the batch resolves `origin/main` to decide what it is building
against; the audit floor is still read at the trunk.

1. **Pick.** Spawn `picker` with a ceiling of candidates — four unless this machine has room for
   more.
   - `no_tasks` or `blocked` → end the day. Say why.
   - Move each `skipped` card to `Backlog` with its reason, and each `finished` card to `In review`.
     **Leave `held_over` where it is** — those cards are still `Ready` and still in the user's order.
   - Say the cards and the `why` in one line before you spawn anything else.
2. **Plan.** Spawn one `planner` per candidate, in parallel, each with its own card dir and the
   base. A card that is `In progress` or has an open PR gets a `recoverer` into that dir first.
   - `already_done` → that card closed itself. Drop it from the batch.
   - `needs_grill` or `blocked` → §4. Drop it from the batch.
3. **Form the batch.** Take the lead, then each surviving card in the picker's order while the batch
   stays under 200 `est_noncomment_lines` and under four cards. A `risk` of `contract`, a lead the
   picker returned `alone`, or a card already carrying an open PR, takes the batch by itself. What does not fit goes back to `Ready`
   unbuilt; say which, and why.
4. **Validate, or don't.** Spawn `validator` once over the formed batch when it holds more than one
   card,
   when any plan returned `floor_paths`, or when a plan carries a decision that holds up other parts
   of the application for months. One card, no floor path, nothing structural → skip it and say so.
   - `revise` → spawn that card's `planner` again, on the same card dir. It reads `review.md` there
     and answers every finding. **Once.** A second `revise` drops the card.
   - `ask` → §4. Drop the card.
   - A collision → drop the card that waits; it is `Ready` and the next pick finds it.

   A card dropped here does not stop the rest.
5. **Work.** Spawn one `worker` per card in the batch, in parallel, each in its own worktree under
   `C:\Users\pc\Documents\GitHub\Personal\worktrees`, never inside the checkout, deleted when the
   card is done and never reused. Give each its card dir by absolute path and the base. Tell each
   which folders its card owns and which it may not enter; say other cards are running and never
   which. Anything you hand a worker past its plan, tell it to declare — undeclared, an audit reads
   it as drift.
   - Anything but `built` drops that card. No card is left `In progress` by a batch that moved on.
6. **Integrate.** Spawn `integrator` with the base, the batch dir, the branches that built and the
   PR number when one already carries this batch. A card it drops is `Ready` again.
7. **Audit, or don't.** Only an open PR reaches here. Decide on whether the work carries a decision
   that holds up other parts of the application for months — a contract, a convention, a name that
   reaches disk, what proves a piece of work done. Which folder the diff touched is not the test.
   - **A path `.claude/audit-floor.md` names is audited whatever you think of it.** Check the PR's
     own files with `gh pr view <n> --json files`, and read that file at `origin/main`, never in a
     card's worktree.
   - A long `departures`, or a PR that ticks a claim, earns an audit more than a big diff does.
   - Say which way you went, and why, in one line on the PR before you spawn anything or merge.
   - **One audit per PR.** It does not run again after a fix.
8. **Act.** Yours, with no subagent, one PR at a time.
   - **Merge only green and only finished.** Bring the branch up to the `main` you are merging onto
     before you read its checks — two independently green PRs land a red trunk otherwise. `gh pr
     checks <n> --watch` red or unfinished is a `hold`, and so is a record declaring
     `blocks_the_pr` or a `left_out` the card asked for.
   - Not audited, `pass` or `pass_with_followup` → `gh pr merge <n> --merge --delete-branch`. The
     card stays in `In review`.
   - `hold` → the PR stays open. Fix it in one line yourself, on the PR's own branch: make it,
     commit, push, say so on the PR, merge on green. More than one line → spawn `worker` once with
     the verdict, then `integrator` again with that PR number to carry the fix onto the same branch.
     A hold the cards do not divide is the batch's, and it is fixed the same way. Then read the
     hold's reason against the diff before you merge. Still there → send
     the card where the verdict says, with the verdict's own body as a comment, and move on.
   - A `hold` over documentation, wording, a step that did not run or a merely poor line is not one.
     Merge, and leave it as a comment.
   - `ask`, or a `blocked` on a claim `ISA.md` does not carry → §4. Write no claim yourself.
   - **`followups_proposed` is a proposal and you decide.** A card is only for what no day finishes:
     a decision somebody has to make, or a piece too big for one PR. Everything smaller is taken
     today — send what a worker can take to a worker, and leave the rest as a comment on the PR.
     Before you open a card, say which existing card it is not.

Then pick again. Nothing paces this.

## 3 · A stage that returns nothing

A stage returns prose instead of its object, dies, or answers about the wrong card. Spawn
`recoverer` for that card, then a fresh agent of the stage that failed.

Twice on one stage in one cycle → stop. Leave the card in `In progress`, say so, take the next.

Do not protect work already done at any price. Say what was lost and move on.

## 4 · Park, never wait

A decision no stage may make: label the card `question`, send it to `Backlog`, take the next card.

The stage that met the decision already wrote it on the card. Do not write it again — move the card
and say in one line what is parked and why.

The second card parked in one day ends it, naming both. Say plainly when a day ends on that twice.

## 5 · A decision the user hands you

Say before you write it down and before you act on it when the framework or the platform will not
take it, naming what refuses it. Taste fires nothing. Then take whatever they answer second.

Never write down an answer you had reason to question and keep the reason.

## 6 · What you say

Speak when a batch is picked, when a cycle closes, when a rule fires, and when the day ends.

- **A pick** — the cards, their names, the `why`, before you spawn the next stage.
- **A cycle closing** — the card, the PR, the verdict, what happened to it.
- **A permission denied to an agent** — quote it exactly, naming the tool and what it tried.

Quote a stage's own words for what it found. Summarise only what you did.

Say once at the start that you report only while this conversation is alive. Open every comment you
leave with `[Day]`, and leave one only where §2 says to.

## 7 · Do not touch the repo

Edit no file, make no commit, switch no branch between cycles. Two exceptions, both in §2.8: the
merge, and the one-line fix in that card's worktree.
