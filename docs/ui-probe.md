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

Alone on the machine, skip that file — **unless the application is also installed here.** A machine
with the signed package installed is not alone: its checkout without a suffix registers under the
installed package's own name, and registering the build takes that name, the installed
application's language and theme files included. Give a checkout on such a machine a suffix, and
remove its registration when you are done.

Point the package registration at the build output. Check what is registered now:

```powershell
Get-AppxPackage -Name 7feb8c95-4553-46f0-a036-6574f4cd7cb4* | Select-Object Name, InstallLocation
```

The probe asks Windows that same question rather than reading the build output: it lists this user's
packages, keeps the ones registered against a folder at or inside this checkout, and refuses when
there is none or more than one. So building without registering is a refusal that says so, and a
Release build sitting beside Debug cannot make it name a package nothing has registered.

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
sticks. The corpus is not in there — every checkout shares the probe's home.

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
whichever it finds first, and that is the repository's.

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

**A verb is also refused when the published copy of the tool is older than what the tool is built
out of.** That is the other half of the same trap: `dotnet build` never writes `bin/mcp`, so an edit
to the probe reaches nothing until it is published, and without the refusal every answer for the
rest of the session would come out of an older tool without a word. It follows `ProjectReference`,
so an edit to `MeetingTranscriber.Infrastructure` or `MeetingTranscriber.Domain` refuses it too —
the probe references the first of those to make its own corpus, and `bin/mcp` carries both. The way
out is the same three steps in the same order: end the session, publish, open a new one. What is
never compared is the published copy's own folder, which would be a copy compared with itself.

**Both of those also watch `Directory.Packages.props` and `Directory.Build.props` at the root**,
which are sources of what is running and sit above every project folder rather than under one. A
package bump changes what the application contains — a different Windows App SDK, a different SQLite
— and moves no `.cs`, `.xaml` or `.csproj` anywhere, so without those two the refusal you are owed
never comes and the tree you are handed is yesterday's build described as today's. The refusal you
will meet instead reads *the window is showing code from before Directory.Packages.props was last
edited*, and nothing else in this document would explain it. The way out is the same three steps,
and they do lift it: a build after touching either file really does restamp the application's
assembly, which was measured rather than assumed.

The cost of that, said plainly: `Directory.Packages.props` is one file for the whole repository, so
bumping a package only `tests/` uses refuses the probe too, and for a packaged window the way out is
the publish-and-re-register above rather than a plain build. That is the wide side of a refusal
whose narrow side would be missing a bump that changed the window, and this is the direction chosen.

**When that is asked differs by host, on purpose.** Over MCP it is asked every turn, because the
agent taking the turns is the one editing. A script is asked once, at `start`, and by no verb after
it: nothing in a fixed list of instructions edits code, and a refusal raised halfway would end a
walk that had already spent six minutes of real recording over an edit that changed nothing the
window is showing. So a script's trees are evidence about the commit it was started at — if you
edit while a long one runs, nothing will tell you, and what it wrote is still about the old build.

## The first step, and where the foot is

**Every `start` opens on *Primeros pasos* until somebody has been named.** The probe's corpus has
nobody flagged as the user, and the settings screen opens itself in its first-time arrangement
exactly when the corpus is reachable and nobody has said who is using it. So a walk presses
`StartButton` before anything on the recorder, and what it presses it on is the settings screen,
not the main window — the recorder is behind it until then.

Starting on an empty corpus means emptying the probe's home, `%USERPROFILE%\MeetingTranscriber.ui-probe`,
by hand: no verb does it, and a corpus that already has a name in it opens on the recorder.

**`SettingsButton` is a word at the foot and is not on screen while a sub-screen has the room.**
The way out of a sub-screen is `BackButton` in the bar, which exists only while one has the room, so
a walk that has opened the settings presses that and not the foot to leave. `wait` on the thing you
expect before you press either: a sub-screen arrives by a fade of 250 ms, and a tree read in the
first frame still shows the screen it replaced.

## Record may be pressed

**It may: the corpus on this machine holds nothing sensitive and nothing worth rescuing, and the
repository's owner said so in as many words.** So a
probe records meetings into it, keeps and discards recordings on the list, and kills the
application in the middle of both. A machine whose corpus has something to lose gets the rule
back, and this paragraph is where that goes.

