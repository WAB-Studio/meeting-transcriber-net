---
phase: climbing
progress: 73/111
updated: 2026-08-13
---

# ISA — meeting-transcriber-net

What *done* means for this product, written as claims that are true or false and never partly
either. This file is the claims surface and the count; it is not the design document and not the
work queue. A claim closes on evidence recorded in `## Verification`, never on a task moving.

- **Why the product is shaped this way** — `arquitectura.md`. §1 the decisions, §2 the overview,
  §5 the model, §6 the flow, §14 the risks, §16 what is deliberately deferred.
- **What to work on next** — the ClickUp board, `MeetingTranscriber` space. Each feature block
  below names its list.
- **How to write, split and close a claim** — `.claude/skills/isa/SKILL.md`.

If a claim here and the prose there disagree, that is a finding about the product, not a
formatting problem. One of the two is wrong.

## Goal

A person records a meeting on their own Windows machine, gets a transcript they were asked to
pay for exactly once, and afterwards can ask the corpus questions — by hand or through an agent
— with every answer tracing back to a turn somebody actually said. Nothing leaves the machine
that the user did not approve leaving it, and nothing that was paid for can be lost or silently
rewritten.

## Features

### F0 · Cross-cutting
Why: the invariants every other feature rests on. Breaking one corrupts meetings already
recorded and artifacts already paid for, so these hold before anything else is worth building.
Board: —
- [x] ISC-1: Only `CapturedAudio` turns a channel into a position, and channel 0 is the loopback while channel 1 is the microphone.
- [x] ISC-2: A source profile whose channel count disagrees with its audio throws instead of transcribing.
- [x] ISC-3: An instant crosses into the domain as `UtcTimestamp`, UTC to the millisecond.
- [x] ISC-4: A length or timeline offset crosses into the domain as `Duration`, in whole milliseconds.
- [ ] ISC-5: Anti: a bare `DateTime` or `TimeSpan` cannot appear in a public domain signature.
- [x] ISC-6: `MeetingTranscriber.Domain` references no Windows assembly and targets no Windows flavour.
- [x] ISC-7: A job leaves a state only through its own methods, and `JobStates` says where each state reaches.
- [x] ISC-8: The runner picks up only the states it owns, and `awaiting_user` is never one of them.
- [x] ISC-9: A speaker is stored as the label `SpeakerLabels.For` builds, carrying the channel it was heard on.
- [x] ISC-10: `Speakers.Resolve` settles the user only when the microphone caught exactly one speaker.
- [x] ISC-11: `Turns.Group` is the only thing that decides where a turn ends.
- [x] ISC-12: Every classification name is closed and stored under a CHECK.
- [x] ISC-13: The thirteen meetings of `arquitectura.md` §5.3 store and are found again.
- [x] ISC-14: Every stored table and column name is spelled out in a test, so a rename fails the suite.
- [x] ISC-81: Anti: a column named `created_at` cannot be written over a row that already exists.
- [x] ISC-15: Anti: nothing under `tests/` names an HTTP or socket type, so no test can reach the network.
- [x] ISC-82: Anti: a Deepgram fixture cannot be named in the test tree outside the one inventory every suite reads.
- [x] ISC-83: Anti: a test cannot open a corpus on disk except through the one `TemporaryCorpus` the suites share.
- [x] ISC-16: The build is clean under `-warnaserror` and `dotnet format --verify-no-changes` passes.
- [x] ISC-17: A meeting, a turn and a job come back off disk as the types they went in as.
- [x] ISC-105: A corpus says which folder it is, and it is the folder its database sits in.
- [x] ISC-106: Anti: nothing that writes a corpus can be handed one corpus's database and another one's folder.
- [x] ISC-107: Anti: closing a corpus cannot drop a connection another corpus has pooled.

### F1 · Contracts and characterisation
Why: what the Python system learned is specified in .NET before any of it is rebuilt, so the
knowledge survives without a runtime dependency on Python.
Board: 0 · Contratos y caracterización
- [x] ISC-18: Every response in `tests/fixtures/deepgram/` parses into the turns it describes.
- [x] ISC-19: A fixture carries the provider's real timings, confidences and channel numbers, with every word replaced from a closed vocabulary.
- [x] ISC-20: Where .NET departs from the Python system on purpose is written down and each departure is tested.
- [x] ISC-21: Anti: the importer never writes to the Python corpus — it comes out exactly as it went in.
- [x] ISC-22: Importing the same corpus twice imports it once, and a renamed folder is still the same meeting.
- [x] ISC-23: Anti: what the import cannot place is named in the report, never dropped, and never mixed with what was skipped on purpose.
- [x] ISC-79: An imported meeting arrives with its derivatives produced here, and importing it again re-renders them rather than duplicating them.
- [x] ISC-80: Anti: a speaker the old corpus resolved arrives under a label a rendered turn actually carries.
- [x] ISC-73: Every imported extraction arrives with the run it came out of, carrying what its file knows.
- [x] ISC-74: A decision, an action and the state a person gives it hang off an imported run.

