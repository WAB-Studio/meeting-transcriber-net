---
name: run-day
description: >-
  Run a day of unattended work on the board: sequence the orchestrator's atoms cycle after cycle
  until the board or the usage window ends it, and report what happened. Triggers:
  "trabajá el día", "work the day", "arrancá el día", "seguí el board todo el día".
---

# run-day — you are the day

Six scripts do everything that must not be forgotten. **You sequence them and you do the telling**,
and those are the only two things you do. Every script takes no arguments and prints a last line
starting with `RESULT` — that line is what you act on; everything above it is for the person.

**Never open a script.** This file says what an atom does; its `RESULT` says what it wants now. If
the two disagree, run what is written here, do what `reason` says, and put it in the report.

```powershell
$O = "$PWD\.claude\orchestrator"
& "$O\start-day.ps1"
```

## 1 · The loop

```powershell
& "$O\run-picker.ps1"      # background — preflight, the board, /pick-task: which card
& "$O\run-worker.ps1"      # background — /next-task on that card, the handoff
& "$O\run-audit.ps1"       # background — /audit-session, the verdict
& "$O\close-cycle.ps1"     # merge the PR, or leave it open and file the card
```

Then start again at `run-picker.ps1`. Nothing paces this and nothing needs to.

**The three that run an agent go in the background: `run-picker.ps1`, `run-worker.ps1` and
`run-audit.ps1`.** A session outlasts the ten minutes a foreground call gets, and dies there with
the money already spent. You are told when it exits — do not poll it, and do not start the next
atom until you have its `RESULT`. `start-day.ps1`, `close-cycle.ps1`, `end-day.ps1` and
`day-status.ps1` run no agent and go in the foreground.

What each `RESULT` means:

| Field | What you do |
| --- | --- |
| `stop` | the day is unsound — `end-day.ps1`, then report |
| `outcome: no_tasks` | nothing eligible is left — `end-day.ps1`, then report |
| `outcome: blocked` | `end-day.ps1`, then report; `blocked_reason` says what has to happen |
| `outcome: picked` | say the card and the `why` in one line, then `run-worker.ps1` |
| `action: merged` / `recovered` | say it in one line and start the next cycle |
| `action: parked` | a decision is owed on that card — §2 — and the next cycle starts |
| `reason` with no `stop` | not an ending: it names what the run needs instead. Do that. |

**Nothing else ends the day.** A `hold` costs one PR a rerun and the next cycle takes the next task.

**`run-picker.ps1` is what says which card, and no other atom answers that.** It asks the board
before it spends anything, so `no_tasks` and `blocked` can come back in seconds with no session run.

`day-status.ps1` says what a run is doing at any moment and takes no arguments either. Nothing here
depends on it — it is for a person who wants to look.

Merging is the audit's here rather than the user's, and `close-cycle.ps1` does it on a `pass`.
Closing the cards is still theirs: `in review` is where a day's work piles up.

## 2 · Nothing here waits for you

A cycle that meets a decision neither the worker nor the audit can make writes it on the card, sends
the card to `pending` tagged `regrill`, and takes the next task. `action: parked` is that, and the
PR it names is open, green and waiting on a grill rather than on a merge. Say it in one line — the
card, the PR, what has to be settled — and go on.

**The second card parked in one day ends it** — `outcome: blocked`, naming both.

A day that ends on that ceiling, and then another, means the grill is not catching what it should.
Say so plainly; do not let it become the normal shape of a day.

## 3 · What you say, and when

**Silence is the default.** You speak when a card is picked, when a cycle closes, when a rule fires,
and when the day ends. Somebody who asked to be left alone for the day does not want a heartbeat.

- **A pick** — the card, its name, the `why`. It goes out *before* you run the worker, and you do
  not wait to be told to go on.
- **A cycle closing** — the card, the PR, the verdict, what happened to it.
- **Anything an atom prints as `!!`** — quote it exactly, above all a denied permission: under `-p`
  that does not prompt, it denies, and the session carries on as if the tool did not exist. Say
  which tool and what it tried. Never round it down to "there were some permission warnings".

## 4 · Do not touch the repo while it runs

Any edit to a tracked file leaves the tree dirty and the next `run-picker.ps1` stops on it —
including the fix you just found. Write it on a card and let a later day take it.

## 5 · When it ends

```powershell
& "$O\end-day.ps1"         # the ending, report.md, and the lock released
```

Give them the short version off what it returns: how many cycles, what merged, what it cost, what
stopped it, what came back unmerged, and every card parked on a decision they now owe.

**Say once, when you launch, that you only report while this conversation is alive.** If it ends
mid-day the atoms stop being called, the day stops where it stood, and the lock it holds keeps the
next one out until it goes stale. What is guaranteed is `report.md` on disk, not a message arriving.

## 6 · Starting on its own

```powershell
$a = New-ScheduledTaskAction -Execute "claude" -Argument '-p "/run-day"'
Register-ScheduledTask -TaskName "meeting-transcriber-dia" -Action $a `
     -Trigger (New-ScheduledTaskTrigger -Daily -At 9am) `
     -WorkingDirectory "C:\Users\pc\Documents\GitHub\Personal\meeting-transcriber-net"
```

Once a day, not a cron every half hour: the lock refuses the second run rather than splitting the
work between them. **The working directory is load-bearing** — a scheduled task starts in
`C:\Windows\System32`, and from there this project does not exist: no `CLAUDE.md`, no skills, no
settings.
