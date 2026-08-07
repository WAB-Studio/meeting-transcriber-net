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
spool/<meeting_id>/      source     while the blocks are the only recoverable copy
```

`manifest.json` is a source even though the database could regenerate it. It exists for the case
where the database is gone, and a recovery card that can only be rebuilt from the thing it is
meant to replace is no recovery card at all.

## In the database

Derived tables — `utterances`, `summaries`, `decisions`, `action_items`, and both FTS5 indexes.
They are projections of `deepgram.json` and the accepted extractions.

Derived means derived all the way down, so an action's row holds only what the extraction
proposed. Where it stands and who owns it are moved by a person and live in
`action_item_progress`, keyed on the extraction run and the action's position inside it — never on
an action's id, because a rebuild mints new ones. Deleting every `action_items` row and projecting
the same extraction again puts each action back under the state it was left in.

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

Runs and jobs — `capture_runs`, `processing_jobs`, `transcription_runs`, `extraction_runs` — are
sources too. They are the record of what was charged and what state a restart found.

## Where the rule is enforced

`artifacts.origin` is `'source'` or `'derived'`, and a CHECK constraint ties each artifact kind to
the side it belongs on. A migration that moves a kind across the line is a deliberate act, not a
typo that slips through.

On disk it is the same line, enforced by the write itself: `StagedArtifact.Commit` puts a source in
place with the replace refused, so the filesystem is what stops a paid response being overwritten
rather than a check above it that a second writer could pass between the looking and the moving. A
derivative replaces what was there, which is what re-rendering is, and keeps the one row the backup
and the rebuild walk.

The two cannot be written together, so the order decides which one is wrong first when the power
goes: the file lands, then the row. A corpus that says less than it holds is recovered by looking;
one that says more is a corpus that lies when it is read. `ArtifactReconciler` is what looks —
unfinished writes, which it may delete because they were never artifacts, and files with no row,
which it never touches because one of them may be the only copy of something that was paid for.

Two consequences worth stating plainly:

- A rerender never touches `deepgram.json` or an earlier extraction. A new extraction gets a new
  id and the previous one stays.
- Names and corrections are applied when rendering. They are never written into the raw response,
  because that response is what a citation is checked against.
