---
name: agents
description: >-
  How an agent file and a skill are written in this repo: imperative, unexplained, encapsulated.
  Use when writing or editing anything under `.claude/agents/` or `.claude/skills/`. Triggers:
  "nuevo agente", "escribí un agente", "editá una skill", "new agent", "write a skill",
  "cómo se escribe un agente".
---

# How agents and skills are written

## Write every line imperative

Say what to do. Cut the why. A file read on every run pays for its reasons every time and changes
nothing.

## Do not over-explain

Say what has to be true. Do not say how to arrive at it. No numbered checklists, no reading order,
no list of questions to ask, no procedure for thinking.

Whoever reads this is intelligent. A procedure written into the file is a ceiling on what they
would have done without it.

## Keep only what cannot be guessed, or is always wrong

An id, a path, the commands they may use, a trap that bites silently. And the acts that stay wrong
however well somebody reasons — never merge, never write a claim, never edit your own input.

Cut the rest.

## Encapsulate

An agent knows its own name, what it is given and what it leaves behind. It never names another
agent, never names whoever spawned it, never says what happens to its answer next.

One skill holds the roster and the order. Only that one.

## Two outputs, where something reads after it

**A file, for whatever reads it next.** Prose, at whatever length the work took. Name the headings it
must carry; ration nothing else. A template tight enough to fill in is one that hides what did not
fit, and what is hidden is found out later at full price.

**A structured object, for the orchestrator alone.** Rich enough to route on and to report from.

Content lives in the file. The orchestrator routes on the object and quotes it; it never retells.
Passing one agent's work through a summary is where the work gets lost.

## Whoever knows, writes

An agent that refuses, parks or decides writes the reason where it belongs — on the card, on the PR
— before it returns. The orchestrator moves cards, merges and spawns. It drafts nothing.

## A skill is not an agent

A skill runs inside a turn already underway. It carries **when** to reach for something and the
rules that govern it. Leave out what the caller already has.

An agent that sits in a chain states what it is given and what it leaves behind. One that does not,
does not need to.
