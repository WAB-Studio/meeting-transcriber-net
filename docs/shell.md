# Running things by hand

Four ways a command on this machine does something other than what it looks like it did. Each one
costs an hour the first time because the failure names the wrong cause.

## PowerShell re-encodes a UTF-8 file it reads

`Get-Content file | ... | Set-Content file` re-encodes as the system ANSI codepage, so every dash
and accent comes back double-encoded — and the file still opens, still builds, and only shows up
as mojibake later. `arquitectura.md`, `ISA.md` and half the comments in this repo are full of both.

The damage is done on the read, so it is not only files going back to disk: `Get-Content -Raw`
handed to a program is the same corruption, and it lands somewhere no diff will ever show it. A
ClickUp description written that way reached the board double-encoded while the same text sent
from `bash` with `"$(cat file)"` arrived intact.

Use `[System.IO.File]::ReadAllText` and `::WriteAllText`, which are byte-faithful and default to
UTF-8, or the editor, or the Bash tool. Never `Get-Content` for text that has to survive.

## PowerShell splits a long argument that holds quotes

PowerShell 5.1 rebuilds the command line for a native executable, and a string argument containing
double quotes reaches the exe as several arguments. It surfaces as the tool complaining about an
argument nobody passed — `unexpected argument 'works' found`, which was a word inside a quoted
phrase in the text. It looks like a length limit or an encoding problem and is neither.

Pipe the text in instead: `[System.IO.File]::ReadAllText($file) | codex exec ... -`, where `-`
says to read from stdin. When the tool has no stdin form, write the text with single quotes.

## `git push` has no credentials, `gh` does

`git push` dies with `could not read Username for 'https://github.com'` while `gh pr create` works,
which makes it read as a permission problem. Git simply has no credential helper configured here.

Borrow gh's for the one command rather than writing it into the machine's git config:

```powershell
git -c credential.helper="!gh auth git-credential" push -u origin <branch>
```

## Building the app project locks the solution build out of its own output

`dotnet build src/MeetingTranscriber.App -p:Platform=x64` is how the packaged application is
refreshed for `tools/MeetingTranscriber.UiProbe`, and it leaves MSBuild worker processes alive by
default. Those nodes are holding `MeetingTranscriber.Presentation.dll` and
`MeetingTranscriber.Domain.dll` open, and the shared projects have no `Platforms`, so they write to
the same `bin/Debug/net10.0` either way. The next `dotnet build` of the solution — which is
AnyCPU, so it is a different set of nodes — dies with a dozen `MSB3027` copy failures naming a
process id, which reads as a stray editor or a running app and is neither.

Turn the reuse off on the app build, which is the one that is out of step:

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64 -nodeReuse:false
```

`dotnet build-server shutdown` clears it after the fact, at the price of every other build on the
machine starting cold. The fix that removes the flag is issue #173.

## A test filter without a project runs everything and passes

`dotnet test --filter "FullyQualifiedName~X"` is silently ignored by the Microsoft.Testing.Platform
runner xunit.v3 uses: it runs the whole suite and exits 0. A green run that proved nothing is worse
than a red one. The form that works names the project:

```powershell
dotnet test tests/<Project> --no-build -- --filter-class "*ClassName"
dotnet test tests/<Project> --no-build -- --filter-method "*Method_name*"
```

Without the project, the suites that match nothing exit non-zero on zero tests.
