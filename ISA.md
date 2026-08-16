---
phase: climbing
progress: 83/118
updated: 2026-08-16
---

# ISA — meeting-transcriber-net

What *done* means for this product, written as claims that are true or false and never partly
either. This file is the claims surface and the count; it is not the design document and not the
work queue. A claim closes on evidence recorded in `## Verification`, never on a task moving.

A claim says what has to be true of the product, a recording or the corpus. It never says which
type, method or test makes it so — that is the design, and it lives in `arquitectura.md` and in
the code. The evidence names the test; the claim names the truth, so a claim survives the code
being rewritten underneath it.

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
- [x] ISC-1: Channel 0 is the loopback and channel 1 the microphone — the same number in the audio, in what the provider is asked about, and in what is stored.
- [x] ISC-2: A recording is never transcribed under a profile its channel count contradicts.
- [x] ISC-3: Every instant the corpus holds is UTC to the millisecond.
- [x] ISC-4: Every length and every offset on a timeline is a whole number of milliseconds.
- [ ] ISC-5: Anti: a time value cannot enter the corpus without saying whether it is an instant or a length.
- [x] ISC-6: The rules of the corpus hold with no Windows behind them.
- [x] ISC-7: A job reaches only the states the one it is in allows, and every state says where it can go.
- [x] ISC-8: The runner starts only what is due, and never something that is waiting on a person.
- [x] ISC-9: Two speakers heard on different channels never share a label, so a name put on one never lands on the other's words.
- [x] ISC-10: The user is settled only when the microphone caught exactly one speaker; every other speaker waits for somebody to say who it is.
- [x] ISC-11: What is stored, and what a citation anchors on, is a turn — and where a turn ends is decided the same way for every meeting.
- [x] ISC-12: Every classification name is closed: a meeting cannot be filed under one the corpus does not know.
- [x] ISC-13: The thirteen meetings of `arquitectura.md` §5.3 store and are found again.
- [x] ISC-14: Anti: nothing stored can be renamed without the rename being deliberate.
- [x] ISC-15: Anti: the moment a row appeared cannot be moved once the row exists.
- [x] ISC-16: Anti: nothing under test reaches the network, so no test spends what a person has to pay for.
- [x] ISC-17: Anti: a test cannot read a provider response the fixture set does not name.
- [x] ISC-18: Anti: a test cannot open a corpus other than the one it made for itself.
- [x] ISC-19: The build carries no warning.
- [x] ISC-20: The code needs no reformatting.
- [x] ISC-21: What goes into the corpus comes back out of it as what it was.
- [x] ISC-22: A corpus is one thing — a database and the folder it sits in — and nothing can be handed half of one and half of another.
- [x] ISC-23: Anti: letting go of one corpus cannot break another that is open.

### F1 · Contracts and characterisation
Why: what the Python system learned is specified in .NET before any of it is rebuilt, so the
knowledge survives without a runtime dependency on Python.
Board: 0 · Contratos y caracterización
- [x] ISC-24: Every response the fixture set holds parses into the turns it describes.
- [x] ISC-25: A fixture carries the provider's real timings, confidences and channel numbers, with every word replaced from a closed vocabulary.
- [x] ISC-26: Where this system departs from the one it replaces is written down, and each departure is held to.
- [x] ISC-27: Anti: importing never writes to the old corpus — it comes out exactly as it went in.
- [x] ISC-28: Anti: what an import cannot place is named, apart from what was left behind on purpose, and never dropped.
- [x] ISC-29: An imported meeting's derived files are produced here rather than carried over.
- [x] ISC-30: A speaker somebody resolved in the old corpus arrives on the words that speaker actually said.
- [x] ISC-31: What the old corpus extracted arrives with the run it came out of, and every decision, action and state projected from it hangs off that run.

