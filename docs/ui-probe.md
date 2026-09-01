# Driving the app's windows

`tools/MeetingTranscriber.UiProbe` starts the packaged application, reads the UI Automation tree of
its windows, photographs them, presses what is on them, and closes it again. Two ways in over the
same verbs: a script, for a finding you want repeatable; MCP, for building a screen a turn at a time.

Run it by hand. It needs an interactive desktop, so it is never part of a build and nothing in
`dotnet test` may come to depend on it.

## Once per machine

Point the package registration at the build output. Check what it is now:

```powershell
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4 | Select-Object InstallLocation
```

If that does not end in `\win-x64`, replace it. Remove it first — registering over an existing
registration keeps the old location:

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4 | Remove-AppxPackage
Add-AppxPackage -Register (Resolve-Path src/MeetingTranscriber.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AppxManifest.xml)
```

Do it again whenever that path changes — another configuration, another target framework.

Then put the server where the checkout can reach it. `.mcp.json` at the repository root names it
already, spelled the same in every clone, so nothing is registered by hand and no path in it is
anybody's machine. What it needs is the copy it points at.

```powershell
dotnet publish tools/MeetingTranscriber.UiProbe -c Debug -o tools/MeetingTranscriber.UiProbe/bin/mcp
claude mcp get ui-probe
```

**`bin/mcp` and not `bin/Debug`, and that is the whole reason the copy exists.** The tool is in the
solution, so `dotnet build --no-restore -warnaserror` writes the exe under `bin/Debug` — and a
connected server holding that file open failed the build for everything else in the solution.
`dotnet build` never writes `bin/mcp`, so the four commands are green while the probe is connected
and nothing has to be closed for them. Publish the exe and not `dotnet run`: a build writes to
stdout, and stdout is the protocol.

The first session after this asks you to approve `ui-probe` once, because it is a project-scoped
server. `claude mcp get ui-probe` then says `Scope: Project config` and `✔ Connected`; if it says
`Scope: User config` instead, a leftover machine-wide registration is shadowing this one and has to
go — `claude mcp remove ui-probe -s user`, with the `-s user`, because without a scope it removes
whichever it finds first and that is now the repository's.

A worktree is its own checkout and gets its own copy, published the same way. It still drives the
one build Windows has registered, so from anywhere else it refuses and says which checkout that is.

## Every run

Build the application first. Publish the tool again if you changed it, and start a new session —
`.mcp.json` is read when a session opens, and `close` and `start` are verbs about the application,
not about the server.

Anything is refused once the application is older than the code on disk. To pick up a change:
close, build, start — in that order, because a running application holds its own assemblies open
and the build fails on them. A build alone does not lift the refusal; only starting again does.

## The verbs

- `see` — the tree of the screen, and a picture of the window. Changes nothing.
- `press <element>` — invoke it. Fails if it is disabled or cannot be invoked.
- `type <element> <text>` — set a field's value. Fails if it is disabled, read only, or takes none.
- `choose <list> <item>` — open the list, pick the item by name, shut it again. A list too long
  to draw whole is asked what it holds rather than walked, so an item below the fold is named the
  same way as one on screen.
- `wait <element>` — block until it is on a window, and make that window the screen from then on.

Put a `wait` after any `press`, `type` or `choose` whose effect you are about to look at. It is the
only thing here that synchronises.

## Over MCP, a turn at a time

`start` first — nothing else works until an application is open — and `close` when you are done,
because it stays open between calls. `close`, build the application, `start` is how you pick up a
change to it. A refused `start` leaves the session you had alone.

Every verb answers with the tree of the screen it became, so you choose the next step from the last
answer instead of writing the whole walk in advance. `see` also returns the picture, inline.

```text
start                          → 7feb8c95-...!App is process 12216, from C:\...\win-x64\...exe
                                 window "Grabar una reunión" ... (the whole tree)
press PackagingChecksButton    → pressed PackagingChecksButton
                                 The application has 2 windows open — "Comprobaciones de
                                 empaquetado", "Grabar una reunión" — and the script has not said
                                 which one it is on.
wait EnvironmentButton         → on "Comprobaciones de empaquetado"
                                 window "Comprobaciones de empaquetado" ... (the whole tree)
see                            → the tree, then the PNG
close                          → Closed.
```

## As a script

```powershell
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...
```

`see` takes a name here: it writes `<name>.tree.txt` and `<name>.png`. Give `--out` a folder of its
own — it is emptied of trees and pictures first, and it refuses to touch a folder holding anything
else. The application is closed on the way out either way.

```powershell
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out $env:TEMP\ui-probe `
  see docked press OpennessButton wait OpennessButton see whole
```

```text
7feb8c95-4553-46f0-a036-6574f4cd7cb4_savbypjtf9g9c!App is process 38684, from C:\...\win-x64\MeetingTranscriber.App.exe
  see docked
    docked.tree.txt and docked.png (1920x1023)
  press OpennessButton
  wait OpennessButton
    on "Grabar una reunión"
  see whole
    whole.tree.txt and whole.png (1920x1023)
done, in C:\Users\pc\AppData\Local\Temp\ui-probe
```

Exit: `0` it ran · `1` the screen or the application failed it · `2` the script was wrong and
nothing was started · `3` the probe broke, which is not news about a screen

## Reading a tree

A line is `Type #x:Name "what it says"`, indented by depth, with `value=`, `help=`, `status=`,
`disabled` and `offscreen` appended when they apply. `value=` is what `type` left in a field.

```text
      ComboBox #MicrophonePicker "Micrófono"
      Button #RecordButton "Grabar"  disabled
      Button #OpennessButton "Abrir la lista entera"
```

## Naming an element

Give the `x:Name` its XAML gave it, or the words on it. Three tiers, and the first with a match
wins: exact `x:Name`, then exact words, then words containing what you asked for, ignoring case.
Two matches in the winning tier is a failure listing both.

Reach an accented name from a shell that mangles one through the third tier —
`choose LanguagePicker Espa` finds `Español`.

## Which window is the screen

The one the last `wait` named. Failing that, the only window open. Anything else stops and tells you
to `wait` for something on the screen you meant. It is never whichever window is in front.

## What it will not do

- **`press` is `Invoke` only, and `type` is `SetValue` only.** Either one fails naming what the
  control offers instead, which is how you find out it wanted another verb.
- **It will not bring a window forward.** A window behind another still photographs correctly.
- **It uses the real corpus and the real preference file.** That is deliberate: it drives the real
  application. Put a setting back if you changed one, and do not press Record — it writes a meeting.
- **It drives only the application it started**, and closes only that one — including when it is
  killed rather than asked, once the application is running.
