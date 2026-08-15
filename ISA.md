---
phase: climbing
progress: 89/127
updated: 2026-08-14
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
- [x] ISC-112: Two sources become one aligned pair of channels from fabricated packets alone, with no audio device opened.
- [x] ISC-113: A packet lands where its device position puts it, not immediately after the packet before it.
- [x] ISC-114: A stretch a source never delivered stays a gap of the length its device positions say, rather than being closed up.
- [x] ISC-115: Whatever rate, width and channel count a source arrives with, it leaves the timeline at one internal rate and one sample format.
- [x] ISC-116: Anti: a source whose clock runs fast or slow is pulled back gradually, so alignment holds throughout the recording and not only at its end.
- [x] ISC-117: A source that goes quiet does not hold the rest of the meeting, and the whole of it is reported as missing.
- [x] ISC-118: Anti: a device whose frame counter and clock disagree by more than any crystal drifts stops the recording rather than being clamped into looking aligned.
- [x] ISC-119: A block leaving the capture carries the frame position its device reported for it, and what a source covers is counted from those and never from how many bytes reached the application.
- [x] ISC-120: A block leaving the capture carries the instant its device reported reading that position at, and never the moment the application collected it.
- [x] ISC-121: A recording a device dropped audio from says how much it lost, rather than coming back shorter with nothing saying so.
- [x] ISC-122: Anti: a stretch of a meeting nobody played into is recorded as the silence it was, and not as whatever the device's buffer last held.
- [x] ISC-126: Two hours of packets from two devices whose crystals disagree leave the channels under 50 ms apart at every marker both of them heard, and not only at the last one.
- [x] ISC-123: A source that opened late reports how long the recording waited for it, apart from the rate its clock ran at.
- [x] ISC-127: Anti: a constant offset between the two devices is never taken for drift and corrected away, so audio that was already in the right place stays there.
- [x] ISC-124: Anti: how late a source's packets are handed over cannot change the recording, up to the half minute after which the timeline gives that source up.
- [x] ISC-125: A recording is as long as the last packet of either source, so a device that stopped inside that half minute does not cut the end off the meeting.
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
- ISC-108 — `capture` runs of 8, 12 and 24 seconds on this machine 2026-08-13: the two streams opened within 32 ms of each other and their files ended within 60 ms of each other, a difference that did not grow with length (60 ms over 8 s, 10 ms over 24 s), so it is start and stop jitter and not accumulated drift, which is ISC-35's to measure. Both files parse as IEEE float WAVs, 48 kHz 2 ch 32 bit, their data chunk ending exactly at the last byte
- ISC-109 — the same runs: `ch0 device` and `ch0 format` named 'Altavoces (High Definition Audio Device)' at 48000 Hz, 2 ch, 32 bit float, and `ch1` its microphone. `StreamFormatTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13 for the extensible format WASAPI really hands over, which reads as neither integer nor float until it is reduced
- ISC-110 — `LevelsTests` and `SourceMeterTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13; the same runs metered both sources every second, between −7.5 and −65.6 dBFS. A width no block of which could be metered is refused before a device is opened rather than on its first block, which `LevelsTests.A_format_that_could_never_be_metered_is_refused_before_anything_is_recorded` holds
- ISC-111 — `AudioDevicesTests` green 2026-08-13, and three runs 2026-08-13: `--microphone "blue yeti"` refused with exit 1 and nothing opened; a channel 1 whose file was already there refused with exit 1 after channel 0 had opened; and a channel 1 whose path could not be claimed at all — a directory standing in its place — refused with exit 1 after channel 0 was already recording, left nothing of channel 0 behind, and let the next attempt succeed once the obstacle was gone
- ISC-112 — `SharedTimelineTests.Two_fabricated_sources_come_out_as_one_pair_of_channels` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13
- ISC-113 — `SharedTimelineTests.A_packet_after_a_gap_lands_where_its_position_says` green 2026-08-13
- ISC-114 — `SharedTimelineTests.A_stretch_the_device_never_delivered_stays_a_gap_of_its_own_length` green 2026-08-13
- ISC-115 — `SharedTimelineTests.Sources_that_agree_on_nothing_come_out_at_one_rate_and_one_width` green 2026-08-13
- ISC-116 — `SharedTimelineTests.A_fast_clock_is_pulled_back_throughout_and_not_at_the_end` green 2026-08-13; with the measurement disabled the same probe puts the microphone's first marker 20 ms out at 10 s and reports 48000 Hz for a device running at 48096
- ISC-117 — `SharedTimelineTests.A_source_that_goes_quiet_does_not_hold_the_rest_of_the_meeting` and `A_source_the_recording_left_behind_is_refused_rather_than_inserted` green 2026-08-13
- ISC-118 — `SharedTimelineTests.A_device_whose_two_counters_disagree_stops_the_recording_rather_than_being_clamped` and `A_source_whose_clock_goes_backwards_stops_the_recording` green 2026-08-13
- ISC-119 — `PacketTallyTests` green 2026-08-14, and `The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` red against a tally advancing on frames instead of positions: it read 900 ms for the second of meeting it was given. Two `capture` runs on this machine 2026-08-14, of 20 s and 14 s, covered 0:00:20 and 0:00:14 on both sources with nothing lost on any of the four
- ISC-120 — the same runs: 2015 and 1352 loopback packets reported 9 to 10 and 9 to 20 ms apart, over a loop that polls the device every 50 ms — five packets a poll, which instants stamped where the application collected them could not have spaced at all. `PacketTallyTests.Packets_are_as_far_apart_as_the_device_read_them`, `A_packet_whose_instant_the_device_would_not_vouch_for_still_counts_as_meeting` and `A_source_that_opened_with_an_unvouched_packet_still_covers_it` green 2026-08-14
- ISC-121 — `PacketTallyTests.The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` green 2026-08-14: ninety packets either way, one covering a second of meeting with a tenth of it lost and the other nine tenths of a second with nothing lost. `capture` prints the number on its own line per source, and both runs printed 0 ms
- ISC-122 — the 14 s run 2026-08-14 played a 440 Hz tone from second 2 to second 12 into the endpoint channel 0 was recording. Its WAV peaks at 0.167 over 5–6 s and is exactly zero over 0–1 s and from 12.5 s to the end, with 1352 packets arriving throughout and nothing lost, so the stretches nothing played into are the silence they were rather than the tone still standing in the device's buffer
- ISC-126 — `TimelineDriftTests.Two_hours_of_two_devices_that_disagree_stay_under_fifty_milliseconds_apart` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-14: two hours of a 48 kHz loopback 50 ppm slow against a 44.1 kHz microphone 200 ppm fast, each packet's instant jittered by up to a millisecond, measured on 120 markers both devices heard a minute apart. The committed assertion is the claim's 50 ms; tightening it by hand in this run put the worst of the 120 markers 0.69 ms from the other channel and 1.5 ms from where the shared clock says the sound happened, so the slack is the product's and not the measurement's. Resampling each source at its label instead of its measured rate puts the same run over 50 ms at the fourth marker and 1.8 s apart by the end
- ISC-123 — `TimelineDriftTests.A_source_that_opened_late_and_runs_fast_reports_the_wait_apart_from_the_rate` green 2026-08-14: a microphone opening 250 ms late and running 2000 ppm fast reports the 250 ms as waited and 48096 Hz as its rate, neither absorbing the other
- ISC-127 — the same test green 2026-08-14 for the other half: all five markers the late microphone heard land within 5 ms of where the loopback heard them, where a quarter second read as drift would have pulled every one of them off
- ISC-124 — `SharedTimelineTests.Handing_a_source_over_in_clumps_seconds_late_records_the_same_meeting` green 2026-08-14: the same minute delivered smoothly and in five second clumps is the same recording sample for sample, with the two deliveries first shown to differ — runs of one source of under 5 packets against over 100. `.Handing_a_source_over_later_than_the_timeline_waits_gives_that_source_up` holds the far side of the bound, where the same packets in 35 second clumps are refused
- ISC-125 — `SharedTimelineTests.A_source_that_stopped_early_does_not_cut_the_meeting_short` green 2026-08-14: a microphone that stops at 15 s of a 20 s recording leaves the recording 20 s long with 5 s reported missing, rather than ending where it stopped
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
