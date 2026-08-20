# How channel 0 is captured

Channel 0 records either everything the machine is playing or one program and the processes it
started. Both are the same Windows call under two modes, and neither of them opens a device. This is
what was measured about that, and how to measure it again.

## What Windows gives, and what it does not

Process loopback is `ActivateAudioInterfaceAsync` against a virtual device rather than an endpoint,
with a process id and a mode in the activation parameters. Four things about it decide the design:

- **The floor is build 20348.** The application declares Windows 11 (22000) as its minimum, which is
  above it, so on any machine this installs on the API is there. The check exists anyway because the
  command line is not packaged and runs wherever .NET does — and because the answer belongs before a
  meeting starts rather than in the middle of one. Since the whole machine comes this way too,
  below that build there is no channel 0 at all rather than one of its two shapes missing.
- **The virtual client will not say what format it mixes at.** `GetMixFormat` is not implemented, so
  the format is asked for rather than read. What is asked for is the format the default playback
  endpoint mixes at, which is what everything being played is already in — so channel 0 is the same
  file whichever way it was opened, and a channel moved from one to the other mid-meeting goes on
  writing into the same spool.
- **It numbers no frames.** Every packet comes back at device position zero, where an endpoint
  reports how many frames it had produced. The instants are real and correct, and that is what the
  positions are built from; `FramePositions` is the rule and says why.
- **A client whose targets are playing nothing is handed silence.** Not nothing — silence, in
  packets, at the rate they would arrive at anyway. Microsoft documents it, and it is why the whole
  machine comes this way; the section below is what that replaced.
- **The playback endpoint is still asked a question.** Not played through, asked: the virtual client
  will not say what it mixes at, so the format comes off the default render endpoint before either
  mode is activated. So channel 0 needs an endpoint that answers and never one it can open a stream
  on — which is the whole of what changed, and what the exclusive-mode run below measures.

There are two modes and no third: include-this-tree and exclude-this-tree. A tree rather than a
process suits following one program, because the process a person points at owns the window and the
audio comes out of a child of it. Everything but this application's own tree is the whole machine,
which is the only way Windows has of being asked for all of it — and excluding our own is what it
would say anyway, so nothing the application ever plays can land in somebody's meeting.

## Why the whole machine is not the playback endpoint's loopback

It was, until 2026-08-20, and what that cost is the reason it is not.

`WasapiLoopbackCapture` on the default playback endpoint hands over nothing at all while nothing is
playing into it — not silence, no packets. A meeting where nobody shared their screen for ten
minutes came back ten minutes short, with everything after it moved earlier and nothing in the file
saying so. Keeping the endpoint awake meant this application opening a **playback** stream on it and
pushing inaudible silence through for the length of the meeting, which made recording the machine
depend on being able to play through it. Another application holding the speakers in exclusive mode,
a stuck audio service or a driver that refused the format each cost channel 0 the stretches nobody
happened to be playing through.

Three other things fall out of the change, and they are the product's rather than the mechanism's:

- **Channel 0 is no longer one device's audio.** It is what this machine plays, wherever it comes
  out. A machine playing through speakers and a headset at once should put both in, where recording
  an endpoint put in whichever of them Windows was calling the default. *Should*, not *does*: this
  machine has one render endpoint, so two at once is what the API means and not something measured
  here.
- **Channel 0 is what programs rendered, before the volume slider.** An endpoint's loopback heard
  the mix after the master volume; a process loopback hears what the program handed the engine. The
  same tone rig read −18.9 dBFS through the old path and −8.7 dBFS through this one, so channel 0
  is about 10 dB hotter than it was and a meeting recorded with the speakers turned down or muted
  now has that stretch at full level rather than quiet or silent. That is the better recording of
  the two — what somebody wanted was the call, not the volume they happened to be listening at —
  but it is a change in what the file holds and not only in how it was obtained.
- **The card beside a recording says which of the two it was.** It used to be worked out from the
  source: a channel following a program named no device and the whole machine named the endpoint it
  was recording. Neither names a device now, so `others_capture_mode` is a field of the card's own,
  and a card naming a device for channel 0 is refused rather than read.

## Which program a typed name means

A name resolves to the **root** of the tree it matched — every match whose parent is also a match is
one of that root's children rather than another candidate. That is what makes `--process msedge`
work with seventeen `msedge` processes running. Two roots of one name is refused, because picking
one is picking which meeting gets recorded.

## What was probed, following one program, 2026-08-15, Windows 11 25H2 build 26200

Every run is `capture --seconds N --process <name-or-pid>`, with the tone played at a level the whole
machine's loopback heard at −18.9 dBFS.

| Program | Result |
| --- | --- |
| A program whose **child** plays | **Works.** −8.7 dBFS steady over 10 s, 1002 packets, nothing lost. The parent plays nothing itself, so this is the tree and not the process. |
| **Edge** (Chromium), Web Audio tone | **Works.** −12.0 dBFS over 6 s. The name resolved to the browser process out of 17; the audio comes from its audio-service child. |
| **Firefox**, the same page | **Works.** −12.0 dBFS over 6 s, resolved out of 10 processes. |
| **Teams** (WebView2, MSIX) | **Opens.** The name resolves to the window process, activation succeeds and the stream runs. Whether a meeting's audio lands in it is **not probed** — that needs a signed-in account and somebody on the other end, and there are reports against Microsoft's own sample of the desktop client rendering where a process loopback of its tree hears nothing. Until somebody holds a meeting through it, treat Teams as unproven and not as working. |
| **Zoom** | **Not probed.** Not installed on this machine. |

