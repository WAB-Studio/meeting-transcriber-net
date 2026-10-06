# Layout

What each folder holds, what it may reference, and why. The rules a task would go wrong without are
in `CLAUDE.md`; this is the map you open when you need to know where something lives.
`arquitectura.md` §3 is the split and why it is shaped so; a project appears when there is code to
put in it, and one appears where code has nowhere it can go — which is what `Recording` is.
`MeetingTranscriber.Domain` stays free of Windows and WinUI references, and tests assert it.

```text
src/MeetingTranscriber.App/               WinUI 3, packaged as MSIX
src/MeetingTranscriber.Audio/             WASAPI: the devices and streams, the spool, the timeline they meet on, the recording that comes off it, and playing one back
src/MeetingTranscriber.Cli/               diagnosis, import, rebuild, recovery, capture, and transcribing a meeting again from a prompt
src/MeetingTranscriber.Domain/            entities, states and pure rules
src/MeetingTranscriber.Infrastructure/    SQLite, filesystem and credentials
src/MeetingTranscriber.Mcp/               the corpus answered read-only over stdio, for an agent
src/MeetingTranscriber.Presentation/      what the application says, and what language it says it in
src/MeetingTranscriber.Processing/        Deepgram, transcript and summaries, the words a transcript keeps getting wrong, and the runner that sends what is queued
src/MeetingTranscriber.Recording/         a meeting recorded into a corpus, and what a launch owes one: where the sides meet
tools/MeetingTranscriber.CorpusFixtures/  builds the fixtures from the Python corpus
tools/MeetingTranscriber.Icons/           renders every logo the package names, and the program's .ico, from Assets/Mark.svg
tools/MeetingTranscriber.UiProbe/         starts the application, reads its window, presses what is on it and
                                          kills it — as a script, and as an MCP server an agent drives a turn
                                          at a time. It drives a corpus of its own and records real
                                          meetings into that
tests/MeetingTranscriber.Testing/         what a test opens: corpus, SQL, fixture inventory, the repository's own tree
tests/MeetingTranscriber.FakeClaudeCode/  the program behind the fake Claude Code CLI; holds no test, and only Processing.Tests references it
tests/fixtures/deepgram/                  anonymised responses, free to test against
```

What each `src/` project may reference:

| Project | References | Targets |
| --- | --- | --- |
| `Domain` | nothing | `net10.0` |
| `Presentation` | nothing | `net10.0` |
| `Infrastructure` | `Domain` | `net10.0` |
| `Processing` | `Domain`, `Infrastructure` | `net10.0` |
| `Mcp` | `Domain`, `Infrastructure` | `net10.0` |
| `Audio` | `Domain` | Windows |
| `Recording` | `Audio`, `Domain`, `Infrastructure`, `Processing` | Windows |
| `Cli` | `Audio`, `Domain`, `Infrastructure`, `Processing`, `Recording` | Windows |
| `App` | `Presentation`, `Recording` | Windows |

Dependencies point inwards. The sections below say why each edge is there and why the missing ones
are missing.

## Projects

### `MeetingTranscriber.Domain`

Entities, states and pure rules. It references nothing and is free of Windows and WinUI types.

### `MeetingTranscriber.Audio`

Everything that touches WASAPI: endpoints, streams, the spool, `SharedTimeline` and the recording
made out of it, and `Playback`.

- `Playback` is here, beside capture: an endpoint, a format and a stream are the same kind of thing
  whichever way the bytes are going, and WASAPI is known in one place.
- It never references `Infrastructure`, and `Infrastructure` never references it — see *Where a
  name two projects both write and read goes*.

### `MeetingTranscriber.Infrastructure`

SQLite, the filesystem and credentials.

- It is plain `net10.0`, so `Processing` is not dragged onto a Windows target framework.
- It never references `Audio`: that edge would put WASAPI behind rendering a transcript.
- A Windows-only type that it needs lives here under that constraint (`DeepgramKey.cs`).

### `MeetingTranscriber.Processing`

Deepgram, the transcript, summaries, corrections, rendering, export and `JobRunner`.

- It references `Infrastructure` and only that way round: rendering reads the paid response out of
  the corpus and puts the derivatives back, so it sits above storage. The opposite edge would make
  SQLite depend on how a Deepgram response is parsed.
