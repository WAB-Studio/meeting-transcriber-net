---
name: grill
description: >-
  Interrogate board cards with the user until no product decision is left inside them, write what
  was decided on the card and tag it `grilled`. Triggers: "grillá el board", "grill", "grillá esta
  card", "preparar el día".
---

# grill — take the decisions out of the cards

You sit with the user and one card at a time, until you cannot name another fork in it. What comes
out is a card somebody can finish without asking anybody, marked `grilled`.

## 1 · The only question worth their time

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

## 2 · Whether the card should exist

Ask it first, on every card. If what breaks without it cannot be said in one sentence about
somebody using this app, that is the question to put to the user — about the card, not about how to
build it. A card asking for the wrong thing is not made right by being answered carefully; it gets
rewritten or closed.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" update <id> --desc @.scratch/desc.md
```

## 3 · Start with the ones that already cost a session

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --space MeetingTranscriber --tag regrill
```

A card tagged `regrill` is one a session took and stopped on, and its latest comment names exactly
what it could not decide. **Read that comment before anything else on the card.** Somebody already
paid a session to find out what was missing; asking around it and missing the same thing again is
paying twice.

The comment opens `**Needs grilling.**` when nothing was built yet, and `**Not merged.**` when there
is an open PR behind it — finished, green, held out of `main` by that one decision. **Those come
first of all**: settling one lands work that already exists.

Then the rest, in the order the picker drains the pool — phase 0, then `3 · Grabador WinUI`,
then the board's own numbering. That order is the `picker` agent's and is written there, not here;
grilling in any other one fills the far end of a pool the day never reaches.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tasks --space MeetingTranscriber --status Open
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" task <id>
```

## 4 · Reading before asking

A question you could have answered by reading is a question you are charging them for. The card and
its comments, the code it is about, `arquitectura.md`, `ISA.md`.

Then one question at a time, with the options you actually see and what each costs.

## 5 · An answer that will not build

They decide what the app does, not what the framework and the platform will do. An answer can be
right about the product and impossible as given. Say so in the same message as the answer, naming
what refuses it — once it is on the card it reads as settled to everybody downstream.

Taste fires nothing. Then they answer again, and a cost they took anyway goes in that answer's own
line, or the next session raises the same objection.

## 6 · What you leave

**Question, answer. Never why.** The alternative that lost, the reasoning that settled it, the case
that made it obvious: nobody acts on any of those. A decision is followed, not understood.

**And every line stands on its own.** Short is not the same as elliptical. What you write is read a
month later, in a file of two hundred of these, by somebody who does not have the card open — so the
question names what is being asked about and the answer is a sentence rather than a word. Cutting
the subject out is not brevity, it is a line that has to be researched before it can be obeyed.

Written short and unreadable:

> - What stop starts → nothing.

Written short and readable:

> - What pressing stop starts → nothing. The meeting is recorded and sits there; transcribing it is
>   a separate press from the meeting itself.

One line longer, and the only one of the two somebody can act on without going to find the card.

Two places, and the decision exists once in each.

**The card, as its one grill comment — edited, never added to.** A second grill comment is the same
decision twice, and whoever reads it has to work out which one is current.

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --text @.scratch/c.md
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" comment <id> --edit <comment-id> --text @.scratch/c.md
```

```markdown
**Grilled.**

- <the question> → <the answer>

Claims: ISC-N, ISC-M.
```

The payload file goes when the call is done: `.scratch/CLAUDE.md` says what may live there.

**`.scratch/grill.md`, under the card's board list.** Same lines, plus the card's id, name and the
date. A card is found by its list and named by its id, because names get rewritten and ids do not.

**The description only when what was decided changed what the card asks for.** Then it is rewritten
and never appended to, and it carries the instruction rather than the record of the decision.

**The claims.** A decision saying what has to be true for somebody using the app belongs in the
ISA — which is almost never a new number. **Settling N forks does not open N claims.** Most of what
a grill decides is a claim that already exists saying it loosely, and the move is to sharpen that
one in place and keep its ID; a fresh ID is what is left when no claim in the file encapsulates the
statement. A sitting that hands the `isa` skill one new claim per fork is not covering the board,
it is inflating the count — and the count is what the whole articulation is scored on.

Open them through the `isa` skill, which owns that check, and never mark one `[x]` — you ran no
probe. Commit `ISA.md` straight to `main`. The orchestrator's own cards get none: a claim is about
the product, a recording or the corpus.

**The tag, last.**

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" tag <id> --add grilled --rm regrill
```

Comment first, tags second, always: the comment is what makes the tag mean anything, and a card
tagged without one says decisions were made that nobody can read. A card where the user did not
settle every fork does not get tagged — half-grilled is worse than ungrilled.

`regrill` comes off in the same call. A card that keeps it goes on being pulled to the front of the
next grill for a decision that has already been made.

A card in `pending` also goes back to `Open`, or nothing will take it:

```powershell
python "$env:USERPROFILE\.claude\skills\clickup\clickup.py" move <id> --status Open
```


## 7 · How many

One card is roughly one working session, and some come back unfinished. **Eight or nine grilled
cards is a full day; three is a morning.** Grill them in one sitting — the user already has the
board in their head and the second card costs a fraction of the first.
