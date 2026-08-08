# Running things by hand

Four ways a command on this machine does something other than what it looks like it did. Each one
costs an hour the first time because the failure names the wrong cause.

## PowerShell rewrites a file it round-trips

`Get-Content file | ... | Set-Content file` re-encodes as the system ANSI codepage, so every dash
and accent comes back double-encoded — and the file still opens, still builds, and only shows up
as mojibake later. `arquitectura.md`, `ISA.md` and half the comments in this repo are full of both.

Use `[System.IO.File]::ReadAllText` and `::WriteAllText`, which are byte-faithful and default to
UTF-8, or the editor. Never `Get-Content`/`Set-Content` for a file that is going back to disk.

## PowerShell splits a long argument that holds quotes

PowerShell 5.1 rebuilds the command line for a native executable, and a string argument containing
double quotes reaches the exe as several arguments. It surfaces as the tool complaining about an
argument nobody passed — `unexpected argument 'works' found`, which was a word inside a quoted
phrase in the text. It looks like a length limit or an encoding problem and is neither.

Pipe the text in instead: `[System.IO.File]::ReadAllText($file) | codex exec ... -`, where `-`
says to read from stdin. When the tool has no stdin form, write the text with single quotes.

## `git push` has no credentials, `gh` does

`git push` dies with `could not read Username for 'https://github.com'` while `gh pr create` and
`gh pr merge` work, which makes it read as a permission problem. Git simply has no credential
helper configured here.

Borrow gh's for the one command rather than writing it into the machine's git config:

```powershell
git -c credential.helper="!gh auth git-credential" push -u origin <branch>
```

## A test filter without a project runs everything and passes

`dotnet test --filter "FullyQualifiedName~X"` is silently ignored by the Microsoft.Testing.Platform
runner xunit.v3 uses: it runs the whole suite and exits 0. A green run that proved nothing is worse
than a red one. The form that works names the project:

```powershell
dotnet test tests/<Project> --no-build -- --filter-class "*ClassName"
dotnet test tests/<Project> --no-build -- --filter-method "*Method_name*"
```

Without the project, the suites that match nothing exit non-zero on zero tests.
