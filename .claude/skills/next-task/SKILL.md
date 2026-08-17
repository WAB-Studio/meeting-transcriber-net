---
name: next-task
description: >-
  Take the next task off the board and carry it to an open PR with nobody in the loop. This is the
  worker session of the unattended orchestrator: it picks, works, proves, opens the PR and hands
  off a structured record. Triggers: "next task", "próxima tarea", "keep going on the board".
---

# next-task — one unattended working session

A run is **one board task carried to an open PR**, and nothing else. `CLAUDE.md` governs the work
itself; what is here is only what changes when nobody is beside you.

The orchestrator passes the handoff path as an argument, and writes that file itself from what you
emit — see §5. Nothing here writes it.

```powershell
$S = "$env:USERPROFILE\.claude\skills\clickup\clickup.py"
python $S tasks --mine
```

**Call the CLI with `python` as the first word of the command, always.** The permission rule that
allows it matches on the start of the command, so `$env:X = "utf-8"; python ...` does not match it
and is denied — and under `-p` a denial does not prompt, it denies, and you carry on as if the
tool did not exist. The orchestrator already exported `PYTHONIOENCODING`, so there is nothing to
set. If a call is refused anyway, that is a missing rule in `.claude/orchestrator/settings.json`:
**say so in `left_out` and stop trying to spell your way around it.**

## 0 · Before touching anything

Clean tree, standing on a current `main`. If it is not, fix nothing: `outcome: "blocked"`, write
the handoff, stop. An unattended session that tidies up what another left half-done is the fastest
way to lose work.

## 1 · Picking

The board's conventions — which list is which, what each state means — are in `arquitectura.md`
§13. What matters here: **`Open` is the pool and `pending` waits on a person.**

**Look at `in progress` first.** A task sitting there belongs to a session that died halfway, and
it is your task, not a new one: pick it back up. Starting another leaves the first abandoned in a
state that already took it out of the pool.

If there is none, walk the phase lists in order and take the first with an eligible task; inside
it, by priority `urgente` → `alta` → `normal` → `baja`.

```powershell
python $S tasks --list "0 · Contratos y caracterización" --status Open
```

Two things that are never confused with each other:

- **No list has an eligible task** → `outcome: "no_tasks"`. A legitimate ending.
- **A list does not resolve** — renamed, moved — → `outcome: "blocked"` naming which. A board that
  changed shape is not an empty board, and confusing the two closes the day in silence exactly
  when there is work.

**Look at the open PRs too** (`gh pr list`). Every session branches from the same `main`, so you
cannot see what the previous one did. If your candidate builds on work sitting in an unmerged PR,
skip it as ineligible and say so: that one waits on a merge, not on a person.

## 2 · What cannot be done alone

**A task is ineligible when finishing it needs something that is not on this side of the CLI.**
Today that is nearly all of phase 2: a real meeting, two sound cards, a device unplugged mid
recording, two hours of drift measured on hardware. So is a decision about the product rather than
the code, which `CLAUDE.md` says to ask about.

The real risk is not stalling: it is that you produce plausible measurements of a meeting that
never happened. **A number that did not come out of a run does not get written** — not in the
code, not in the ISA, not in a board comment.

```powershell
python $S move <id> --status pending
python $S comment <id> --text "Needs <what exactly> — <what a person has to do or bring>."
```

Record it in `skipped[]` and move to the next candidate. The comment says what is missing, not
that you could not do it: whoever reads it tomorrow needs to know what to bring. **Every card you
move goes in the handoff** — the audit re-lists the board and compares, so an undeclared move
surfaces anyway, as a finding against you.

## 3 · Working

Like any task, with `CLAUDE.md` in charge: the claims in `ISA.md` before building, the four
commands each on its own line, `/adversarial-review` over 50 non-comment lines and whatever the
verdict confirms fixed in the same pass. All the unattended mode adds:

```powershell
python $S move <id> --status "in progress"    # on starting, so a crash leaves a trace
```

If you get stuck in a way you cannot resolve: `outcome: "blocked"` with `blocked_reason`, **and
leave the comment on the card yourself** — in the morning that gets read on the board, not in a
log. Do not open a half PR to show progress.

## 4 · Delivering

Branch, commit, `gh pr create`. You do not merge it — the audit's verdict does that, and only if
it passes. Then:

```powershell
python $S move <id> --status "in review"
python $S comment <id> --text "<probes that ran, review verdict, PR>"
```

Return to `main` with a clean tree, and take the branch tip — `git rev-parse <branch>` — for
`head_sha`.

## 5 · The handoff

**Your last message is the handoff, and nothing else.** One JSON object, no prose around it; the
orchestrator reads it off what you emitted and writes the file itself. You do not write that file
— a session that said the whole handoff and forgot to write it once killed a day with the work
done and the PR open, so the step that could be forgotten was removed rather than repeated.

The shape is in `handoff.schema.json`, next to this file. Four fields are the whole point:

- **`decisions_deferred`** — every fork you resolved without anybody confirming it, and every one
  you left open. `[]` is not silence: **it asserts there were none**, and somebody checks that
  against the diff. If you wrote "left pending" anywhere — the PR, a comment, the code — that is
  an entry here.
- **`left_out`** — what the task asked for and you did not deliver. Cutting scope is the user's.
- **`probes`** — what actually ran. It is taken as your claim, not as evidence: the audit
  corroborates it against CI and by re-running whatever holds a claim up. Writing `passed: true`
  over something that never ran saves you nothing and stops the day. **A step `CLAUDE.md` requires
  and you could not run — the cross-model review over a large diff, most of all — is a probe with
  `passed: false` saying it did not run and why.** Substituting something weaker and not saying so
  is the one failure this whole arrangement exists to catch; the audit holds the PR over it.
- **`head_sha`** — the exact commit you delivered. The audit returns the one it read and the
  orchestrator stops if they disagree.

An honest handoff saying `blocked` is worth more than a tidy one saying `pr_opened` over
half-finished work. The second gets caught and stops the whole day.
