---
name: audit-session
description: >-
  Audit what an unattended worker session left behind: weigh its handoff against the diff, the PR,
  the board and the PR's own ISA to decide whether the day goes on or stops. Opens cards for the
  work that was named. Triggers: "audit session", "auditar la sesión".
---

# audit-session — the independent reader

A worker session grades itself, and that is the problem. A worker that **knows** it deferred a
decision declares it; one that never noticed it was deciding fills in `decisions_deferred: []`
in complete honesty. You exist for the second case, so one rule orders everything here:

**Never judge from the worker's account. Judge from the diff, the PR, the board and the ISA.**
The handoff says where to look, not what you will find. It only reaches you when there was a PR:
a blocked worker, or one with no tasks, comments its own card and the day ends without you.

The orchestrator passes the handoff path and the verdict path as arguments.

```powershell
$S = "$env:USERPROFILE\.claude\skills\clickup\clickup.py"
python $S task <id>
```

**`python` has to be the first word of the command**: the permission rule matches on the start of
it, and anything before it — an env var, a variable assignment — gets the call denied. Under `-p`
that does not prompt. `PYTHONIOENCODING` is already exported by the orchestrator.

## 0 · You never touch the working tree

Everything you read is reachable without a checkout — `gh pr view`, `gh pr diff`, and
`git show <headRefOid>:<path>` for any file at the tip. **You do not check the PR out, do not add a
worktree, and do not build or test locally.** The two things you write go to GitHub and not to
disk: the comment on the PR and, when there is one, a card.

That is not tidiness, and it is not weaker evidence. The tree you would check out is the one the
next worker starts from, so a session that dies mid-audit leaves it on somebody else's commit: on
2026-08-17 an audit that was refused a worktree fell back to detaching the main checkout, and got
away with it only by finishing. And a build you run here runs on the machine the worker just used,
with its caches and its leftovers, while the PR's CI ran the same four commands on a clean
`windows-latest` under an identity the worker cannot forge. Re-running them buys nothing and costs
the tree.

## 1 · The evidence, not routed through the worker

```powershell
gh pr view <n> --json headRefOid,headRefName,title,body,files,additions,deletions
gh pr diff <n>
python $S task <id>                              # the task and its comments
python $S tasks --space MeetingTranscriber       # the whole board, for check 5
```

`headRefOid` is your `audited_head_sha`, and it comes from the PR — never copy it from the
handoff. If it disagrees with the `head_sha` the worker delivered, the orchestrator stops the day
on its own: somebody pushed on top and your verdict would be about different code.

**Read the ISA at the PR's tip, not from disk.** The worker went back to `main`, so the local
`ISA.md` still shows the claim open and a `hold` drawn from it would be false:

```powershell
git show <headRefOid>:ISA.md
```

## 2 · The five checks

1. **Did it do what the task asked?** The task description against the diff — not against the PR
   body, written by the same one who did the work. What is missing goes to `reasons`.
2. **Are there decisions it did not declare?** The central check. In the diff and the body: a
   `TODO`, a "for now", "left pending", "could be improved", a case handled in a defensible but
   non-obvious way, a default with no reasoning in sight, a signature promising less than the task
   asked. Each one absent from `decisions_deferred` goes to `unreported_decisions`. **This is
   where this skill pays for itself.**
3. **Do the claims really close?** `probes[].passed` is the worker's assertion and does not count
   as evidence. What counts is **the PR's CI**, which runs the four commands on its own. Each
   `isc_closed` names its probe on its `## Verification` line, and the chain that closes it without
   running anything is three links: the test **exists** at the tip, which you read out of the diff;
   its assembly came back green with **`Skipped: 0`**, which the run's log says per assembly; and a
   green assembly with nothing skipped means every test in it ran and passed, that one included.

   Whether it passes *vacuously* is read, not timed. The assertion is in the diff: one that says
   `elapsed.ShouldBeGreaterThanOrEqualTo(StopsWithin)` cannot be green unless the deadline was
   really spent, and one that would hold with the behaviour removed is the finding — reading it
   settles that, and a stopwatch on your machine only corroborates what the assertion already says.

   A claim you cannot corroborate that way goes to `isc_unproved`. It is the property the whole
   rest of the repo rests on.
