---
name: supervise-day
description: >-
  Run a day of unattended work and be the one who reports it. Launches the orchestrator, watches
  the run's event stream while it works, stays quiet while everything is normal and speaks the
  moment something is not. Triggers: "trabajá el día", "work the day", "arrancá el orquestador",
  "supervise the day", "seguí el board todo el día".
---

# supervise-day — the door, and the one who reports

The day has three roles and this is the third. `run-day.ps1` executes and decides nothing about
the code; the worker and the audit sessions do the work and judge it; **you observe and you talk
to the person.** You are the only piece here with judgement, and the only one they hear from.

The person asked for this in a sentence. They do not know a path, they should not have to learn a
command, and they are not going to go read a log — that is the whole reason you exist.

## 1 · Before launching

```powershell
$O = "$PWD\.claude\orchestrator"
git status --porcelain      # has to be empty
git rev-parse --abbrev-ref HEAD
```

A dirty tree or a branch that is not `main` fails the preflight and the day stops on cycle 1. Do
not tidy it up yourself: say what is in the way and let them decide. Uncommitted work is theirs.

## 2 · Launching

Detached, so the day survives this conversation ending:

```powershell
$p = Start-Process -FilePath "powershell.exe" `
     -ArgumentList '-NoProfile','-File',"$O\run-day.ps1" `
     -WorkingDirectory $PWD -WindowStyle Hidden -PassThru
```

Tell them the PID and that `Stop-Process -Id <pid>` ends it. `day.lock` holds that PID and is
released on exit, so a second launch is refused rather than sharing a checkout with the first.

## 3 · Watching

```powershell
& "$O\day-status.ps1" -Json      # the state, for you
& "$O\day-status.ps1"            # the same thing, for them
```

The reader applies the thresholds; you decide what they mean. A worker session runs 20–60 minutes,
so **do not poll tightly** — every check costs context and buys nothing. Watch the event stream
for new lines and read the status when one arrives, or check every few minutes. Either way the
rule is the same:

**Silence is the default. You speak when a rule fires, when a cycle closes, and when the day
ends.** A person who asked to be left alone for the day does not want a heartbeat.

## 4 · What each anomaly is worth

| Code | What it means | What you do |
| --- | --- | --- |
| `denials` | a permission was refused and the session went around it | **interrupt now** — see below |
| `killed` | a session passed 90 minutes and was killed | interrupt; the work is lost, say so |
| `window` | the usage window closed | interrupt; say when it comes back |
| `stopped` | the day ended on something other than `no_tasks` | interrupt with the reason |
| `silence` | a live session has emitted nothing for 15+ min | mention it once, then watch |
| `cost` | a cycle spent several times the median | mention it with the number |

## 5 · Denials are the loud one

Under `-p` there is nobody to ask, so a missing permission does not prompt — **it denies, and the
session carries on as if the tool did not exist.** The damage is never the denial. It is what
happens next: the session does the same job by a worse route, or drops it, and writes nothing
about either. On 2026-08-16 one cycle collected 27 denials, the board CLI among them, and the
cross-model review this repo requires over a large diff never ran. Nobody would have known until
the morning.

So when `denials` fires you interrupt, and you say three things: **which tool, what it tried, and
what it probably did instead.** The reader hands you the command that was refused; that command is
the permission rule that is missing from `.claude/orchestrator/settings.json`, and naming it is
the difference between a complaint and a fix.

Never round it down to "there were some permission warnings".

## 6 · While the day runs, do not touch the repo

Any edit to a tracked file leaves the tree dirty, and **the next cycle's preflight stops the day
over it.** That includes the fix you just found, the typo, and the settings entry that would have
prevented the denial you are reporting. Write it down, tell them, and land it after the day ends
or in a card the day itself can pick up.

## 7 · When it ends

```powershell
& "$O\day-status.ps1" -Report     # writes report.md and prints where
```

Read it and give them the short version: how many cycles, what merged, what it cost, what stopped
it, and every anomaly that fired. The card each PR came from is in `in review` — closing those is
theirs, and so is merging anything the day left open.

If they were away the whole time, `report.md` is what they read instead of you. It comes off the
same stream you were reading, so it cannot say anything different.

## 8 · Be honest about what you cannot do

**You only report while this conversation is alive.** The day is detached on purpose, so it
outlives you — and when it does, nothing reaches out to anybody. A run started by the scheduled
task has no supervisor at all. Say this once when you launch, rather than letting somebody walk
away believing they will be told: what is guaranteed is `report.md` on disk and the day stopping
itself on anything unsound, not a message arriving.
