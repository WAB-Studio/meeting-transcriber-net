# Seeing a screen run

`tools/MeetingTranscriber.UiProbe` starts this application, reads the automation tree of the window
it opens, photographs it, presses what is on it and closes it again. It is how a screen gets
checked by somebody who is not sitting in front of it.

Everything else that checks a screen reads the screen's **source** — `MeetingTranscriber.App.Tests`
opens the `.xaml` and the `.xaml.cs` as text, because a WinUI tree needs a UI thread and a packaged
host and a build agent has neither. Source says what a screen was written to do. Only a running
window says that it opened at all, that the button is alive, and that the second screen is reached
by pressing something on the first.

It needs a desktop somebody is logged into, so **it is run by hand and never by a build**. Nothing
under `src/` or `tests/` references it, and nothing in `dotnet test` may come to depend on it.

## Once per machine: make what Windows starts be what you built

This is the one thing that goes wrong silently, so do it before anything else.

Windows starts the package layout it has **registered**. `dotnet build` does not touch that
registration — it writes new assemblies and the shell goes on launching whatever the layout held
when somebody last registered it. Nothing about a stale window looks stale: it opens, it reads
correctly, and it is showing code from days ago.

Point the registration at the build output root, which is what `dotnet build` refreshes:

```powershell
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4 | Select-Object InstallLocation
```

If that is anything other than `...\win-x64` — in particular if it ends in `\AppX` — replace it.
Registering over an existing one keeps the old location, so it has to go first:

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64 -nodeReuse:false
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4 | Remove-AppxPackage
Add-AppxPackage -Register (Resolve-Path src/MeetingTranscriber.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AppxManifest.xml)
```

The probe refuses to run when the assembly Windows started is older than the newest `.cs` or
`.xaml` in the projects the application is built from, and its message says both stamps. That is
the check, not this paragraph: it is why forgetting to build shows up as a failure rather than as a
picture of the wrong week.

## The loop

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64 -nodeReuse:false
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...
```

`-nodeReuse:false` is not optional if you are going to build the solution afterwards, which the
four commands do. `docs/shell.md` says what it costs to leave it off, and issue #173 is the fix
that would let it go.

Walking from the opening screen to the meetings, writing a tree and a picture of each:

```powershell
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out probe `
  see recorder press MeetingsButton wait RefreshButton see meetings
```

```text
7feb8c95-...-savbypjtf9g9c!App is process 4472, from ...\win-x64\MeetingTranscriber.App.exe
  see recorder
    recorder.tree.txt and recorder.png (1920x1023)
  press MeetingsButton
  wait RefreshButton
    on "Reuniones"
  see meetings
    meetings.tree.txt and meetings.png (1920x1023)
```

The application is closed on the way out, whether the script finished or failed, so the next run
starts one rather than finding this one.

**What it exits with:** `0` it ran, `1` the screen or the application failed it — that is a finding,
`2` the script was wrong and nothing was started, `3` the probe itself broke, which is a bug in the
probe and not news about a screen.

## The four things it does

| Instruction | What it does |
| --- | --- |
| `see <name>` | Writes `<name>.tree.txt` and `<name>.png` of the screen. Changes nothing |
| `press <element>` | Invokes it. A disabled control, or one that is not pressable, is a failure |
| `choose <list> <item>` | Opens the list, picks the item by name, shuts it again |
| `wait <element>` | Blocks until it is on a window, **and that window is the screen from then on** |

An **element** is named by the `x:Name` its XAML gave it, or by the words on it. Three tiers, tried
in order, and the first with anything in it decides: exact `x:Name`, then exact words, then any
element whose words contain what was asked for, ignoring case. A tier with more than one match is a
failure that lists them rather than a coin toss. The third tier is also how a name with an accent
in it is reached from a shell that mangles one — `choose LanguagePicker Espa` finds `Español`.

**Which window is the screen** is the question `wait` exists to answer. Failing a `wait`, it is the
only window the application has; several windows and none of them named is a failure telling you to
`wait` for something on the one you meant. It is never "whichever is in front": this tool cannot
raise a window and does not try, so the front is decided by the person's last click, and an
artifact of the wrong screen under the right name would be believed.

**`wait` is the only thing that synchronises.** A press whose effect you are about to photograph
needs one after it — `press MeetingsButton see meetings` is a race, and
`press MeetingsButton wait RefreshButton see meetings` is not.

## What it does not do

- **It does not type.** The card that built this asked for a field, and no screen in this
  application has one. A `type` verb here would be the one instruction that had never run against a
  window, in a tool whose whole premise is that only a running window tells the truth. The screen
  that first grows a field adds it, against a real target: it is a verb, an arity and ten lines of
  `ValuePattern`.
- **It presses; it does not toggle or select.** `press` is `Invoke` and nothing else. A control that
  offers something else is a failure that names what it does offer, so the screen that first needs
  a switch says which verb to add.
- **It cannot bring a window forward.** Windows does not let a background process steal the front,
  and nothing here tries. It does not need to: a picture is printed out of the window itself, so a
  screen behind another photographs correctly.
- **It uses the real corpus and the real preference file.** There is no throwaway anything: a script
  that changes the language changes it, and the next launch — by the probe or by a person — opens
  in it. Put the setting back if you changed it, and do not press Record. Issue #172 is the seam
  that would make this untrue.
- **It only drives what it started.** Activating this application while a copy is open starts a
  second process rather than joining the first, so the probe never touches a window somebody else
  opened — and never closes one either. Two probes at once are two applications over one corpus,
  which is a thing to avoid for the corpus's sake, not the probe's.