- It names neither the Deepgram key's type nor its store (`DeepgramTranscription.cs`); the key is
  handed to it.
- What a provider response must hold is `LiveInvariants`, here.

### `MeetingTranscriber.Recording`

Where the rules that need more than one of `Audio`, `Infrastructure` and `Processing` live, and what
the application composes through. `Audio` and `Infrastructure` may not reference each other: an
edge from `Infrastructure` to `Audio` would put WASAPI behind rendering a transcript and force
`Processing` onto a Windows target framework, and an edge the other way would stop the audio engine
being provable on a machine with no corpus. So the composition sits above both.

It holds:

- the corpus side of recording: the meeting row and its folder before the first sample, the run
  written from the card the recording wrote about itself, what stopping makes of the spools, and
  what a start after a crash finds waiting. All of it runs with no device; one thin type opens the
  devices in order and is too small to hold a rule;
- audio brought in from outside: reading a WAV is the engine's and filing a meeting is the
  corpus's, and whether a two-channel file is a meeting's two sources is decided from both — the
  audio's own shape and the recovery card beside it;
- the watch that tells a list of meetings it has stopped saying what the corpus says. It is the only
  thing in the product that polls the corpus, because that list draws the corpus's meetings and the
  spool folder's recordings and only this project sees both;
- `RowPresses`: the ids that tell one press on one row of that list from the same press on another
  row, so a re-read can hand somebody's keyboard back; half name a meeting and half a spool folder;
- `WhatALaunchOwes`: what a launch owes the corpus, as one ordered list, because two of it were two
  background writers over one corpus. This is the only project that may hold the rule and see both
  the sweep and the renders: the prompt sees both and holds no rule, the application has no probe a
  build agent could run, and `Processing` may not see this side;
- the closed list of what a read of the corpus throws that a screen says rather than stops over. The
  watch reads the corpus from the thread a window is being built on, and every exception on the list
  — the audio engine's, the recording's, the filesystem's two and SQLite's — is visible from here
  and from nowhere lower.

`MeetingTranscriber.App` was not an option for any of it: touching a type from that assembly fires
the Windows App SDK module initializer and throws outside a packaged host, so anything living there
would have no probe a build agent could run.

`Recording` references `Processing`, and rendering reaches the application only through it: the
application names two projects, `Presentation` for the words and `Recording` for everything else.
`Audio`, `Infrastructure`, `Domain` and `Processing` arrive through `Recording`, which is the same
composition the command line goes through. The edge from `Recording` to `Processing` is narrow on
purpose. It is there for:

- `WhatALaunchOwes`, which finds the renders a launch owes through `OwedRenders`;
- `NamingTheVoices`, which saves the names on a meeting's voices and renders that one meeting again
  in the same transaction, so a name it saved is a name that transcript already shows;
- `RenamingSomebody`, which corrects a person's name and renders every meeting it touches, each in a
  transaction of its own so the rename does not hold the corpus's write lock across them. What did
  not render is named on the way out and caught up by the next launch;
- `CorrectingWords`, which saves a correction and renders every meeting it touches the same way.

Any of the three can be there because of the direction: `Processing` knows nothing about a window,
so nothing came back the other way.

### `MeetingTranscriber.Cli`

The product's other front end. It holds no rule of its own: every command is a call into the same
service the application calls, and what it adds is argument parsing, a report and an exit code.

- It targets Windows because `capture` does. `capture` is here rather than only in the window
  because drift is claimed over two hours, a measurement nobody repeats by clicking.
- It is where the whole path from a paid response to an answer can be exercised without automating a
  window; `tests/MeetingTranscriber.Cli.Tests/` walks it.
- It is one of the two faces the alias reaches: `MeetingTranscriber.App` publishes it into the
  package and `Package.appxmanifest` declares `meeting-transcriber` on the PATH beside
  `meeting-transcriber-mcp`. ISC-113 waits on a run: nobody has installed a build and started either
  name.

**One exception, named: a spend a person agrees to at the prompt.** `LiveCheck`, `SendingMark` and
`TypedBack` are rules and they are here:

- which files a live run sends;
- how much audio that is;
- what the ceiling allows;
- the claim over the folder the responses land in;
- the minutes a person typed back before anything was sent, for a live run and for a meeting
  transcribed again alike.