### F2 · Deterministic core from artifacts
Why: given a paid response, everything that does not need a microphone works — parse, store,
project, rebuild — so the artifact is the only input the rest of the system needs.
Board: 1 · Núcleo .NET desde artefactos
- [x] ISC-32: A paid response becomes turns carrying their channel, their speaker and where on the timeline they were said.
- [x] ISC-33: A paid response on disk becomes a meeting, with its turns and its derived files, from the command line alone.
- [x] ISC-34: Anti: the same meeting cannot enter the corpus twice — filed or imported again, under whatever folder name, it is the one already there and it is rendered again.
- [x] ISC-35: A rebuild throws away only what it can produce again: every correction, name and classification a person gave survives it.
- [x] ISC-36: Everything derived comes back the same from the sources alone — the same projections and the same files.
- [x] ISC-37: Anti: a citation always resolves — nothing can cite a turn its meeting never had, and a turn something cites cannot be deleted out from under it.
- [x] ISC-38: Anti: a rebuild that would move a turn's position fails rather than rewriting what every stored citation points at.
- [x] ISC-39: Anti: what was paid for, or cannot be produced again, is never written over.
- [x] ISC-40: A derivative produced again replaces itself and stays one row.
- [x] ISC-41: A write cut off part-way leaves either nothing or the finished artifact, never half of one.
- [x] ISC-42: What is recorded for an artifact is the hash of the bytes that were actually written.
- [x] ISC-43: Anti: a meeting cannot write into another meeting's folder.
- [x] ISC-44: Anti: a write cannot put one kind of artifact over the path another kind already holds.
- [x] ISC-45: Anti: bytes no row of this corpus describes never reach an artifact's path.
- [x] ISC-46: A paid file the disk has lost comes back from bytes that hash to what its row already says, before anything is rendered from it.
- [x] ISC-47: A corpus that is not sound fails and names what broke.
- [x] ISC-48: Anti: a name or a correction reaches the rendered files and never the stored turn.
- [x] ISC-49: A merged turn's confidence is the mean of its parts, weighted by their length.
- [x] ISC-50: Every meeting's folder carries a card saying what the corpus now says about it — after it is filed, filed again, renamed or rebuilt.
- [x] ISC-51: A meeting is recognised from its card alone — its id, when it started, its profile, its language and its title — with nothing else read.
- [x] ISC-52: Anti: the card and the corpus never disagree — a change that cannot reach both does not happen at all.
- [x] ISC-53: The corpus's state can be read from the command line without opening the application.
- [x] ISC-54: Exactly one person is the user of this install.
- [x] ISC-55: Anti: a speaker somebody resolved is never overwritten by what the recording settled.

### F3 · Audio engine
Why: two sources become one timeline a person can trust. This is the largest technical risk in
the product and it is settled before any UI is built on top of it.
Board: 2 · Spike y motor de audio
- [x] ISC-56: The selected microphone and the full system loopback are captured over the same stretch of time, each into its own stream.
- [x] ISC-57: A capture names each source's device and the format that device handed it.
- [x] ISC-58: A source's level is what actually arrived, so a source that heard nothing reads as silent.
- [x] ISC-59: Anti: a capture that cannot open both of its sources stops, rather than recording one of them.
- [x] ISC-60: Two sources become one aligned pair of channels.
- [x] ISC-61: A stretch of audio lands where its device says it belongs, not immediately after the stretch before it.
- [x] ISC-62: A stretch a source never delivered stays a gap of the length its device says, rather than being closed up.
- [x] ISC-63: Whatever rate, width and channel count a source arrives with, it leaves the timeline at one rate and one sample format.
- [x] ISC-64: What a source covers is counted from where its device says the audio was, and never from how much of it reached the application.
- [x] ISC-65: Audio is placed by the instant its device read it, and never by the moment the application collected it.
- [x] ISC-66: Two hours of a meeting whose two devices' clocks disagree leave the channels under 50 ms apart at every point, and not only at the end.
- [x] ISC-67: A source that opened late is a wait and not a rate: the delay is reported as the wait it was, and audio already in the right place is never pulled off it as though it had drifted.
- [x] ISC-68: Anti: a device whose frame counter and clock disagree by more than any crystal drifts stops the recording, rather than being clamped into looking aligned.
- [x] ISC-69: A source that goes quiet does not hold the rest of the meeting back.
- [x] ISC-70: A recording says how much of each source never arrived, rather than coming back shorter with nothing saying so.
- [x] ISC-71: A recording is as long as the last audio of either source, so one that stopped early does not cut the end off the meeting.
- [x] ISC-72: Anti: how late a source's audio is handed over cannot change the recording, up to the half minute after which that source is given up.
- [x] ISC-73: Anti: a stretch of a meeting nobody played into is recorded as the silence it was, and not as whatever the device's buffer last held.
- [x] ISC-74: Anti: the recorded file never carries the microphone on channel 0.
- [x] ISC-75: A recording cut off mid-block comes back to its last whole block.
- [x] ISC-76: Finishing the same recording twice produces the same file.
- [ ] ISC-77: Capture falls back to the full loopback when the meeting's process cannot be followed.
- [ ] ISC-78: A device changing mid-recording does not end the recording.
- [x] ISC-117: A recording that follows one application carries what that application played, including what the processes it started played.
- [x] ISC-118: Anti: a recording that follows one application carries nothing another application played over it.

