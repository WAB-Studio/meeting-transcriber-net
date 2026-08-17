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
$S = "$env:USERPROFILE\.claude\skills\clickup\clickup.py"
python $S update <id> --desc @.scratch/desc.md
```

## 3 · Start with the ones that already cost a session

```powershell
python $S tasks --space MeetingTranscriber --tag regrill
```

A card tagged `regrill` is one a session took and stopped on, and its latest comment names exactly
what it could not decide. **Read that comment before anything else on the card.** Somebody already
paid a session to find out what was missing; asking around it and missing the same thing again is
paying twice.

The comment opens `**Needs grilling.**` when nothing was built yet, and `**Not merged.**` when there
is an open PR behind it — finished, green, held out of `main` by that one decision. **Those come
first of all**: settling one lands work that already exists.

Then the rest, in board order.

```powershell
python $S tasks --space MeetingTranscriber --status Open
python $S task <id>
```

## 4 · Reading before asking

A question you could have answered by reading is a question you are charging them for. The card and
its comments, the code it is about, `arquitectura.md`, `ISA.md`.

Then one question at a time, with the options you actually see and what each costs.

## 5 · What you leave on the card

**The decisions, as a comment.** In a file, because prose with a semicolon in it does not survive a
command line:

```powershell
python $S comment <id> --text @.scratch/grill-<id>.md
```

```markdown
**Grilled.** `<sha>` — `main` as it stood when these were decided.

- **<the fork, one sentence>** → <what was decided>
  <the why, when the why is what settles a case nobody listed>

Claims: ISC-N, ISC-M.
```

What was decided, never how you got there.

**The claims.** A decision saying what has to be true for somebody using the app is an ISC. Open it
through the `isa` skill and never mark one `[x]` — you ran no probe. Commit `ISA.md` straight to
`main`, and take `git rev-parse main` after that commit for the `sha` above.

**The tag, last.**

```powershell
python $S tag <id> --add grilled --rm regrill
```

Comment first, tags second, always: the comment is what makes the tag mean anything, and a card
tagged without one says decisions were made that nobody can read. A card where the user did not
settle every fork does not get tagged — half-grilled is worse than ungrilled.

`regrill` comes off in the same call. A card that keeps it goes on being pulled to the front of the
next grill for a decision that has already been made.

A card in `pending` also goes back to `Open`, or nothing will take it:

```powershell
python $S move <id> --status Open
```

## 6 · How many

One card is roughly one working session, and some come back unfinished. **Eight or nine grilled
cards is a full day; three is a morning.** Grill them in one sitting — the user already has the
board in their head and the second card costs a fraction of the first.
