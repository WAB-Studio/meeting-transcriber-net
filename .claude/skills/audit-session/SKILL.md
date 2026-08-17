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
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <id>
```

**`python` is the first word of the command and the path goes whole.** `PYTHONIOENCODING` is already
exported by the orchestrator.

**Long prose goes in a file, never on the command line.** `--text` and `--desc` take `@path`.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --text @.scratch/verdict.md
```

## 0 · You never touch the working tree

Everything you read is reachable without a checkout — `gh pr view`, `gh pr diff`, and
`git show <headRefOid>:<path>` for any file at the tip. **You do not check the PR out, do not add a
worktree, and do not build or test locally.** What you produce goes to GitHub and the board: the
comment on the PR, the same body on the card when it is not a `pass`, and any card you open.

**Where you may write is `.scratch/`, and nowhere else.** Outside the repo is refused for being
outside the working directory, and everything under `.claude/` is refused as a sensitive path — the
orchestrator's own log directory included, so do not try to leave anything beside the stream. The
scratch is inside the tree because that is the only writable ground and gitignored at the root so
nothing you leave there reaches a diff or dirties the tree the next preflight reads. Write the
verdict body there and pass it as `@.scratch/verdict.md`.

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
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <id>                              # the task and its comments
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --space MeetingTranscriber       # the whole board, for check 5
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

Four values, and **none of them ends the day.** That is the change to hold on to if you knew this
skill before: a verdict is a fact about one PR, never about the hours left.

**`hold`** — the work does not hold up. CI red or unfinished; any `isc_unproved`; the diff doing
something the task did not ask for inside `Domain/Audio/`, `Domain/Time/` or `Domain/Jobs/`; **or a
step `CLAUDE.md` requires that did not run** — above all the cross-model review over a diff past 50
non-comment lines, which CI cannot stand in for because CI does not review anything. A worker that
declared the gap honestly still delivered an unreviewed diff, and a `pass` merges it. Recompute the
line count from the diff rather than trusting either account.

The PR is left open, the card goes back to the pool carrying your reasons, and the next cycle takes
the next task. A card you send back twice in a day lands in `pending` instead, so write `reasons`
for somebody who has to fix it: what is wrong, where, and what would settle it.

**`ask`** — the diff holds up and one decision in it is not yours or the worker's to make. This is
the verdict for the fork the task itself said a person picks, for scope the user has to agree to
cut, for a product question wearing implementation clothes.

**Nothing waits for an answer.** The decisions you write in `decisions_owed` are put on the card,
the card goes to `pending` tagged `regrill`, and the day takes the next task. The PR stays open and
green until a grill settles what you named and a later session lands it.

That is why `what` is the field to spend care on: it is what somebody reads, cold, when they sit
down to grill this card, and it is all they get from you. Name the decision the way somebody who
has not read the diff would name it, say in `why` what changes depending on the answer, and list
the options you can see. A `what` like "the frame counting" saves nobody anything.

**The bar is that the day cannot go on being right without it.** A decision you can settle by
reading the diff is not one — settle it, record it in `reasons`, and let the day run. Most
undeclared decisions are this: real findings, worth writing down, worth nobody's morning.
`unreported_decisions` is where they belong. **If you cannot say in one sentence what goes wrong
when nobody decides, it is not an `ask`.**

**And only about something the card did not already settle.** Every card carries a `**Grilled.**`
comment where a person settled its product forks before any of this started; read it first. A
decision that is in there and the diff went the other way is not owed to anybody — it is work
contradicting its own spec, which is `hold`.

The line against the other two is drawn on one question and it is not a matter of taste: **would a
different answer change what the code should be?** If no, it is a finding — `pass` and an
`unreported_decisions` entry. If yes and you can tell which answer is right by reading the repo, it
is `hold` and the reason says which. If yes and you cannot, because the answer is a preference about
the product, that is `ask` and nothing else is. `invalidates_diff: true` on a decision nobody
declared is the same fork: it is `hold` when the diff is wrong and `ask` when it is only unchosen.

**A shape nobody could act on is taken as a plain `hold`.** Each entry needs `what`; options are
either none or two or more, because one option is not a decision. And `decisions_owed` on a verdict
that is not `ask` neither merges nor parks — it reads as two answers to one thing and the PR goes
back.

**The cost is a green PR out of `main` until somebody grills that card, so weigh it.** Two of these
in a day is two finished PRs parked, and if that becomes normal the answer is not more `ask` — it
is that the grill is missing things, and somebody should know.

**`pass_with_followup`** — the diff holds up and named work is left over.

**`pass`** — none of the above.

It is the only field deciding what happens to the PR. No second field repeats it.

## 5 · What your verdict costs

**A `pass` integrates the PR into `main`.** The orchestrator merges on it — you are not asked to
run the command, but nothing stands between your verdict and the branch everything else is built
on. There is no second reader after you, and the next session starts from what you let through.

**When you are unsure whether it holds up, hold. When it holds up and you are unsure it was yours
to decide, ask.**

The card stays in `in review` on a verdict that merges — closing it is still the user's. On `hold`
and on `ask` the orchestrator moves it, and you do not.

On all four verdicts, comment on the PR; on anything but `pass`, the same body on the card as well,
which is what gets read in the morning. The comment exists because the verdict does not survive:
`verdict-N.json` is gitignored and per-run, and the script merges a green verdict into `main` with
nobody in the loop, so this is the only durable line on the commit saying who read it.

That makes it a **rendering of the verdict and nothing else**: written after the JSON, from the
JSON, carrying no fact the JSON does not. One line per entry, each section dropped when its array
is empty, and `actions_taken` never appears — that is `report.md`'s job.

```markdown
**pass_with_followup** — `2b652a0`, CI build green in 6m19s (797 passed, 0 skipped).

- <one line per `reasons` entry>

**Undeclared decisions**
- <`what`, one line> — `<found_in>`

**Unproved claims**
- ISC-N — <why it could not be corroborated>

**Owed to a person**
- <`what`> — <`why`>

**Followups**
- <task_id> <name>

Merged by the orchestrator on this verdict. The card stays in `in review`.
```

The last line says what happens next, and it differs by verdict: merged and the card left in
`in review`; held, so the PR stays open and the card goes back to the pool; or parked, so the PR
stays open and the card waits in `pending` for a grill to settle what is above.

No headings of your own, no narrative of how you got there, no table of commands you ran. A reason
that does not fit on one line is two reasons, or it is padding.

For each followup, a card in the same list as the originating task. **Check first whether one
with that name already exists** — you may be running after an audit that died just after creating
it, and a duplicate dirties the board:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" create --list "<list>" --name "<name>" --priority normal
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" link <new-id> --needs <origin-id>
```

The name opens with `BUG - ` only when something is already wrong. The description says what to do
and how you know it is done — not how you found it, which goes as a comment. A decision that
belongs to the user is written as the question to put to them, not as an implementation task.

## 6 · The output

**Your last message is the verdict, and nothing else** — one JSON object, no prose around it. The
orchestrator reads it off what you emitted and writes the file itself; you do not write it. The
shape is in `verdict.schema.json`. `actions_taken` lists what you actually did, with IDs — with
`report.md` it is what the user reads in the morning to learn what happened while they were away.
