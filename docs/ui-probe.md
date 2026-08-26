# Driving the app's windows

`tools/MeetingTranscriber.UiProbe` starts the packaged application, reads the UI Automation tree of
its windows, photographs them, presses what is on them, and closes it again. Two ways in over the
same verbs: a script, for a finding you want repeatable; MCP, for building a screen a turn at a time.

Run it by hand. It needs an interactive desktop, so it is never part of a build and nothing in
`dotnet test` may come to depend on it.

## Once per machine

If anybody else is driving the app from another checkout, give this one a package of its own. Write
`PackageIdentity.props` at the top of the checkout, putting a name of your own in it — `-slot-3`,
`-mine`. **Thirteen characters at most, including the dash**, and different from every other
checkout on this machine. Windows caps a package name at fifty and this one already spends
thirty-six on a GUID; over that, `Add-AppxPackage` refuses the manifest and does not say why. It is
in `.gitignore`, and every build here picks it up from then on:

```powershell
"<Project><PropertyGroup><PackageIdentitySuffix>-slot-3</PackageIdentitySuffix></PropertyGroup></Project>" |
  Set-Content PackageIdentity.props
```

Alone on the machine, skip that file.

Point the package registration at the build output. Check what is registered now:

```powershell
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4* | Select-Object Name, InstallLocation
```

If this checkout is not in that list against a path ending in `\win-x64`, register it. Remove
whatever it has first — registering over an existing registration keeps the old location:

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4* |
  Where-Object InstallLocation -Like "$(Get-Location)\*" | Remove-AppxPackage
Add-AppxPackage -Register (Resolve-Path src/MeetingTranscriber.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AppxManifest.xml)
```

Do it again whenever that path changes — another configuration, another target framework — or
whenever `PackageIdentity.props` changes.

A registration is machine-wide and outlives the folder it points at. Run those two middle lines
from the checkout before deleting it, or the machine keeps a package aimed at nothing.

A checkout with a package of its own gets its own redirected `LOCALAPPDATA`, so it opens in whatever
Windows says rather than in the language somebody last picked: the examples below are in Spanish and
a package with no preference yet opens in English here. `choose LanguagePicker` on it once and it
sticks. The corpus is not in there — every checkout shares one.

Then register the MCP server. It is registered machine-wide because an agent may be working from a
worktree: it drives whichever checkout it is started from, and the package that checkout was built
with — with nothing registered under that package it refuses and says so.

```powershell
dotnet build tools/MeetingTranscriber.UiProbe/MeetingTranscriber.UiProbe.csproj
claude mcp add ui-probe --scope user -- (Resolve-Path tools/MeetingTranscriber.UiProbe/bin/Debug/net10.0-windows/MeetingTranscriber.UiProbe.exe) --mcp
claude mcp get ui-probe
```

The last line must say `✔ Connected`. Register the exe and not `dotnet run`: a build writes to
stdout, and stdout is the protocol.

## Every run

Build the application first. Build the tool too if you changed it — MCP starts the exe, not the
source.

Anything is refused once the application is older than the code on disk. To pick up a change:
close, build, start — in that order, because a running application holds its own assemblies open
and the build fails on them. A build alone does not lift the refusal; only starting again does.

## The verbs

- `see` — the tree of the screen, and a picture of the window. Changes nothing.
- `press <element>` — invoke it. Fails if it is disabled or cannot be invoked.
- `type <element> <text>` — set a field's value. Fails if it is disabled, read only, or takes none.
- `choose <list> <item>` — open the list, pick the item by name, shut it again.
- `wait <element>` — block until it is on a window, and make that window the screen from then on.

Put a `wait` after any `press`, `type` or `choose` whose effect you are about to look at. It is the
only thing here that synchronises.

## Over MCP, a turn at a time

`start` first — nothing else works until an application is open — and `close` when you are done,
because it stays open between calls. `close`, build, `start` is how you pick up a change. A refused
`start` leaves the session you had alone.

Every verb answers with the tree of the screen it became, so you choose the next step from the last
answer instead of writing the whole walk in advance. `see` also returns the picture, inline.

```text
start                          → 7feb8c95-...!App is process 12216, from C:\...\win-x64\...exe
                                 window "Grabar una reunión" ... (the whole tree)
press MeetingsButton           → pressed MeetingsButton
                                 The application has 2 windows open — "Reuniones", "Grabar una
                                 reunión" — and the script has not said which one it is on.
wait RefreshButton             → on "Reuniones"
                                 window "Reuniones" ... (the whole tree)
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
  see recorder press MeetingsButton wait RefreshButton see meetings
```

```text
7feb8c95-4553-46f0-a036-6574f4cd7cb4_savbypjtf9g9c!App is process 38684, from C:\...\win-x64\MeetingTranscriber.App.exe
  see recorder
    recorder.tree.txt and recorder.png (1920x1023)
  press MeetingsButton
  wait RefreshButton
    on "Reuniones"
  see meetings
    meetings.tree.txt and meetings.png (1920x1023)
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
      Button #MeetingsButton "Reuniones"
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
- **It uses the real corpus, and the preference file of whichever package this checkout registered.**
  That is deliberate: it drives the real application. The corpus is one folder for every checkout, so
  do not press Record — it writes a meeting. The preferences are the package's own, so a checkout
  with a package of its own has its own.
- **It drives only the application it started**, and closes only that one — including when it is
  killed rather than asked, once the application is running.
