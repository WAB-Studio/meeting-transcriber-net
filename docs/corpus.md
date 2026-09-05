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
  capture.mark           neither    held from the press until the last device is let go of, and empty
  reading.mark           neither    held while a list, a keep or an export reads these blocks through, and empty
  saving.mark            neither    held while a finish is writing the meeting down, and empty
spool/.removing-<meeting_id>/
  <meeting_id>/          neither    a recording somebody threw away, between the move and the delete
```

**The three marks are neither, and that is not a mistake in the table.** All three hold no bytes and
nothing ever reads whether one is there. What each means is carried by a process having it open, so
a backup that restored one would restore a fact that stopped being true when that process ended, and
one that dropped it loses nothing. Nothing clears the one a crashed save, a crashed capture or a
crashed read leaves, because a file nothing holds already reads as no save, no capture and no read.

**`spool/.removing-<meeting_id>/<meeting_id>/` is a recording somebody threw away, part-way out, and
is neither too.** Throwing a recording away renames its folder into that one and then removes the
copy, so a discard something is still reading is refused with the recording exactly as it was rather
than emptied as far as the first held file. For the instant between those two steps the whole
recording is under that path, which is what a backup sweeping `spool/` would find. A backup that
skipped it loses nothing: what is in it is a recording whose owner already said to throw it away.

One still there after a start is a discard that did not finish — a machine that died inside one, or
a delete that stayed refused. It holds whatever the delete had not reached yet, so it may be the
whole recording or a part of one. **Nothing in the product ever cleans it**: the sweep of folders
nothing was recorded into names it and removes nothing, and no second discard of that recording is
reachable, because the recording is no longer under a name anything offers. Deleting it by hand is
safe, and nothing volunteers that it is there — `check` lists what is in it among the corpus's
spooled files, and otherwise it is visible only to somebody looking at the folder.

`capture.mark` is taken by whatever makes the folder — in `MeetingRecordings.Open` for a meeting
recorded into a corpus, and in `CaptureSession.Start` for a capture into a folder somebody named at
a prompt — and let go of with the last device, the press handing it on to the session in between. It
covers the one stretch a folder holding a recording is indistinguishable from a folder holding
nothing: between the folder being made and the first spool file landing in it, there is nothing in
it at all. After that the blocks say it themselves. It is what lets a start sweep away the meeting a
press left behind when the recording never started, without ever sweeping one that is starting.

`reading.mark` is held for the whole of a read and covers the window the file system cannot see on
its own. Reading a recording through is one source at a time, and the card and the changes are held
by nobody, so between the two sources there is an instant in which somebody is reading and nothing
in the folder is open at all — long enough for a discard typed in another window to rename the
folder out from under the read. One file held across the whole pass closes that, and it is what
turns a discard arriving during a read into a refusal that says somebody is reading the recording.
A listing takes it too, because a listing reads every block of every waiting recording.

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

## The two files that live beside a destination

A write in flight and a replace in flight each leave a file next to where the artifact goes, and
they are opposite things in the same shape. Which one a file is is its suffix, and `sweep` is the
command that acts on the difference.

| Suffix | What it holds | What a sweep does |
| --- | --- | --- |
| `.partial` | bytes on their way in, which were never an artifact | deletes it |
| `.superseded` | the old copy of a derived file, moved aside so a set of them could be replaced together | deletes it once the file that replaced it is back; until then, leaves it and `check` reports it |

`.partial` is deleted on sight — no age, no second thought — and that licence rests entirely on the
middle column: nothing is lost, because nothing was ever there. So the copy a replace sets aside
must not wear it. That copy *was* an artifact, and between the emptying and the moves it is the only
copy of one; a sweep taking it there turns a replace that refused and put everything back into a
derived file that quietly stopped existing.

The other half of the same licence is a write somebody is still making, which is spelled `.partial`
because that is exactly what it is. `sweep` is run from a terminal, so it runs beside a working
application as a matter of course. What separates the two is a handle: a staged artifact holds its
temporary open from the moment it is created until the line before the rename, so the delete is
refused, the file is left and the command says which ones it left. A temporary nothing holds is a
dead write; one something holds is a live one. That is the liveness test, and there is no clock in
it — but it is the artifact write's own, and the `.partial` files the audio engine writes beside a
recording it is materialising are held only while something is reading or writing them.

A `.superseded` file on disk means the machine stopped inside a replace, or the tidy-up at the end
of one was refused. Which of those it is is not a guess: the copy is named for the destination it
came out of, so ask whether that destination is on disk. It is back → the moves ran, the file that
replaced it is the one anybody wants, and the copy is what a finished replace did not get to remove.
It is missing → the copy is the last one of that derived file, and it may be the file a put-back is
on its way to putting back.

`sweep` takes the first and never the second, which is the only thing separating a corpus that comes
back from a crashed render on its own from one where `check` exits non-zero until somebody deletes a
file by hand. There is no clock in the question and no age: a copy is put back only into a
destination the same run of vacates emptied, and that run gives up before anything is moved in, so
a destination standing where a copy came out of means nothing is ever coming back for it.

What is left for a person is the second case, and it is one step: `rebuild` produces the derived
file again from the sources, and the next `sweep` clears the copy.

## Two spellings of one path

The corpus is a folder on a Windows filesystem, so `transcript.md` and `Transcript.md` are one file
— while the unique index on `artifacts.relative_path` is SQLite's default binary collation and sees
two values. Two writes spelled differently would move to one destination and both be recorded: two
rows over one file's bytes, which is the state the whole write sequence exists to make unreachable.

`CorpusFiles.PathComparer` is the answer for everything that asks the question **in memory** — today
the guard refusing a set that names one destination twice, and the reconciler matching what it
scanned against what the rows say. Comparing the stored strings is enough because
`CorpusFiles.EnsureBelongsTo` has already refused every other way two spellings could differ: a
backslash, a `.` or a `..`, a rooted path.

What it does not reach is a lookup that asks the database, which is how a write finds the row
already at its path. That one is still binary, so two spellings across two separate writes would
still make two rows over one file. Closing it means a collation on the column, which is a migration,
and nothing has needed it: every stored path is composed from a meeting id and a constant, so no
caller produces a second spelling. The day one does, that is the change.

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