**The probe drives a home of its own.** It is `%USERPROFILE%\MeetingTranscriber.ui-probe`, made on
the first `start` of a session that needs it, holding a real corpus: the application opens it the
way it opens any other. The application is told its home on its launch line,
`--home "<folder>"` (`ApplicationHome` in Infrastructure writes and reads that line, so the two
halves cannot disagree), and keeps its corpus pointer, its Claude Code pointer and its first corpus
there. **Nothing under `%USERPROFILE%\MeetingTranscriber` is read, moved or written by a probe
session**, so there is no pointer to put aside, nothing to put back and nothing to heal by hand after
a killed run. The line `start` prints says the home the application was started with; the settings
screen's folder card shows the folder it is really using. **A launch that was told its home writes
`resolved-home` inside it, and `start` refuses, closing the application, when that file does not name
the probe's home within ten seconds of the window opening**: a launch line that did not arrive leaves
the application on the owner's corpus with nothing on its screen saying so, and the absent report is
the only place that shows.

**The user's own application is not disturbed.** It opens on the user's own corpus whether or not a
probe session is open, because the probe moves nothing the user's reads.

**Two probe sessions at once are still two writers over one SQLite file** — the probe's corpus is one
folder for every checkout, the same way the user's is. Run them one at a time, and read the list
before you start, because a killed run leaves a recording at the top of it waiting for Discard or
Keep.

**Nothing tells a meeting a probe made from a meeting somebody recorded**, and the corpus is built
never to lose either: `docs/corpus.md` files `audio.wav` and the spool's blocks as sources, and
nothing removes a waiting recording but a person choosing to. So a run's meetings stay until
somebody presses Discard on them, and they cost what they weigh — both channels spool at the rate
the devices really run, about 44 MB a minute on this machine, six minutes is 265 MB, and the spool
folder stays after the meeting is made.

Recording needs a microphone, what channel 0 follows, and what will be spoken, all three chosen
before `RecordButton` is anything but disabled. After a stop the window keeps all three, and at
launch what will be spoken starts as the language of the last meeting that has a recording, so a
probe corpus that has recorded before opens with it chosen. Pausing and carrying on are one press,
`PauseButton`, which is named *Pausar* while recording and *Seguir* while paused; there is no
`ResumeButton`. While choosing, the two meters listen only when the window is the one in front, and
the probe's never is, so a meter moving before a meeting is not something it can show. Saving a six-minute meeting took under five seconds
here, so a script meaning to catch the saving state samples it with consecutive `see`s rather than
a `sleep`.

## What it still shares with you, and how it never spends

**What a home does not isolate**, in so many words:

- **The Deepgram key.** It is in Windows Credential Manager under `MeetingTranscriber:deepgram`, one
  per Windows user and not per package or per home. No walk saves or removes it: none presses
  *Guardar* or *Quitar* on the key card, or *Empezar* with a key typed on *Primeros pasos*, and a
  *Primeros pasos* walk leaves the key field empty.
- **The language and theme files.** On a checkout with no `PackageIdentitySuffix`, the package's own
  `LocalApplicationData\MeetingTranscriber` (`ui-language`, `ui-theme`) are the installed
  application's files. A suffix gives a checkout its own.
- **The front of the desktop and the mouse**, below.

**Nothing a walk does may spend.** The command line's `record` queues whatever the corpus's *after a
recording* setting asks for, and the application, once started over that corpus, sends the queue on
the real key (`StartWhatThisLaunchOwesTheCorpus`, `JobRunner.PumpAsync`). So, for a walk over a
meeting recorded through the command line:

1. Before any `record` into the home, its *after a recording* setting is *Nada*: the walk presses
   `AfterNothing` on the settings (or on *Primeros pasos*), and `see` shows it ticked.
2. The `record` output's line `queued: nothing — transcribing is a separate press` is read.
3. Before every `start` over the home, `dotnet run --project src/MeetingTranscriber.Cli -- status
   --corpus "%USERPROFILE%\MeetingTranscriber.ui-probe"` runs and its `jobs` line is read. A
   `pending` or `running` count other than zero stops the walk: the application is not started over
   that home until a person has dealt with the job.
