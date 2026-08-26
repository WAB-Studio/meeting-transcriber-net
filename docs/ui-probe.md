# Driving the app's windows

`tools/MeetingTranscriber.UiProbe` starts the packaged application, reads the UI Automation tree of
its windows, photographs them, presses what is on them, and closes it again.

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
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64 -nodeReuse:false
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4 | Remove-AppxPackage
Add-AppxPackage -Register (Resolve-Path src/MeetingTranscriber.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/AppxManifest.xml)
```

Do it again whenever that path changes — another configuration, another target framework.

## Every run

Build the application first, and keep `-nodeReuse:false` or the next solution build fails with
`MSB3027` (`docs/shell.md`).

```powershell
dotnet build src/MeetingTranscriber.App/MeetingTranscriber.App.csproj -p:Platform=x64 -nodeReuse:false
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...
```

Give `--out` a folder of its own: it is emptied of trees and pictures first, and it refuses to
touch a folder holding anything else. The application is closed on the way out either way.

If it refuses because the window would be older than the code, build and run it again.

## Walking from the recorder to the meetings

```powershell
dotnet run --project tools/MeetingTranscriber.UiProbe -- --out $env:TEMP\ui-probe `
  see recorder press MeetingsButton wait RefreshButton see meetings
```

```text
7feb8c95-4553-46f0-a036-6574f4cd7cb4_savbypjtf9g9c!App is process 38352, from C:\...\win-x64\MeetingTranscriber.App.exe
  see recorder
    recorder.tree.txt and recorder.png (1920x1023)
  press MeetingsButton
  wait RefreshButton
    on "Reuniones"
  see meetings
    meetings.tree.txt and meetings.png (1920x1023)
done, in C:\Users\pc\AppData\Local\Temp\ui-probe
```

## The verbs

- `see <name>` — write `<name>.tree.txt` and `<name>.png` of the screen. Changes nothing.
- `press <element>` — invoke it. Fails if it is disabled or cannot be invoked.
- `type <element> <text>` — set a field's value. Fails if it is disabled, read only, or takes none.
- `choose <list> <item>` — open the list, pick the item by name, shut it again.
- `wait <element>` — block until it is on a window, and make that window the screen from then on.

Put a `wait` after any `press`, `type` or `choose` whose effect you are about to `see`. It is the
only thing here that synchronises.

A tree line is `Type #x:Name "what it says"`, indented by depth, with `value=`, `help=`, `status=`,
`disabled` and `offscreen` appended when they apply. `value=` is what `type` left in a field. Grep
it:

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

## Exit codes

`0` it ran · `1` the screen or the application failed it · `2` the script was wrong and nothing was
started · `3` the probe broke, which is not news about a screen

## What it will not do

- **`press` is `Invoke` only, and `type` is `SetValue` only.** Either one fails naming what the
  control offers instead, which is how you find out it wanted another verb.
- **It will not bring a window forward.** A window behind another still photographs correctly.
- **It uses the real corpus and the real preference file.** Put a setting back if a script changed
  it, and do not press Record — issue #172.
- **It drives only the application it started**, and closes only that one.
