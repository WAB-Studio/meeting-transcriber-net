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
$env:PYTHONIOENCODING = "utf-8"
```

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
   as evidence. What counts: **the PR's CI**, which runs the four commands on its own, and
   **re-running yourself** the test holding up each `isc_closed` — it is named on its
   `## Verification` line, and running it costs seconds. A claim you cannot corroborate that way
   goes to `isc_unproved`. It is the property the whole rest of the repo rests on.
4. **Is `blocks_the_pr` true?** Recompute it from the diff. The worker classified its own
   decision, and that is exactly the classification you cannot delegate to the audited.
5. **Did it move cards it did not declare?** List the board and compare against `skipped[]`. A
   task in `pending` that is not declared is one quietly got rid of. And for the declared ones,
   open the card: if it was merely hard and needs nobody, put it back to `Open` and record that.

## 3 · CI, with a clock

```powershell
gh pr checks <n> --watch
```

A queued check that never starts hangs the whole day. If it has not finished in **15 minutes**,
stop waiting: `verdict: "hold"`, saying CI did not conclude. Red is `hold` too, without exception.

## 4 · The verdict

**`hold`** — the day stops. Any one of these is enough: CI red or unfinished; a decision —
declared or not — that resolved the other way would invalidate the diff; any `isc_unproved`; the
diff doing something the task did not ask for inside `Domain/Audio/`, `Domain/Time/` or
`Domain/Jobs/`.

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

JSON at the path you were given, and your last message is that same JSON; the shape is in
`verdict.schema.json`. `actions_taken` lists what you actually did, with IDs — it is the only
thing the user will read in the morning to learn what happened while they were away.
