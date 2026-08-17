# The unattended orchestrator

A day of work with nobody in the loop: fresh sessions chained one after another, each taking a
task off the board and leaving it in an open PR that somebody who did not write it has read.

Ask for it in a sentence — *"trabajá el día y avisame si algo se sale de lo normal"* — and the
`supervise-day` skill launches it, watches it and does the telling. By hand it is still one line:

```powershell
.\.claude\orchestrator\run-day.ps1
.\.claude\orchestrator\run-day.ps1 -DryRun    # preflight and what it would launch, then exits
.\.claude\orchestrator\day-status.ps1         # what it is doing right now
.\.claude\orchestrator\day-status.ps1 -Report # write report.md
```

There is no session count and no dollar ceiling. The day runs until something real ends it —
the board, the usage window, or a verdict — because a number picked in the morning is a guess
about those and stops the run either too early or too late. What is spent is logged all the same;
reporting a cost and capping it are different jobs.

## One stream, several readers

Each run writes to `.claude/orchestrator/log/<date>_<time>/`, ignored by git. The one file that
matters is **`events.jsonl`: one line per transition, and the run's only source of truth.**
Everything else in that folder is derived from it or feeds it:

| File | What it is |
| --- | --- |
| `events.jsonl` | every transition the executor made, append-only |
| `worker-N.stream.jsonl`, `audit-N.stream.jsonl` | what each session emitted, **as it emitted it** |
| `handoff-N.json`, `verdict-N.json` | the contracts, written by the script from what the session said |
| `day.log` | a running commentary for a person scrolling — not authoritative |
| `report.md` | written at close, computed from the stream |

Everything anybody decides anything on is computed from `events.jsonl`, so `day-status.ps1` and
`report.md` cannot disagree. `day.log` is not a render of it and does not try to be: it is prose
written alongside. The consequence is a rule when adding to this — **a fact that reaches only
`day.log` is a fact the morning report cannot carry**, so what matters goes on the stream first and
is echoed to the log second. Anomalies are written down when they fire for the same reason: a
twenty-minute silence stops existing the moment the session ends, and a cycle that cost triple the
median stops looking like one as soon as the next cycle moves the median.

Sessions run with `--output-format stream-json --verbose`, so a session's file grows while it
works. That is what makes a working session distinguishable from a stuck one, and it is why
`day.log` going quiet for fifty minutes is no longer the only thing you can see. It also means the
session's result is one *line* of that file and never the file: parsing the whole thing throws, and
the day would stop on every successful cycle.

## The five pieces

| Piece | What it decides |
| --- | --- |
| `run-day.ps1` | Nothing about the code. When to launch, when to stop, when to merge. |
| `day.psm1` | Nothing at all. The stream, the contracts, and where the thresholds sit. |
| `day-status.ps1` | Nothing. Reads the stream and says which thresholds are crossed. |
| skill `next-task` | Which task, how to do it, what cannot be done alone. |
| skill `audit-session` | Whether what was left holds up, reading the diff rather than the account. |
| skill `supervise-day` | What is worth interrupting a person over. The only piece with judgement. |

The split is the same everywhere here: **the deterministic part is a script and the judgement is a
session.** Picking the moment, adding up dollars and comparing a number to a threshold have no
judgement in them, and a process watching all day would degrade exactly when it carries the most.
Every session starts at zero, and that is why the day does not run out of context.

## The contract is what the session said

A session's last message **is** its handoff or its verdict — one JSON object — and the script
reads it off the session's own output and writes the file itself. It used to be the session's job
to write that file too, and on 2026-08-16 a worker said the whole handoff, did not write it, and
the day stopped on a formality with the work finished and the PR open. A step that can be
forgotten was removed rather than repeated.

## Permission denials are the expensive failure

Under `-p` there is nobody to ask: **a missing permission does not prompt, it denies, and the
session carries on as if the tool did not exist.** The damage is never the denial. It is that the
session then does the same job by a worse route, or drops it, and says nothing about either.

That same day one cycle collected 27 of them, starting with the board CLI, and ended with the
cross-model review this repo requires over a large diff never having run. So:

- the executor prints them and puts them on the event, per program and with the command attempted;
- the reader raises **two refused attempts at the same program to `stop`** — that is not a missing
  tool, that is a model groping for a way around. Grouping is by program rather than by tool
  because that is what a missing rule is about: `$env:X = "utf-8"; python …` and `python …` are one
  rule refused twice, while a `python` and a `codex` refused through the same PowerShell are two;
- **the executor stops the day on it**, after the session and before the audit and the merge, so a
  worker that spent its turns going around a refusal never reaches `main`. The verdict decides
  whether good work merges; this decides whether the session was sound enough to be worth judging;
- `report.md` lists every one, because each row is a permission rule missing from
  `.claude/orchestrator/settings.json`;
- `next-task` records a required step it could not run as a red probe, and `audit-session` **holds
  the PR over it**, because CI cannot stand in for a review.

The extras live in `.claude/orchestrator/settings.json`, passed with `--settings`, so the
project's `deny` (`deepgram.json`, `settings.json`, force-push) still rules. That file also carries
`skillOverrides`, which hides from an unattended session every skill it has no business reaching
for — a worker that cannot see `dataviz` has one less way to spend an hour sideways.

### A pipe is denied whole, not in part

