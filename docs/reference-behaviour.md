# What the Python system knows

The Python system is the functional reference, not a runtime. It ran against real meetings for
long enough to learn things that are not visible from a provider's documentation, and those are
worth more than its code. This is that list: what it guarantees, why, whether .NET keeps it, and
where .NET does something else on purpose.

Everything marked **ported** is a test in `MeetingTranscriber.Domain.Tests` running offline
against `tests/fixtures/deepgram/`. Nothing here needs the network, credits or the user's corpus.

## Turns

**A turn, not an utterance, is the unit worth citing.** — ported, `Turns.Group`

A provider splits a sentence across utterances freely: "Con", "Creo", "Eso todavía no lo tengo
hecho". A claim pointing at one of those lands on something that supports nothing. Consecutive
speech from one speaker merges into one turn, joined by a single space, and that is what a
citation anchors on. The Python renderer carries the same thing as `turn_at` on every row of
`utterances.jsonl`, and its summary validation refuses a citation whose timestamp is not the
start of a turn.

**A multichannel response is not in conversation order.** — ported

The provider returns utterances grouped by channel, so the raw order can be one whole side of the
call followed by the other, and a transcript rendered in that order reads as two monologues.
Sorting by start is what rebuilds the back and forth, and it is stable, so speech sharing an
instant keeps the order it arrived in.

Today's responses happen to come back interleaved by time — every committed fixture is already in
order. The sort stays anyway: nothing announces the day that stops being true, and a test pins
that grouping a channel-ordered response gives the same turns as grouping the real one.

**Speech with nothing in it is not a turn.** — ported

A blank or whitespace-only transcript is dropped before grouping, so it neither becomes a turn nor
moves the start of the one it fell inside.

## Channels

**An empty channel is a bill, not an error.** — ported as a fixture

A channel whose transcript is `""` was transcribed and said nothing. The usual cause is the wrong
language: nova-3 against a language the audio is not in returns an empty string with confidence
0.0, no error, and charges in full. `two-channel-silent-me.json` is that case, and the meeting
still projects from the channel that did carry speech.

**Channel 1 is the user and needs no diarization.** — already the contract

`Domain/Audio/` fixes it: channel 0 is the meeting, channel 1 is the user. The Python renderer
applies the user's name to channel 1 without consulting the diarizer, which is the same statement.

## What .NET does differently, on purpose

**Turns merge on the channel and the label, not on the label alone.**

The Python renderer resolves the speaker to a display name first — channel 1 becomes "Carlos",
channel 0 becomes "Speaker 2" — and merges on that string. It is correct only because the channel
is already folded into the name. .NET keeps the provider's label and the channel apart, because a
name applied to stored evidence is what stops the evidence being comparable to the raw response.
So merging keys on the pair. Merging on the label alone would weld the two sides of a call into
one turn, since a provider numbers speakers within a channel and both sides start at zero.

**A turn never ends before it starts.**

Python takes the end of the last merged utterance. .NET takes the later of the two ends, so
overlapping speech cannot produce a span a renderer is unable to draw.

**Speaker labels are stored raw, and names are applied when rendering.**

Python writes `Speaker 1` for the provider's speaker 0 — one-based, because it is a label a person
reads — and that display string ends up in `utterances.jsonl`, which is also what its citations
resolve against. It gets away with it by never substituting names into that file: only
`transcript.md` gets real names, so the jsonl stays the ground truth.

.NET keeps the two apart at the storage layer instead: `utterances.speaker_label` holds what the
provider said, `speaker_assignments` maps a label to a person, and rendering joins them. One
consequence is that the one-based display is a rendering concern here and lives nowhere in the
domain.

**Byte-identical output is not a goal.** What the tests check is domain invariants. Where the
rendered text differs from the Python system's, the difference is intentional and gets written
down here rather than chased.

## Still to settle

Neither of these is answered by the Python system, so neither is decided here:

- **What a speaker label looks like.** The fixtures are read as `speaker_0`, which is only what
  the test reader does; the stored form is the parser's to fix, and it is the key
  `speaker_assignments` hangs off, so it is a contract and not a formatting choice.
- **What a merged turn's confidence means.** `utterances.confidence` exists and grouping does not
  fill it. Python has per-utterance confidence and no per-turn number, so there is nothing to
  port: the lowest of the merged parts, a weighted mean and nothing at all are all defensible, and
  the projection has to pick one deliberately.

## Not ported, and where it goes instead

- **Terminology corrections** are applied to derived views only, longest alias first, matching
  whole words and never inside one, and the paid response is never touched. In .NET they are rows
  of `terminology_corrections` applied when rendering — the rule is the same, its home is not, so
  it is tested with the renderer.
- **Citation validation** — a claim whose timestamp is not a turn start is refused, and so is a
  speaker label belonging to nobody in that recording. .NET moves the anchor from a timestamp to
  the meeting and the turn's position, which is what survives a rebuild; the check itself belongs
  to extraction.
- **Markdown and JSONL shape** — one `##` heading per turn so a chunker splits on speaker
  boundaries, frontmatter a person can read, UTF-8 without a BOM. That is the renderer's task.
