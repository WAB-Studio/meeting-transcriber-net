---
contract-sha: 5daa595f5833
entries: 16
---

# The audit floor

A pull request touching any path under **The floor** is audited — not because anybody judged it
worth auditing, but because nobody gets to judge. Everything else is a judgement call. This is the
set where being wrong costs a corpus that cannot be recorded again.

## Where it comes from

**The contract** in `CLAUDE.md` is the source: its bullets are the invariants, and every entry down
to `ISA.md` is where one of them lives. The direction is the whole rule — an invariant that section
stops stating comes off this list, and one it starts stating goes on.

The frontmatter is what keeps that honest. `.github/workflows/ci.yml` fingerprints the contract's
words and counts this file's entries, and fails when either moves without this file being edited.

**It fingerprints rather than counts**, because a bullet count is blind to the way invariants have
actually arrived here. The contract has held seven bullets since 2026-08-06, and both invariants it
has gained since grew **inside** an existing bullet: `Speakers.Resolve` into bullet 1 that same
week, and the human layer — who attended, what a meeting was about, an affiliation with a period —
into bullet 5 the week after. A count is green through both, and `Speakers.cs`, `Classification.cs`
and `HumanLayerEntities.cs` are three of the entries below. That is not a hypothetical way for the
floor to go quietly short: it is how every invariant this contract has gained arrived. The
fingerprint collapses whitespace, so rewrapping a paragraph is free and changing a word is not.

Recomputing it is what the step does — the section's lines joined, whitespace collapsed, SHA-256,
first twelve hex — over a byte-faithful read, because `docs/shell.md`'s first trap is a PowerShell
read that changes the answer, and a gate answering differently per shell is the corruption it exists
to stop. What it cannot tell is a re-derivation from a paste. It makes the question unavoidable, not
the answer right.

**Some entries decide checking rather than carry an invariant**, and that is deliberate. This file,
`.claude/agents/auditor.md` and `.claude/skills/run-day/SKILL.md` decide whether a given change is
checked at all. `.claude/agents/planner.md` and `.claude/agents/validator.md` decide what a check is
able to find: an audit judges a diff against the plan it was built from, so a planner told to write
vaguer plans hands every later audit a baseline that can no longer contradict anything, and the
verdict comes back clean. `ISA.md` is here for the separate reason its own entry gives.

The rule is closed on one sentence — *decides whether a change is checked, or what a check can find*
— and the near misses are named below, because a membership rule nobody can see the edge of is the
one that grows by argument. The second half was added the day the work moved to a plan written
before the code: until then nothing but the diff was checked, and there was no baseline to soften.

CI can prove the readers still name this file, which is what it does, but it cannot tell a pointer
kept from a mandate weakened, and "whatever anybody judges" is one adjective away from being a
judgement call again.

Its cost is that editing how the work is run is now audited. That is the rule working rather than a
side effect of it: these are the only places the mandate can be narrowed, so an unwitnessed edit to
one of them is precisely the thing being stopped.

**The unit is the invariant, not the folder.** A folder is named only where every file under it
serves one; everywhere else the file is. A folder is a proxy for the type inside it and goes quiet
the day somebody moves the file out.

**What the CI step proves, and what it does not.** It proves every path here resolves, that the
contract still reads as it did when this file was last derived from it, that the entry count
agrees, that no document deciding audits restates the list, and that each reader still points here.
It does **not** prove an entry still carries its invariant: extract `Turns.Group` into a new file
and the old path goes on resolving. This is a tripwire, not a proof, and reading a diff is still
somebody's job.

## The floor

- `src/MeetingTranscriber.Domain/Audio/` — which channel is the loopback and which the microphone,
  and that a source profile agrees with the audio it describes.
- `src/MeetingTranscriber.Domain/Time/` — instants to the millisecond in UTC, lengths and timeline
  offsets in whole milliseconds.
- `src/MeetingTranscriber.Domain/Jobs/` — where each job state reaches, and what a restart may not
  repeat on its own because the charge may already have happened.
- `src/MeetingTranscriber.Domain/Knowledge/Speakers.cs` — `Speakers.Resolve`, the only thing that
  settles the user from a channel, and only when the microphone caught exactly one speaker.
- `src/MeetingTranscriber.Domain/Knowledge/SpeakerLabels.cs` — `SpeakerLabels.For`, the key
  `speaker_assignments` hangs off. Two speakers sharing a label put somebody's name on another
  person's words.
- `src/MeetingTranscriber.Domain/Knowledge/Turns.cs` — `Turns.Group`, the only thing that decides
  where a turn ends, and a turn is what a citation anchors on.
