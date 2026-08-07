# meeting-transcriber-net

Native Windows app that records meetings, transcribes them with Deepgram and turns them into a
local, queryable corpus. No Python, no WSL, no OBS, no FFmpeg, no backend, no remote database.

## What belongs in this file

Everything here is read on every task, so a fact needed once costs something every time and helps
nobody. Three places, and only the last is this file:

- **It explains one file** — a comment in that file, where whoever changes it is already looking.
- **It is needed only while doing one occasional job** — `docs/`, and what stays here is the line
  saying when to open it.
- **A task would go wrong without it** — here, in as few lines as it takes.

How something was diagnosed — what was suspected, what was measured, what turned out not to be the
cause — is none of the three. That is a commit message or a comment on the board.

CI fails over 175 lines. The ceiling is a forcing function, not a target: reaching it means
something here has stopped earning its place, and the fix is to move a section out.

| Where the rest lives | Open it when |
| --- | --- |
| `arquitectura.md` | The design as a whole, in Spanish. Written in the destination, not the present. |
| `docs/layout.md` | Looking for where something lives. |
| `docs/migrations.md` | Adding or editing an EF migration. |
| `docs/packages.md` | Adding, bumping or choosing a package. |
| `docs/corpus.md` | Deciding what gets backed up, what is deletable, what is rebuildable. |
| `docs/reference-behaviour.md` | Grouping turns, and where .NET departs from the Python system. |

## Nothing has shipped yet

There is no installed build and no corpus anybody keeps, so nothing has to carry old data forward:
a migration may drop what it replaces and say so, a column may be renamed for the name it should
have had, and none of it needs a compatibility path, a fallback or a version check. Do what is
right now, not what keeps a hypothetical old corpus readable. The legacy Python corpus is not an
exception — it is read by a tool that never migrates anything and gets deleted whole.

The first install somebody records into is when this section gets deleted, and only migrations
written after that point owe anybody anything.

## Build and test

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

Those four commands are exactly what CI runs on `windows-latest`. Warnings fail the build in CI
only; locally they show up and do not block. `dotnet format` has to pass clean.

WinUI 3, the Windows App SDK and WASAPI do not build or run on WSL, and the repo must not be
cloned under `\\wsl$\`: MSBuild is slow over the crossed filesystem and Hot Reload misses changes.

## Layout

`src/` is the product and `tools/` is run by hand beside it: nothing under `src/` may reference
`tools/` or know the Python system existed, so deleting the importer deletes two folders rather
than untangling the application. The seven-project split in `arquitectura.md` §3 is the
destination — a project appears when there is code for it, dependencies point inwards, and
`MeetingTranscriber.Domain` stays free of Windows references, with tests asserting exactly that.

## How work starts and ends

`ISA.md` says what done means, the board says what to do next, and **a claim closes on a probe
that ran, never on a task moving.**

1. Work starts from a board task, and the claims it closes exist in `ISA.md` before anything is
   built — if they do not, they get written first, each stating what would prove it false.
2. The task names them: `Cierra: ISC-12, ISC-13`. The pointer only goes that way, because ISC IDs
   are stable forever and ClickUp IDs churn.
3. Closing is running the probe, marking `[x]`, adding the `## Verification` stub and recounting
   `progress:`; `IsaStructureTests` fails if the count disagrees.
4. The task then moves to `in review` with the evidence in a comment. Closing it is the user's.

A failed probe asks whether the code or the claim is wrong; the `isa` skill has the rest.

## The contract

`Domain/Audio/`, `Domain/Time/` and `Domain/Jobs/` hold invariants the rest of the system assumes.
Breaking one corrupts meetings already recorded and artifacts already paid for.

- Channel 0 is the loopback, channel 1 is the microphone. The number is the channel index Deepgram
  reports back, not an internal detail, so only `CapturedAudio` turns a channel into a position.
  A channel names a device and never a person: `Speakers.Resolve` settles the user only when the
  microphone caught exactly one speaker, and every other label waits for somebody to say who it is.
- `multichannel` is two channels, `diarize` is one. A profile that disagrees with its audio throws.
- Instants are UTC to the millisecond (`UtcTimestamp`); lengths and timeline offsets are whole
  milliseconds (`Duration`). A bare `DateTime` or `TimeSpan` does not cross into the domain.
- A job moves only through its own methods, and `JobStates` is the table saying where each state
  reaches. The runner starts what `IsDue` says is due, which is never `awaiting_user`: a job that
  a restart found running stops on a person, because a charge that may already have happened is
  not something the app gets to repeat on its own.
