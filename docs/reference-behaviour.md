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
Sorting by start is what rebuilds the back and forth.

Today's responses happen to come back interleaved by time — every committed fixture is already in
order. The sort stays anyway: nothing announces the day that stops being true, and a test pins
that grouping a channel-ordered response gives the same turns as grouping the real one.

**A silence of more than two seconds ends a turn.** — .NET only, `Turns.MaxSilence`

Python merges consecutive speech from one speaker however long the gap, and it gets away with it
because a citation there is validated against a timestamp. Anchored on a position instead, an
oversized turn stops both halves of the contract from checking anything: every timestamp inside it
is "the start of a turn" and every quote "belongs to" it. Measured over the four fixtures, a
meeting where one side never interrupts came back as one turn holding the whole recording, with a
103-second silence welded into it.

Two seconds is a convention, not a measurement. Conversational pause lengths are lognormal with a
long tail and no valley in the histogram to cut at, so no threshold falls out of the data: the
anchors available are ~180 ms (the technical floor that keeps a stop consonant from splitting a
word), ~1 s (conversation analysis's "standard maximum silence"), and 2–5 s (a lapse, where the
floor is treated as abandoned). One second would split legitimate planning pauses, which a working
meeting over a shared screen is full of; two seconds settles the pathological case — a 103-second
silence welded inside one turn — without fragmenting. It agrees with the fixtures, where the median
within-turn pause is 0.7 s and the third quartile 1.3 s, but the fixtures did not choose it.

**It does not vary by language.** Cross-linguistic work on turn-taking puts the whole between-language
spread in gap length at tens to a couple of hundred milliseconds — Stivers et al. (2009) across ten
languages, Weilhammer and Rabold (2003) finding American English, German and Japanese
statistically indistinguishable — which is one to two orders of magnitude below this threshold, and
neither language family nor typology predicts where a language sits. What does move pause length is
modality and the kind of interaction, not the language: face-to-face pauses run several times
longer than telephone ones. So the rule is one number for the whole corpus, and if it is ever
parameterised it is by the kind of meeting, never by `meetings.language`.

The threshold is a domain rule and not a rendering setting — moving it renumbers every turn, and
the ordinals are what stored citations point at.

**An ordinal does not depend on who handed the segments over.** — .NET only

Sorting by start alone leaves segments that begin in the same millisecond in the order they
arrived, so reading one response by `results.utterances` and reading it channel by channel would
hand out different ordinals for the same meeting — and every citation already stored would point
at another turn without anything failing. Grouping therefore orders on the whole segment: start,
then channel, then speaker label, then end, then text. No fixture has a tie today, which is
exactly why the rule is pinned by a test rather than left to the sort.

**Speech with nothing in it is not a turn.** — ported

A blank or whitespace-only transcript is dropped before grouping, so it neither becomes a turn nor
moves the start of the one it fell inside.

## Channels

**An empty channel is a bill, not an error.** — ported as a fixture

A channel whose transcript is `""` was transcribed and said nothing. The usual cause is the wrong
language: nova-3 against a language the audio is not in returns an empty string with confidence
0.0, no error, and charges in full. `two-channel-silent-me.json` is that case, and the meeting
still projects from the channel that did carry speech.

**Channel 1 is the microphone, and how many people it caught is not deterministic.** — .NET only,
`Speakers.Resolve`

The Python renderer applies the user's name to channel 1 without consulting the diarizer, and
`Domain/Audio/` copied that as "channel 1 is the user". The fixtures disprove it:
`two-channel-long` has two diarized speakers on channel 1, which is two people in one room sharing
one microphone, and Python would have signed both sets of words with one name.

What a channel fixes is the device the audio arrived through, never how many people spoke into it.
So the members are named for the devices — `Loopback` and `Microphone` — and the free assignment is
narrow: one speaker on the microphone is the user, because there was nobody else it could be, and
anything else is a label with no row in `speaker_assignments` until somebody says who it is. A
microphone that caught nobody settles nobody and asks nothing either.

Not one committed fixture takes the free assignment, which is worth knowing when reading the
tests: the set has no meeting recorded with a single person at the microphone.

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

The label itself is `SpeakerLabels.For`: `ch1:speaker_0` when the recording has channels, and
`speaker_0` when it is a single track that has none. The channel is in there because a provider
numbers speakers within a channel — both sides of a call start at zero — and `speaker_assignments`
is keyed on the meeting and the label alone, so `speaker_0` on its own would have been two people
under one key. Changing the shape of that string is a migration, not a rename.

**Byte-identical output is not a goal.** What the tests check is domain invariants. Where the
rendered text differs from the Python system's, the difference is intentional and gets written
down here rather than chased.

## Still to settle

Not answered by the Python system, so not decided here:

- **What a merged turn's confidence means.** `utterances.confidence` exists and grouping does not
  fill it. The parser carries the provider's number on each `SpeechSegment` so the answer is still
  available, but Python has per-utterance confidence and no per-turn number, so there is nothing to
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
