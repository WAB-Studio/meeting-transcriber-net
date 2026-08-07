# Layout

What each folder holds. The rules a task would go wrong without are in `CLAUDE.md`; this is the
map you open when you need to know where something lives.

```text
src/MeetingTranscriber.App/               WinUI 3, packaged as MSIX
src/MeetingTranscriber.Domain/            entities, states and pure rules
src/MeetingTranscriber.Infrastructure/    SQLite, filesystem and credentials
src/MeetingTranscriber.Processing/        Deepgram, transcript and summaries
tools/MeetingTranscriber.CorpusFixtures/  builds the fixtures from the Python corpus
tools/MeetingTranscriber.CorpusImport/    reads a Python corpus in, then gets deleted
tests/fixtures/deepgram/                  anonymised responses, free to test against
```

Domain, Infrastructure, Processing and CorpusImport each have their tests under
`tests/<project>.Tests/`.

`Processing` references `Infrastructure`, and only that way round: rendering reads the paid
response out of the corpus and puts the derivatives back, so it sits above storage. The opposite
edge would make SQLite depend on how a Deepgram response is parsed.

`tests/MeetingTranscriber.Isa.Tests/` is the exception to that pattern and references no `src/`
project: it reads `ISA.md` at the repo root. The claims surface is a repo document rather than a
layer, so its gate does not belong under any one of them.

`tools/` is run by hand and is not part of the product. Nothing under `src/` may reference it or
know the Python system existed, so deleting the importer is deleting two folders rather than
untangling the application — its README says what that deletion is.

The seven-project split in `arquitectura.md` §3 is the destination, not the scaffolding: a
project appears when there is code to put in it. Dependencies point inwards, and
`MeetingTranscriber.Domain` stays free of Windows and WinUI references, with tests asserting
exactly that.