Two things the runs settled that are not in the table:

- **A program that cannot be followed does not fail.** Following `System` (pid 4) activates happily
  and delivers a silent stream — 3 s of packets, nothing lost, no audio. So a person who picks the
  wrong program gets silence, not an error, and what has to catch that is the meter rather than an
  exception. The refusal in the capture only ever fires for a Windows that has no such API at all.
- **The program exiting mid-recording does not end the recording.** Killing the followed process
  half way through 12 s left the stream running silent to the end, the file finished, and the six
  milliseconds the instants jumped recorded as the gap they were.

Levels are higher than the whole machine's endpoint loopback used to be for the same sound: a
process stream is what the program rendered, before the master volume the endpoint's loopback heard.

## What was probed, the whole machine, 2026-08-20, Windows 11 25H2 build 26200

Every run is `capture --seconds N` on the same machine, with the same tone rig.

| Run | Result |
| --- | --- |
| **20 s with nothing playing** | 2006 packets 10 ms apart, covering 0:00:20 with **0 ms lost**, loudest silent. This is the run the endpoint loopback could not do: it would have delivered no packets at all. |
| **12 s with the tone playing** | −8.7 dBFS steady every second, 1203 packets, 16 ms lost. The same level a process loopback of the playing program reads, which is what it is. |
| **15 s with the speakers held in exclusive mode by another application** | The whole recording ran: 1491 packets, 0:00:15 covered, `audio.wav` produced. Channel 0 is silent throughout, which is correct — nothing else on the machine can render while an exclusive client holds the endpoint. At the same moment, opening the **shared playback** stream the old design needed was refused with `AUDCLNT_E_DEVICE_IN_USE` (`0x8889000A`), so the recording that ran here is one that would not have started before. |
| **20 s following a silent `explorer`, moved to the whole machine at 12 s** | The offer appeared at 10 s, the move landed at 12 s, and the tone that had been playing throughout appeared on channel 0 at −8.7 dBFS from the next second. One spool, 2006 packets, 0:00:20 covered, 17 ms lost. The folder's `changes.jsonl` names the moment, what it moved to and what it had been following, beside a card still saying `process_loopback`. |

The runs that lost a few milliseconds lost them to this machine being busy — a build, the tone rig
and the holder all running beside the capture — and not to anything about the mode or about the
move. Two quiet 20 s runs lost none. The moved run was repeated with the machine otherwise idle and
came back with 21 ms lost against the first one's 17 ms, which says the loss is jitter rather than
anything the seam does: the second run is on the other side of a fix to how a moved channel's
packets are placed, and if the seam had been the cause it would have gone the other way.

## What is not probed

Said out loud, because channel 0 is now obtained one way only and there is no second path under it:

- **The packaged host.** Every run here is `capture`, which is the unpackaged command line. Nothing
  has yet activated `VAD\Process_Loopback` from the MSIX/WinUI process, and until this change there
  was an ordinary `WasapiLoopbackCapture` underneath for the machine's audio. One recording started
  from the installed application is what closes it, and it is ISC-73.2.2 — open, which is why
  ISC-73.2 above it is open with the runs on this page closing only its leaf.
- **Two render endpoints at once.** Speakers and a headset both playing is the case this change is
  meant to improve on, and this machine has one output. What is written above is what the API
  means, not a measurement.
- **Audio from a process this one may not see.** An elevated program, another session's, a
  protected stream: the endpoint's loopback carried all of them and exclude-this-tree is asserted to
  as well, on the documentation rather than on a run.
- **A machine with no render endpoint at all.** The format is read off the default playback endpoint
  before either mode is activated, so a machine Windows names no playback device for has no
  channel 0 — the same as before this change, and still unprobed.

## Repeating it

The tone rig is three lines of PowerShell — a WAV of a sine wave, a process that loops it through
`System.Media.SoundPlayer`, and a second process that plays nothing. Follow the silent one while the
other plays and channel 0 has to come back silent; follow a parent whose child plays and it has to
come back loud. For a browser, `--autoplay-policy=no-user-gesture-required` with a throwaway
`--user-data-dir` is what makes a page play without somebody clicking it; Firefox wants a throwaway
profile with `media.autoplay.default` set to 0.

Taking the speakers in exclusive mode needs a program of its own, because nothing on a stock machine
does it on demand: a `WasapiOut` on the default render endpoint in `AudioClientShareMode.Exclusive`,
initialised with a `SilenceProvider` at a format the device accepts — 48 kHz, 16 bit, 2 ch on this
one — held for the length of the run. Windows PowerShell 5.1 cannot load this repo's NAudio, so it
is a throwaway console project referencing the built `NAudio.Core.dll` and `NAudio.Wasapi.dll`
rather than a script.