Whether a meeting may be sent again, and the sending itself, is decided in `MeetingWork` and
`JobRunner`. The one thing the prompt still decides for itself is how many named voices a new
response would take a name off: it counts before and after, because it is the one number
`transcribe-again` exists to report honestly and no screen prints it. These are a prompt's own rules
about a folder somebody named on its command line, or about a number somebody typed back, with
nothing behind them a service could be a call into. The exception does not grow past a spend
confirmed at a prompt, so every other statement that the prompt holds no rule of its own is true as
written.

### `MeetingTranscriber.Mcp`

The corpus's other read-only face: the tools an agent asks about meetings somebody recorded and the
nodes they are filed under. It holds no rule of its own, exactly as `Cli` does not: every tool is a
read the application already does, and what it adds is a tool surface, a bounded answer and a
sentence for each way a corpus can refuse to open.

- It references `Infrastructure` and `Domain` and no further. `Processing` would put rendering
  behind an MCP tool, `Audio` would put WASAPI behind one, and `Recording` would bring both.
- It opens the corpus read-only, so no row of anybody's corpus can be written through it. What
  SQLite still writes is its own `-wal` and `-shm` beside the database, which is what reading a
  write-ahead-logged file costs; a corpus on a volume this user cannot write to is not readable from
  here either.
- The one file it writes of its own is a line per request, appended to `agent-requests.jsonl` under
  the user's local application data and outside the corpus, so what an agent read can be
  reconstructed. A packaged build keeps that file in the container an uninstall removes.

### `MeetingTranscriber.Presentation`

Every word a person reads, and UI that has to be provable without a window: the catalogue, the rule
that picks a language, the choice on disk, and `Movement`.

- It references nothing and targets plain `net10.0`, which is what lets a test read it.
- That is not tidiness. The Windows App SDK compiles a module initializer into every assembly that
  references it, and touching any type from `MeetingTranscriber.App` fires it and throws outside a
  packaged host. Anything there has no probe a build agent can run, which is why `RecorderScreen`,
  `MeetingScreen`, `Playback` and `ScreenStatus` are not beside the window.
- A WASAPI call in it would not compile.

### `MeetingTranscriber.App`

The WinUI 3 window, packaged as MSIX. It names two projects, `Presentation` and `Recording`, and the
rest arrives through `Recording`. Eight files also name `Processing` for one thing a window cannot do
without; all eight reach it through the reference `MeetingTranscriber.App.csproj`'s own comment
already says brings `Processing` along, so a second, explicit `ProjectReference` would only restate
it:

- `App.xaml.cs` starts `JobRunner`'s pump;
- `TranscribingOnThisMachinesKey.cs` binds the key a transcription sends with;
- `SummarisingOnThisMachine.cs` composes the provider a summary is sent with, and tells the list of
  meetings which memory file stopped one;
- `Configuracion.xaml.cs` exports the corpus through `Processing.Export` and asks Claude Code
  whether it answers through `Processing.Summaries`;
- `AddingSomebody.xaml.cs` catches the `RenderException` a corrected name can end on, from
  `Processing.Rendering`;
- `WordsThatComeOutWrong.xaml.cs` reads `Processing.Corrections` and catches the `RenderException`
  a correction can end on, reading the meetings it could not render off `RenderException.Meetings`.
  It is the one screen a correction ends on, whether the words were typed there or selected where
  they are read and brought in;
- `ReadingAMeeting.xaml.cs` reads the transcript through `MeetingRenderer.AsRead`, so the screen and
  `transcript.md` say the same words;
- `SayingWhoIsWho.xaml.cs` reads each voice's quotation through it for the same reason.

The rule still lives where a build agent runs it, in `Processing` and below; what the application
holds is the call and the thread it goes on. The rendered files are the one thing a person is never
asked about: they cost nothing and can be produced again, so no screen offers them and nothing at a
prompt is supposed to be needed for them to exist. Something therefore produces them without being
asked, and that is `WhatALaunchOwes`.

## Where a screen's rules go

The record a window reads its controls off never lives beside the window, for the reason in
`Presentation` and `Recording` above: the half of a screen that has rules is the half a build agent
runs, and the half that needs a microphone is the half a person presses. The window sets every
control from the record and asks it again inside each handler.

Past that, the record goes in the project its **subject** already lives in — the thing the screen is
asking about, which is not the same as the types the record is made of.