### F2 · Deterministic core from artifacts
Why: given a `deepgram.json`, everything that does not need a microphone works — parse, store,
project, rebuild — so the paid artifact is the only input the rest of the system needs.
Board: 1 · Núcleo .NET desde artefactos
- [x] ISC-24: A Deepgram response becomes turns with their channel, speaker label and offsets.
- [x] ISC-25: Rebuilding the projections deletes only derivatives and leaves every edit a person made.
- [x] ISC-26: Anti: a claim cannot cite a turn the meeting never had, and deleting a cited turn fails rather than taking the claim along.
- [x] ISC-27: A source that cannot be produced again is never written over; a derivative is replaced and stays one row.
- [x] ISC-28: A write cut off part-way leaves either nothing or the finished artifact, never a half one.
- [x] ISC-29: What is recorded for an artifact is the hash of the bytes that were actually written.
- [x] ISC-30: Anti: a meeting cannot write into another meeting's folder.
- [x] ISC-31: A corpus that is not sound fails and names the table or index that broke.
- [x] ISC-32: Compacting leaves search answering exactly what it answered before.
- [x] ISC-33: `transcript.md` and `utterances.jsonl` render from the stored turns and re-render identically.
- [x] ISC-75: Anti: a name or a correction reaches the rendered files and never the stored turn.
- [x] ISC-76: A merged turn's confidence is the mean of its parts, weighted by their length.
- [x] ISC-77: Rebuilding the whole corpus from its sources produces the same projections and the same derived files.
- [x] ISC-78: Anti: a rebuild that moved a turn's position fails rather than rewriting what every stored claim points at.
- [x] ISC-34: The diagnostic CLI reports corpus state without opening the application.
- [x] ISC-84: A paid response on disk becomes a meeting with its turns and its derived files, through the command line alone.
- [x] ISC-85: Anti: the same response cannot become two meetings — filing it again re-renders the one it already is.
- [x] ISC-86: A meeting's folder carries a `manifest.json`, recorded as a source artifact, from the moment the meeting is filed.
- [x] ISC-87: A meeting is recognised from its `manifest.json` alone — its id, when it started, its profile, its language and its title — with nothing else read.
- [x] ISC-88: Anti: a second filing cannot leave the recovery card stale or missing — it writes the card as the corpus now says it is.
- [x] ISC-98: A rebuild leaves every meeting in the corpus holding the card the corpus now describes, including one that never had a card at all.
- [x] ISC-100: Changing a meeting's title leaves its recovery card saying the new title, with no other command run.
- [x] ISC-101: Anti: a rename whose card cannot be written does not change the title either.
- [x] ISC-99: Anti: a write cannot put one kind of artifact over the path another kind already holds.
- [x] ISC-102: A source the corpus records and the disk has lost comes back from bytes that hash to what its row already says, leaving the check clean.
- [x] ISC-103: Anti: bytes no row of this corpus describes never reach an artifact's path.
- [x] ISC-104: Filing a response again when its file is gone puts the paid bytes back before anything is rendered from them.
- [x] ISC-66: Every table of the human layer is written through `HumanLayer`.
- [x] ISC-67: Exactly one person carries the flag naming the user of this install.
- [x] ISC-68: Anti: a speaker label a person resolved is never overwritten by one the recording settled.

### F3 · Audio engine
Why: two sources become one timeline a person can trust. This is the largest technical risk in
the product and it is settled before any UI is built on top of it.
Board: 2 · Spike y motor de audio
- [x] ISC-108: The selected microphone and the full system loopback are captured over the same stretch of time, each into its own stream.
- [x] ISC-109: A capture names each source's device and the format that device handed it.
- [x] ISC-110: A source's level is measured from the samples that arrived, so a source that heard nothing reads as silent.
- [x] ISC-111: Anti: a capture that cannot open both of its sources stops, rather than recording one of them.
- [ ] ISC-35: Two hours of capture end with under 50 ms of measured drift between the two channels.
- [ ] ISC-36: Anti: the produced WAV never carries the microphone on channel 0.
- [ ] ISC-37: A spool cut off mid-block recovers to its last complete block.
- [ ] ISC-38: Finishing the same spool twice produces the same WAV.
- [ ] ISC-39: Capture falls back to full loopback when the target process cannot be followed.
- [ ] ISC-40: A device change mid-recording does not end the recording.

### F4 · WinUI recorder
Why: the application replaces OBS. Recording, pausing, stopping and recovering happen in one
native app with no Python, no WSL and no FFmpeg anywhere behind it.
Board: 3 · Grabador WinUI
- [ ] ISC-41: A recording survives the process being killed and is offered back on the next start.
- [ ] ISC-42: A silent source is shown as silent while the meeting is still running.
- [ ] ISC-43: Record, pause and stop produce one continuous timeline.
- [ ] ISC-44: The queue shows each job's state and what it is waiting for.
- [ ] ISC-45: A mono or stereo file imported from disk becomes a meeting.

### F5 · Deepgram BYOK
Why: a recording becomes a transcript on the user's own key, and the user is charged exactly
once for exactly what they approved.
Board: 4 · Deepgram BYOK
- [ ] ISC-46: The Deepgram key is stored in Windows Credential Manager and read from nowhere else.
- [ ] ISC-47: Anti: no Deepgram call happens without an explicit approval carrying the estimated cost.
- [ ] ISC-48: Anti: a confirmed `deepgram.json` is never overwritten — re-transcribing creates a new version.
- [ ] ISC-49: A job whose outcome is uncertain stops on a person and is never retried by the runner.
- [ ] ISC-50: The live integration returns the structure the fixtures describe.

### F6 · Summaries
Why: a meeting becomes a summary whose every claim resolves to something said, using the user's
own Claude Code credits — and the product stays whole when Claude Code is not installed.
Board: 5 · Summaries
- [ ] ISC-51: Anti: recording, transcription, rendering, search and recovery all work with Claude Code absent.
- [ ] ISC-52: Every claim in a summary cites a turn that exists in that meeting.
- [ ] ISC-53: A summary that fails validation is stored as a failed run, not as a summary.
- [x] ISC-54: A second extraction leaves the first one's state alone and starts its own blank.
- [ ] ISC-55: The provider adapter is exercised against a fake CLI, offline.

### F7 · Local knowledge
Why: people and agents query the corpus with no server, no network and no cloud, and every
answer traces back to a turn. What an answer says still stands is maintained as meetings arrive
rather than re-derived at every question, and the corpus answers the same way whether or not a
run has been over it.
Board: 6 · Conocimiento local
- [x] ISC-56: Search is the index answering, not the table.
- [ ] ISC-57: Everything search promises to find is indexed.
- [x] ISC-69: A hit carries the meeting, its date, its title, an elided snippet and where on the timeline it was said.
- [x] ISC-70: Anti: a meeting on its way out is never something search offers.
- [x] ISC-71: Throwing both search indexes away and rebuilding them leaves search answering exactly what it answered before.
- [x] ISC-72: Anti: a query the index cannot parse is refused naming the query, never as a database error.
- [x] ISC-58: An edited classification or speaker assignment survives a full rebuild.
- [ ] ISC-59: The MCP server answers read-only over stdio and never writes.
- [ ] ISC-60: Anti: an MCP response is bounded and the request is recorded locally.
- [ ] ISC-89: What a meeting recorded is never rewritten by a later one — what changed is recorded beside it and both stay readable.
- [ ] ISC-90: Two people asking the same corpus what still stands get the same answer, whoever is reading and whatever they read first.
- [ ] ISC-91: What still stands comes back at the same cost with three hundred meetings behind it as with ten.
- [ ] ISC-92: A decision comes back with when it was settled and what has happened around it since, so "nothing contradicted it" is never read as "somebody confirmed it".
- [ ] ISC-93: Anything saying a decision no longer stands cites the turn where that was said, the way a decision cites the turn it came from.
- [ ] ISC-94: Anti: two decisions that contradict each other with nothing settling it come back as a conflict, and neither is hidden for being the older one.
- [ ] ISC-95: Anti: nothing is hidden for want of a pass having run over it — a decision stands until something says otherwise.
- [ ] ISC-96: A person's word on whether a decision stands outranks whatever the machine concluded, and survives a rebuild.
- [ ] ISC-97: Deciding what an arriving meeting changed reads a bounded part of the corpus, and what bounds it does not grow as meetings accumulate.

