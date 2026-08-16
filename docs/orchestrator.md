# The unattended orchestrator

A day of work with nobody in the loop: fresh sessions chained one after another, each taking a
task off the board and leaving it in an open PR that somebody who did not write it has read.

```powershell
.\.claude\orchestrator\run-day.ps1
.\.claude\orchestrator\run-day.ps1 -DryRun          # preflight and what it would launch
```

There is no session count and no dollar ceiling. The day runs until something real ends it —
the board, the usage window, or a verdict — because a number picked in the morning is a guess
about those and stops the run either too early or too late. What is spent is logged all the same;
reporting a cost and capping it are different jobs.

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

`no_tasks` is the ending to expect on a good day, and the usage window is the one to expect on a
long one: running out of it surfaces as `is_error`, which stops the day rather than waiting it
out, because a worker that errored may already have moved a card or opened a PR.

One thing to watch, since nothing caps the run: the audit files followups as `Open`, so they land
back in the pool the worker draws from. What brakes that is your merges — `next-task` skips a task
building on an unmerged PR, so as PRs stack the candidates thin out and the day reaches
`no_tasks`. If a day ever seems to feed itself, that is where to look first.

## One day at a time

`day.lock` holds the run's PID and is released on exit, including when the day is cut short. A
lock whose process no longer exists is discarded on its own. Without it, the 9am scheduled task
and a run you start by hand would share a checkout, a card and a set of files.

## The PR is integrated on a green verdict

`pass` and `pass_with_followup` merge the PR; `hold` leaves it open and stops the day. The
**script** runs the merge, not the audit — the verdict decides and the script acts, the same split
as everywhere else here. It cannot be forgotten, cannot happen twice, and shows up in `day.log`.

That is what makes a chain of sessions work at all. The next preflight fast-forwards local `main`,
so cycle N+1 branches from a base already carrying cycle N. Without it every session would branch
from the same stale `main` and each PR after the first would be written against code it could not
see. A merge that fails — a conflict, a branch protection — stops the day with the PR left open.

**This is the one place the unattended mode departs from a hand-run session**, where `CLAUDE.md`
leaves merging to the user. What replaces the user here is the audit, and it is a weaker
guarantee: it reads the diff, the board and the ISA, and it waits for CI, but it is not a person.
The lever that tunes this is the audit's own bar — a `hold` costs the rest of the day, and a bad
`pass` costs `main` plus whatever gets built on it, so the skill is written to hold when unsure.

Closing the card is still the user's. `in review` is where they pile up, and that is deliberate:
the board is where you see what a day did.

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
