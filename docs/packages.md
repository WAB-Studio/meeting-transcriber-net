# Packages

Versions live in `Directory.Packages.props` and a `<PackageReference>` carries no `Version`.

## Pins that are not "latest is fine"

Three versions are held back for a reason, and the reason is not visible from the version number.
Bumping one of these breaks the build or the restore, so the comment in `Directory.Packages.props`
says it too — this is the longer version.

- **`Microsoft.Testing.Extensions.CodeCoverage` at 18.0.x.** xunit.v3 3.2.2 uses
  `xunit.v3.core.mtp-v1` and so needs Microsoft.Testing.Platform 1.x. From 18.1.0 onwards the
  extension jumps to MTP 2.x and the run dies with `TypeLoadException`. Can move once xunit.v3 4
  is stable.
- **`SQLitePCLRaw.bundle_e_sqlite3` transitively pinned to the 3.x line.** Microsoft.Data.Sqlite,
  which EF Core pulls in, still asks for 2.1.x, whose native e_sqlite3 carries GHSA-2m69-gcr7-jv3q.
  That fails `restore` outright, it does not merely warn, so the pin is what makes the repo
  restorable at all.
- **`Microsoft.EntityFrameworkCore.Sqlite` and its `.Design` sibling move together.** A mismatch
  shows up as `dotnet-ef` refusing to load the model rather than as a version error.

## What each area uses

Not all of these are referenced yet. What each area uses is settled here so it does not get decided
in a hurry later, mid-task.

| Area | Package |
| --- | --- |
| SQLite and migrations | `Microsoft.EntityFrameworkCore.Sqlite` |
| Process capture and other Win32 APIs | `Microsoft.Windows.CsWin32` |
| Windows credentials | `Meziantou.Framework.Win32.CredentialManager` |
| Audio | `NAudio.Wasapi` |
| Deepgram | `HttpClient` + `System.Text.Json`, no SDK |
| MCP, in the app | `ModelContextProtocol`, which is `.Core` plus the hosting and DI it is served under |
| MCP, in the UI probe | `ModelContextProtocol.Core` alone: a hand-run tool has no host to put it in |
| MVVM | `CommunityToolkit.Mvvm` |
| DI, hosting and logging | `Microsoft.Extensions.*` |
| Tests | xUnit v3 + Shouldly |
| Reading the legacy Python corpus | `YamlDotNet`, in the import tool only |

A package outside this table is a decision, not a detail: it gets added here with the area it
serves, or it does not get referenced.