- A meeting is not filed under one thing, and neither is a person. It links to nodes of a
  three-level tree and the link carries which way; somebody is named on it as having attended, as
  what it was about, or both; and where they belong is an affiliation with a period, of which they
  have as many as they have. Every one of those names is closed and stored under a CHECK — renaming
  one is a migration — and what they were closed against is the thirteen meetings in
  `arquitectura.md` §5.3, which `ClassificationStoriesTests` stores and finds again.
- A turn, not a provider utterance, is what the `utterances` table stores and what a citation
  anchors on, and `Turns.Group` is the only thing that decides where one ends. What the Python
  system learned about that, and what .NET does differently on purpose, is in
  `docs/reference-behaviour.md`.
- A speaker is stored as the label `SpeakerLabels.For` builds and never as a person's name. A
  provider numbers speakers within a channel, so the channel is part of the label — `ch1:speaker_0`
  — and only a single track has labels without one. It is the key `speaker_assignments` hangs off,
  so two speakers sharing a label would put somebody's name on another person's words.

## Conventions

- Storage is EF Core over SQLite, and the schema lives in the model: a migration is generated by
  diffing it, never hand-edited into shape. That command goes wrong five ways, two of them
  silently: read `docs/migrations.md` before running it, not after.
- Tests are xunit v3 with Shouldly. Versions live in `Directory.Packages.props`, a
  `<PackageReference>` carries no `Version`, and three pins are load-bearing: `docs/packages.md`.
- Every context comes from `CorpusDatabase`. A connection interceptor turns foreign keys on —
  SQLite has them off per connection — and sets WAL and `busy_timeout`.
- Tables and columns are snake_case by a naming pass in `CorpusDbContext`, enum values by a
  convention in `WireNames`. Both are conventions, so a rename changes what is on disk; the
  naming tests spell out every stored name so that shows up as a failure.
- Sources versus rebuildable derivatives is the distinction the whole backup and deletion policy
  hangs off: `docs/corpus.md`, enforced by the `artifacts.origin` CHECK.

## Rules that are not preferences

- Tests never touch the network and never spend Deepgram credits or Claude Code quota. Live tests
  are separate opt-in commands and are not part of `dotnet test`.
- What they read instead is `tests/fixtures/deepgram/`: real responses with every word replaced
  from a closed vocabulary and every timing, confidence and channel number left as sent. A test
  needing a case the set lacks extends the set — never the user's corpus, which no test may depend
  on. `tests/fixtures/deepgram/README.md` says how.
- `deepgram.json` is a paid artifact: never overwritten, and corrections are never written into
  it. Re-transcribing creates a new version and needs explicit cost approval.
- The legacy importer never writes to the Python corpus, is repeatable, and names what it cannot
  place rather than dropping it. It is a tool, not a feature: nothing in `src/` may depend on it.
- The corpus never lives in the MSIX package data folder — uninstalling wipes it, and the corpus
  holds artifacts that cannot be obtained again.
- Claude Code is an optional dependency. Nothing about recording, transcription, rendering,
  search or recovery may depend on it being installed.

## The role

Work as the senior engineer who owns this codebase, not as somebody executing a ticket. A task is
where the work starts, not its outer edge: it was written from what was visible then, and you are
the one reading the code.

So the standard is the decision that leaves the product better, not the one that matches the
wording — the contract that says what it means, the failure that shows up loudly instead of
silently, the design still defensible in a year. Take what is right and say why, in the same
message; if the task is asking for the wrong thing, its description gets rewritten and the right
thing gets built.

That is judgement inside the work, not permission to grow it. Finishing one thing properly and
starting three more are different acts, and the second is still a task on the board.

## What gets fixed and what gets asked

Part of what this repo is for is seeing how far the work runs without a person in the loop, so the
default is to act and asking is the exception.

- **Ask only about a decision that changes the shape of the work** — where human input lives, what
  a contract promises, whether to build for a query nobody has written yet. Ask before doing that
  work, not after: picking one and reporting it as a finding is deciding without asking.
- **A trivial decision made with confidence is not a question.** A name, the shape of a test, one
  of two equivalent spellings: decide, and say so afterwards.
- **Something plainly wrong gets corrected, not raised.** A convention with a hole in it, a path
  the design leans on and no test covers, a name that says one thing while the code does another.
  The question has one answer, so asking it only hands the work back.
- **A fix too big to land inline becomes a task on the board**, linked to the one that surfaced it,
  and the task being worked on says what was left out. Size decides that, not risk appetite.

An unasked fix carries the same proof as any other change: `dotnet format`, the build and the tests
clean over it, in the same pass.