- `src/MeetingTranscriber.Domain/Meetings/Classification.cs` — the three-level tree, which way a
  meeting's link to a node runs, how somebody is named on one, and that all three name sets are
  closed under a CHECK against the thirteen meetings in `arquitectura.md` §5.3.
- `src/MeetingTranscriber.Domain/Meetings/HumanLayerEntities.cs` — `Affiliation`, where somebody
  belongs and over which period, of which they have as many as they have.
- `src/MeetingTranscriber.Domain/Meetings/HumanLayer.cs` — `SpeakerAssignmentSource`, what settled
  a label onto a person; `Channel` is the one the recording gives for free.
- `src/MeetingTranscriber.Infrastructure/Storage/HumanLayer.cs` — `SettleTheMicrophone`, the one
  row that writes that source. It is the single place the channel-is-never-a-person invariant
  reaches disk, which is why it is here and the rest of its project is not.
- `ISA.md` — what done means, and the only place a claim closes. Here for its own reason rather
  than the contract's: a claim narrowed until the work it scores fits it is not something one
  branch's diff shows, and it is not something a test reaches either.
  `tests/MeetingTranscriber.Isa.Tests` compares one baseline to one head, so it refuses a claim born
  ticked, one reworded into its own closure, a stub left on words a claim no longer has, and a claim
  issued beside the work that closes it — and a narrowing that landed on `main` in a change of its
  own is *inside* that baseline and passes every one of them. Reading the claim's history against
  the work is a person's, and so is whether the evidence under a tick proves the claim at all.
- `.claude/audit-floor.md` — this file. What decides which changes are audited is itself audited,
  or the floor is one unwitnessed edit away from naming less than it used to.
- `.claude/agents/auditor.md` — what an audit reads, and what makes a verdict `hold`. The floor
  overrides judgement only for as long as this says so.
- `.claude/skills/run-day/SKILL.md` — §2, where the floor overrides the day's judgement about which
  PRs are worth a second read. The day is the one being overridden, so it does not get to edit it
  unwatched.
- `.claude/agents/planner.md` — what a plan has to name. The plan is the baseline an audit reads a
  diff against, so what this file stops demanding is what no later audit can find.
- `.claude/agents/validator.md` — what holds a plan back before any code exists. The only gate that
  runs while a wrong decision is still free to undo.

## Read by

- `CLAUDE.md`
- `.claude/agents/auditor.md`
- `.claude/agents/planner.md`
- `.claude/agents/validator.md`
- `.claude/skills/run-day/SKILL.md`

Each must name this file, and CI fails when one stops. A document that decides audits and neither
states the floor nor points here has silently dropped it.

## Deliberately not here

A list nobody can see the end of is one that gets widened by argument, so the edge is drawn here.

- **`src/MeetingTranscriber.Domain/Artifacts/` and the `artifacts.origin` CHECK** — stated under
  *Conventions* in `CLAUDE.md`, not under *The contract*, and the source is the source. Judgement
  still reaches it: the floor overrides a judgement call, it does not replace one.
- **The rest of `src/MeetingTranscriber.Domain/Meetings/` and
  `src/MeetingTranscriber.Domain/Knowledge/`** — a meeting's stage and lifecycle, what a screen
  offers, the projections, what the AI left. The contract states nothing about them.
- **The rest of `src/MeetingTranscriber.Infrastructure/`** — putting the schema on the floor puts
  most storage work on it, and a floor that fires on everything is one people learn to argue past.
  The naming tests spell out every stored name, and `docs/migrations.md` gates a migration.
- **`.claude/skills/isa/SKILL.md`, `.claude/skills/adversarial-review/SKILL.md` and
  `.claude/skills/github/SKILL.md`** — the near misses, and the reason the rule above is a sentence
  rather than a feeling. Each shapes how work is checked; none decides whether a change is checked
  or what a check can find. `ISA.md` is on the floor and the skill that edits it is not, the same
  way `Turns.cs` is and the tests that exercise it are not.
- **`.claude/skills/agents/SKILL.md`** — the closest miss of all, and it is out for that same
  reason. It says how an agent file is written, so it reaches every entry above it at one remove:
  the rule that an agent file carries no procedure is what turned the audit from a list of checks
  into a question. But it decides nothing about any particular change, and putting it on would put
  the next document that governs this one on too. The gate is on the files that name the checks,
  not on the grammar they are written in.
- **Prose outside the documents that decide audits.** The duplication check reads `CLAUDE.md` and
  `.claude/`, not the whole repository, so `docs/reference-behaviour.md` may go on naming a floor
  path in a sentence about history. Restating the floor is the defect; mentioning a path is not.