### F4 · WinUI recorder
Why: the application replaces OBS. Recording, pausing, stopping and recovering happen in one
native app with no Python, no WSL and no FFmpeg anywhere behind it.
Board: 3 · Grabador WinUI
- [ ] ISC-79: A recording survives the process being killed and is offered back on the next start.
- [ ] ISC-80: A source that is hearing nothing is shown as silent while the meeting is still running.
- [ ] ISC-81: Record, pause and stop produce one continuous timeline.
- [ ] ISC-82: The queue shows each job's state and what it is waiting for.
- [ ] ISC-83: A mono or stereo file from disk becomes a meeting.

### F5 · Deepgram BYOK
Why: a recording becomes a transcript on the user's own key, and the user is charged exactly
once for exactly what they approved.
Board: 4 · Deepgram BYOK
- [ ] ISC-84: The Deepgram key lives in Windows Credential Manager and is read from nowhere else.
- [ ] ISC-85: Anti: no Deepgram call happens without an explicit approval carrying the estimated cost.
- [ ] ISC-86: Transcribing again is a new version beside what was paid for, never a replacement.
- [ ] ISC-87: A job whose outcome is uncertain — a charge that may already have happened — stops on a person.
- [ ] ISC-88: What the provider returns has the shape the fixtures describe.

### F6 · Summaries
Why: a meeting becomes a summary whose every claim resolves to something said, using the user's
own Claude Code credits — and the product stays whole when Claude Code is not installed.
Board: 5 · Summaries
- [ ] ISC-89: Anti: recording, transcription, rendering, search and recovery all work with Claude Code absent.
- [ ] ISC-90: A summary that fails validation is stored as a failed run, not as a summary.
- [x] ISC-91: A second extraction leaves the first one's state alone and starts its own blank.
- [ ] ISC-115: A rejected summary is handed back once, saying what was wrong with it.
- [ ] ISC-116: Anti: a statement nothing said supports can come back only without that statement — one that comes back pointing at something else for the same statement is refused.

### F7 · Local knowledge
Why: people and agents query the corpus with no server, no network and no cloud, and every
answer traces back to a turn. What an answer says still stands is maintained as meetings arrive
rather than re-derived at every question, and the corpus answers the same way whether or not a
run has been over it.
Board: 6 · Conocimiento local
- [x] ISC-92: Search costs what an index costs and not what a scan costs, however many meetings there are.
- [ ] ISC-93: Everything search promises to find is found.
- [x] ISC-94: A hit carries the meeting, its date, its title, an elided snippet and where on the timeline it was said.
- [x] ISC-95: Anti: a meeting on its way out is never something search offers.
- [x] ISC-96: Maintaining the corpus — compacting it, or throwing the indexes away and building them again — leaves search answering exactly what it answered before.
- [x] ISC-97: Anti: a query the index cannot parse is refused naming the query, never as a database error.
- [ ] ISC-98: The MCP server answers read-only over stdio and never writes.
- [ ] ISC-99: Anti: an MCP response is bounded.
- [ ] ISC-100: Every MCP request is recorded locally.
- [ ] ISC-101: Anti: what a meeting recorded is never rewritten by a later one — what changed is recorded beside it and both stay readable.
- [ ] ISC-102: Two people asking the same corpus what still stands get the same answer, whoever is reading and whatever they read first.
- [ ] ISC-103: What still stands comes back at the same cost with three hundred meetings behind it as with ten.
- [ ] ISC-104: A decision comes back with when it was settled and what has happened around it since, so "nothing contradicted it" is never read as "somebody confirmed it".
- [ ] ISC-105: Anything saying a decision no longer stands cites the turn where that was said, the way a decision cites the turn it came from.
- [ ] ISC-106: Anti: two decisions that contradict each other with nothing settling it come back as a conflict, and neither is hidden for being the older one.
- [ ] ISC-107: Anti: nothing is hidden for want of a pass having run over it — a decision stands until something says otherwise.
- [ ] ISC-108: A person's word on whether a decision stands outranks whatever the machine concluded, and survives a rebuild.
- [ ] ISC-109: Deciding what an arriving meeting changed reads a bounded part of the corpus, and what bounds it does not grow as meetings accumulate.

