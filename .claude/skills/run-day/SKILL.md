---
name: run-day
description: >-
  Run a day of unattended work on the board: cycle after cycle, spawn the picker, the worker and the
  audit as subagents, act on what each returns, and report what happened. Triggers: "trabajá el día",
  "work the day", "arrancá el día", "seguí el board todo el día".
---

# run-day — you are the orchestrator

**You are the day.** Not a sequencer of scripts: the session that spawns each stage as a subagent,
reads what it returns, and does the acting itself. There is no engine under this file, no lock, no
run folder and no event stream. There used to be all four; §1 is the rule that replaced them.

Four agents do the thinking, and you do everything between them:

| Stage | `subagent_type` | Give it | It returns |
| --- | --- | --- | --- |
| pick | `picker` | nothing | one card id, or a reason there isn't one |
| work | `worker` | card id, PR number, any briefing | a record: what was built, the PR, what it left out |
| audit | `auditor` | PR number and that record | a verdict: `pass`, `pass_with_followup`, `ask` or `hold` |
| recover | `recoverer` | card id, PR number | a briefing on what was already done |

Spawn each with the Agent tool, one at a time, and wait for it. **An agent's final message is its
answer** — you read it directly, and nothing writes it to disk on the way.

**None of them knows the others exist.** Each is written for its own inputs and its own answer, so
you pass what the table says and nothing more: never who produced it, never what happens to it next,
never where the day stands.

## 1 · The state is not yours to keep

**Everything that has to survive you already lives somewhere durable:** the card's status and its
comments on the board, the branch and its commits in git, the PR on GitHub. That is the whole state
of a day, and none of it is a copy.

So you keep nothing. No file says which cycle you are on, because the board says which card is in
progress and GitHub says which PR is open — and those two are what a next session reads anyway.
**Never write a parallel record of what a cycle did.** The moment there are two, they disagree, and
the one you kept is the one nobody else can see.

Which means a day that dies is picked up by starting a new one: the picker reads the board, finds
the card in progress or the PR open, and §4 gets its context back.

## 2 · The cycle

1. **Pick.** Spawn `picker` with no input. It returns one card, or `no_tasks`, or `blocked`.
   - `no_tasks` or `blocked` → the day ends. Say why.
   - A card → say the card and the `why` in one line, before you spawn anything else.
   - It also returns `skipped[]` and `finished[]`. **You do the board moves it declared** —
     `skipped` to `Backlog` with its reason, `finished` to `In review` — because a subagent that
     moves a card and then dies has taken it out of the pool with nothing saying why.
2. **Work.** Spawn `worker` with the card id, and the PR number if the pick found one. If the card
   was `In progress` or its PR is open, spawn `recoverer` first and pass the worker its briefing. §4.
3. **Audit.** The record says `pr_opened` → spawn `auditor` with the PR number and that record. Any
   other outcome closed the card itself and there is nothing to audit.
4. **Act on the verdict.** This part is yours and there is no subagent for it:
   - `pass` or `pass_with_followup` → every finding this card owns goes back to `worker` first,
     however small, and then audit again. Only once the verdict names none, merge the PR.
     `gh pr merge <n> --merge --delete-branch`. The card stays in `In review`; closing it is the
     user's.
   - `hold` → the PR stays open. Spawn `worker` again on the same card, passing the verdict as its
     briefing, and audit again.
   - Three rounds of work and audit on one card is the ceiling. Still holding, send the card where
     the verdict says with the verdict's own body as a comment, and take the next card.
   - `ask` → a decision nobody here may make. Write it on the card, label it `question`, send it
     back to `Backlog`, and take the next card. §5.

Then start again at the pick. Nothing paces this.

## 3 · Cards in parallel

One card at a time is the default. How many run beside it is your decision — the picker knows about
none of them.

**Where a worker works:** a slot at `../worktrees/slot-N`, a sibling of the checkout, never inside
it. One slot a worker. The main checkout is a slot like any other and stays one worker's.

**Slots are taken and given back, never created and destroyed.** Add slot-N+1 when every slot is
busy. On the merge, return the slot: `git checkout main`, `git pull`, `git reset --hard`,
`git clean -fd`. Never `-fdx` — `bin/` and `obj/` are what the slot is for, and a worker that
starts on a cold build has paid for a slot it did not get.

**A slot is free when `git worktree list` puts it on `main` and `git status` is clean.** Anything
else is a worker's, or is wreckage — read it before you hand it out.

**Run cards together when** they sit in different projects, or all but one are tests or documents
only.

**Never run a card beside anything when** it refactors, moves or renames what exists; changes a
contract under `Domain/`, a migration, or a name that reaches disk; or settles a convention.
Whatever ran beside it was built against a shape that stopped being true, and lands green agreeing
with nothing.

Conflicting lines at merge are expected and are not the thing being avoided.

**Tell each worker which folders its card owns and which it may not enter.** Say other cards are
running; never say which.

**Merge one at a time**, and audit against what is already merged.

The ceiling is what this machine builds and tests at once, not how many cards look independent.

## 4 · A stage that comes back with nothing

A subagent can end holding nothing useful: it returns prose instead of its contract, or it dies, or
it answers about the wrong card. **That is not the day ending.** It costs one stage, and the fix is
to give the next attempt the context the last one had.

Spawn `recoverer` with the card id. Pass its briefing to a fresh agent of the stage that failed, and
go on.

**Twice on the same stage in one cycle and you stop trying.** Leave the card in `In progress`, say
so, and take the next card: a stage that fails twice with context in hand is not failing over
context.

**Work already done is not worth protecting at any price.** A cycle that ends with the branch
committed and no PR is a cycle whose work a later session can find or rebuild. Say what was lost
and move on; standing still costs more than rebuilding.

## 5 · Nothing here waits for the user

A cycle that meets a decision neither the worker nor the audit can make writes it on the card, sends
the card back to `Backlog` labelled `question`, and takes the next task. Say it in one line — the card, the
PR, what has to be settled — and go on.

**The second card parked in one day ends it**, naming both. A day that ends on that ceiling twice
means the grill is not catching what it should. Say so plainly.

## 6 · A decision the user hands you

Decisions arrive outside a grill — over a verdict, in passing, to get a cycle moving — and the
grill's check on them does not arrive with them. It runs here: an answer the framework or the
platform will not take gets said before you write it on a card and before you act on it, naming
what refuses it. Taste fires nothing.

Then take whatever they answer second. What you may not do is write down an answer you had reason
to question and keep the reason.

## 7 · What you say, and when

**Silence is the default.** You speak when a card is picked, when a cycle closes, when a rule fires,
and when the day ends. Somebody who asked to be left alone for the day does not want a heartbeat.

- **A pick** — the card, its name, the `why`. It goes out *before* you spawn the worker.
- **A cycle closing** — the card, the PR, the verdict, what happened to it.
- **A permission denied to an agent** — quote it exactly, naming the tool and what it tried.
  Never round it down to "there were some permission warnings".

**Say once, when you start, that you only report while this conversation is alive.** If it ends
mid-day the day stops where it stood. What survives is on the board and on GitHub, which is the
whole point, and the next day picks it up from there.

Every comment you leave on a card opens with `[Day]`.

## 8 · Do not touch the repo yourself

The worker owns the checkout. You do not edit files, do not commit and do not switch branches
between cycles — a dirty tree stops the next worker in its preflight, including over a fix you
found. Write it on a card and let a later day take it.

The one exception is the merge in §2, which touches GitHub rather than the checkout. Taking and
returning the slots of §3 is not a second exception: they sit outside the checkout, and the reason
they sit outside it is that a worktree inside one is a dirty tree under another name.
