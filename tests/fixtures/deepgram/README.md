# Deepgram fixtures

Four responses that let almost the whole system be tested for free. They come from meetings that
were already transcribed and already paid for in the Python corpus, and nothing in them was ever
sent to Deepgram again.

| Fixture | What it is there for |
| --- | --- |
| `two-channel-long.json` | Both channels, 58 minutes. The long meeting. |
| `two-channel-short.json` | Both channels, 34 minutes. The one to use when length is not the point. |
| `single-track-diarized.json` | One track, six diarized speakers. The `diarize` shape. |
| `two-channel-silent-me.json` | Both channels, and the microphone caught nothing. |

`vocabulary.txt` is the closed list of words the fixtures are made of. Read them through
`DeepgramFixtures` in `MeetingTranscriber.Domain.Tests`.

## What was changed, and what was not

**Every word was replaced.** Each token became a word from `vocabulary.txt`, chosen by a stable
hash of the original, so a term repeated in the meeting is still repeated in the fixture and the
same word is always the same word. No name of a person, a company or a product survives, and the
test that says so does not work from a list of the real names — it checks that every word in every
fixture is one of the vocabulary's, which is the same claim without publishing what it hides.

The test walks the whole document rather than the fields the tool substitutes, because those two
lists used to be the same one and shared a blind spot. Ten paths are exempt as structure, and each
of them has to keep looking like structure: the request id, the audio hash, the date, Deepgram's
`transaction_key` constant, the model ids and what the models are called, and the per-utterance
ids. A string anywhere else — a block a request option turns on, a field the next API release
adds — is held to the vocabulary and fails until the tool is taught to substitute it.

**Nothing that was measured was touched.** Timings, confidences, speaker and channel numbers, word
counts, ordering, the model and the duration are the provider's own, byte for byte: the two
unreshaped fixtures hold exactly the numeric literals their sources do, in the same order. That
half of the response is what the tests are about.

**The paid call was stripped.** `request_id` and `sha256` identify the request that was billed and
are zeroed; `created` is normalised to `2020-01-01`, because when the user met somebody is not test
data. `transaction_key` is Deepgram's own constant `deprecated` and is left alone.

## The two that were reshaped

The corpus has no single track meeting and no silent channel — every meeting was recorded with
`multichannel=true` and both sides speaking — so two fixtures are derived rather than found:

- **`single-track-diarized.json`** is the meeting channel of a two channel response on its own:
  one entry in `results.channels`, `metadata.channels` at 1, its paragraphs under the alternative,
  and no `results.paragraphs`, which is an aggregate across channels and has nothing left to
  aggregate. Its turns still carry `channel: 0`, as a one channel response does.
- **`two-channel-silent-me.json`** empties channel 1: no words, an empty transcript, confidence 0,
  no paragraphs, and no turns on that channel.

Both are faithful to the schema and neither is a byte for byte response from a request Deepgram
actually answered. Nothing else was reshaped, trimmed or shortened.

## Rebuilding them

```powershell
dotnet run --project tools/MeetingTranscriber.CorpusFixtures -- <corpus-directory> <sources.json>
```

`sources.json` says which meeting folder each fixture is built from:

```json
{
  "two-channel-long":      "<folder>",
  "two-channel-short":     "<folder>",
  "single-track-diarized": "<folder>",
  "two-channel-silent-me": "<folder>"
}
```

It belongs next to the corpus and not in this repository. A folder name is the date and the time
the user met somebody, which is the same fact the tool normalises inside every response — writing
four of them into a recipe here would have published from the source what the output takes out.
`single-track-diarized` needs a meeting whose channel 0 carries several diarized speakers; the
other three take any.

The tool is one way and never writes into the corpus. It needs the Python corpus, which only
exists on the machine that recorded those meetings — which is the point of the fixtures being
committed: no test needs it.

The substitution is deterministic, so re-running over the same corpus rewrites the same bytes. A
fixture that comes back with a diff means the source, the vocabulary or the tool changed, and
which of the three it was is worth knowing before committing it.