### F8 · Distribution and backup
Why: the application installs, upgrades and comes back from a lost disk, because the corpus
holds artifacts that cannot be obtained again.
Board: 7 · Distribución y backup
- [ ] ISC-110: Anti: the corpus never lives in the MSIX package data folder.
- [ ] ISC-111: A snapshot restores to an alternate directory and comes back sound.
- [ ] ISC-112: An upgrade over an installed build leaves the corpus intact.
- [ ] ISC-113: The CLI and the MCP server are reachable by app execution alias.
- [ ] ISC-114: The corpus location is configurable and validated at startup.

## Not yet specified

- **How a summary citation anchors.** ISC-37 says a citation always resolves; whether it stores
  the turn id or an offset into the transcript changes what happens when turns are regrouped.
  `docs/reference-behaviour.md` has the grouping rules but not this.
- **What bounds an MCP response in ISC-99.** Rows, bytes or tokens — and the answer depends on
  what an agent actually asks for, which nobody has measured yet.
- **How the corpus decides that a decision stopped standing.** ISC-101 to ISC-109 say what has to
  be true of the answer; none of them says what produces it, and four shapes are on the table with
  no evidence between them. A pass over each arriving meeting, linking a new decision to the
  standing one it replaces — the most precise, and wrong in the direction that hides something
  somebody decided. The same question asked at read time over the decisions of one node, which
  stores nothing and answers differently on two days. A person asked at the end of a meeting which
  standing decisions this one touched — the most reliable, and a chore that gets skipped. Or
  nothing inferred at all: every decision comes back with its date and what has happened since,
  and the person judges, which is ISC-104 on its own and can hide nothing. What decides between
  them is a corpus with enough meetings on one node to measure, and that does not exist yet — the
  extraction that fills `decisions` outside the importer is F6 and unbuilt. Two things would have
  to be measured before choosing: how close two statements have to be before one is offered as
  replacing the other, and how much of one node actually fits in a context, which is the number
  the read-time shape lives or dies on.

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

- **conjecture** — WASAPI's per-packet flags have no reader here, so leaving them off
  `CapturePacket` is the rule about abstractions built for callers that do not exist.
- **refuted-by** — The same pass added a check that stops a recording when a device's frame
  counter and its clock disagree. `TIMESTAMP_ERROR` is the device saying it cannot vouch for the
  pair that check reads, so the check is that flag's reader — and without it a device that flags
  one bad packet loses the whole meeting. An adversarial reviewer found it; nothing went red,
  because no fabricated packet has ever set the flag.
- **learned** — A field's caller can be added in the same pass as the field, and a check that can
  refuse work is exactly the kind of caller that appears late. The rule asks whether a caller
  exists, and the honest question is whether one exists *once this change lands*, which is not
  the same question when the change adds a failure the flag exists to suppress.
- **criterion-now** — A field carrying a device's own "do not trust this" is added when something
  in the same change would otherwise act on the untrustworthy value. `DATA_DISCONTINUITY` stays
  off the packet on the original grounds and the reason sits in the type, since a lost stretch is
  a jump in the device position and a flag nothing reads can disagree with the number beside it.

- **conjecture** — A device's reported position and the frames it hands over are counted in the
  same unit, so a position that goes backwards is a driver whose counter is broken.
- **refuted-by** — A webcam microphone on this machine, opened at the endpoint's 48 kHz mix
  format, hands over 480 frames a packet and advances its position by 160 — its own 16 kHz frames,
  while the samples arrive converted. Its first packet of 463 frames put the next expected frame
  at 463 and its second packet said 160, so an 8 s recording came back as a refusal about a
  counter going backwards. The other microphone on the same machine, which runs at 48 kHz
  natively, records fine, so the whole difference is what the device runs at.
- **learned** — Where a stretch of audio belongs is two numbers in two units, and only one of them
  is the client's. A shared-mode client is converted to the format it asked for; the position
  counter is not, so a position means nothing on the recording's timeline until it has been scaled
  by the rate that device really counts in.
