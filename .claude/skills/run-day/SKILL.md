---
name: run-day
description: >-
  Run a day of unattended work on the board: sequence the orchestrator's atoms cycle after cycle,
  carry the one question a cycle cannot answer to the user, and report what happened. Triggers:
  "trabajá el día", "work the day", "arrancá el día", "seguí el board todo el día".
---

# run-day — you are the day

Six scripts do everything that must not be forgotten. **You do the sequencing and the asking**, and
those are the only two things you do. Every script takes no arguments and prints a last line
starting with `RESULT` — that line is what you act on; everything above it is for the person.

```powershell
$O = "$PWD\.claude\orchestrator"
& "$O\start-day.ps1"
```

## 1 · The loop

```powershell
& "$O\run-worker.ps1"      # preflight, /next-task, the handoff
& "$O\run-audit.ps1"       # /audit-session, the verdict
& "$O\close-cycle.ps1"     # merge, or put the card back, or hand you the questions
```

Then start again at `run-worker.ps1`. Nothing paces this and nothing needs to: a cycle is two
sessions of twenty to sixty minutes each.

What each `RESULT` means:

| Field | What you do |
| --- | --- |
| `stop` | the day is unsound — `end-day.ps1`, then report |
| `hold` | this cycle does not go on — `close-cycle.ps1`, then the next cycle |
| `outcome: no_tasks` | nothing grilled is left — `end-day.ps1`, then report |
| `outcome: blocked` | `end-day.ps1`, then report; the reason is on the card already |
| `action: merged` / `recovered` | say it in one line and start the next cycle |
| `action: ask` | §2 — from `run-worker.ps1` or from `close-cycle.ps1` alike |

**Nothing else ends the day.** A `hold` costs one PR a rerun and the next cycle takes the next task;
a verdict is a fact about one PR and never about the hours left.

`day-status.ps1` says what a run is doing at any moment and takes no arguments either. Nothing here
depends on it — it is for a person who wants to look.

### Why this merges at all

`CLAUDE.md` leaves merging to the user, and **this is the one place that is departed from**. What
replaces them is the audit, and it is a weaker guarantee: it reads the diff, the board and the ISA,
and it waits for CI, but it is not a person. That is why the two verdicts that do not merge are
written to be cheap — a `hold` costs one PR a rerun, a question costs one answer — while a bad
`pass` costs `main` plus everything built on it before anybody notices.

Closing the cards is still theirs. `in review` is where a day's work piles up, and that is
deliberate: the board is where you see what a day did.

## 2 · The question is the only thing you can do that they cannot

Two atoms can hand you one and they mean different things. `run-worker.ps1` asking means the worker
met a fork before building — the cheap one, a short session lost. `close-cycle.ps1` asking means the
audit found one in a PR that already exists, and that PR is neither merged nor put back until you
come back with an answer.

Either way the questions are already written down and the cycle has stopped.

**One question per `AskUserQuestion` call, in the order given.** Each carries its own options and
its own consequence, and batching them makes somebody weigh four things to answer the first. Take
the text, the `why` and the options straight off the `RESULT` — add nothing, recommend nothing,
invent no fifth option.

Then write what they picked and run the atom that reads it:

```json
[ { "id": "q1", "label": "<the option, character for character>", "notes": "<what they typed>" } ]
```

```powershell
& "$O\answer-cycle.ps1"    # reads .scratch/answers.json
```

Write that file with the `Write` tool at `.scratch/answers.json`. **The label is all you write** —
never what it means. The effect is read back off the verdict's own option list, so a label spelled
loosely is refused rather than guessed at. If the atom refuses the file, it says why; fix it and run
it again.

`answer-cycle.ps1` knows which of the two asked and does the right thing with it: the audit's answer
merges the PR or sends the card back, and the worker's puts the card in the pool carrying what was
decided, for a later cycle to build.

**If nobody is there to ask** — you are running headless and `AskUserQuestion` has no one on the
other end — do not guess and do not wait. Leave the cycle where it is and end the day: the question
is on the stream, the card has not moved, and whoever comes back finds it.

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
stopped it, what came back unmerged, every decision they made. The card each PR came from is in
`in review`; closing those is theirs, and so is merging anything left open.

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

A day launched this way has nobody to ask, which is §2's last paragraph and the reason the grill
exists. Everything else it can do alone.
