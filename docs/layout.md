# Layout

What each folder holds. The rules a task would go wrong without are in `CLAUDE.md`; this is the
map you open when you need to know where something lives.

```text
src/MeetingTranscriber.App/               WinUI 3, packaged as MSIX
src/MeetingTranscriber.Audio/             WASAPI: the devices and streams, the spool, the timeline they meet on, and the recording that comes off it
src/MeetingTranscriber.Cli/               diagnosis, import, rebuild, recovery and capture from a prompt
src/MeetingTranscriber.Domain/            entities, states and pure rules
src/MeetingTranscriber.Infrastructure/    SQLite, filesystem and credentials
src/MeetingTranscriber.Presentation/      what the application says, and what language it says it in
src/MeetingTranscriber.Processing/        Deepgram, transcript and summaries
src/MeetingTranscriber.Recording/         a meeting recorded into a corpus: where the audio engine and the corpus meet
tools/MeetingTranscriber.CorpusFixtures/  builds the fixtures from the Python corpus
tools/MeetingTranscriber.CorpusImport/    reads a Python corpus in, then gets deleted
tools/MeetingTranscriber.UiProbe/         starts the application, reads its window, presses what is on it and
                                          kills it — as a script, and as an MCP server an agent drives a turn
                                          at a time. It drives the real corpus and records real meetings into it
tests/MeetingTranscriber.Testing/         what a test opens: corpus, SQL, fixture inventory
tests/fixtures/deepgram/                  anonymised responses, free to test against
```

Domain, Audio, Infrastructure, Processing, Presentation, Recording and CorpusImport each have their
tests under `tests/<project>.Tests/`. What `Audio.Tests` can hold is bounded by there being no device on a
build agent: the rules — which endpoint a typed name means, what a block of bytes is worth on a
meter — are tested there, and that two streams really open at once is a probe somebody runs with
`capture`, recorded in the ISA like a paid one. What touches a file in there is the spool and the
recording made out of it, and both have to: what the first claims is the shape of a file on disk
after a write was cut, and what the second claims is that the bytes read back off the disk are the
recording that was made — neither of which is something a stream in memory can be.

`SharedTimeline` is the exception that boundary was drawn around. It takes packets rather than
opening streams, so two hours of a clock running fast is arithmetic in `Audio.Tests` instead of a
meeting on a machine nobody has — which is the only way the product's largest technical risk gets
tested at all. Nothing in it touches WASAPI, and `Fabricated` is where the devices that never
existed are written. That arithmetic is why `Audio.Tests` takes about a minute where every other
suite takes seconds: `TimelineDriftTests` really does run the two hours ISC-66 claims, half a
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
`meeting-transcriber` on the PATH until ISC-113 is closed.

`MeetingTranscriber.Recording` is the only project that references both `Audio` and
`Infrastructure`, and that is the whole of what it is for. Neither of those two may reference the
other: an edge from `Infrastructure` to `Audio` would put WASAPI behind rendering a transcript and
force `Processing` onto a Windows target framework, and an edge the other way would stop the audio
engine being provable on a machine with no corpus. So the composition sits above both. What is in
it is the corpus side of recording — the meeting row and its folder before the first sample, the
run written from the card the recording wrote about itself, what stopping makes of the spools, and
what a start after a crash finds waiting and makes of one of them — all of which runs with no
device, plus one thin type that opens the devices in that order and is
deliberately too small to hold a rule. Audio somebody brought in from outside is here for that same
reason and not a second one: reading a WAV is the engine's and filing a meeting is the corpus's, and
whether a two-channel file is a meeting's two sources is decided from both — the audio's own shape
and the recovery card beside it. `MeetingTranscriber.App` was not an option for any of it:
touching a type from that assembly fires the Windows App SDK module initializer and throws outside
a packaged host, so anything living there would have no probe a build agent could run.

