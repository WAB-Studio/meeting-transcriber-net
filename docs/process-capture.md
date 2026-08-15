# Following one program's audio

Channel 0 records either everything the machine is playing or one program and the processes it
started. This is what was measured about the second one, and how to measure it again.

## What Windows gives, and what it does not

Process loopback is `ActivateAudioInterfaceAsync` against a virtual device rather than an endpoint,
with the process id in the activation parameters. Three things about it decide the design:

- **The floor is build 20348.** The application declares Windows 11 (22000) as its minimum, which is
  above it, so on any machine this installs on the API is there. The check exists anyway because the
  command line is not packaged and runs wherever .NET does — and because the answer belongs before a
  meeting starts rather than in the middle of one.
- **The virtual client will not say what format it mixes at.** `GetMixFormat` is not implemented, so
  the format is asked for rather than read. What is asked for is the format the default playback
  endpoint mixes at, which is what everything being played is already in — so channel 0 is the same
  file whichever way it was opened.
- **It numbers no frames.** Every packet comes back at device position zero, where an endpoint
  reports how many frames it had produced. The instants are real and correct, and that is what the
  positions are built from; `FramePositions` is the rule and says why.

There is no mode that follows one process alone: Windows offers include-this-tree and
exclude-this-tree, and the tree is what is wanted anyway, because the process a person points at
owns the window and the audio comes out of a child of it.

## Which program a typed name means

A name resolves to the **root** of the tree it matched — every match whose parent is also a match is
one of that root's children rather than another candidate. That is what makes `--process msedge`
work with seventeen `msedge` processes running. Two roots of one name is refused, because picking
one is picking which meeting gets recorded.

## What was probed, 2026-08-15, Windows 11 25H2 build 26200

Every run is `capture --seconds N --process <name-or-pid>`, with the tone played at a level the whole
machine's loopback heard at −18.9 dBFS.

| Program | Result |
| --- | --- |
| A program whose **child** plays | **Works.** −8.7 dBFS steady over 10 s, 1002 packets, nothing lost. The parent plays nothing itself, so this is the tree and not the process. |
| **Edge** (Chromium), Web Audio tone | **Works.** −12.0 dBFS over 6 s. The name resolved to the browser process out of 17; the audio comes from its audio-service child. |
| **Firefox**, the same page | **Works.** −12.0 dBFS over 6 s, resolved out of 10 processes. |
| **Teams** (WebView2, MSIX) | **Opens.** The name resolves to the window process, activation succeeds and the stream runs. Whether a meeting's audio lands in it is **not probed** — that needs a signed-in account and somebody on the other end. |
| **Zoom** | **Not probed.** Not installed on this machine. |

Two things the runs settled that are not in the table:

- **A program that cannot be followed does not fail.** Following `System` (pid 4) activates happily
  and delivers a silent stream — 3 s of packets, nothing lost, no audio. So a person who picks the
  wrong program gets silence, not an error, and what has to catch that is the meter rather than an
  exception. The fallback in the capture only ever fires for a Windows that has no such API at all.
- **The program exiting mid-recording does not end the recording.** Killing the followed process
  half way through 12 s left the stream running silent to the end, the file finished, and the six
  milliseconds the instants jumped recorded as the gap they were.

Levels are higher than the whole machine's for the same sound: a process stream is what the program
rendered, before the master volume the endpoint's loopback hears.

## Repeating it

The tone rig is three lines of PowerShell — a WAV of a sine wave, a process that loops it through
`System.Media.SoundPlayer`, and a second process that plays nothing. Follow the silent one while the
other plays and channel 0 has to come back silent; follow a parent whose child plays and it has to
come back loud. For a browser, `--autoplay-policy=no-user-gesture-required` with a throwaway
`--user-data-dir` is what makes a page play without somebody clicking it; Firefox wants a throwaway
profile with `media.autoplay.default` set to 0.
