---
phase: climbing
progress: 37/68
updated: 2026-08-07
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
- [x] ISC-15: Anti: nothing under `tests/` names an HTTP or socket type, so no test can reach the network.
- [x] ISC-16: The build is clean under `-warnaserror` and `dotnet format --verify-no-changes` passes.
- [x] ISC-17: A meeting, a turn and a job come back off disk as the types they went in as.

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

### F2 · Deterministic core from artifacts
Why: given a `deepgram.json`, everything that does not need a microphone works — parse, store,
project, rebuild — so the paid artifact is the only input the rest of the system needs.
Board: 1 · Núcleo .NET desde artefactos
- [x] ISC-24: A Deepgram response becomes turns with their channel, speaker label and offsets.
- [x] ISC-25: Rebuilding the projections deletes only derivatives and leaves every edit a person made.
- [x] ISC-26: Anti: a claim cannot cite a turn the meeting never had, and deleting a cited turn fails rather than taking the claim along.
- [x] ISC-27: A source artifact is never written over; a derivative is replaced and stays one row.
- [x] ISC-28: A write cut off part-way leaves either nothing or the finished artifact, never a half one.
- [x] ISC-29: What is recorded for an artifact is the hash of the bytes that were actually written.
- [x] ISC-30: Anti: a meeting cannot write into another meeting's folder.
- [x] ISC-31: A corpus that is not sound fails and names the table or index that broke.
- [x] ISC-32: Compacting leaves search answering exactly what it answered before.
- [ ] ISC-33: `transcript.md` and `utterances.jsonl` render from the stored turns and re-render identically.
- [ ] ISC-34: The diagnostic CLI reports corpus state without opening the application.
- [x] ISC-66: Every table of the human layer is written through `HumanLayer`.
- [x] ISC-67: Exactly one person carries the flag naming the user of this install.
- [x] ISC-68: Anti: a speaker label a person resolved is never overwritten by one the recording settled.

### F3 · Audio engine
Why: two sources become one timeline a person can trust. This is the largest technical risk in
the product and it is settled before any UI is built on top of it.
Board: 2 · Spike y motor de audio
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
answer traces back to a turn.
Board: 6 · Conocimiento local
- [x] ISC-56: Search is the index answering, not the table.
- [ ] ISC-57: Everything search promises to find is indexed.
- [x] ISC-58: An edited classification or speaker assignment survives a full rebuild.
- [ ] ISC-59: The MCP server answers read-only over stdio and never writes.
- [ ] ISC-60: Anti: an MCP response is bounded and the request is recorded locally.

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

## Decisions

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
