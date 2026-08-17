---
name: run-day
description: >-
  Run a day of unattended work on the board: sequence the orchestrator's atoms cycle after cycle
  until the board or the usage window ends it, and report what happened. Triggers:
  "trabajá el día", "work the day", "arrancá el día", "seguí el board todo el día".
---

# run-day — you are the day

Five scripts do everything that must not be forgotten. **You sequence them and you do the telling**,
and those are the only two things you do. Every script takes no arguments and prints a last line
starting with `RESULT` — that line is what you act on; everything above it is for the person.

```powershell
$O = "$PWD\.claude\orchestrator"
& "$O\start-day.ps1"
```

## 1 · The loop

```powershell
& "$O\run-worker.ps1"      # preflight, /next-task, the handoff
& "$O\run-audit.ps1"       # /audit-session, the verdict
& "$O\close-cycle.ps1"     # merge the PR, or leave it open and file the card
```

Then start again at `run-worker.ps1`. Nothing paces this and nothing needs to: a cycle is two
sessions of twenty to sixty minutes each.

What each `RESULT` means:

| Field | What you do |
| --- | --- |
| `stop` | the day is unsound — `end-day.ps1`, then report |
| `outcome: no_tasks` | nothing eligible is left — `end-day.ps1`, then report |
| `outcome: blocked` | `end-day.ps1`, then report; the reason is on the card already |
| `action: merged` / `recovered` | say it in one line and start the next cycle |
| `action: parked` | a decision is owed on that card — §2 — and the next cycle starts |
| `reason` with no `stop` | not an ending: it names what the run needs instead. Do that. |

**Nothing else ends the day.** A `hold` costs one PR a rerun and the next cycle takes the next task;
a verdict is a fact about one PR and never about the hours left.

`run-worker.ps1` asks the board what is eligible before it spends anything, so `no_tasks` can come
back in seconds and with no session run: a card in `in progress` is one to pick back up, a card in
`Open` tagged `grilled` is one to take, and with neither there is nothing to pay for. It is the same
ending either way — what changes is that an unstarted day costs a request instead of a session.

`day-status.ps1` says what a run is doing at any moment and takes no arguments either. Nothing here
depends on it — it is for a person who wants to look.

### Why this merges at all

`CLAUDE.md` leaves merging to the user, and **this is the one place that is departed from**. What
replaces them is the audit, and it is a weaker guarantee: it reads the diff, the board and the ISA,
and it waits for CI, but it is not a person. That is why the two verdicts that do not merge are
written to be cheap — one costs a PR a rerun, the other costs it a wait on a grill — while a bad
`pass` costs `main` plus everything built on it before anybody notices.

Closing the cards is still theirs. `in review` is where a day's work piles up, and that is
deliberate: the board is where you see what a day did.

## 2 · Nothing here waits for you

A cycle that meets a decision neither the worker nor the audit can make does not stop and does not
ask. It writes the decision on the card, sends the card to `pending` tagged `regrill`, and the day
takes the next task. `action: parked` in a `RESULT` is that, and the PR it names is open, green and
waiting on a grill rather than on a merge.

Say it in one line when it happens — the card, the PR, and what has to be settled — and go on.

**This is the arrangement's soft spot, and it is no longer yours to watch.** Every parked PR is
finished work that is not in `main`, and the reason it is acceptable is that it should be rare: the
grill exists to make it rare. So the second card sent back in one day ends it — `outcome: blocked`,
naming both cards — because two in a day is the grill behind the board, and a third would only be
one more card moved by a session that built nothing.

What is still yours is the shape across days. A day that ends on that ceiling, and then another,
means the grill is not catching what it should and the audit ought to go back to stopping the day.
Say so plainly when you see it; do not let it become the normal shape of a day.

## 3 · What you say, and when

**Silence is the default.** You speak when a cycle closes, when a rule fires, and when the day
ends. Somebody who asked to be left alone for the day does not want a heartbeat.

A cycle closing is one line: the card, the PR, the verdict, what happened to it.

Anything an atom prints as `!!` is worth interrupting for and worth quoting exactly — above all a
permission that was denied, because under `-p` that does not prompt: it denies, and the session
carries on as if the tool did not exist. Say which tool and what it tried. Never round it down to
"there were some permission warnings".

## 4 · Do not touch the repo while it runs

Any edit to a tracked file leaves the tree dirty and the next `run-worker.ps1` stops on it —
including the fix you just found. Write it on a card and let a later day take it.

## 5 · When it ends

```powershell
& "$O\end-day.ps1"         # the ending, report.md, and the lock released
```

Give them the short version off what it returns: how many cycles, what merged, what it cost, what
stopped it, what came back unmerged, and every card parked on a decision they now owe. The card
each merged PR came from is in `in review`; closing those is theirs, and so is merging what is left.

**Say once, when you launch, that you only report while this conversation is alive.** If it ends
mid-day the atoms stop being called, the day stops where it stood, and the lock it holds keeps the
next one out until it goes stale. What is guaranteed is `report.md` on disk, not a message
arriving.

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

A day launched this way behaves exactly like one you watch: nothing in it waits for a person, so
the only difference is who reads `report.md` afterwards.