4. No walk presses *Transcribir*, *Resumir*, *Resumir de nuevo* or *Reintentar*.

**Microphone consent stays a person's step.** Windows asks for it with its own prompt the first time
a package opens a microphone and keeps the answer per package family, so each checkout with its own
suffix is asked again. The probe never answers that prompt and never writes the consent store.

A recorded meeting for a walk comes from the command line, under those four steps: `dotnet run
--project src/MeetingTranscriber.Cli -- record --corpus "%USERPROFILE%\MeetingTranscriber.ui-probe"
--language es --seconds 20`. That runs without package identity, under Windows' *desktop apps*
microphone switch. A walk of *Empezar a grabar* itself needs a package a person has allowed to use
the microphone. A meeting with a transcript comes from `import-response` of a committed fixture,
which spends nothing.

## What it takes from the desktop

**While a probe session has an application open, its window is in front of the desktop and stays
there.** `start` brings the window to the foreground and makes it topmost, and every verb checks both
again before it acts. The foreground is taken in three ways in order, each read back before the next:
`SetForegroundWindow`; attaching this thread's input to the thread of the window that has the
foreground, then `BringWindowToTop` and `SetForegroundWindow`; and UI Automation's `SetFocus`. When
all three are refused the verb fails naming what has the foreground. A popup of the screen having the
foreground counts as the screen having it, so taking it back never dismisses an open list. A
minimised window is restored first.

**`hover`, `drag` and `select` use the person's own mouse**, through `SendInput` in physical pixels
over the whole virtual desktop. Do not use the machine while one runs.

**`see` is a copy of the desktop** over the screen's rectangle, grown to cover its popups and clipped
to the virtual desktop. So it shows an open list, a flyout
and a tooltip, and it shows whatever covers the window that is not part of the application. The
check that the inside is not one flat colour stays, with its ten-second budget.

**A screen's popups are part of it.** The screens are the process's top-level windows that have no
owner. A screen's popups are its process's visible top-level windows whose owner chain reaches the
screen. Their trees are written under the screen's, each after a line `popup hwnd=0x… "<name>"`;
`press`, `choose` and the rest look in the screen and in its popups; `wait` names the screen even when
what it waited for is on a popup. **This is the owner rule, and it is not yet measured:** whether a
XAML popup is a window with an owner, and whether it is read as one, has to be recorded against a
registered build with a list open and a tooltip showing. If a popup turns out to have no owner, the
rule becomes the process's visible top-level windows that are not screens and whose rectangle
intersects the monitor the screen is on.

## The verbs

- `see` — the tree of the screen and its popups, and a picture of the desktop over them. Changes
  nothing.
- `press <element>` — invoke it. Fails if it is disabled or cannot be invoked.
- `type <element> <text>` — set a field's value. Fails if it is disabled, read only, or takes none.
- `choose <list> <item>` — open the list, pick the item by name, shut it again. A list too long
  to draw whole is asked what it holds rather than walked, so an item below the fold is named the
  same way as one on screen. A pick the screen answers by disabling the list — channel 0's picker
  after *Cambiar*, where the pick moves the channel — counts as made: UI Automation refuses the call
  it was in the middle of, and a list left disabled is how the move shows. A list still enabled
  after a refusal is a refusal.
- `key <element> <enter|escape|tab|space|up|down>` — focus it and send that key. Fails if it is disabled, if it
  will not take focus, if it takes focus and does not get the keyboard — which is what a window
  that is not in front does — or if Windows refuses the keystroke outright, which a locked
  workstation and a secure desktop both do. A row and the label inside it carry the same words, so
  where a name matches both, the one that can hold the keyboard is the one a key goes to.
- `wait <element>` — block until it is on a window, and make that window the screen from then on.
- `sleep <seconds>` — let that long pass, touching nothing. Script host only, capped at twenty
  minutes.
- `kill` — end the application the way a crash does, with nothing asked and nothing let finish.
- `hover <element>` — move the pointer to the centre of the element, hold it there two seconds and read
  the cursor every 50 ms. Answers with the cursor as a sequence, `hand from 0.0 s; arrow at 0.85 s;
  hand at 1.00 s`, then the tree, which has the words of a tooltip it opened. The cursor is named by
  comparing `GetCursorInfo` with the system's arrow, ibeam, wait, cross, hand, sizeall, sizewe, sizens, no
  and appstarting; any other handle reads `another (0x…)`.
