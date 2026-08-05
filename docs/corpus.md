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

The FTS5 indexes are external content keyed on rowid, and a `VACUUM` may renumber the rowids of
the tables they index. Whatever vacuums the corpus rebuilds them afterwards — `INSERT INTO
utterances_fts (utterances_fts) VALUES ('rebuild')`, and the same for `summaries_fts` — or search
answers with the wrong rows and says nothing.

Everything else is a source, and the part that matters most is the **human layer**: `people`,
`projects`, `meeting_participants`, `speaker_assignments`, `terminology_corrections`, and the
titles and classifications on `meetings`. None of it is inferable from any artifact, so a backup
that copies only the files loses it.

Runs and jobs — `capture_runs`, `processing_jobs`, `transcription_runs`, `extraction_runs` — are
sources too. They are the record of what was charged and what state a restart found.

## Where the rule is enforced

`artifacts.origin` is `'source'` or `'derived'`, and a CHECK constraint ties each artifact kind to
the side it belongs on. A migration that moves a kind across the line is a deliberate act, not a
typo that slips through.

Two consequences worth stating plainly:

- A rerender never touches `deepgram.json` or an earlier extraction. A new extraction gets a new
  id and the previous one stays.
- Names and corrections are applied when rendering. They are never written into the raw response,
  because that response is what a citation is checked against.