### F8 · Distribution and backup
Why: the application installs, upgrades and comes back from a lost disk, because the corpus
holds artifacts that cannot be obtained again.
Board: 7 · Distribución y backup
- [ ] ISC-61: Anti: the corpus never lives in the MSIX package data folder.
- [ ] ISC-62: A snapshot restores to an alternate directory and passes the integrity check.
- [ ] ISC-63: An upgrade over an installed build leaves the corpus intact.
- [ ] ISC-64: The CLI and the MCP server are reachable by app execution alias.
- [ ] ISC-65: The corpus location is configurable and validated at startup.

## Not yet specified

- **Whether a property-based testing library enters the solution.** The format spec asks for a
  probe stronger than an example on core surface, and every probe here is example-based.
  `Turns.Group` and the timeline arithmetic are the two places it would pay. The question is
  statable; the probe is not, because no library is chosen — naming one now would be inventing a
  row rather than writing one. Decide when F3 starts, since drift is where examples run out.
- **What the 50 ms in ISC-35 is measured against.** A synthetic packet suite is on the board,
  but the reference signal, the sample rate and where the measurement is taken are not settled,
  and those three decide whether the number means anything.
- **How a summary citation anchors.** ISC-52 says a citation resolves to a turn; whether it
  stores the turn id or an offset into the transcript changes what happens when turns are
  regrouped. `docs/reference-behaviour.md` has the grouping rules but not this.
- **What bounds an MCP response in ISC-60.** Rows, bytes or tokens — and the answer depends on
  what an agent actually asks for, which nobody has measured yet.
- **How the corpus decides that a decision stopped standing.** ISC-89 to ISC-97 say what has to be
  true of the answer; none of them says what produces it, and four shapes are on the table with no
  evidence between them. A pass over each arriving meeting, linking a new decision to the standing
  one it replaces — the most precise, and wrong in the direction that hides something somebody
  decided. The same question asked at read time over the decisions of one node, which stores nothing
  and answers differently on two days. A person asked at the end of a meeting which standing
  decisions this one touched — the most reliable, and a chore that gets skipped. Or nothing inferred
  at all: every decision comes back with its date and what has happened since, and the person
  judges, which is ISC-92 on its own and can hide nothing. What decides between them is a corpus
  with enough meetings on one node to measure, and that does not exist yet — the extraction that
  fills `decisions` outside the importer is F6 and unbuilt. Two things would have to be measured
  before choosing: how close two statements have to be before one is offered as replacing the other,
  and how much of one node actually fits in a context, which is the number the read-time shape lives
  or dies on.

## Decisions

- **2026-08-13** — A corpus lets go of its own pooled connections and nobody else's, against the
  other candidate of taking test connections out of the pool entirely. Pooling off would have made
  the suites stop exercising the connection settings the design leans on, and it needs the way a
  corpus is opened to grow a flag that exists for tests; clearing one corpus's pools needs no flag,
  because a pool is found by connection string and the two strings a corpus can be opened under are
  already built in one place. That is also why the clearing lives next to them rather than at the
  test helper: spelled out a second time, it would still be clearing the right pool right up until
  something is added to the first spelling.

- **2026-08-13** — refined: copying the sources in is what importing is, against a first pass where
  `--copy` was an option. `/adversarial-review` found all three reviewers on the same thing, and
  they were right: without it the importer writes rows whose `relative_path` is read against the
  corpus holding them and points into the Python corpus instead, so every source of an imported
  meeting resolves to a file that is not there — nothing renders, the check calls them all missing,
  and the copy the corpus is for is still one folder away from being deleted by hand. The option
  saved disk and cost the corpus being a corpus. Two other things fell out of making it mandatory,
  and both were live: a meeting whose transaction the corpus refused still reached the file writer,
  because the gate read whether a `Meeting` had been built rather than whether the save committed,
  and it threw the card writer's "there is no meeting" over the run; and the reflection probe never
  saw `tools/`, which is where the mismatch actually was, so the rule now lives in
  `MeetingTranscriber.Testing` and both suites assert it over what each can reach. What the
  reviewers asked for and did not get is an immutable corpus handle: `CorpusDbContext.Root` refuses
  after a `SetConnectionString` instead, which closes the same hole without a second type standing
  between every caller and the corpus.
- **2026-08-13** — A corpus is opened by naming its folder, and the context carries it: the folder
  and the database stop being two arguments a caller has to keep pointing at the same place. The
  choice was between that and a pair checked where it is built, and the pair loses on the same
  ground the check would stand on — the folder *is* the database's folder, so it is one fact said
  twice, and the check only exists because the second saying can be wrong. What it costs is that
  every write signature changed at once, which is the whole point: `DirectoryInfo` and
  `CorpusDbContext` no longer meet in any parameter list, so ISC-106 is a compile-time property and
  its probe is a reflection sweep rather than a throw somebody has to remember to write. The one
  live instance was the importer, which took `--database` and `--copy` as unrelated strings and had
  two green tests copying into a folder no row pointed at; it takes `--corpus` now, and `--copy` is
  a switch. `CorpusDbContext.Root` is read off the open connection rather than remembered from a
  constructor, because that is the answer nothing can disagree with, and a context open on anything
  that is not an absolute file path is refused rather than answered with a folder that depends on
  the working directory.

- **2026-08-13** — What authorises putting a lost source back is the hash the row already carries,
  and nothing else. The alternative shape was to name the meeting and the path on the command line
  and check the bytes against the row found that way, which is the same rule with a second address
  bolted on — and the second address is what makes it weaker: two things that can disagree, where
  one of them is typed. Taking the hash as both the authority and the address means the bytes reach
  a path only where the corpus already says those exact bytes are, so a different file under a paid
  response's row is impossible rather than guarded against, and a loose `deepgram.json` in a backup
  folder needs nobody to work out which meeting it was. What it costs is the case where somebody has
  *a* response for a meeting and not *the* one: they get told nothing in this corpus is those bytes,
  which is correct — that file is a re-transcription and has to be paid for and filed as its own
  version, never dropped over the row that describes another. It writes only where there is no file,
  so a file that is there and is wrong is named and left: `check` says which of the two it is, and
  destroying what might be the only copy of something stays a person's deliberate act.
