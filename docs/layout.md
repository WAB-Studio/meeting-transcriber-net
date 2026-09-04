# Layout

What each folder holds. The rules a task would go wrong without are in `CLAUDE.md`; this is the
map you open when you need to know where something lives.

```text
src/MeetingTranscriber.App/               WinUI 3, packaged as MSIX
src/MeetingTranscriber.Audio/             WASAPI: the devices and streams, the spool, the timeline they meet on, the recording that comes off it, and playing one back
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
and the recovery card beside it. The watch that tells a list of meetings it has stopped saying what
the corpus says is here for that reason again, and it is the only thing in the product that polls
the corpus: what that list draws is the corpus's meetings *and* the spool folder's recordings, and
this is the only project that can see both. `RowPresses` is here for that reason once more: the ids
it builds tell one press on one row of that list from the same press on another row — which is what
a re-read needs to hand somebody's keyboard back — and what they name is a row, so half of them are
a meeting and half a spool folder. The closed list of what a read of the corpus throws
that a screen says rather than stops over is here for the same reason once more: the watch reads
the corpus from the thread a window is being built on, so that list stopped being only the
screens' — and every exception it names, the audio engine's and the recording's and the
filesystem's two and SQLite's, is visible from exactly here and from nowhere lower.
`MeetingTranscriber.App` was not an option for any of it: touching a type from that assembly fires
the Windows App SDK module initializer and throws outside a packaged host, so anything living
there would have no probe a build agent could run.

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

The same split, in the same two places, is what the screen a meeting is read from is made of, and
the halves land in different projects because the two questions are different. What that screen
shows and offers — whether the player is there at all, what act is on the right, whether the name
may be typed — is `MeetingScreen` in `Domain`, over the corpus side `MeetingReading` in
`Infrastructure`. Playing the file is `Playback` in `Audio`, beside capture: an endpoint, a format
and a stream are the same kind of thing whichever way the bytes are going, and this repository does
not get a second place that knows what WASAPI is. `Presentation` was never the alternative for
either half — it holds every word a person reads and nothing else, it is plain `net10.0`, and a
WASAPI call in it would not compile.

**Where a screen's rules go**, since there are now several sets of them and the two paragraphs
above only say where two went. The record a window reads its controls off never lives beside the
window, for the reason those paragraphs give; past that it goes in the project its **subject**
already lives in — the thing the screen is asking about, which is not the same as the types the
record is made of. Everything the recorder screen decides is about a recording, so `RecorderScreen`,
`RecordingMeters`, `WaitingRows` and the states behind them are in `Recording`. What the screen a
meeting is read from decides is about a meeting, so `MeetingScreen` is in `Domain/Meetings`. And
`WhoIsUsingThisRow` is about the person the corpus flags as me, so it is in `Domain/Meetings` too,
beside the `Person` its answer is written onto — not in `Presentation`, where it first landed for
being made of four primitives and needing nothing.

What a record is made of is the weakest reason available and is the one to distrust. It is a design
choice rather than a fact about the screen, so it moves a screen's rules between projects on a
changed parameter type, and it says nothing at all when every parameter is a primitive — which is
exactly the case that went wrong. What a screen is about does not move. `Presentation` is never the
answer either, and not for what it references: it holds what a screen **says** — the catalogue, the
rule that picks a language, the line a screen keeps instead of a string. A record of what a screen
decides is a different kind of thing and goes with its subject. The earlier version of this
paragraph said the stronger thing, that nothing in `Presentation` is ever a subject a screen asks
about, and the language picker falsifies it: `LanguageChoice` and `UiLanguages` are exactly what
that picker is about. The positive reason is the one that decides, and it decides those three cases
without the universal.

That paragraph is about a **record a window reads its controls off**, and one rule in `Presentation`
is not one: `Movement`, which says how long each of the four things that move takes and whether
Windows was asked for none. It has no subject in the corpus at all — it is not about a recording or
a meeting, it is the one rule every screen obeys — so "the project its subject lives in" has no
answer to give, and the rule that decides instead is the other half of what `Presentation` is for:
it references nothing, so it is where a thing about the UI goes when it has to be provable without a
window. A project of its own for three numbers would be a folder and a `.csproj` holding a lookup
table.

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
meter's anatomy and the rules the design imposes — with the eighteen artboards it was written from
beside it in `docs/design/`. Nothing under `src/` reads that folder and nothing builds it: they are
pictures a person opens. A screen is built from the prose, and the artboards are what the prose is
checked against.

**That page reaches a screen through `src/MeetingTranscriber.App/Olivo.xaml`**, which is the one
place a colour, a type rank, a corner, a height or a control rank is written down. `docs/design.md`
says the resource key is its suggestion and the first screen to need one settles it; `MainWindow`
was the first, so the keys are settled and every screen after it names them rather than proposing
its own. `OlivoTests` is what holds the page and the dictionary to each other, in both directions:
no screen may carry a colour, a size or a corner of its own, every row of that page's colour table
is a brush here at the value the row gives, and every colour the page writes down either has a
brush here behind it or stands in §Colour's *Decided, and not yet a key* table, which is what stops
the page sanctioning a value the screens are refused. The two fonts sit beside it in
`Assets/Fonts/`, inside the package with the licence that permits it.

The one thing about how a screen looks that is **not** in that dictionary is how long a move takes,
and that is deliberate: a duration fixed when the application starts cannot be zero on a machine
asked for no animation. `Movement` decides it, `ScreenMotion` applies it. `docs/design/README.md` is
what to open before touching an artboard.

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
