# meeting-transcriber-net

Native Windows app that records meetings, transcribes them with Deepgram and turns them into a
local, queryable corpus. Everything stays on the user's machine: SQLite for what is queryable,
the filesystem for audio and artifacts. The design is in [`arquitectura.md`](arquitectura.md).

## The contract

Everything else assumes it. It lives in `MeetingTranscriber.Domain`, and tests fail if it breaks.

- **Channel 0 is the meeting, channel 1 is you.** The number is the channel index Deepgram reports
  back, not an internal detail; only `CapturedAudio` translates it to a position.
- **`multichannel` is two channels, `diarize` is one.** A profile that disagrees with its audio throws.
- **Instants are UTC to the millisecond (`UtcTimestamp`), lengths are whole milliseconds (`Duration`).**

## Development

Windows native: WinUI 3, the Windows App SDK and WASAPI do not run on WSL, and the repo is not
cloned under `\\wsl$\`. Needs Visual Studio 2026 with the .NET desktop and Windows application
development workloads.

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

Warnings fail the build in CI, not on the development machine.
