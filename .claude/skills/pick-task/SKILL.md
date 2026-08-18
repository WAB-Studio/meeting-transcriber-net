---
name: pick-task
description: >-
  Decide which board card the next unattended working session takes, and return only that. This is
  the picker session of the orchestrator: it reads the board and the open PRs, applies the order,
  and emits one card id — or says the day cannot go past what is standing in front of the pool.
  Triggers: "pick the next task", "qué tarea sigue", "which card is next".
---

# pick-task — which card, and nothing else

One question: **which card does the next session take?** You do not read the code it will touch,
you do not plan the work, and you do not start it. What you emit is a card id or a reason there
isn't one.

This is a session of its own because picking and working fail differently. A bad pick is cheap and
found in seconds; a bad session is fifty minutes and a diff. Keeping them apart also means the
judgement below is made cold, by something that has not spent an hour inside one feature and does
not have its ending to defend.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --space MeetingTranscriber --status Open --tag grilled
```

**Every call starts with the word `python` and carries the path whole.** A refused call is a missing
rule in `.claude/orchestrator/settings.json`: say so in `blocked_reason` and stop, rather than
spelling your way around it.

**Every query carries `--space MeetingTranscriber`.** The workspace holds other people's projects,
and the filters that do not name a space — `--mine` above all — answer across all of them. An
`in progress` card from another board then reads as a session of ours that died halfway.

## 1 · The order

Take the first of these that answers:

1. **A card in `in progress` whose work has not landed.** It belongs to a session that died halfway;
   it is not a new task and it is not up for reconsideration. `outcome: "picked"`. **Check the
   premise before you act on it** — §1b — because a session that died *after* pushing leaves a card
   that looks identical and has nothing left to build.
2. **A card whose PR is still open.** `gh pr list --state open` is how you find these — its branches
   and bodies name their cards, and the card carries a `**Not merged.**` comment saying whether the
   audit held it or a grill answered it. Emit the card **and** `pr_number`, so the worker pushes to
   the branch that exists rather than opening a second PR against one task. Check the PR is still
   open before believing an old comment.
3. **The first grilled card in pick order.** Inside a list, `urgente` → `alta` → `normal` →
   `baja`.

### 1b · The card in `in progress` that is already finished

Rule 1 reads `in progress` as a session that died halfway. That is a premise, not a fact, and it is
wrong in the one case that costs the most: a session that pushed, opened its PR and had it merged,
and died before anything moved the card. The board says the work is owed and `main` says it is done.

**One query answers it, and it is cheaper than every alternative** — the whole worker session that
would otherwise be spent proving it:

```powershell
gh pr list --search "<task_id>" --state merged --json number,mergedAt,headRefName
```

- **Nothing merged** → the premise holds, rule 1 applies, pick it.
- **Something merged for that card** → it is finished. Put it in `finished[]` with the PR number and
  the commit that carries it, **do not pick it**, and go on down the order to the next candidate.

`finished[]` is not `skipped[]`. Skipped is work nobody on this side could build, and it goes to
`pending` where a person is owed something. Finished is work already in `main`, and it goes to
`in review` where a person only has to close a card. Putting one in the other's list sends the card
to a queue nobody reads it in.

**Pick order is not the board's own numbering**, and this is the only place it is written:

1. `0 · Contratos y caracterización` — the day loop itself. A card here is the machinery every
   other card is worked by, so a session spent below it is a session that may not happen.
2. `3 · Grabador WinUI` — the recorder screen. Everything under it is engine nobody has seen run;
   the product is not real to anybody until there is a window that records a meeting.
3. Everything else as the board lists it: 1, 2, 4, 5, 6, 7.

The two in front are ahead **because of what they are, not because of what is in them today**: an
empty phase 0 is walked past in one listing, and the order does not change when it empties.

**Walk the lists with `--status Open` and no tag filter.** Every `Open` card in the list comes back
and you sort them yourself, because the ungrilled ones are half of what you are here to look at:
filtering by `--tag grilled` would hand you a clean candidate list with the card standing in front
of it invisible, and §2 — the one judgement this session exists to make — would never come up.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --space MeetingTranscriber --status "in progress"
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --list "0 · Contratos y caracterización" --status Open
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --list "3 · Grabador WinUI" --status Open
```

`tasks` does not print tags, so a card whose tags decide something gets read: `task <id>` says
`tags:` and the description with it. Read the ones you are about to act on, not the whole list.