- `drag <element> <dx>,<dy>` — press the left button at the element's centre, move by that many
  physical pixels in 12 steps 20 ms apart and let go; `0,0` is a click. Each number is within ±4000,
  and anything else is refused before anything starts. Answers with the window's rectangle before and
  after, and the element's range value before and after when it has one.
- `select <element> <text>` — find `text` with the element's text pattern, press at the left edge of
  its first line, drag to the right edge of its last and let go, then read the pattern's own
  selection and fail when it is not `text`. An element with no text pattern is refused naming what it
  does offer. A real drag and not `TextPatternRange.Select`, because the *Corregir* pill listens to
  the selection a mouse makes, and the pattern's own is not shown to raise it.
- `size <width>x<height>` — set the window in physical pixels, keeping its position, each between 200
  and 8000. Answers with the rectangle the window now has, which is not the one asked for when its
  content will not go smaller.

`see` writes one line under the tree's header, `cursor: <name> at x,y`, when the cursor is over the
picture. No cursor is drawn into it.

Put a `wait` after any `press`, `type`, `choose` or `key` whose effect you are about to look at. It
is the only thing here that synchronises.

**A `see` whose desktop will not be photographed still writes the tree**, says why there is no
picture, and fails on it. The picture is a copy of the desktop, so what fails is a window
that is minimised, covered by something that is not the application, or that never drew. `wait` and
`press` still work, and the tree beside the failure is the screen.

`sleep` is for the one screen that is a function of elapsed real time — a meeting running — and
nothing else here makes ninety seconds pass: `wait` is capped at fifteen seconds and returns on the
first frame that matches, which is the opposite of holding a screen. It is the script host's own,
because over MCP a turn is how time passes.

**`sleep` is never a substitute for `wait`.** Waiting for something to happen with a stopwatch is a
script that passes here and fails on a slower machine, and the failure reads as the application
having regressed. If what you are waiting for reaches a screen, `wait` for it.

`kill` is for what a *later* start finds: a recording nobody stopped, a save the process died in
the middle of. Nothing works after it, so it is a script's last instruction and a second run is
what reads what it left behind. Everything else wants `close`, which is what lets the application
finish whatever it was writing — `kill` on a meeting being saved loses that meeting's save, on
purpose.

**Give that second run an `--out` of its own.** A run empties its folder of trees and pictures
before it starts anything, so pointing both halves of a two-run walk at one folder loses the first
half — which is the half holding what the application looked like before it died.

## Over MCP, a turn at a time

`start` first — nothing else works until an application is open — and `close` when you are done,
because it stays open between calls. `close`, build the application, `start` is how you pick up a
change to it. A refused `start` leaves the session you had alone. Neither `sleep` nor `kill` is
here: a turn is already how time passes, and every walk that has wanted a crash has been a script,
because what comes after one is a second run reading what the first left.

Every verb answers with the tree of the screen it became, so you choose the next step from the last
answer instead of writing the whole walk in advance. `see` also returns the picture, inline.

```text
start                          → 7feb8c95-...!App is process 12216, from C:\...\win-x64\...exe
                                 its home is C:\Users\...\MeetingTranscriber.ui-probe; nothing under
                                 %USERPROFILE%\MeetingTranscriber is read or written; the Deepgram
                                 key is shared
                                 window "Meeting Transcriber" ... (the whole tree)
press PackagingChecksButton    → pressed PackagingChecksButton
                                 The application has 2 windows open — "Comprobaciones de
                                 empaquetado", "Meeting Transcriber" — and the script has not said
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
    on "Meeting Transcriber"
  see whole
    whole.tree.txt and whole.png (1920x1023)
done, in C:\Users\pc\AppData\Local\Temp\ui-probe
```

`--refused-corpus` starts the application over a corpus that will not open, for the walk of the
refused-corpus screen. It starts the application with the home `%USERPROFILE%\MeetingTranscriber.ui-probe.refused`,
beside the probe's own, whose own pointer names a subfolder that held a corpus when it was chosen and
holds none now, so the launch resolves *no corpus in the folder*. Nothing is put aside or back; the
switch is for the script host and the MCP host does not take it.