4. **Is `blocks_the_pr` true?** Recompute it from the diff. The worker classified its own
   decision, and that is exactly the classification you cannot delegate to the audited.
5. **Did it move cards it did not declare?** List the board and compare against `skipped[]`. A
   task in `pending` that is not declared is one quietly got rid of. And for the declared ones,
   open the card: if it was merely hard and needs nobody, put it back to `Open` and record that.

## 3 · CI is your probe, with a clock

```powershell
gh pr checks <n> --watch
gh run view <run-id> --log        # what each step actually printed
```

A queued check that never starts hangs the whole day. If it has not finished in **15 minutes**,
stop waiting: `verdict: "hold"`, saying CI did not conclude. Red is `hold` too, without exception.

Name the run in `reasons`, not just its colour: the four commands, the per-assembly counts, and
the commit. Which commit matters — on a `pull_request` the checkout is the **merge commit**, so
the log reads `HEAD is now at <x> Merge <head> into <base>`. What CI proves is therefore about what
would land, not literally about `audited_head_sha`, and that is the stronger of the two questions.
Say which one your evidence is about rather than letting the morning reader assume.

The log stops at the assembly: counts, skips and durations per test DLL, never a test name. That is
enough for check 3 and it is the whole of what you get. **If a claim needs a probe CI does not run
at all**, you do not run it here to fill the gap — the gap is that the repo has no probe for it,
and a green command on your machine hides that rather than reporting it. It goes to `isc_unproved`,
which is a `hold`, and the durable fix is a card asking for the probe.

## 4 · The verdict

**`hold`** — the day stops. Any one of these is enough: CI red or unfinished; a decision —
declared or not — that resolved the other way would invalidate the diff; any `isc_unproved`; the
diff doing something the task did not ask for inside `Domain/Audio/`, `Domain/Time/` or
`Domain/Jobs/`; **or a step `CLAUDE.md` requires that did not run** — above all the cross-model
review over a diff past 50 non-comment lines, which CI cannot stand in for because CI does not
review anything. A worker that declared the gap honestly still delivered an unreviewed diff, and a
`pass` merges it. Recompute the line count from the diff rather than trusting either account.

**`pass_with_followup`** — the diff holds up and named work is left over.

**`pass`** — none of the above.

It is the only field deciding whether the day goes on. No second field repeats it.

## 5 · What your verdict costs

**A `pass` integrates the PR into `main`.** The orchestrator merges on it — you are not asked to
run the command, but nothing stands between your verdict and the branch everything else is built
on. There is no second reader after you, and the next session starts from what you let through.

That is the whole reason to be slow here. `hold` is cheap: it costs the rest of the day, and the
work survives in an open PR for a person to look at. Letting a bad diff into `main` costs the day
plus everything built on top of it before anybody notices. **When you are unsure, hold.**

The card stays in `in review` either way — closing it is still the user's.

On all three verdicts, comment on the PR with what you reviewed and found; on `hold`, comment on
the card as well, which is what gets read in the morning.

For each followup, a card in the same list as the originating task. **Check first whether one
with that name already exists** — you may be running after an audit that died just after creating
it, and a duplicate dirties the board:

```powershell
python $S create --list "<list>" --name "<name>" --priority normal
python $S link <new-id> --needs <origin-id>
```

The name opens with `BUG - ` only when something is already wrong. The description says what to do
and how you know it is done — not how you found it, which goes as a comment. A decision that
belongs to the user is written as the question to put to them, not as an implementation task.

## 6 · The output

**Your last message is the verdict, and nothing else** — one JSON object, no prose around it. The
orchestrator reads it off what you emitted and writes the file itself; you do not write it. The
shape is in `verdict.schema.json`. `actions_taken` lists what you actually did, with IDs — with
`report.md` it is what the user reads in the morning to learn what happened while they were away.
