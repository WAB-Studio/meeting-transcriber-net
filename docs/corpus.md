# Sources and derivatives

Every file and every row of the corpus is one of two things, and the difference decides what a
backup has to carry, what deletion is allowed to touch, and what a rebuild may throw away.

**A source cannot be recovered from anything left on the machine.** Some were paid for, some were
typed by a person, and none of them is ever rewritten in place.

**A derivative can be produced again from the sources.** Deleting all of it and re-rendering is a
safe operation, and it has to stay that way.

## On disk

```text
meetings/<meeting_id>/
  manifest.json          source     recovery card, readable without the database
  audio.wav              source     if the user's retention policy keeps it
  deepgram.json          source     paid, immutable
  extractions/<id>.json  source     one file per accepted extraction, older ones kept
  transcript.md          derived
  utterances.jsonl       derived
  summary.md             derived
spool/<meeting_id>/
  manifest.json          source     what the recording said about itself when it started
  changes.jsonl          source     what somebody moved while it was recording, if anything
  <channel>.blocks       source     while the blocks are the only recoverable copy
```

`changes.jsonl` is a source for the reason the card beside it is, and it is the half the card
cannot hold: the card is written once and says what each channel opened on, so a channel somebody
moved to the whole machine an hour in is only written down here. Losing it leaves a folder saying
its channel 0 followed one program when most of what is in the file is everything the machine
played. Most recordings never have one.

**Two files are called `manifest.json` and they are not the same card.** The one in a meeting's
folder is produced from the corpus every time and may be replaced; the one beside a spool is
written once when the devices open and never again, because there is no corpus to produce it from
— it is the only record of which meeting a folder of blocks belongs to and what was on each
channel. Neither is deletable and neither is rebuildable, and a backup that carried one and not
the other would restore a recording nobody could attach to a meeting.

`manifest.json` is a source even though the database could regenerate it. It exists for the case
where the database is gone, and a recovery card that can only be rebuilt from the thing it is
meant to replace is no recovery card at all.

It carries five fields — the meeting's id, when it started, its profile, its language and its
title — and that is the whole of it. They answer the one question the folder cannot answer for
itself: the files in it are named after what they hold, so a card listing them would repeat the
directory listing, and everything else somebody typed is what the backup is for. It is always those
five keys, with a null title where a meeting has no name, so nothing reading these meets a second
shape of the file on the one meeting nobody got round to naming. `MeetingManifest` both writes it
and reads it back, because a card this product could only write is one it could not actually
recover from.

Filing a meeting writes its card, and so does a rebuild — every meeting, every time. That second
one is what makes this a promise about a folder rather than about a moment: intake only ever
reaches the meeting being filed, so a meeting that predates the card gets its card from `rebuild`
and from nothing else. A title somebody changed is no longer one of those: the screen a meeting is
read from is the only thing in the application that renames one, and it writes the card in the same
transaction it writes the row — which is what ISC-52 asks of every change that has to reach both.

It is also the one source that may be written over, which is the distinction the rest of this
section turns on: *source* decides what a backup carries and what a deletion spares, and it is a
separate question from whether a second write destroys anything. For every other source it does —
those bytes cannot be obtained again. The card is produced from the `meetings` row every time, so
filing a meeting again writes the card the corpus now describes, and a card somebody deleted comes
back. Making it write-once would not be the careful choice: it would pin whatever the card said
first and leave a meeting whose card was never written without one for good.

## In the database

Derived tables — `utterances`, `summaries`, `decisions`, `action_items`, `open_questions`, and both
FTS5 indexes. They are projections of `deepgram.json` and the accepted extractions.

Derived means derived all the way down, so an action's row holds only what the extraction
proposed. Where it stands and who owns it are moved by a person and live in
`action_item_progress`, keyed on the extraction run and the action's position inside it — never on
an action's id, because a rebuild mints new ones. Deleting every `action_items` row and projecting
the same extraction again puts each action back under the state it was left in.

That is the rule for every projected row somebody can annotate, not only for actions, so a
decision and an open question carry the position too — whether a decision still stands is a
person's word and has the same problem to solve. The database refuses two decisions, two actions or
two open questions at one position of one run: two would not be a visible error, they would be a
note that reads against either of them, and the writer projecting the extraction is the one with no
way to notice. The position counts inside its own list — an extraction returns the three
separately — so the first decision and the first action of one run are both at position zero, and
what tells them apart is the table they landed in.

A citation names its turn by the meeting and the turn's position in it, never by a turn's id — for
the same reason, one step further in. The ids belong to the projection, so a rebuild deletes them
and hands out new ones, and a claim reprojected from the extraction that wrote one down would land
on a turn that no longer exists. The pair is what projecting the same `deepgram.json` reproduces.
The meeting is not stored twice: a citation reads the one on the claim that carries it, so citing a
turn of another meeting has nowhere to be written.

A citation names its turn without cascading off it. It used to, which made deleting utterances
take every decision and action citing them and say nothing about it. Deleting turns on their own
now fails, and a meeting still deletes whole: the turns and the claims go in one statement, and
that is when the constraint is checked.

`CorpusRebuild.Run` is how a rebuild gets past that without deleting the claims. Every meeting is
reprojected inside one transaction with `PRAGMA defer_foreign_keys`, so the turns go and come back
under the same positions while the claims stay where they are, and the check happens at the commit.
That is not a way around the constraint — it is what makes it useful: a rebuild that renumbered a
turn fails at the end instead of quietly rewriting what every stored claim points at. Summaries,
decisions, actions and open questions are left alone rather than reprojected, because nothing reads
an accepted extraction back into rows yet; when something does, it becomes a step in there.