`Open` is the pool and `pending` waits on a person: a card in `pending` is never eligible, however
good it looks. The lists and what each state means are `arquitectura.md` §13.

**No list has anything eligible** → `outcome: "no_tasks"`. **A list does not resolve** — renamed,
moved — → `outcome: "blocked"` naming which: a board that changed shape is not an empty board, and
confusing the two ends the day in silence exactly when there is work.

## 2 · The ungrilled card in front

Walking the lists you will meet `Open` cards without `grilled`. The tag is what says a person
settled what the card leaves open, so an ungrilled card is not a candidate — it is the grill's
queue, and the grill reads `--status Open`, so it is already in that queue and needs nothing from
you. **You do not send it anywhere.** Moving it to `pending` would take it out of the one pool the
grill reads.

The question is only whether the day may walk past it, and that is the one judgement this session
exists to make:

> **Would the first grilled candidate be built on top of something this card is about to change?**

- **No** → walk past it and go on down the board. Most are this. A card being earlier on the board
  is not by itself a reason: position is how the board is ordered, not a dependency you can point
  at.
- **Yes** → `outcome: "blocked"`, and `blocked_reason` names the card, what about it is unsettled,
  and which grilled card would be built on it. The day ends there. It has to: the alternative is a
  session building a floor on a decision nobody has made, and that work is thrown away twice — once
  when the decision lands, and once by whoever has to work out which parts to keep.

A card tagged `bloqueante` is a person having already answered that question with **yes**. Take
their word for it and do not relitigate it from the card text.

That is the same narrow standard the open-PR rule uses, and it is narrow for the same reason: this
outcome ends a day that may have twelve grilled cards behind it. It should be rare, and when it
fires it should name something a person can settle in one sitting.

## 3 · What nobody could build

**A card is ineligible when finishing it needs something that is not on this side of the CLI** —
a real meeting, two sound cards, a device unplugged mid recording, two hours of drift measured on
hardware. Today that is nearly all of phase 2.

**Put it in `skipped[]` and go on to the next candidate. Do not move it yourself.** The `why` is what
somebody reads tomorrow, so write it as what they have to bring — "needs two sound cards in one
machine", not "could not be done" — because that entry is what reaches the card.

Moving it is the orchestrator's, after your answer is recorded, and the split is not tidiness: a
session that moves a card and then dies has taken it out of the pool with nothing anywhere saying
why, and `pending` is not a place anything looks again. Declared, the worst case is a card still in
the pool, which the next pick meets again.

The real risk here is not stalling. It is a session producing plausible measurements of a meeting
that never happened, so a card that needs a number off real hardware is one nothing on this side
may invent.

## 4 · Building on a PR that has not merged

Every session branches from the same `main`, so a candidate that **actually builds on** work sitting
in an unmerged PR waits on a merge, not on a person. Skip it and take the next one, declaring it in
`skipped[]` — no board move, since nothing about the card changed.

Judge each candidate against every open PR, including the ones parked on a decision, which
accumulate. If every remaining candidate was skipped for that reason the board is not empty, it is
blocked: `outcome: "blocked"` naming the PRs everything is waiting behind.

## 5 · What you emit

**Your last message is the pick, and nothing else.** One JSON object, no prose around it; the
orchestrator reads it off what you emitted and writes the file itself. The shape is in
`pick.schema.json`, next to this file.

```json
{
  "outcome": "picked",
  "task_id": "86ak1ejve",
  "pr_number": null,
  "why": "first grilled card in pick order; phase 0 is empty and the WinUI card ahead of this one is ungrilled, and nothing here builds on it",
  "skipped": [],
  "finished": [],
  "blocked_reason": ""
}
```

`why` is one sentence and it is not decoration: it goes on the day's stream, so an ordering rule
that is picking the wrong thing shows up in the morning report instead of inside a transcript
nobody opens. Say what you took **and what you passed to get to it**.

`pr_number` is said either way — the number, or `null`. Leaving the field out reads as a picker that
found an open PR and did not mention it, and it is refused, because that card gets picked up as
fresh work and a second PR opened against it.

**This session writes nothing to the board.** Not the card you picked — the worker moves that to
`in progress` when it starts — and not the ones you skipped, and not the ones you found finished.
A pick nothing consumes leaves the board exactly as it found it. Where those cards go is decided
here, in the contract, and the orchestrator does the moving once your answer is on disk.