- **2026-08-13** — refined: nothing in the restore takes an artifact row from its caller, against a
  first pass where the intake path handed over the row it was already holding and the method
  compared the offered bytes against that row's hash. That guard was worth nothing: both sides of
  the comparison came from the caller, and `StagedArtifact.Commit` writes the hash of what it wrote
  onto whatever row it finds by meeting and path — so a fabricated hash with bytes to match would
  have landed under a real paid path and left the corpus saying it had always been those bytes. It
  is the same lesson `DurableArtifact` already carries one field over, where the *kind* is read off
  the corpus rather than believed from the caller, and it was missed one layer up. The row is now
  looked up from the bytes in every case, which also made the two paths one: filing a response
  again restores every row those bytes belong under, where before it restored the one arbitrary row
  its own `FirstOrDefault` had found. Found by `/adversarial-review`; the architect reviewer, rated
  high, and the skeptic and architect independently on the `FirstOrDefault`. which settles the question the card's own
  decision below left open. Only `Describe` uses it: the title is the one of the card's five fields
  a person moves after the meeting was filed, so it is the one human edit the folder also carries.
  The alternative was a layer above holding both the database and the folder and calling `Describe`
  as its database half, and it was refused for what it leaves possible — `Describe` stays public and
  callable across projects, so the rename that skips the card is still one call away, which is the
  bug rather than the fix. The root arrives in the constructor and not in the method for the same
  reason: a human layer that cannot keep the card current should not be constructible. What it costs
  is that a type about rows now holds a directory, and that is the honest shape — a corpus is a
  database and a folder, and this is the layer a person writes into.
- **2026-08-13** — refined: the rename and its card are one transaction, against a first pass that
  saved the row and then wrote the card outside one. The reasoning that pass rested on was wrong:
  it took the choice to be between a stale card and a card whose bytes are ahead of the row it is
  recorded against, when the second of those happens either way — the artifact row is saved by the
  same statement in both, so the only thing the transaction changes is whether a card that could
  not be written takes the rename back with it. Unwrapped, a full disk left the title changed and
  the folder saying the old one, which is the bug in a narrower window and with nobody told which
  half they got. What no transaction can cover is the replace itself, and the direction that
  window falls in was already chosen — a file ahead of the corpus, never a corpus ahead of the
  file. Found by `/adversarial-review`; three reviewers, unanimous.
- **2026-08-13** — Who gets to say that one decision superseded another was already settled, on the
  board, before this session proposed the opposite: a person declares it, a model may only offer it,
  and a later date is never evidence on its own. The entry below saying the run's links count was
  written without reading that, and it does not stand. The claims are unaffected, which is the point
  of having rewritten them to properties — ISC-96 says a person's word outranks the machine either
  way. What the board's call costs is that nothing gets marked superseded until somebody looks, and
  ISC-95 is what keeps that from hiding anything: until then both stand.
- **2026-08-13** — refined: ISC-89 to ISC-97 were written to a mechanism and are rewritten to the
  property. The first wording named links, a reconciliation run and a scope of nodes — a design
  chosen in one conversation, before anything outside the importer fills `decisions`, written into
  the file whose job is to say what must be true and not what was built. The properties outlive
  whatever produces them: what was recorded stays readable, the same question gets the same answer,
  the answer says how old it is, a change cites a turn, a conflict is not settled by a date, a pass
  that never ran hides nothing, a person outranks the machine, and neither the read nor the pass
  grows with the corpus. Every one of those survives all four candidates, which is the test the
  first wording failed. The mechanism moves to `## Not yet specified`, where it is one candidate and
  not the answer. One argument used to pick it was overstated and does not survive: that the
  decisions of one node would not fit in a context at three hundred meetings. Scoped to a node they
  probably do, and what is left standing for maintaining the state is determinism and citability —
  the same question answered the same way, and a change that points at a turn — not size.
- **2026-08-13** — Whether a decision still stands is maintained when a meeting arrives, never
  recomputed when somebody asks. Handing an agent every decision in the corpus and letting it work
  out what survived was the shape to beat, and it loses three ways: at three hundred meetings it
  does not fit in a context, it answers the same question differently on two days because the
  ordering is re-derived each time, and it has nothing to cite — the evidence that a decision
  survived is that nothing contradicted it, and an absence has no turn. Reconciling on arrival costs
  one bounded pass per meeting instead, because the only candidates are what stands and the only new
  evidence is the meeting that just landed. A link is pairwise and one of three kinds: `replaces`
  ends standing, `refines` keeps it and adds to it, `contradicts` marks a conflict and hides neither
  side. So a thread is a chain of links rather than a clustering pass over the whole corpus, which
  is the part that neither scales nor can be trusted. `decisions` stays derived and untouched — it
  is the evidence of what was said, and `corpus-db.md` §2 is what a half-derived table costs. The
  run proposes and its links count, since a corpus that will not say anything until somebody
  confirms it is one nobody keeps up with; a person's word outranks them and survives a rebuild,
  which is the `action_items` and `action_item_progress` split again. Two things it may not do on
  its own: settle a contradiction by date, and hide anything for want of having run. It is its own
  job after the extraction rather than a wider extraction prompt — `arquitectura.md` §7.2 gives each
  meeting a clean process and §7.3 refuses an extraction that mixes another meeting's content, and a
  second job asking one narrow question is the more reliable prompt besides.
- **2026-08-13** — The recovery card carries five fields and no more: the meeting's id, when it
  started, its profile, its language and its title. Those are what says *which meeting this folder
  is*, which is the one question the folder cannot answer for itself — the files in it are named
  after what they hold, so the card listing them would only repeat the directory. Everything else a
  person typed is the backup's job and not the card's, which is what `arquitectura.md` §4.1 means
  by a card that does not stand in for SQLite; the line has to fall somewhere and identity is a
  line that holds, where "the human layer that happens to be short" is not.
- **2026-08-13** — The card lives in `Infrastructure/Artifacts` beside the write it is made of, not
  in `Processing/Intake` where the first caller was. Nothing in it belongs to processing a response
  — it reads a meetings row and writes a file — and the place it has to stay reachable from is
  every layer that changes one of the five fields, which includes `HumanLayer` renaming a meeting.
  A card only ingestion could write is a card that goes stale the first time somebody uses the
  product, with the layer that made it stale unable to say so. `HumanLayer` still does not write it
  — it holds no corpus root, and giving it one is a decision about what that type is for — so the
  refresh a rename needs is `rebuild` today and a task on the board.
