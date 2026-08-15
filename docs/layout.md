# Layout

What each folder holds. The rules a task would go wrong without are in `CLAUDE.md`; this is the
map you open when you need to know where something lives.

```text
src/MeetingTranscriber.App/               WinUI 3, packaged as MSIX
src/MeetingTranscriber.Audio/             WASAPI: the devices and streams, and the timeline they meet on
src/MeetingTranscriber.Cli/               diagnosis, import, rebuild, recovery and capture from a prompt
src/MeetingTranscriber.Domain/            entities, states and pure rules
src/MeetingTranscriber.Infrastructure/    SQLite, filesystem and credentials
src/MeetingTranscriber.Processing/        Deepgram, transcript and summaries
tools/MeetingTranscriber.CorpusFixtures/  builds the fixtures from the Python corpus
tools/MeetingTranscriber.CorpusImport/    reads a Python corpus in, then gets deleted
tests/MeetingTranscriber.Testing/         what a test opens: corpus, SQL, fixture inventory
tests/fixtures/deepgram/                  anonymised responses, free to test against
```

Domain, Audio, Infrastructure, Processing and CorpusImport each have their tests under
`tests/<project>.Tests/`. What `Audio.Tests` can hold is bounded by there being no device on a
build agent: the rules — which endpoint a typed name means, what a block of bytes is worth on a
meter — are tested there, and that two streams really open at once is a probe somebody runs with
`capture`, recorded in the ISA like a paid one.

`SharedTimeline` is the exception that boundary was drawn around. It takes packets rather than
opening streams, so two hours of a clock running fast is arithmetic in `Audio.Tests` instead of a
meeting on a machine nobody has — which is the only way the product's largest technical risk gets
tested at all. Nothing in it touches WASAPI, and `Fabricated` is where the devices that never
existed are written. That arithmetic is why `Audio.Tests` takes about a minute where every other
suite takes seconds: `TimelineDriftTests` really does run the two hours ISC-126 claims, half a
billion frames of it, and a shorter one would be a different claim.

`tests/MeetingTranscriber.Testing/` holds no test. It is where `TemporaryCorpus`, the raw-SQL
helpers and the inventory of the Deepgram fixtures live, so a suite that opens a corpus or walks
the fixture set references it instead of carrying a copy — and adding a fixture is one edit every
suite sees. It stops at `Infrastructure` on purpose: `Domain.Tests` references it, and a path from
there to `Processing` would let a domain rule be proved against the parser's own output.

`MeetingTranscriber.Cli` is the product's other front end and holds no rule of its own: every
command is a call into the same service the application calls, and what it adds is argument
parsing, a report and an exit code. It targets Windows because `capture` does, and `capture` is
there rather than only in the window because drift is claimed over two hours — a measurement
nobody repeats by clicking. It is where the whole path from a paid response to an answer
can be exercised without automating a window — `tests/MeetingTranscriber.Cli.Tests/` walks it —
and it is the half of the alias that exists: nothing packages it yet, so an installed build has no
`meeting-transcriber` on the PATH until ISC-64 is closed.

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
