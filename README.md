# meeting-transcriber-net

Native Windows app that records meetings, transcribes them with Deepgram and turns them into a
local, queryable corpus. What is kept stays on the person's machine, bar what is sent to the
providers they choose: SQLite for what is queryable, the filesystem for audio and artifacts. No
Python, no WSL, no OBS, no FFmpeg, no backend, no remote database.

## What it does today

- Records the whole machine or one program, and a microphone, on two channels, with pause and source
  changes in the middle of a meeting.
- Recovers a recording a crash cut.
- Transcribes with Deepgram, on the person's own key.
- Summarises through Claude Code on their own plan. Claude Code is optional: nothing else depends on
  it being installed.
- Files meetings under a three-level tree, and names the people and the voices in them.
- Corrects words a transcript keeps getting wrong.
- Searches meetings, exports the corpus, archives a meeting and deletes its audio, its transcript or
  the meeting.
- Answers an agent, read-only, over MCP as `meeting-transcriber-mcp`.
- Has a command line, `meeting-transcriber`, for diagnosis, import, rebuild, recovery and capture.

## Status

An alpha: nothing has been handed to anybody. It is installed by sideloading a signed MSIX
(`docs/packaging.md`) on Windows 11 x64, build 22000 or later. The screens are in Spanish and
English.

## Where to read next

- [`arquitectura.md`](arquitectura.md) is the design and why it is shaped so.
- [`CLAUDE.md`](CLAUDE.md) holds the contract (`## The contract`), how work runs, and which `docs/`
  page to open for which job.
- [`ISA.md`](ISA.md) says what done means, as claims that close on a probe that ran.

## Development

Windows native: WinUI 3, the Windows App SDK and WASAPI do not run on WSL, and the repository is not
cloned under `\wsl$\`. The .NET 10 SDK on Windows 11 x64 is enough for the four commands below.
Visual Studio is needed only to F5-deploy the window from the IDE.

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

Warnings fail the build in CI, not on the development machine.

## Licence

Apache 2.0, see [`LICENSE`](LICENSE).