- **2026-08-13** — A rebuild writes every meeting's card, which is what makes the promise hold of a
  folder rather than of a moment. Intake only ever reaches the meeting being filed, so without this
  every meeting that existed before the card did would keep having none, and no command would ever
  give it one. It also writes the card for a meeting whose response is missing and cannot be
  rendered, which is deliberate: that is precisely the meeting worth being able to recognise.
- **2026-08-13** — refined: ISC-27 said *a source artifact is never written over*, and that turned
  out to forbid the one thing the recovery card is for. The rule worth keeping is narrower — an
  artifact that **cannot be produced again** is never written over — and the manifest is the single
  source that fails that test, since the corpus regenerates it from the meetings row every time. So
  `Artifacts.MayBeReplaced` now answers the overwrite question directly instead of `IsRebuildable`
  inferring it from the origin, and the two questions the origin used to answer at once — *is this
  in the backup* and *would a second write destroy something* — are separate. A card that may not
  be corrected is not a safer card: it is one that keeps whatever it said the first time, and is
  missing forever if the write that would have created it never happened.
- **2026-08-07** — The command line has no way of its own to do anything. Every command is a call
  into the service the application would have called, and the one thing that had no service was
  filing a response that has no meeting yet: `MeetingIntake` is that, in `Processing`. It is not
  written wide enough to take the response a transcription job brings back — that one belongs to a
  meeting that already exists and to the run that paid for it, and a parameter added now for a
  caller nobody has written would be guessing at what that caller needs. What the two will share is
  `MeetingRenderer`. The response identifies the meeting, so filing the same bytes twice re-renders
  the meeting it already is, whatever the file was called.
- **2026-08-07** — The response is committed the moment it is on disk, and the derivatives are
  rendered outside that transaction. One transaction over both was the first shape and it is the
  wrong one: `DurableArtifact` puts the file in place before the row, so a render that failed would
  have rolled the response's row back and left the paid file behind as an unrecorded thing the
  reconciler may never adopt — and the retry, finding no row with that hash, would have filed a
  second copy under a second meeting. Filing first means a failed render leaves a meeting whose
  source is in the corpus and whose derivatives are not, which running the same command again puts
  right. The three reads of the response — parse, hash, copy — are one handle no writer may replace
  underneath, so the length, the identity and the stored bytes cannot come from three versions.
- **2026-08-07** — `import` asks for the source profile instead of reading it off the response's
  channel count, and pays a flag for it. Inferring would mean the profile can never disagree with
  the audio, which sounds like one less way to be wrong and is the opposite: a recording made on
  two channels that came back as one track was billed as something other than what was sent, and
  that is exactly what the contract's refusal exists to say out loud. What is inferred instead is
  the meeting's length, which the response does know.
- **2026-08-07** — Only `migrate` brings a corpus into existence; every other command refuses a
  directory with no `corpus.db` in it. A path typed wrong is a far commoner cause than a corpus
  somebody meant to start, and answering a typo with an empty corpus reads as success. `status` is
  the one command that opens a corpus this build's schema has moved past, because "why does
  everything else refuse" is a question about the corpus's state.
- **2026-08-07** — `tests/MeetingTranscriber.Testing` holds what a test opens — the temporary
  corpus, the raw-SQL helpers, the fixture inventory — and every suite references it, the
  importer's included. The importer's copy was a deliberate duplicate so that deleting the tool
  would be a deletion rather than a change to the tests that stay; with a project whose only job is
  support, deleting it still is — the support project loses a consumer. It stops at
  `Infrastructure`: `Domain.Tests` references it, and a path from there to `Processing` would let a
  domain rule be proved against the parser's own output, which `DomainAssemblyTests` now refuses.
  The inventory carries no `TheoryData`, because xunit's own analyzer crashes on a `MemberData`
  pointing at another assembly and a crashed analyzer is a warning CI fails on; each suite wraps
  the set in one line over `All`.
- **2026-08-07** — `refined:` ISC-18 and ISC-19 say *every* response in `tests/fixtures/deepgram/`,
  and their probes walked the inventory rather than the directory — so both were only as universal
  as somebody remembering to add a line. `DeepgramFixtureTests` now compares the two directions,
  and a response committed without being named fails. The claims did not change; what could be
  offered as proof of them did.
- **2026-08-07** — `created_at` means the row's own age everywhere, and the model refuses to write
  one over a row that exists rather than each writer being trusted to. The two that held a
  different fact under it get the names for what they record — `artifacts.confirmed_at`,
  `speaker_assignments.assigned_at`, the latter completing a pair `assigned_by` had been half of.
  A rule declared once is what makes the next timestamp that wants to move ask for a name instead
  of borrowing the column already there.
- **2026-08-07** — A rebuild replaces the turns underneath the claims that cite them, in one
  transaction with `PRAGMA defer_foreign_keys`, rather than deleting the claims and putting them
  back. Deleting them was the obvious shape and is the wrong one: nothing reads an accepted
  extraction back into rows yet, so a rebuild that deleted them would be losing what it cannot
  produce again. Deferring turns "the ordinals came out the same" from an assumption into a thing
  the commit refuses when it is false — which is the one failure a rebuild could have that nothing
  else would notice.
- **2026-08-07** — A merged turn's confidence is the mean of its parts weighted by their length.
  The lowest of the parts and nothing at all were the alternatives: the lowest makes every long
  turn look untrustworthy, since one bad part is enough and turns get longer the less somebody is
  interrupted, and nothing at all leaves the column for whoever writes a screen to decide. A part
  the response said nothing about is left out of the mean rather than counted as zero.
- **2026-08-07** — `MeetingTranscriber.Processing` references `MeetingTranscriber.Infrastructure`,
  and the edge only goes that way. Rendering reads the response out of the corpus and puts the
  derivatives back, so it sits above storage; the opposite edge would make SQLite depend on how a
  Deepgram response is parsed, which is the dependency the split exists to prevent.
