# The unattended orchestrator

A day of work with nobody in the loop: fresh sessions chained one after another, each taking a
task off the board and leaving it in an open PR that somebody who did not write it has read.

```powershell
.\.claude\orchestrator\run-day.ps1                  # 4 sessions
.\.claude\orchestrator\run-day.ps1 -MaxSessions 8 -MaxUsdDay 120
.\.claude\orchestrator\run-day.ps1 -DryRun          # preflight and what it would launch
```

Each run writes to `.claude/orchestrator/log/<date>_<time>/`, ignored by git: `day.log` — the
thing read in the morning — plus each session's `handoff-N.json` and `verdict-N.json`.

## The four pieces

| Piece | What it decides |
| --- | --- |
| `run-day.ps1` | Nothing about the code. When to launch, when to stop, how much to spend. |
| skill `next-task` | Which task, how to do it, what cannot be done alone. |
| skill `audit-session` | Whether what was left holds up, reading the diff rather than the account. |
| `handoff` / `verdict` | The shape the two sessions speak in. |

The orchestrator is deliberately not an AI: picking the moment and adding up dollars has no
judgement in it, and a process watching all day would degrade exactly when it carries the most.
Every session starts at zero, and that is why the day does not run out of context.

The audit is an AI, fresh per session, and exists for one concrete failure: the worker that writes
"this is left pending" in prose and moves on. The handoff has a field for that — where `[]` is an
assertion rather than a silence — but a schema only catches the one who knows it deferred
something. The one who never noticed it was deciding is caught by a reader that looks at the diff,
the board and the ISA.

## What stops the day

The loop stops — it never retries — and `day.log` names which:

- **Preflight**: dirty tree, off `main`, `main` diverged, or a `git` that did not run. Each command
  is judged by its own exit code: a tool that fails cannot pass for a healthy state.
- **A `claude -p` exiting non-zero, running past 90 minutes, or ending with `is_error`.** An error
  leaves unknown effects — it may have moved a card or opened a PR — and repeating it blind is
  what `CLAUDE.md` forbids for a job that may already have been charged.
- **A malformed handoff or verdict**, or an `outcome`/`verdict` outside the contract. Neither is
  read with good will.
- **`head_sha` different from `audited_head_sha`**: the audit read a different commit than was
  delivered.
- **`verdict: hold`**, or `blocked` / `no_tasks` from the worker.
- **The dollar ceiling**, which reserves both sessions of a cycle before starting it.

## One day at a time

`day.lock` holds the run's PID and is released on exit, including when the day is cut short. A
lock whose process no longer exists is discarded on its own. Without it, the 9am scheduled task
and a run you start by hand would share a checkout, a card and a set of files.

## PRs stay open

The session ends at the PR and **merging stays the user's**, exactly as by hand — `CLAUDE.md`,
"How work starts and ends". The audit does not change that: it leaves a comment on the PR with
what it reviewed and found, so the morning merge reads quickly.

The practical consequence: each session branches from the same `main`, so session 3 cannot see
what session 1 did. `next-task` looks at the open PRs and skips a task building on unmerged work,
but that shrinks the problem rather than removing it. Hence a default of 4 rather than 12 — the
right number is the one that fits between two of your merges.

## What no session can do

Some tasks end outside the CLI — a real meeting, two sound cards, two hours of drift on hardware —
today nearly all of the `2 · Spike y motor de audio` list. The risk is not that the worker stalls:
it is that it **produces plausible measurements of a meeting that never happened**. Those move to
`pending` with a comment naming what to bring, and the audit re-lists the board to catch any moved
without being declared.

What each board state means is in `arquitectura.md` §13, because it governs hand-run sessions too.

## Things that break without showing

- **The working directory.** The scheduled task starts in `C:\Windows\System32`; the script does a
  `Set-Location` to the repo before anything else. Without it `claude -p` finds no `CLAUDE.md`, no
  skills and no `.claude/settings.json`, and works as if the project did not exist.
- **`PYTHONIOENCODING=utf-8`.** Without it the ClickUp CLI dies with `UnicodeEncodeError` printing
  accents, because the Windows console hands over cp1252. The script sets it and children inherit
  it; by hand it has to be set.
- **stdin.** Sessions are launched with stdin closed. A CLI that reads stdin as well as its prompt
  waits for an EOF that never arrives in the background, and hangs without writing a line.
- **Permissions.** Under `-p` there is nobody to ask: a missing permission does not prompt, it
  denies, and the session carries on as if that tool did not exist. The extras live in
  `.claude/orchestrator/settings.json`, passed with `--settings`, so the project's `deny`
  (`deepgram.json`, `settings.json`, force-push) still rules.

## Making it start on its own

```powershell
$a = New-ScheduledTaskAction -Execute "powershell.exe" `
     -Argument '-NoProfile -File "C:\Users\pc\Documents\GitHub\Personal\meeting-transcriber-net\.claude\orchestrator\run-day.ps1"'
Register-ScheduledTask -TaskName "meeting-transcriber-dia" -Action $a `
     -Trigger (New-ScheduledTaskTrigger -Daily -At 9am)
```

Once a day, not a cron every half hour: the loop already paces itself and the lock rejects the
second run rather than splitting the work between them.