The rule that decides most of these is not per tool, it is per *piece*: a command is decomposed and
every subcommand has to be allowed, so `git diff | grep | head` dies over `grep` and reports as a
denied `git`. On 2026-08-17 that alone was eleven of one cycle's fifteen denials —
`dotnet build 2>&1 | Select-String`, `printenv | grep`, `tasklist | grep`, `Get-ChildItem |
Where-Object` — and the day stopped itself on a worker whose actual work was sound. A twelfth was
`echo "TMPDIR=$TMPDIR"`, refused for containing an expansion at all.

So the allow list carries the *filters* — `grep`, `head`, `tail`, `Select-String`, `Where-Object`
and the rest — and not just the programs whose output they read. They are all read-only, which is
why the list can be broad without being a hole. What it cannot be is complete: a pipe through a
filter nobody listed is denied like any other, so a session that has to grep something new still
loses the cycle. The rule to write against it is to reach for a tool rather than a pipeline —
`Grep` and `Read` are not decomposed and cannot be refused this way.

Two more that look like missing rules and are not: anything under `.claude/**` is refused as a
sensitive path however the allow list reads, which is why a session's scratch belongs outside the
repo and not in the gitignored log directory; and a command whose first word is an assignment or a
variable is refused for the expansion, not for the program.

## What stops the day

The loop stops — it never retries — and both `day.log` and `report.md` name which:

- **Preflight**: dirty tree, off `main`, `main` diverged, or a `git` that did not run. Each command
  is judged by its own exit code: a tool that fails cannot pass for a healthy state.
- **A `claude -p` exiting non-zero, running past 90 minutes, or ending with `is_error`.** An error
  leaves unknown effects — it may have moved a card or opened a PR — and repeating it blind is
  what `CLAUDE.md` forbids for a job that may already have been charged.
- **A handoff or verdict that cannot be read out of the session's output**, or an `outcome` /
  `verdict` outside the contract. Neither is read with good will.
- **`head_sha` different from `audited_head_sha`**: the audit read a different commit than was
  delivered.
- **`verdict: hold`**, or `blocked` / `no_tasks` from the worker.
- **A session that is not sound**: a program refused twice, a closed usage window, a session the
  clock killed. Checked after each session, before the audit and before the merge.
- **The executor throwing.** It writes `day_crashed`, an ending and a report before it goes, so a
  run never sits there looking alive. If it is killed outright it cannot do that, so the reader
  checks the executor's own PID and reports a day whose process is gone as `vanished`.

`no_tasks` is the ending to expect on a good day, and the usage window is the one to expect on a
long one: running out of it surfaces as `is_error`, which stops the day rather than waiting it
out, because a worker that errored may already have moved a card or opened a PR.

One thing to watch, since nothing caps the run: the audit files followups as `Open`, so they land
back in the pool the worker draws from. What brakes that is your merges — `next-task` skips a task
building on an unmerged PR, so as PRs stack the candidates thin out and the day reaches
`no_tasks`. If a day ever seems to feed itself, that is where to look first.

## Do not touch the repo while it runs

Any edit to a tracked file leaves the tree dirty, and the next cycle's preflight stops the day over
it — including the fix you just found. Write it on a card and let the day pick it up, or land it
after the day ends. Editing `run-day.ps1` itself is safe for the running process, which already
read it, but takes effect only on the next launch.

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

## What is proved, and what is only exercised

`test-day.ps1` runs in CI and spends no session: it covers pulling a contract out of what a
session emitted, reading a result off a stream, and what each threshold fires on. What it cannot
cover is a real `claude -p`, a real merge and a real board, and those are exercised by running the
day and reading `report.md` afterwards.

**None of this is in `ISA.md`, on purpose.** That file is the product's claims surface — what has
to be true for somebody recording a meeting — and this is the workshop, not the product. The four
commits that first built the orchestrator did not touch it either.

## Things that break without showing

- **The working directory.** The scheduled task starts in `C:\Windows\System32`; the script does a
  `Set-Location` to the repo before anything else. Without it `claude -p` finds no `CLAUDE.md`, no
  skills and no `.claude/settings.json`, and works as if the project did not exist.
- **`PYTHONIOENCODING=utf-8`.** Without it the ClickUp CLI dies with `UnicodeEncodeError` printing
  accents, because the Windows console hands over cp1252. The script sets it and children inherit
  it, which is also why a session must call `python` as the command's first word: the permission
  rule matches on the start of the command, and an env assignment in front of it gets the call
  denied.
- **stdin.** Sessions are launched with stdin closed. A CLI that reads stdin as well as its prompt
  waits for an EOF that never arrives in the background, and hangs without writing a line.
- **The scripts are ASCII and English.** Windows PowerShell reads a `.ps1` without a BOM as ANSI,
  so an accent or a long dash in a comment is a parse error on the machine this runs on.

## Making it start on its own

```powershell
$a = New-ScheduledTaskAction -Execute "powershell.exe" `
     -Argument '-NoProfile -File "C:\Users\pc\Documents\GitHub\Personal\meeting-transcriber-net\.claude\orchestrator\run-day.ps1"'
Register-ScheduledTask -TaskName "meeting-transcriber-dia" -Action $a `
     -Trigger (New-ScheduledTaskTrigger -Daily -At 9am)
```

Once a day, not a cron every half hour: the loop already paces itself and the lock rejects the
second run rather than splitting the work between them.