- **2026-08-07** — An imported extraction run carries `schema_version` `legacy` and, when the file
  does not say, `prompt_version` `unknown`. Neither is a version somebody chose: the Python system
  had exactly one extraction shape and never numbered it, so `legacy` names that shape, which is
  what a schema version is for; `unknown` is reported as an assumption every time it is written, so
  it cannot be mistaken for one. `input_hash` is the response the meeting was transcribed from —
  what the model was actually given was the rendered transcript, and that file is deliberately not
  imported, so the response is the furthest back this corpus can still name.
- **2026-08-07** — Search takes FTS5's query syntax rather than a bag of words, and one hit is one
  row rather than one meeting. The syntax is what makes `presupuesto AND cliente` and a quoted
  phrase mean what somebody typing them expects, and the cost — a query FTS5 cannot parse is an
  error — is paid as `CorpusSearchException` naming the query. Per hit rather than per meeting
  because "where was this said" has as many answers as it has, and collapsing them would make one
  limit mean two different things depending on how long the meeting was.
- **2026-08-07** — One person is the user of this install, and that is not a unique index. The flag
  moving is one row losing it while another gains it, and SQLite checks a unique index at the end of
  each statement rather than of the transaction, so the pair would be refused whenever the update
  setting it happened to run before the one clearing it — an order the caller does not choose.
  `HumanLayer.ThisIsMe` is where the rule holds instead, and with the speaker label a person
  resolved outranking one the recording settled, it is the reason the human layer has a service and
  not only tables.
- **2026-08-07** — `refined:` ISC-25's stub named a schema assertion about one table while the claim
  said "every edit a person made". It now names the probe that compares every table a rebuild is not
  allowed to touch, reading the tables and their columns out of the database rather than from a
  list. The claim did not change; what was offered as proof of it did.
- **2026-08-07** — `ISA.md` lands as the claims surface. `arquitectura.md` keeps the design and
  its reasoning; its §13 is removed in favour of the board, which is the live version of the
  same plan, and its §15 graduates into these claims, which is what it always was.
- **2026-08-07** — The board task points at the claim; the claim never points at the task. ISC
  IDs are stable by rule and ClickUp IDs churn as the board is groomed, so a task id stored here
  would rot the artifact on every grooming pass.
- **2026-08-07** — Sixteen LifeOS sections become seven. Problem, Vision, Out of Scope,
  Principles and Constraints would duplicate `arquitectura.md`, which is the parallel artifact
  the ISA doctrine exists to prevent. `## Language` is dropped because CLAUDE.md's contract
  already fixes the vocabulary this product argues about.
- **2026-08-07** — `refined:` ISC-5 is written open rather than omitted. CLAUDE.md states the
  bare-`DateTime` rule as part of the contract and no test covers it; an unprobed contract line
  is an open claim, not an absent one.
- **2026-08-07** — Dead end: a Test Strategy row of the form
  `dotnet test --filter "FullyQualifiedName~X"`. See Learning.
- **2026-08-07** — `refined:` `## Test Strategy` is dropped. Sixty-five rows of near-identical
  `dotnet test … --filter-class` boilerplate cost more to keep true than they bought, and the
  probe for a claim is discoverable from the test named in its `## Verification` stub. The cost
  is real and stated here rather than glossed: an open claim no longer carries the probe it will
  close on, so what would falsify it lives in whoever picks up the work rather than in the file.

## Learning

- **conjecture** — A helper every test opens a corpus through is where a rule about corpora is
  held, so mending the process-wide pool clear inside it mends it everywhere.
- **refuted-by** — A second call sat in `CorpusSchemaTests`, in a test that opens a corpus
  read-only after a writer, and it was found by an adversarial reviewer rather than by anything
  going red. The suite passed twenty times over the half-fix, because what the call breaks is
  another test that happened to be between putting a connection back and taking it out again.
- **learned** — A rule saying what nothing may call is not held by fixing the place it was called.
  The helper concentrates how a corpus is opened, which is why the rule looked contained; it has
  no say over a suite that reaches for the provider's static method directly, and that call reads
  as harmless everywhere it appears.
- **criterion-now** — The test tree is swept for `ClearAllPools` and the sweep names the file, so
  the second one costs a red test at the moment it is written. A test lets go of its own corpus
  through `CorpusDatabase.ClearPoolsFor`, which is also the only place that knows both connection
  strings a corpus can be pooled under.

- **conjecture** — A `corpus.db` that is there is either a corpus or a permissions problem, and
  SQLite's error says which.
- **refuted-by** — A file of zero bytes is neither. SQLite reads it as an empty database and
  refuses to put it into WAL, so every write against it comes back as `attempt to write a readonly
  database` — including the migration that would have made it a corpus. The directory was a dead
  end: the corpus could not be created there again, and the message sent whoever read it to look at
  permissions that were fine.
- **learned** — The state a create leaves behind when it is cut off is not "nothing" and not "a
  corpus". It is a third thing, and it is the one state where the error a component reports is
  about something other than what is wrong. Every file this program opens can be in it.
- **criterion-now** — An empty `corpus.db` is not a corpus: every command refuses it saying so, and
  `migrate` removes it, which is the only file this program deletes without being asked. A refusal
  that comes from SQLite keeps SQLite's words and adds the path, because two very different causes
  arrive under the same sentence and this program cannot tell them apart.

- **conjecture** — A timestamp column is named after the moment it holds, so `created_at` on a
  table is a description of that column and nothing more.
- **refuted-by** — Two tables kept an honest value under it and rewrote it in place: an artifact
  every time a derivative was rendered again, a speaker assignment every time somebody corrected
  what the channel had guessed. Both reads at the call site looked right, and both were storing
  when the answer was settled under a name promising when the row appeared.
- **learned** — A column present on nearly every table stops being a description and becomes the
  default place to put a time. Nothing was wrong at either call site in isolation; what was wrong
  is that neither had to name what it was recording, so the vocabulary quietly widened to mean
  "the last time anything about this row was true".
- **criterion-now** — `created_at` is read-only once its row exists, held on the model rather than
  by each writer. A timestamp that moves gets a column named for what moves it, and finding that
  out costs a failed `SaveChanges` at the moment it is written rather than a query years later.

- **conjecture** — The importer stored a resolved speaker as `speaker_0`, and that was the label,
  because nothing else in the system had an opinion about it.
- **refuted-by** — The first legacy meeting rendered through `MeetingRenderer` produced turns
  labelled `ch0:speaker_0`, so every assignment the old corpus carried matched no turn and no name
  reached the transcript. Nothing failed: an assignment for a label that does not exist is a row
  the join simply does not find.