The FTS5 indexes are external content keyed on rowid, and a `VACUUM` may renumber the rowids of the
tables they index, after which search answers with the wrong rows and says nothing. So nothing
vacuums the corpus directly: `CorpusIntegrity.Compact` does both halves, and the reason it exists is
that today's SQLite happens not to renumber them — a bare `VACUUM` looks correct every time anybody
tries it, and is one release away from not being.

## Checking a corpus

`CorpusIntegrity.Check` reports what is wrong instead of answering yes or no, and
`CorpusIntegrity.Ensure` throws the same list. Anything that copies the corpus runs it first: a
backup taken of a corpus that was already wrong is a backup of being wrong, restored later with
confidence.

It covers three things — `PRAGMA integrity_check` for the file, `PRAGMA foreign_key_check` for
orphans, and each FTS5 index against the table it indexes. The third one is easy to write and have
do nothing: FTS5's bare `VALUES ('integrity-check')` only asks whether the index is internally
consistent, which an index built against the wrong rows is. The comparison against the content table
is `VALUES ('integrity-check', 1)`, and that is the only form that catches this.

A migration that changes `utterances` or `summaries` costs the same and one thing more. SQLite
cannot alter a constraint in place, so EF drops the table and rebuilds it: the rows come back under
new rowids, and the triggers go, because a trigger belongs to its table and not to the schema the
model tracks. Both have to be put back by hand, and not in that same migration — EF emits raw SQL
before a rebuild it still has pending, so the statements would run against the table about to be
dropped.

Everything else is a source, and the part that matters most is the **human layer**: `nodes`,
`meeting_nodes`, `templates`, `people`, `affiliations`, `meeting_people`, `speaker_assignments`,
`terminology_corrections`, `action_item_progress`, and the titles, context notes and
classifications on `meetings`. None of it is inferable from any artifact, so a backup that copies
only the files loses it.

`HumanLayer` writes all of it, and the reason it exists rather than a page of `context.Add` is the
two rules that cannot be constraints: exactly one person is the user of this install, and a speaker
label somebody resolved is not overwritten by one the recording settled. The first is two rows
changing together, which a unique index refuses halfway through; the second is the same row written
twice, of which the database only ever sees the second.

Runs and jobs — `capture_runs`, `processing_jobs`, `transcription_runs`, `extraction_runs` — are
sources too. They are the record of what was charged and what state a restart found.

## Where the rule is enforced

`artifacts.origin` is `'source'` or `'derived'`, and a CHECK constraint ties each artifact kind to
the side it belongs on. A migration that moves a kind across the line is a deliberate act, not a
typo that slips through.

On disk the line the write enforces is the other one — whether the corpus can produce this artifact
again. `Artifacts.MayBeReplaced` answers it, and `StagedArtifact.Commit` puts anything it says no to
in place with the replace refused, so the filesystem is what stops a paid response being overwritten
rather than a check above it that a second writer could pass between the looking and the moving. A
derivative replaces what was there, which is what re-rendering is, and keeps the one row the backup
and the rebuild walk. The manifest is the one artifact the two lines fall on opposite sides of.

Replaceability is decided by the kind the caller names, and the destination is named by the same
caller, so on their own the two say nothing about each other — a manifest addressed at
`deepgram.json` would put a regenerable file over a paid one. What closes that is the row: a path
holds one kind for as long as the corpus does, and a write that calls it something else is refused
before the file moves, which is the only moment the refusal is still worth anything.

The two cannot be written together, so the order decides which one is wrong first when the power
goes: the file lands, then the row. A corpus that says less than it holds is recovered by looking;
one that says more is a corpus that lies when it is read. `ArtifactReconciler` is what looks —
unfinished writes, which it may delete because they were never artifacts, and files with no row,
which it never touches because one of them may be the only copy of something that was paid for.

## Putting a lost file back

The reconciler reports a row whose file is gone and stops there. `ArtifactRestore` is what a person
runs when they still have the bytes, and the hash the corpus already recorded is its whole
authority: the bytes go back only where the corpus already says those exact bytes are. Nothing is
accepted for being the right size, carrying the right name or arriving from the right folder — which
makes a different file under a paid response's row impossible rather than guarded against, and means
the command needs no meeting and no path, because a loose `deepgram.json` in a backup folder does
not say which meeting it is and the corpus does.

Nothing there takes a row from its caller, and that is the same rule as the one above it rather than
a second one. A method handed a row and a stream compares two things one caller supplied, and
`StagedArtifact.Commit` writes the hash of what it wrote onto the row it finds — so a fabricated
hash with bytes to match would land under a real path and leave the corpus saying it had always been
those bytes. The row is looked up from the bytes, for the reason the kind is looked up from the
path.

It writes only where there is no file. A file that is there and is not what its row describes is
damage, and destroying it could destroy the only copy of something: the path is named, `check` is
what says which of the two it is, and deleting it and running the restore again is a person's
deliberate act.

Filing a response whose file is gone does the same thing on the way past, because that is how this
gets noticed — the check says the corpus claims a file it has not got, and the obvious move is to
hand the original over again. The bytes are put back before anything is derived from them, and only
because they are the ones the row records, which is what identified the meeting in the first place.

Two consequences worth stating plainly:

- A rerender never touches `deepgram.json` or an earlier extraction. A new extraction gets a new
  id and the previous one stays.
- Names and corrections are applied when rendering. They are never written into the raw response,
  because that response is what a citation is checked against.