- The recorder screen decides things about a recording, so `RecorderScreen`, `RecordingMeters` and
  `WaitingRows` and the states behind them are in `Recording`. `RecorderScreen` and the table beside
  it say what can be pressed at any moment and what a press is answered with first; they hold no
  meeting, open no device and start nothing. `RecordingMeters` answers about a recording: one half
  takes the numbers and holds the rules, the other is the projection off two open devices that no
  build agent can run.
- The screen a meeting is read from decides things about a meeting, so `MeetingScreen` is in
  `Domain/Meetings`: whether the player is there at all, what act is on the right, whether the name
  may be typed. It sits over the corpus side, `MeetingReading` in `Infrastructure`. Playing the file
  is `Playback` in `Audio`.
- `WhoIsUsingThisRow` is about the person the corpus flags as me, so it is in `Domain/Meetings`,
  beside the `Person` its answer is written onto.

What a record is made of is the weakest reason available and the one to distrust: it is a design
choice rather than a fact about the screen, it moves a screen's rules on a changed parameter type,
and it says nothing when every parameter is a primitive. What a screen is about does not move.

`Presentation` holds what a screen **says** — the catalogue, the rule that picks a language, the line
a screen keeps instead of a string. A record of what a screen decides is a different kind of thing
and goes with its subject. Where the subject is in `Presentation`, so is the record:
`LanguageChoice` and `UiLanguages` are what the language picker is about.

One rule in `Presentation` is not a record a window reads its controls off: `Movement`, which says
how long each of the four things that move takes and whether Windows was asked for none. It has no
subject in the corpus — it is the one rule every screen obeys — so "the project its subject lives
in" has no answer, and the other half of what `Presentation` is for decides: it references nothing,
so it is where a thing about the UI goes when it has to be provable without a window. A project of
its own for three numbers would be a folder and a `.csproj` holding a lookup table.

## Where a name two projects both write and read goes

`Audio` and `Infrastructure` may not reference each other, so a string both have to spell goes in
`Domain`, the only project either can see, and each side defines its own constant from that one so
the compiler proves they agree. `RecordingFiles` in `Domain/Artifacts/` is the case that settled it,
and says on itself what it cost to find out. It is under `Domain/Artifacts/` because the question a
file name answers here — what the file is, and whether losing it costs anything — is the one that
folder already holds; another folder is on the audit floor for the channel contract and the profile
rule, which `.claude/audit-floor.md` is the place to read.

## Tests

Every project under `src/` has its tests under `tests/<project>.Tests/`. Three directories are not
that:

- `Isa.Tests` has no project behind it: it reads `ISA.md` and this tree, because the claims surface
  is a repository document rather than a layer. It references no `src/` project.
- `UiProbe.Tests` is the suite whose project is under `tools/` rather than `src/`; the section on
  `tools/` below says what it may and may not do.
- `Testing` is no suite at all. It is what a suite opens, and `FakeClaudeCode` is a program one
  suite runs.

### `tests/MeetingTranscriber.Testing`

Holds no test. It is where `TemporaryCorpus` (the corpus outside application data that the facts
about *where* a corpus may live need), the rows a summarised meeting is made of, the raw-SQL helpers,
the inventory of the Deepgram fixtures, where this clone is and what is under it, a source file
read without its prose, and what a folder holds live, so a suite that opens a corpus or walks the
fixture set references it instead of carrying a copy, and adding a fixture is one edit every suite
sees. It stops at `Infrastructure` on purpose: `Domain.Tests` references it, and a path from there to
`Processing` would let a domain rule be proved against the parser's own output.

### `tests/MeetingTranscriber.Audio.Tests`

What it can hold is bounded by there being no device on a build agent. The rules are tested here —
which endpoint a typed name means, what a block of bytes is worth on a meter. That two streams
really open at once is a probe somebody runs with `capture`, recorded in the ISA like a paid one.

- What touches a file here is the spool and the recording made out of it, and both have to: the
  first claims the shape of a file on disk after a write was cut, and the second that the bytes read
  back off the disk are the recording that was made. A stream in memory can be neither.
- `SharedTimeline` is the exception the boundary was drawn around. It takes packets rather than
  opening streams, so two hours of a clock running fast is arithmetic here instead of a meeting on a
  machine nobody has — the only way the product's largest technical risk gets tested at all. Nothing
  in it touches WASAPI, and `Fabricated` is where the devices that never existed are written.
