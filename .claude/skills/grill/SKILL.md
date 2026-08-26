---
name: grill
description: >-
  Sit with the user and take the product decisions out of a board card, then write it into the
  `Ready` template so somebody can build it without asking anybody. Triggers: "grillá el board",
  "grill", "grillá esta card", "preparar el día".
---

# grill — take the decisions out of the cards

**You run this yourself, with the user in the room.** Never a subagent: every question here is one
only they can answer, and an agent has nobody to ask.

You sit with one card at a time until you cannot name another fork in it. What comes out is the
template in `.claude/skills/github/SKILL.md` filled in, and a card in `Ready`.

## 1 · Which cards

**Named** — `/grill 96 97 98` takes those, in that order, and you start.

**Not named** — read the board and propose a batch first.

```powershell
gh project item-list 1 --owner WAB-Studio --format json --limit 200
gh issue view <n> --json number,title,body,labels,state,comments
```

Say which cards, why those, and in what order. Then wait. Never grill a batch they did not confirm.

Two things go to the front of any batch you propose:

- **A card in `In review` or `In progress` carrying `question`** — finished or half built, held out
  of `main` by one decision. Settling it lands work that already exists, so it comes first of all.
- **A card in `Backlog` carrying `question`** — a session took it and stopped. Its last comment names
  what it could not settle. **Read that before anything else on the card.** Somebody already paid a
  session to find out what was missing; asking around it and missing the same thing again pays twice.

Then the rest of `Backlog`, in the order the user has it.

## 2 · The only question worth their time

> Does the answer change what the person using this app experiences, or only how the code gets
> there?

If it does not show from outside, it is not theirs — say what you would do and move on. Three ways
to count a frame position, record or struct, where a boundary sits: those go to whoever builds it.

**Two tells, both mechanical, and a question failing either is already decided.** Choose, write it
down, ask nothing.

- **It cannot be written without naming something only the code has** — a type, a method, a field,
  an object being disposed. *Where does releasing a device get bounded: the audio client, the
  endpoint, the silent playback?* names three things nobody recording a meeting will ever see.
  Rewrite it as what somebody sees and one of two things happens: it becomes a real question —
  *what does the person who pressed record see when a device never answers?* — or it evaporates,
  and that is the answer.
- **Its two branches look the same from outside.** A fork is two outcomes somebody could tell
  apart. *Stopping gives up after five seconds* against *stopping waits forever* is a fork; *where
  the deadline is enforced* is the same product either way.

What is theirs:

- A behaviour somebody would notice — a device disappearing mid-recording, a meeting nobody named,
  a transcript that arrived half.
- **Scope the card asks to cut.** Cutting is always theirs.
- A fork the card itself parks: "definir con el usuario", "queda a criterio", two options and no
  pick.
- A name a person will read.

## 3 · Whether the card should exist

Ask it first, on every card. If what breaks without it cannot be said in one sentence about
somebody using this app, that is the question to put to the user — about the card, not about how to
build it. A card asking for the wrong thing is not made right by being answered carefully; it gets
rewritten or closed.

## 4 · Reading before asking

A question you could have answered by reading is a question you are charging them for. The card and
its comments, the code it is about, `arquitectura.md`, `ISA.md`, and for anything with a screen,
`docs/design/` and `docs/design.md`.

Then one question at a time, with the options you actually see and what each costs.

## 5 · An answer that will not build

They decide what the app does, not what the framework and the platform will do. An answer can be
right about the product and impossible as given. Say so in the same message as the answer, naming
what refuses it — once it is on the card it reads as settled to everybody downstream.

Taste fires nothing. Then they answer again, and a cost they took anyway goes in that answer's own
line, or the next session raises the same objection.

## 6 · What you leave

**One place: the issue body, rewritten to the template.** Not a comment, not a file beside it. The
body is what the worker reads, and a decision that exists twice is a decision somebody will read the
stale copy of. Rewritten, never appended to — the body is the present.

```powershell
gh issue edit <n> --body-file <scratchpad>/body.md
```

The payload is a file only because `gh` takes one. It goes in the session scratchpad, outside the
tree, and nothing reads it after the call returns.

**`Decisions` is question and answer. Never why.** The alternative that lost, the reasoning that
settled it, the case that made it obvious: nobody acts on any of those. A decision is followed, not
understood.

**And every line stands on its own.** Short is not the same as elliptical. What you write is read a
month later by somebody who does not have the card open — so the line names what is being asked
about, and the answer is a sentence rather than a word. Cutting the subject out is not brevity, it
is a line that has to be researched before it can be obeyed.

Written short and unreadable:

> - **Stop starts nothing** (2026-08-25).

Written short and readable:

> - **Pressing stop starts nothing** (2026-08-25). The meeting is recorded and sits there;
>   transcribing it is a separate press from the meeting itself.

One line longer, and the only one of the two somebody can act on without going to find the card.

**The claims.** A decision saying what has to be true for somebody using the app belongs in the
ISA — which is almost never a new number. **Settling N forks does not open N claims.** Most of what
a grill decides is a claim that already exists saying it loosely, and the move is to sharpen that
one in place and keep its id; a fresh id is what is left when no claim in the file encapsulates the
statement. A sitting that hands the `isa` skill one new claim per fork is not covering the board, it
is inflating the count — and the count is what the whole articulation is scored on.

Open them through the `isa` skill, which owns that check, and never mark one `[x]` — you ran no
probe. Commit `ISA.md` straight to `main`. The orchestrator's own cards get none: a claim is about
the product, a recording or the corpus. A card with no claim says `**Claim:** none`, which is a whole
answer and not a gap.

**The move, last.** `Backlog` → `Ready`, by the two calls in `.claude/skills/github/SKILL.md`, and
`question` comes off in the same pass. Body first, move second, always: the body is what makes the
column mean anything, and a card in `Ready` whose body says nothing promises a definition the next
session cannot read.

**Where it lands in `Ready` is the user's.** Propose a position with the context they lack — a
dependency, a claim already closed — and move it once they give the word.

**A card where they did not settle every fork does not move.** It stays in `Backlog`, keeps
`question`, and its body says what is still open. Half-defined in `Ready` is worse than undefined in
`Backlog`: the picker would hand it to a worker who stops on the same fork.

## 7 · How many

One card is roughly one working session, and some come back unfinished. **Eight or nine cards is a
full day; three is a morning.** Grill them in one sitting — the user already has the board in their
head and the second card costs a fraction of the first.
