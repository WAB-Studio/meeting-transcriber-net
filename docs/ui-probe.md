# Driving the app's windows

`tools/MeetingTranscriber.UiProbe` starts the packaged application, reads the UI Automation tree of
its windows, photographs them, presses what is on them, and closes it again. Two ways in over the
same verbs: a script, for a finding you want repeatable; MCP, for building a screen a turn at a time.

Run it by hand. It needs an interactive desktop, so it is never part of a build and nothing in
`dotnet test` may come to depend on it.

## Once per machine

If anybody else is driving the app from another checkout, give this one a package of its own. Write
`PackageIdentity.props` at the top of the checkout with a suffix nothing else on this machine is
using. Keep it short and keep it to letters, digits, a dash and a dot: Windows caps a package name
and most of the cap is already spent on a GUID, and a name that is too long or holds anything else
— an underscore, a space, an accent — is refused by `Add-AppxPackage` without a word about why. The
build refuses it first instead, and says how much room there is. The file is in `.gitignore`, and
every build here picks it up from then on:

```powershell
"<Project><PropertyGroup><PackageIdentitySuffix>-$(Get-Random -Maximum 99999)</PackageIdentitySuffix></PropertyGroup></Project>" |
  Set-Content PackageIdentity.props
```

That picks a number rather than a name because it will be pasted more often than it is read, and two
checkouts landing on the same suffix is the whole failure it exists to prevent. Put a word of your
own there if you prefer — the listing below names every registration against its folder, so the
suffix never has to be the memorable part. A build with a suffix prints the identity it settled on,
and a file setting none warns — the element is `PackageIdentitySuffix`, and a typo in it would
otherwise be indistinguishable from having no file at all.

Alone on the machine, skip that file.

Point the package registration at the build output. Check what is registered now:

```powershell
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4* | Select-Object Name, InstallLocation
```

If this checkout is not in that list against a path ending in `\win-x64`, register it. Remove
whatever it has first — registering over an existing registration keeps the old location. The remove
below is scoped to this folder, so if the name you want is in that list against **somebody else's**
folder, it removes nothing and the register then quietly leaves the name where it was: that is two
checkouts on one suffix, and the way out is a different suffix, not a second attempt.

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4* |
  Where-Object InstallLocation -Like "$(Get-Location)\*" | Remove-AppxPackage
Add-AppxPackage -Register (Resolve-Path src/MeetingTranscriber.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AppxManifest.xml)
```

Do it again whenever that path changes — another target framework — or whenever
`PackageIdentity.props` changes. Debug is the only configuration the suffix reaches: the product's
identity is `Package.appxmanifest`'s, and an untracked file on one machine does not get to decide
what a Release build is called.

A registration is machine-wide and outlives the folder it points at. Run those two middle lines
from the checkout before deleting it, or the machine keeps a package aimed at nothing.

A checkout with a package of its own gets its own redirected `LOCALAPPDATA`, so it opens in whatever
Windows says rather than in the language somebody last picked: the examples below are in Spanish and
a package with no preference yet opens in English here. `choose LanguagePicker` on it once and it
sticks. The corpus is not in there — every checkout shares one.

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

A worktree is its own checkout and gets its own copy, published the same way. It drives the build
that checkout wrote, under the name that checkout registered — which is what `PackageIdentity.props`
above is for. Two checkouts left on the same name are still one registration between them, and the
one that did not register last is refused at `start`, naming the folder that holds it.

## Every run

Build the application first. Changing the tool costs one more step and it only runs one way: end
the session, publish, open a new one. A connected server holds `bin/mcp` open, so a publish under
a live session fails on the same kind of lock this setup took out of the build — and a new session
is what reads `.mcp.json` anyway. `close` and `start` are verbs about the application, not about
the server.

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
- **It uses the real corpus, and the preference file of whichever package this checkout registered.**
  That is deliberate: it drives the real application. The corpus is one folder for every checkout, so
  do not press Record — it writes a meeting. The preferences are the package's own, so a checkout
  with a package of its own has its own.
- **It drives only the application it started**, and closes only that one — including when it is
  killed rather than asked, once the application is running.