- **learned** — A contract only holds where two sides of it meet. `SpeakerLabels.For` was written,
  documented and tested, and the importer spelled the string out by hand a few lines away — which
  is the one thing that could silently lose the most valuable rows in the old corpus, because every
  one of them is somebody having listened.
- **criterion-now** — A stored label is built by `SpeakerLabels.For` and never spelled out, and a
  writer of `speaker_assignments` is tested against a rendered turn rather than against a string
  literal. Both sides come from the same function or neither is proved.

- **conjecture** — `dotnet test --filter "FullyQualifiedName~X"` selects a test class, so a
  Test Strategy row could name the filter alone and stay short.
- **refuted-by** — Run against `TurnsTests` it executed all 439 tests across all four projects
  and exited 0. The MTP runner that xunit.v3 3.2.2 uses ignores the flag rather than rejecting
  it.
- **learned** — The probe passed while probing nothing. A green result from a filter that was
  silently discarded is indistinguishable from a real pass, which is the exact failure the
  evidence rule exists to catch — and it would have marked sixty-five claims verified on one
  command that never ran them.
- **criterion-now** — A `dotnet-test` row names its project and uses `-- --filter-class`, since
  a bare `--filter-class` makes the three non-matching projects exit non-zero on zero tests. No
  row is trusted until it has been run once and seen to select what it claims.

## Verification

