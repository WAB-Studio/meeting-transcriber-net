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
$O = "$PWD\.claude\orchestrator"; & "$O\start-day.ps1"
```

**That line is the whole calling convention, and every atom below repeats it in full.** Two halves,
and getting either wrong costs a call that looks like the atom refusing:

- **Through the PowerShell tool, never `powershell` from a shell.** Invoked that way the machine this
  runs on refuses the script for its execution policy and prints a `PSSecurityException` — before the
  atom has run, so nothing of the day happened and nothing says so. It reads exactly like a script
  that is broken.
- **`$O` is defined again in every call.** Shell state does not survive between tool calls, so an
  atom run off an `$O` set in an earlier one resolves to `\run-picker.ps1` at the drive root and comes
  back saying the file is not there. The path is absolute because `$PWD` is; a relative one answers to
  whatever directory the call happened to start in.

## 1 · The loop

```powershell
$O = "$PWD\.claude\orchestrator"; & "$O\run-picker.ps1"    # background — preflight, the board, /pick-task: which card
$O = "$PWD\.claude\orchestrator"; & "$O\run-worker.ps1"    # background — /next-task on that card, the handoff
$O = "$PWD\.claude\orchestrator"; & "$O\run-audit.ps1"     # background — /audit-session, the verdict
$O = "$PWD\.claude\orchestrator"; & "$O\close-cycle.ps1"   # merge the PR, or leave it open and file the card
```

Then start again at `run-picker.ps1`. Nothing paces this and nothing needs to.

**`start-day.ps1` does not always hand you `run-picker.ps1` next.** A day stopped mid-cycle -- the
usage window, a conversation that ended, an atom that refused -- is continued rather than replaced,
and `start-day.ps1`'s `RESULT` carries `action` (`started` or `continued`) and `next`, the atom the
run is waiting on. Begin the loop there instead of at the top: a continued run whose worker already
has a PR open resumes at `run-audit.ps1`, not at a fresh pick that would open a second one. Say
which it did -- a fresh day or a continued one, and from where -- before running anything else.

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
| `outcome: already_done` | the card was finished before the cycle began — say it and go on to `run-audit.ps1`, which will send you to `close-cycle.ps1`. **Not an ending.** |
| `action: merged` / `recovered` / `settled` | say it in one line and start the next cycle |
| `action: parked` | a decision is owed on that card — §2 — and the next cycle starts |
| `reason` with no `stop` | not an ending: it names what the run needs instead. Do that. |

**Nothing else ends the day.** A `hold` costs one PR a rerun and the next cycle takes the next task.

`outcome: blocked` is the one to read carefully, because two different things wear it. **A worker
blocked on the card in front of it** — it needs hardware nobody plugged in, the branch it was given
is gone — ends the day only in the sense the table says: run `end-day.ps1`. **A day blocked by the
board or the park ceiling** is the same. What is *not* `blocked` is a card that turned out to cost
nothing: that is `already_done`, and a day that ends on one has thrown away every hour after it over
a card that was never work.

## 1b · The steps you may skip, and the only ones

An atom refusing is not always an ending, and three of them say so in `reason` rather than in `stop`.
**These are the whole list. A step skipped for any other reason is the sequencer improvising**, which
is what `atom.psm1` says the atoms exist to stop — and it ends with a PR abandoned and its card left
in `in progress`, which is exactly the wreck this day is built to prevent.

| The atom says | Skip to | Why it is safe |
| --- | --- | --- |
| `run-audit.ps1`: *"cycle N ended as 'X', so there is no PR to audit"* | `close-cycle.ps1` | There is no diff. Auditing nothing cannot reach a verdict, and the cycle still has to be closed. |
| `close-cycle.ps1`: `action: already closed` | the next cycle | The close landed the first time; running it again would comment twice on one card. |
| `run-picker.ps1`: `outcome: no_tasks` before any session ran | `end-day.ps1` | The board was read without spending anything, and it said there is nothing. |

**Everything else is run in order**, including the atoms that look pointless. `close-cycle.ps1` after
a cycle that built nothing is not a formality: it is what records the cycle as finished, and without
it `Test-CycleStillOpen` refuses the next pick.

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
$O = "$PWD\.claude\orchestrator"; & "$O\end-day.ps1"    # the ending, report.md, and the lock released
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