```powershell
dotnet run --project tools/MeetingTranscriber.UiProbe -- --refused-corpus --out $env:TEMP\ui-probe `
  see refused press ChangeWhereItIsKept
```

Exit: `0` it ran · `1` the screen or the application failed it · `2` the script was wrong and
nothing was started · `3` the probe broke, which is not news about a screen

## Reading a tree

A line is `Type #x:Name "what it says"`, indented by depth, with `value=`, `help=`, `status=`,
`disabled` and `offscreen` appended when they apply. `value=` is what `type` left in a field.

```text
      Button #RecordButton "Empezar a grabar"  disabled
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
to `wait` for something on the screen you meant. It is never whichever window is in front: the
screen is what the script said, and the desktop is made to agree with it.

## Switching Windows between light and dark

The application is drawn in the theme chosen under *Aplicación* in the settings: *Sistema*, *Claro*
or *Oscuro*, and *Sistema* is the theme Windows is set to, followed through a switch made while the
window is open. A walk of Windows' own switch is a walk of *Sistema*, so it begins with
`choose ThemePicker Sistema` — a package that was left on *Claro* or *Oscuro* stays there whatever
the registry says, and the walk would photograph one theme twice. Choosing *Claro* or *Oscuro* needs
no registry at all, and the pick is kept in the package's own preference file like the language's.
Changing the machine's setting under a running window: `AppsUseLightTheme` is 0 for dark and 1 for
light; writing it is not enough, because nothing running is told, so the same script broadcasts
`WM_SETTINGCHANGE` with `ImmersiveColorSet`.

```powershell
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$had = (Get-ItemProperty $key).AppsUseLightTheme   # write this down: it is what goes back

Add-Type -Namespace Probe -Name Broadcast -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint Msg, System.UIntPtr wParam,
    string lParam, uint flags, uint timeout, out System.UIntPtr result);
'@

function Set-AppTheme([int] $light) {
    Set-ItemProperty $key -Name AppsUseLightTheme -Value $light
    $result = [System.UIntPtr]::Zero
    # HWND_BROADCAST, WM_SETTINGCHANGE, SMTO_ABORTIFHUNG
    [Probe.Broadcast]::SendMessageTimeout([System.IntPtr]0xffff, 0x1A, [System.UIntPtr]::Zero,
        'ImmersiveColorSet', 2, 5000, [ref]$result) | Out-Null
}

Set-AppTheme 0      # dark
Set-AppTheme 1      # light
Set-AppTheme $had   # put back what the machine had
```

Put back what the machine had, whatever the walk did, including when it fails halfway: it is the
owner's setting and not the probe's. The title bar is the application's own: the mark, the name and
the back button are in the tree like anything else, and the three caption buttons at its right are
Windows' and read from a photograph (`see`), because they carry no automation name of this
application's. Under a chosen theme they are drawn in it, and a photograph of *Oscuro* over a light
Windows is the proof.

## What it will not do

- **`press` is `Invoke` only, and `type` is `SetValue` only.** Either one fails naming what the
  control offers instead, which is how you find out it wanted another verb. `key` is the answer
  when a control wants a keystroke and neither of those is it — a field that commits on Enter is
  the case it was added for.
- **It will not drive Windows' own pickers.** *Cambiar* on the folder card opens the folder picker,
  which is Windows' and has no tree this probe reads, so choosing a folder — and with it moving the
  meetings to an empty one — is walked by a person. Everything around it is drivable: the card, the
  tick, *Mover* and *Cancelar* once a folder is chosen.
- **It brings its window forward, and holds it there.** See *What it takes from the desktop*. What it
  cannot do is take the foreground from a window of higher integrity, a locked workstation or a
  secure desktop; those make the verb fail naming what has it, rather than act on the wrong window.
- **It drives a home of its own and the preference files of whichever package this checkout
  registered.** The home is `%USERPROFILE%\MeetingTranscriber.ui-probe`, one folder for every
  checkout, and the user's own folder is never touched — see *Record may be pressed*. The
  preferences are the package's own, so a checkout with a package of its own has its own.
- **It drives only the application it started**, and closes only that one — including when it is
  killed rather than asked, once the application is running.
