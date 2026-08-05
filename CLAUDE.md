# meeting-transcriber-net

Native Windows app that records meetings, transcribes them with Deepgram and turns them into a
local, queryable corpus. No Python, no WSL, no OBS, no FFmpeg, no backend, no remote database.
The full design lives in `arquitectura.md`, which is written in Spanish.

## Build and test

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

Those four commands are exactly what CI runs on `windows-latest`. Warnings fail the build in CI
only; locally they show up and do not block. `dotnet format` has to pass clean.

WinUI 3, the Windows App SDK and WASAPI do not build or run on WSL, and the repo must not be
cloned under `\\wsl$\`: MSBuild is slow over the crossed filesystem and Hot Reload misses changes.

## Layout

```text
src/MeetingTranscriber.App/        WinUI 3, packaged as MSIX
src/MeetingTranscriber.Domain/     entities, states and pure rules
tests/MeetingTranscriber.Domain.Tests/
```

Three projects, not twelve. The seven-project split in `arquitectura.md` §3 is the destination; a
project appears when there is code to put inside it.

Dependencies point inwards. `MeetingTranscriber.Domain` stays free of Windows and WinUI
references, and there are tests asserting exactly that.

## The contract

`Domain/Audio/` and `Domain/Time/` hold invariants the rest of the system assumes. Breaking one
corrupts meetings already recorded and artifacts already paid for.

- Channel 0 is the meeting, channel 1 is the user. The number is the channel index Deepgram
  reports back, not an internal detail, so only `CapturedAudio` turns a channel into a position.
- `multichannel` is two channels, `diarize` is one. A profile that disagrees with its audio throws.
- Instants are UTC to the millisecond (`UtcTimestamp`); lengths and timeline offsets are whole
  milliseconds (`Duration`). A bare `DateTime` or `TimeSpan` does not cross into the domain.

## Conventions

- Central package management: versions live in `Directory.Packages.props` and a
  `<PackageReference>` carries no `Version`.
- Tests are xunit v3 with Shouldly. `Microsoft.Testing.Extensions.CodeCoverage` is pinned to
  18.0.x on purpose — xunit.v3 3.2.2 needs Microsoft.Testing.Platform 1.x, and 18.1.0 onwards
  jumps to 2.x and throws `TypeLoadException`.
- Storage is Dapper over SQLite, not EF Core. The design needs FTS5, WAL, explicit transactions
  and projection rebuilds, and the SQL is meant to stay visible.

## Rules that are not preferences

- Tests never touch the network and never spend Deepgram credits or Claude Code quota. Live tests
  are separate opt-in commands and are not part of `dotnet test`.
- `deepgram.json` is a paid artifact: never overwritten, and corrections are never written into
  it. Re-transcribing creates a new version and needs explicit cost approval.
- The corpus never lives in the MSIX package data folder — uninstalling wipes it, and the corpus
  holds artifacts that cannot be obtained again.
- Claude Code is an optional dependency. Nothing about recording, transcription, rendering,
  search or recovery may depend on it being installed.