That last sentence is also why the recording screen's rules are here rather than beside the window.
What can be pressed at any moment — and what a press would have to be answered with first — is
`RecorderScreen` and the table beside it, which hold no meeting, open no device and start nothing.
What the screen shows while a meeting runs is `RecordingMeters`, and it is the other shape: it
answers about a recording, so one half of it takes the numbers and holds the rules, and the other
half is the projection off two open devices that no build agent can run.
The window sets every control from one of those and asks it again inside each handler, so the half
of a screen that has rules is the half a build agent runs, and the half that needs a microphone is
the half a person presses. `MeetingTranscriber.App` references this project for all of that:
`Audio`, `Infrastructure` and `Domain` arrive through it, which is the same composition the command
line goes through.

`Processing` references `Infrastructure`, and only that way round: rendering reads the paid
response out of the corpus and puts the derivatives back, so it sits above storage. The opposite
edge would make SQLite depend on how a Deepgram response is parsed.

`MeetingTranscriber.App` references `Processing` too, and that is the second and last edge out of
the application. It is there because the rendered files are the one thing a person is never asked
about — they cost nothing and can be produced again, so no screen offers them and nothing at a
prompt is supposed to be needed for them to exist. Something inside the application therefore has
to produce them, and the rule for which meetings are owed one lives on the `Processing` side, where
a build agent runs it; what the application holds is the call and the thread it goes on. The edge
is narrow on purpose and the reason it can be is the direction: `Processing` knows nothing about a
window, so nothing came back the other way.

`MeetingTranscriber.Presentation` holds every word a person reads and nothing else — the
catalogue, the rule that picks a language, and the choice on disk. It references nothing and
targets plain `net10.0`, which is what lets a test read it. That is not tidiness: the Windows
App SDK compiles a module initializer into every assembly that references it, and touching any
type from `MeetingTranscriber.App` fires it and throws outside a packaged host. Anything about
the UI that has to be provable lives here rather than beside a window.

`tests/MeetingTranscriber.App.Tests/` follows from that: it references no project either — not
even the ones the app itself references — and reads the app's `.xaml` and `.xaml.cs` as source to hold every screen to naming an entry in the
catalogue instead of carrying words of its own. Running a WinUI tree would need a UI thread and
a packaged host, neither of which a build agent has — so the check that needed one is the check
that would never run there. It runs somewhere: `tools/MeetingTranscriber.UiProbe` starts the
packaged application on a desktop somebody is logged into and reads the tree itself, by hand and
never in a build. `docs/ui-probe.md` is when to reach for it.

It reads source out of other `src/` projects as well as out of the application — the enum a
screen's table is over, the prompt's own recording command — and that is not a widening of what it
is about. A table falling behind its enum, or a window that files a meeting the prompt files
through one call, are both facts about two files agreeing, and one of the two is the application's.
`AppSources` resolves every path under `src/`, so how the repo is laid out is written down there
once rather than once per check.

`tests/MeetingTranscriber.Isa.Tests/` is the exception to that pattern and references no `src/`
project: it reads `ISA.md` at the repo root. The claims surface is a repo document rather than a
layer, so its gate does not belong under any one of them.

What a screen looks like lives in `docs/design.md` — the tokens, the type ramp, the radii, the
meter's anatomy and the rules the design imposes — with the thirteen artboards it was written from
beside it in `docs/design/`. Nothing under `src/` reads that folder and nothing builds it: they are
pictures a person opens. A screen is built from the prose, and the artboards are what the prose is
checked against.

`tools/` is run by hand and is not part of the product. Nothing under `src/` may reference it or
know the Python system existed, so deleting the importer is deleting two folders rather than
untangling the application — its README says what that deletion is. The UI probe is in here for the
same reason and a second one: it needs an interactive desktop, so no build agent can run it and
nothing under `tests/` may come to depend on it. It references no project at all — touching a type
from `MeetingTranscriber.App` would fire the Windows App SDK module initializer in the probe's own
process — and reaches the application only the way anybody else does, through the shell.

The project split in `arquitectura.md` §3 is the destination, not the scaffolding: a project
appears when there is code to put in it, and one the destination never named appears when the code
turns out to have nowhere it can go — which is what `Recording` is. Dependencies point inwards, and
`MeetingTranscriber.Domain` stays free of Windows and WinUI references, with tests asserting
exactly that.