- That arithmetic is why this suite takes about a minute where every other takes seconds:
  `TimelineDriftTests` really does run the two hours ISC-66 claims, half a billion frames of it, and
  a shorter one would be a different claim.

### `tests/MeetingTranscriber.App.Tests`

It cannot reference the application at all, for the module-initializer reason under `Presentation`,
so it references `Presentation` and nothing else. It reads the app's `.xaml` and `.xaml.cs` as source
to hold every screen to naming an entry in the catalogue instead of carrying words of its own.

- Running a WinUI tree needs a UI thread and a packaged host, neither of which a build agent has, so
  a check that needed one would never run there. It runs somewhere: `tools/MeetingTranscriber.UiProbe`
  starts the packaged application on a desktop somebody is logged into and reads the tree itself,
  by hand and never in a build. `docs/ui-probe.md` is when to reach for it.
- It reads source out of other `src/` projects as well — the enum a screen's table is over, the
  prompt's own recording command. A table falling behind its enum, or a window that files a meeting
  the prompt files through one call, are facts about two files agreeing, and one of the two is the
  application's. `AppSources` resolves every path under `src/`, so how the repository is laid out is
  written down there once.

## What a screen looks like

`docs/design.md` holds the tokens, the type ramp, the radii, the meter's anatomy and the rules the
design imposes, with the nineteen artboards it was written from beside it in `docs/design/`. Nothing
under `src/` reads that folder and nothing builds it: they are pictures a person opens. A screen is
built from the prose, and the artboards are what the prose is checked against.

**That page reaches a screen through `src/MeetingTranscriber.App/Olivo.xaml`**, the one place a
colour, a type rank, a corner, a height or a control rank is written down. Every screen names its
keys rather than proposing its own. `OlivoTests` holds the page and the dictionary to each other, in
both directions:

- no screen may carry a colour, a size or a corner of its own;
- every row of the page's colour table is a brush there, at both values the row gives;
- every colour the page writes down either has a brush behind it or stands in §Colour's *Decided,
  and not yet a key* table, which is what stops the page sanctioning a value the screens are
  refused.

The two fonts sit beside it in `Assets/Fonts/`, inside the package with the licence that permits it.

How long a move takes is **not** in that dictionary, deliberately: a duration fixed when the
application starts cannot be zero on a machine asked for no animation. `Movement` decides it,
`ScreenMotion` applies it. `docs/design/README.md` is what to open before touching an artboard.

## `tools/`

Run by hand and not part of the product. Nothing under `src/` may reference it, which is why
deleting the Python corpus importer was two folders and two solution lines rather than untangling
the application.

The UI probe needs an interactive desktop, so no build agent can run it and **nothing under `tests/`
may drive it**. A suite may reference it, and one does: `tests/MeetingTranscriber.UiProbe.Tests/`
holds the halves that open no window, which are the two walks every staleness refusal the probe
makes is computed from. Starting a window is the line, not the reference, and
`ProbeIsNotDrivenTests` holds the line: it sweeps the suite's own source and fails the moment a fact
names anything that opens or presses a window.

The probe references one project, `MeetingTranscriber.Infrastructure`, for `ApplicationHome`,
`CorpusLocation` and `CorpusDatabase`. It gives the application a home of its own and tells it so on
its launch line, `--home "<folder>"`, rather than moving a setting of the owner's: the application
keeps its corpus pointer, its Claude Code pointer and its first corpus in that home, and nothing
under the owner's `%USERPROFILE%\MeetingTranscriber` is read or written by a probe session. It never
references `MeetingTranscriber.App` — touching a type from that assembly would fire the Windows App
SDK module initializer in the probe's own process — so it reaches the application only the way
anybody else does, through the shell.

## After a crash

The application writes what an exception nobody caught told it to `crash-record.log`, beside the
language and theme choices and never in the corpus: `%LOCALAPPDATA%\MeetingTranscriber\`, which an
installed build has redirected to
`%LOCALAPPDATA%\Packages\<package family>\LocalCache\Local\MeetingTranscriber\`. One entry is the
time, where it was caught, and each exception's type, message and stack; `CrashRecord` says what it
leaves out and when the file starts again. Not every kind of crash reaches it (a native fail-fast
may not); an empty file after a crash is itself an answer.
