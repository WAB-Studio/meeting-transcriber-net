---
contract-bullets: 7
entries: 12
---

# The audit floor

A pull request touching any path under **The floor** is audited — not because anybody judged it
worth auditing, but because nobody gets to judge. Everything else is a judgement call. This is the
set where being wrong costs a corpus that cannot be recorded again.

## Where it comes from

**The contract** in `CLAUDE.md` is the source: its seven bullets are the invariants, and the first
ten entries below are where each one lives. The direction is the whole rule — an invariant that
section stops stating comes off this list, and one it starts stating goes on.

The frontmatter is what keeps that honest. `.github/workflows/ci.yml` counts the contract's bullets
and this file's entries and fails when either number moves without this file being edited, so
growing the contract cannot leave the floor quietly short — which is the failure this document
exists about, happening one level up.

**The unit is the invariant, not the folder.** A folder is named only where every file under it
serves one; everywhere else the file is. A folder is a proxy for the type inside it and goes quiet
the day somebody moves the file out.

**What the CI step proves, and what it does not.** It proves every path here resolves, that the two
counts agree, that no document deciding audits restates the list, and that each reader still points
here. It does **not** prove an entry still carries its invariant: extract `Turns.Group` into a new
file and the old path goes on resolving. This is a tripwire, not a proof, and reading a diff is
still somebody's job.

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
  branch's diff shows.
- `.claude/audit-floor.md` — this file. What decides which changes are audited is itself audited,
  or the floor is one unwitnessed edit away from naming less than it used to.

## Read by

- `CLAUDE.md`
- `.claude/agents/auditor.md`
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
- **Prose outside the documents that decide audits.** The duplication check reads `CLAUDE.md` and
  `.claude/`, not the whole repository, so `docs/reference-behaviour.md` may go on naming a floor
  path in a sentence about history. Restating the floor is the defect; mentioning a path is not.