- ISC-1 — `AudioChannelTests` green 2026-08-07
- ISC-2 — `SourceProfileTests` green 2026-08-07
- ISC-3 — `UtcTimestampTests` green 2026-08-07
- ISC-4 — `DurationTests` green 2026-08-07
- ISC-6 — `DomainAssemblyTests.Domain_references_no_windows_assembly` green 2026-08-07
- ISC-7 — `JobStateTests.Every_state_says_where_it_can_go` green 2026-08-07
- ISC-8 — `JobStateTests.Only_the_states_the_runner_owns_are_picked_up_by_itself` green 2026-08-07
- ISC-9 — `SpeakersTests` green 2026-08-07
- ISC-10 — `SpeakersTests` green 2026-08-07
- ISC-11 — `TurnsTests` green 2026-08-07
- ISC-12 — `CorpusSchemaTests` green 2026-08-07
- ISC-13 — `ClassificationStoriesTests` green 2026-08-07
- ISC-14 — `CorpusNamingTests` green 2026-08-07
- ISC-15 — `git grep` over `tests/` returned no match 2026-08-07
- ISC-16 — `dotnet build` 0 warnings 0 errors 2026-08-07
- ISC-17 — `CorpusStorageTests` green 2026-08-07
- ISC-105 — `CorpusIsOneThingTests.A_corpus_says_which_folder_it_is` green 2026-08-13
- ISC-106 — `CorpusIsOneThingTests` in `Cli.Tests` and `CorpusImport.Tests` green 2026-08-13, each red against a signature taking both in the assemblies it covers
- ISC-107 — `TemporaryCorpusTests` green 2026-08-13, and the whole suite twenty times over: `Closing_a_corpus_leaves_another_corpus_the_connection_it_had_pooled` red against `SqliteConnection.ClearAllPools` on a different handle coming back, `No_test_empties_the_pools_of_every_corpus_in_the_process` red naming `CorpusSchemaTests.cs`, which was the second call site and the one no test had ever caught
- ISC-18 — `FixtureParsingTests` green 2026-08-07
- ISC-19 — `DeepgramFixtureTests` green 2026-08-07
- ISC-20 — `ReferenceBehaviourTests` green 2026-08-07
- ISC-21 — `CorpusImporterTests.The_corpus_it_reads_comes_out_exactly_as_it_went_in` green 2026-08-07
- ISC-22 — `CorpusImporterTests.Importing_the_same_corpus_twice_imports_it_once` green 2026-08-07
- ISC-23 — `CorpusImporterTests.What_is_left_behind_on_purpose_is_not_mixed_with_what_had_nowhere_to_go` green 2026-08-07
- ISC-24 — `DeepgramTranscriptParserTests` green 2026-08-07
- ISC-25 — `CorpusRebuildTests.Deleting_every_derived_row_and_projecting_again_leaves_every_other_table_as_it_was` green 2026-08-07
- ISC-26 — `CorpusRebuildTests.A_claim_cannot_cite_a_turn_the_meeting_never_had` green 2026-08-07
- ISC-27 — `DurableWriteTests.A_source_is_never_written_over` green 2026-08-07
- ISC-28 — `DurableWriteTests.A_write_cut_while_its_content_is_produced_leaves_nothing_at_all` green 2026-08-07
- ISC-29 — `DurableWriteTests.What_is_recorded_is_the_hash_of_the_bytes_that_were_written` green 2026-08-07
- ISC-30 — `DurableWriteTests.Another_meetings_folder_is_not_somewhere_this_meeting_may_write` green 2026-08-07
- ISC-31 — `CorpusIntegrityTests.A_row_pointing_at_a_meeting_that_is_not_there_fails_and_names_the_table` green 2026-08-07
- ISC-32 — `CorpusIntegrityTests.Compacting_leaves_search_answering_exactly_what_it_answered_before` green 2026-08-07
- ISC-54 — `CorpusRebuildTests.A_second_extraction_leaves_the_first_ones_state_alone_and_starts_its_own_blank` green 2026-08-07
- ISC-56 — `CorpusIntegrityTests.Search_is_the_index_answering_and_not_the_table` green 2026-08-07
- ISC-58 — `CorpusRebuildTests.Deleting_every_derived_row_and_projecting_again_leaves_every_other_table_as_it_was` green 2026-08-07
- ISC-66 — `HumanLayerTests.Every_table_of_the_human_layer_has_a_way_in` green 2026-08-07
- ISC-67 — `HumanLayerTests.Exactly_one_person_is_the_user_of_this_install` green 2026-08-07
- ISC-68 — `HumanLayerTests.A_label_the_recording_settled_does_not_overwrite_one_a_person_resolved` green 2026-08-07
- ISC-108 — `capture --out … --seconds 12` on this machine 2026-08-13: the two streams opened 32 ms apart and each wrote 4,654,138 bytes of 48 kHz float32, the loopback of the default playback endpoint and a fifine microphone
- ISC-109 — the same run: `ch0 device`/`ch0 format` named 'Altavoces (High Definition Audio Device)' at 48000 Hz, 2 ch, 32 bit float before a sample was written, and `ch1` its microphone. `StreamFormatTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13 for the extensible format WASAPI really hands over
- ISC-110 — `LevelsTests` and `SourceMeterTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13; the same run metered both sources every second, between −7.5 and −58.8 dBFS
- ISC-111 — `AudioDevicesTests` green 2026-08-13, and two runs 2026-08-13: `--microphone "blue yeti"` refused with exit 1 and no file written, and a channel 1 whose file was already there refused with exit 1 after channel 0 had opened, leaving nothing of channel 0 behind
- ISC-69 — `CorpusSearchTests.A_hit_carries_the_meeting_the_date_the_title_a_snippet_and_where_it_was_said` green 2026-08-07
- ISC-70 — `CorpusSearchTests.A_meeting_being_deleted_is_not_something_search_offers` green 2026-08-07
- ISC-71 — `CorpusSearchTests.Throwing_both_indexes_away_and_rebuilding_them_answers_exactly_the_same` green 2026-08-07
- ISC-72 — `CorpusSearchTests.A_query_the_index_cannot_parse_says_so_and_names_it` green 2026-08-07
- ISC-73 — `CorpusImporterTests.An_imported_extraction_arrives_with_the_run_it_came_out_of` green 2026-08-07
- ISC-74 — `CorpusImporterTests.A_decision_and_an_action_projected_from_it_hang_off_that_run` green 2026-08-07
- ISC-33 — `MeetingRendererTests.Rendering_again_leaves_the_sources_alone_and_produces_the_same_files` green 2026-08-07
- ISC-75 — `MeetingRendererTests.A_name_and_a_correction_reach_the_transcript_and_not_the_stored_turns` green 2026-08-07
- ISC-76 — `TurnsTests.A_turns_confidence_is_the_mean_of_its_parts_weighted_by_their_length` green 2026-08-07
- ISC-77 — `CorpusRebuildTests.Rebuilding_produces_the_same_projections_and_the_same_files` green 2026-08-07
- ISC-78 — `CorpusRebuildTests.A_claim_still_points_at_the_turn_it_came_from` green 2026-08-07
- ISC-79 — `CorpusImporterTests.Importing_again_does_not_duplicate_or_rewrite_the_derivatives` green 2026-08-07
- ISC-81 — `CorpusNamingTests.No_created_at_anywhere_can_be_written_over_a_row_that_exists` and `.Moving_a_created_at_on_a_stored_row_fails_instead_of_being_written` green 2026-08-07, both red with the model rule commented out
- ISC-80 — `CorpusImporterTests.A_speaker_somebody_resolved_arrives_under_the_label_the_provider_wrote` green 2026-08-07
- ISC-82 — `git grep -l` for the five fixture names over `tests/**/*.cs` returned `MeetingTranscriber.Testing/DeepgramFixtures.cs` alone 2026-08-07; the other hit is the tool that builds them, which is not the test tree. `DeepgramFixtureTests.The_inventory_names_exactly_the_responses_that_are_committed` green, red with a fixture dropped from the inventory
- ISC-83 — `git grep -l "class TemporaryCorpus" -- tests/` returned `MeetingTranscriber.Testing/TemporaryCorpus.cs` alone 2026-08-07
- ISC-34 — `CommandLineTests` green 2026-08-07: `status` answers for a corpus this build has moved past, and `check` names the file the corpus claims and does not have
- ISC-84 — `CliWalkthroughTests.A_response_becomes_a_meeting_that_renders_rebuilds_and_is_found_again` green 2026-08-07
- ISC-85 — `CliWalkthroughTests.The_same_response_imported_twice_is_one_meeting` green 2026-08-07
- ISC-86 — `MeetingManifestTests.Filing_a_response_leaves_a_card_recorded_as_a_source` and `CorpusImporterTests.An_imported_meeting_arrives_with_the_card_that_names_it` green 2026-08-13, covering both doors a meeting comes in through
- ISC-87 — `MeetingManifestTests.A_meeting_is_recognised_from_its_card_with_nothing_else_left` green 2026-08-13: the corpus is disposed and deleted before the card is read, so only the copied file can answer
- ISC-88 — `MeetingManifestTests.Filing_again_writes_the_card_as_the_corpus_now_says_it_is` and `.A_card_that_is_gone_comes_back_when_the_response_is_filed_again` green 2026-08-13; `DurableWriteTests.The_recovery_card_is_the_source_a_second_write_replaces` pins the rule under them, and `.A_source_is_never_written_over` still holds for the response
- ISC-98 — `CorpusRebuildTests.A_rebuild_leaves_every_meeting_with_the_card_that_names_it` and `.A_rebuild_brings_a_card_up_to_a_title_somebody_changed_since` green 2026-08-13, the first starting from a corpus with no manifest row at all
- ISC-99 — `DurableWriteTests.A_write_that_calls_a_path_something_it_is_not_is_refused_before_the_file_moves` green 2026-08-13: a manifest addressed at `deepgram.json` is refused and the paid bytes are still there afterwards. `ArtifactsTests.The_manifest_is_the_only_source_a_second_write_may_replace` holds the exception to one kind
- ISC-100 — `HumanLayerTests.Renaming_a_meeting_leaves_its_card_saying_the_new_title` green 2026-08-13: the meeting is given its card first, so the probe fails on a card gone stale rather than on one never written — it read `la daily` against `la daily del equipo` before the fix, and the corpus is closed before the file is read
- ISC-101 — `HumanLayerTests.A_rename_whose_card_cannot_be_written_does_not_happen_at_all` green 2026-08-13: a directory standing where the card goes makes the replace fail, and the title is read back past the tracked entity to prove the corpus kept the old one
- ISC-102 — `ArtifactRestoreTests` and `CommandLineTests.A_paid_file_the_corpus_lost_is_put_back_from_the_bytes_it_already_describes` green 2026-08-13; the command line one deletes the paid response, sees `check` refuse, restores from the original and gets `Sound` out of `check --verify-contents`
- ISC-103 — `ArtifactRestoreTests.Bytes_no_row_of_this_corpus_describes_are_refused_and_nothing_is_written` and `.Bytes_the_corpus_records_elsewhere_do_not_land_where_another_row_is_missing` green 2026-08-13; the second is the one worth having, since bytes the corpus does know are the case where a wrong path is reachable at all
- ISC-104 — `MeetingIntakeTests.A_meeting_whose_response_is_gone_gets_it_back_when_the_same_bytes_are_filed_again` green 2026-08-13, red with the restore taken out of `Receive`: `RenderException` naming the response the row points at, which is the failure the task was raised for