- **criterion-now** — A capture is not assumed to number its frames in the format it was opened
  at, and a device whose positions cannot be laid out refuses the recording at the moment it is
  stopped, with every block still on disk and the message saying so. Recording such a device
  rather than refusing it is a board task, and it is the one thing a person with that microphone
  needs.

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
- ISC-14 — `CorpusNamingTests` green 2026-08-07, which spells out every stored table and column, so a rename that nobody meant fails the suite
- ISC-15 — `CorpusNamingTests.No_created_at_anywhere_can_be_written_over_a_row_that_exists` and `.Moving_a_created_at_on_a_stored_row_fails_instead_of_being_written` green 2026-08-07, both red with the model rule commented out
- ISC-16 — `git grep` for HTTP and socket types over `tests/` returned no match 2026-08-07
- ISC-17 — `git grep -l` for the five fixture names over `tests/**/*.cs` returned `MeetingTranscriber.Testing/DeepgramFixtures.cs` alone 2026-08-07; the other hit is the tool that builds them, which is not the test tree. `DeepgramFixtureTests.The_inventory_names_exactly_the_responses_that_are_committed` green, red with a fixture dropped from the inventory
- ISC-18 — `git grep -l "class TemporaryCorpus" -- tests/` returned `MeetingTranscriber.Testing/TemporaryCorpus.cs` alone 2026-08-07
- ISC-19 — `dotnet build --no-restore -warnaserror` 0 warnings 0 errors 2026-08-14
- ISC-20 — `dotnet format --verify-no-changes` clean 2026-08-14
- ISC-21 — `CorpusStorageTests` green 2026-08-07
- ISC-22 — `CorpusIsOneThingTests.A_corpus_says_which_folder_it_is` green 2026-08-13, and `CorpusIsOneThingTests` in `Cli.Tests` and `CorpusImport.Tests` green 2026-08-13, each red against a signature taking both in the assemblies it covers
- ISC-23 — `TemporaryCorpusTests` green 2026-08-13, and the whole suite twenty times over: `Closing_a_corpus_leaves_another_corpus_the_connection_it_had_pooled` red against `SqliteConnection.ClearAllPools` on a different handle coming back, `No_test_empties_the_pools_of_every_corpus_in_the_process` red naming `CorpusSchemaTests.cs`, which was the second call site and the one no test had ever caught
- ISC-24 — `FixtureParsingTests` green 2026-08-07
- ISC-25 — `DeepgramFixtureTests` green 2026-08-07
- ISC-26 — `ReferenceBehaviourTests` green 2026-08-07
- ISC-27 — `CorpusImporterTests.The_corpus_it_reads_comes_out_exactly_as_it_went_in` green 2026-08-07
- ISC-28 — `CorpusImporterTests.What_is_left_behind_on_purpose_is_not_mixed_with_what_had_nowhere_to_go` green 2026-08-07
- ISC-29 — `CorpusImporterTests.Importing_again_does_not_duplicate_or_rewrite_the_derivatives` green 2026-08-07
- ISC-30 — `CorpusImporterTests.A_speaker_somebody_resolved_arrives_under_the_label_the_provider_wrote` green 2026-08-07
- ISC-31 — `CorpusImporterTests.An_imported_extraction_arrives_with_the_run_it_came_out_of` and `.A_decision_and_an_action_projected_from_it_hang_off_that_run` green 2026-08-07
- ISC-32 — `DeepgramTranscriptParserTests` green 2026-08-07
- ISC-33 — `CliWalkthroughTests.A_response_becomes_a_meeting_that_renders_rebuilds_and_is_found_again` green 2026-08-07
- ISC-34 — `CliWalkthroughTests.The_same_response_imported_twice_is_one_meeting`, `CorpusImporterTests.Importing_the_same_corpus_twice_imports_it_once` — which renames the folder between the two runs — and `.Importing_again_does_not_duplicate_or_rewrite_the_derivatives` green 2026-08-07
- ISC-35 — `CorpusRebuildTests.Deleting_every_derived_row_and_projecting_again_leaves_every_other_table_as_it_was` green 2026-08-07, which holds the classifications and the speaker assignments a person edited as well as the rows nothing touched
- ISC-36 — `CorpusRebuildTests.Rebuilding_produces_the_same_projections_and_the_same_files` and `MeetingRendererTests.Rendering_again_leaves_the_sources_alone_and_produces_the_same_files` green 2026-08-07
- ISC-37 — `CorpusRebuildTests.A_claim_cannot_cite_a_turn_the_meeting_never_had` green 2026-08-07
- ISC-38 — `CorpusRebuildTests.A_claim_still_points_at_the_turn_it_came_from` green 2026-08-07
- ISC-39 — `DurableWriteTests.A_source_is_never_written_over` green 2026-08-07
- ISC-40 — `DurableWriteTests.A_derivative_is_replaced_and_stays_one_row` green 2026-08-07, and `ArtifactsTests.Which_kinds_a_second_write_may_replace` for which kinds those are
- ISC-41 — `DurableWriteTests.A_write_cut_while_its_content_is_produced_leaves_nothing_at_all` green 2026-08-07
- ISC-42 — `DurableWriteTests.What_is_recorded_is_the_hash_of_the_bytes_that_were_written` green 2026-08-07
- ISC-43 — `DurableWriteTests.Another_meetings_folder_is_not_somewhere_this_meeting_may_write` green 2026-08-07
- ISC-44 — `DurableWriteTests.A_write_that_calls_a_path_something_it_is_not_is_refused_before_the_file_moves` green 2026-08-13: a card addressed at `deepgram.json` is refused and the paid bytes are still there afterwards. `ArtifactsTests.The_manifest_is_the_only_source_a_second_write_may_replace` holds the exception to one kind
- ISC-45 — `ArtifactRestoreTests.Bytes_no_row_of_this_corpus_describes_are_refused_and_nothing_is_written` and `.Bytes_the_corpus_records_elsewhere_do_not_land_where_another_row_is_missing` green 2026-08-13; the second is the one worth having, since bytes the corpus does know are the case where a wrong path is reachable at all
- ISC-46 — `ArtifactRestoreTests` and `MeetingIntakeTests.A_meeting_whose_response_is_gone_gets_it_back_when_the_same_bytes_are_filed_again` green 2026-08-13, the second red with the restore taken out of the intake: `RenderException` naming the response the row points at. `CommandLineTests.A_paid_file_the_corpus_lost_is_put_back_from_the_bytes_it_already_describes` deletes the paid response, sees `check` refuse, restores from the original and gets `Sound` out of `check --verify-contents`
- ISC-47 — `CorpusIntegrityTests.A_row_pointing_at_a_meeting_that_is_not_there_fails_and_names_the_table` green 2026-08-07
- ISC-48 — `MeetingRendererTests.A_name_and_a_correction_reach_the_transcript_and_not_the_stored_turns` green 2026-08-07
- ISC-49 — `TurnsTests.A_turns_confidence_is_the_mean_of_its_parts_weighted_by_their_length` green 2026-08-07
- ISC-50 — `MeetingManifestTests.Filing_a_response_leaves_a_card_recorded_as_a_source`, `.Filing_again_writes_the_card_as_the_corpus_now_says_it_is`, `.A_card_that_is_gone_comes_back_when_the_response_is_filed_again`, `CorpusImporterTests.An_imported_meeting_arrives_with_the_card_that_names_it`, `CorpusRebuildTests.A_rebuild_leaves_every_meeting_with_the_card_that_names_it` — starting from a corpus with no card row at all — `.A_rebuild_brings_a_card_up_to_a_title_somebody_changed_since` and `HumanLayerTests.Renaming_a_meeting_leaves_its_card_saying_the_new_title` green 2026-08-13, the last of them reading `la daily` against `la daily del equipo` before the fix
- ISC-51 — `MeetingManifestTests.A_meeting_is_recognised_from_its_card_with_nothing_else_left` green 2026-08-13: the corpus is disposed and deleted before the card is read, so only the copied file can answer
- ISC-52 — `HumanLayerTests.A_rename_whose_card_cannot_be_written_does_not_happen_at_all` green 2026-08-13: a directory standing where the card goes makes the replace fail, and the title is read back past the tracked entity to prove the corpus kept the old one
- ISC-53 — `CommandLineTests` green 2026-08-07: `status` answers for a corpus this build has moved past, and `check` names the file the corpus claims and does not have
- ISC-54 — `HumanLayerTests.Exactly_one_person_is_the_user_of_this_install` green 2026-08-07
- ISC-55 — `HumanLayerTests.A_label_the_recording_settled_does_not_overwrite_one_a_person_resolved` green 2026-08-07
- ISC-56 — `capture` runs of 8, 12 and 24 seconds on this machine 2026-08-13: the two streams opened within 32 ms of each other and their files ended within 60 ms of each other, a difference that did not grow with length (60 ms over 8 s, 10 ms over 24 s), so it is start and stop jitter and not accumulated drift, which is ISC-66's to measure. Both files parse as IEEE float WAVs, 48 kHz 2 ch 32 bit, their data chunk ending exactly at the last byte
- ISC-57 — the same runs: `ch0 device` and `ch0 format` named 'Altavoces (High Definition Audio Device)' at 48000 Hz, 2 ch, 32 bit float, and `ch1` its microphone. `StreamFormatTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13 for the extensible format WASAPI really hands over, which reads as neither integer nor float until it is reduced
- ISC-58 — `LevelsTests` and `SourceMeterTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13; the same runs metered both sources every second, between −7.5 and −65.6 dBFS. A width no block of which could be metered is refused before a device is opened rather than on its first block, which `LevelsTests.A_format_that_could_never_be_metered_is_refused_before_anything_is_recorded` holds
- ISC-59 — `AudioDevicesTests` green 2026-08-13, and three runs 2026-08-13: `--microphone "blue yeti"` refused with exit 1 and nothing opened; a channel 1 whose file was already there refused with exit 1 after channel 0 had opened; and a channel 1 whose path could not be claimed at all — a directory standing in its place — refused with exit 1 after channel 0 was already recording, left nothing of channel 0 behind, and let the next attempt succeed once the obstacle was gone
- ISC-60 — `SharedTimelineTests.Two_fabricated_sources_come_out_as_one_pair_of_channels` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13
- ISC-61 — `SharedTimelineTests.A_packet_after_a_gap_lands_where_its_position_says` green 2026-08-13
- ISC-62 — `SharedTimelineTests.A_stretch_the_device_never_delivered_stays_a_gap_of_its_own_length` green 2026-08-13
- ISC-63 — `SharedTimelineTests.Sources_that_agree_on_nothing_come_out_at_one_rate_and_one_width` green 2026-08-13
- ISC-64 — `PacketTallyTests` green 2026-08-14, and `The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` red against a tally advancing on frames instead of positions: it read 900 ms for the second of meeting it was given. Two `capture` runs on this machine 2026-08-14, of 20 s and 14 s, covered 0:00:20 and 0:00:14 on both sources with nothing lost on any of the four
- ISC-65 — the same runs: 2015 and 1352 loopback packets reported 9 to 10 and 9 to 20 ms apart, over a loop that polls the device every 50 ms — five packets a poll, which instants stamped where the application collected them could not have spaced at all. `PacketTallyTests.Packets_are_as_far_apart_as_the_device_read_them`, `A_packet_whose_instant_the_device_would_not_vouch_for_still_counts_as_meeting` and `A_source_that_opened_with_an_unvouched_packet_still_covers_it` green 2026-08-14
- ISC-66 — `TimelineDriftTests.Two_hours_of_two_devices_that_disagree_stay_under_fifty_milliseconds_apart` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-14: two hours of a 48 kHz loopback 50 ppm slow against a 44.1 kHz microphone 200 ppm fast, each packet's instant jittered by up to a millisecond, measured on 120 markers both devices heard a minute apart. The committed assertion is the claim's 50 ms; tightening it by hand in this run put the worst of the 120 markers 0.69 ms from the other channel and 1.5 ms from where the shared clock says the sound happened, so the slack is the product's and not the measurement's. Resampling each source at its label instead of its measured rate puts the same run over 50 ms at the fourth marker and 1.8 s apart by the end. `SharedTimelineTests.A_fast_clock_is_pulled_back_throughout_and_not_at_the_end` holds the shorter case, and with the measurement disabled puts the microphone's first marker 20 ms out at 10 s. The positions and instants this is measured from are a real device's, by ISC-64 and ISC-65; a two-hour run on hardware end to end is a board task and not a second claim
- ISC-67 — `TimelineDriftTests.A_source_that_opened_late_and_runs_fast_reports_the_wait_apart_from_the_rate` green 2026-08-14: a microphone opening 250 ms late and running 2000 ppm fast reports the 250 ms as waited and 48096 Hz as its rate, neither absorbing the other, and all five markers it heard land within 5 ms of where the loopback heard them — where a quarter second read as drift would have pulled every one of them off
- ISC-68 — `SharedTimelineTests.A_device_whose_two_counters_disagree_stops_the_recording_rather_than_being_clamped` and `A_source_whose_clock_goes_backwards_stops_the_recording` green 2026-08-13
- ISC-69 — `SharedTimelineTests.A_source_that_goes_quiet_does_not_hold_the_rest_of_the_meeting` and `A_source_the_recording_left_behind_is_refused_rather_than_inserted` green 2026-08-13
- ISC-70 — `PacketTallyTests.The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` green 2026-08-14: ninety packets either way, one covering a second of meeting with a tenth of it lost and the other nine tenths of a second with nothing lost. `capture` prints the number on its own line per source, and both runs printed 0 ms
- ISC-71 — `SharedTimelineTests.A_source_that_stopped_early_does_not_cut_the_meeting_short` green 2026-08-14: a microphone that stops at 15 s of a 20 s recording leaves the recording 20 s long with 5 s reported missing, rather than ending where it stopped
- ISC-72 — `SharedTimelineTests.Handing_a_source_over_in_clumps_seconds_late_records_the_same_meeting` green 2026-08-14: the same minute delivered smoothly and in five second clumps is the same recording sample for sample, with the two deliveries first shown to differ — runs of one source of under 5 packets against over 100. `.Handing_a_source_over_later_than_the_timeline_waits_gives_that_source_up` holds the far side of the bound, where the same packets in 35 second clumps are refused
- ISC-73 — the 14 s run 2026-08-14 played a 440 Hz tone from second 2 to second 12 into the endpoint channel 0 was recording. Its WAV peaks at 0.167 over 5–6 s and is exactly zero over 0–1 s and from 12.5 s to the end, with 1352 packets arriving throughout and nothing lost, so the stretches nothing played into are the silence they were rather than the tone still standing in the device's buffer
- ISC-74 — `MeetingAudioTests.The_recording_never_carries_the_microphone_on_channel_0` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16, both ways round: with only the loopback loud the recording read back off disk peaks above half scale at position 0 of every frame and exactly zero at position 1, and with only the microphone loud it is the other way — so a build that swapped the contract and the recording together still fails one of the two. The 8 s `capture` run on this machine 2026-08-16 recorded −18.5 dBFS on channel 0 with its microphone silent, which is the source that was playing landing on channel 0 on real devices
- ISC-75 — `BlockSpoolTests` (`tests/MeetingTranscriber.Audio.Tests`) and `SpoolCommandTests` (`tests/MeetingTranscriber.Cli.Tests`) green 2026-08-15, over a file cut inside a block, one cut before its samples, a tail of zeroes and a block changed after it was written — each costing that block and none of the ones before it. `.A_block_the_disk_did_not_keep_is_dropped_and_the_ones_before_it_are_not` red against a reader that skipped the checksum. Four `capture` runs on this machine 2026-08-15 killed at 4, 7, 8 and 11 s of 60 came back through `spool` whole, nothing discarded on any of the eight sources — 341, 636, 793 and 996 blocks on channel 0, about 90 a second either way — so a block reaching the disk in one write is what a killed process leaves. 1500 bytes then taken off one of those files' tails cost the 2376 byte block they were inside and left the 792 before it
- ISC-76 — `MeetingAudioTests.Finishing_the_same_recording_twice_produces_the_same_file` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16: the same two spools finished twice are the same bytes, the same length and the same levels. On this machine 2026-08-16 an 8 s capture's `audio.wav` and the one `spool` produced from those same blocks afterwards both hashed to `03aaad46b9e9d5e96cb4c25ff52eea4f07047a31bfa6c1a7dc29184896267a6b`, 130563 frames either way
- ISC-91 — `CorpusRebuildTests.A_second_extraction_leaves_the_first_ones_state_alone_and_starts_its_own_blank` green 2026-08-07
- ISC-92 — `CorpusIntegrityTests.Search_is_the_index_answering_and_not_the_table` green 2026-08-07
- ISC-94 — `CorpusSearchTests.A_hit_carries_the_meeting_the_date_the_title_a_snippet_and_where_it_was_said` green 2026-08-07
- ISC-95 — `CorpusSearchTests.A_meeting_being_deleted_is_not_something_search_offers` green 2026-08-07
- ISC-96 — `CorpusSearchTests.Throwing_both_indexes_away_and_rebuilding_them_answers_exactly_the_same` and `CorpusIntegrityTests.Compacting_leaves_search_answering_exactly_what_it_answered_before` green 2026-08-07
- ISC-97 — `CorpusSearchTests.A_query_the_index_cannot_parse_says_so_and_names_it` green 2026-08-07
- ISC-117 — `capture --process` runs on this machine 2026-08-15. A program that played nothing itself while a process it started looped a 440 Hz tone: 10 s at −8.7 dBFS every second, covering 0:00:10 over 1002 packets 10 ms apart with nothing lost — so it is the tree and not the process. Edge and Firefox playing the same tone from a page: 6 s each at −12.0 dBFS, the name resolving to the browser process out of 17 and 10 of that name. `AudioProcessesTests` holds that resolution and `FramePositionsTests` the placement the virtual device leaves to be worked out (`tests/MeetingTranscriber.Audio.Tests`), green 2026-08-15
- ISC-118 — the same session: following a program that played nothing while another program looped the tone left channel 0 silent for all 8 s and its file loudest silent, over 802 packets with nothing lost. Eight seconds later the whole machine's loopback heard that same tone at −13.9 dBFS, so it was there to record and the followed program's file did not have it
