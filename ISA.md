---
phase: climbing
progress: 142/209
updated: 2026-09-02
---

# ISA — meeting-transcriber-net

What *done* means for this product, written as claims that are true or false and never partly
either. This file is the claims surface and the count; it is not the design document and not the
work queue. A claim closes on evidence recorded in `## Verification`, never on a task moving.

A claim says what has to be true of the product, a recording or the corpus. It never says which
type, method or test makes it so — that is the design, and it lives in `arquitectura.md` and in
the code. The evidence names the test; the claim names the truth, so a claim survives the code
being rewritten underneath it.

- **Why the product is shaped this way** — `arquitectura.md`. §1 the decisions, §2 the overview,
  §5 the model, §6 the flow, §14 the risks, §16 what is deliberately deferred.
- **What to work on next** — the ClickUp board, `MeetingTranscriber` space. Each feature block
  below names its list.
- **How to write, split and close a claim** — `.claude/skills/isa/SKILL.md`.

If a claim here and the prose there disagree, that is a finding about the product, not a
formatting problem. One of the two is wrong.

## Goal

A person records a meeting on their own Windows machine, gets a transcript they were asked to
pay for exactly once, and afterwards can ask the corpus questions — by hand or through an agent
— with every answer tracing back to a turn somebody actually said. Nothing leaves the machine
that the user did not approve leaving it, and nothing that was paid for can be lost or silently
rewritten.

## Features

### F0 · Cross-cutting
Why: the invariants every other feature rests on. Breaking one corrupts meetings already
recorded and artifacts already paid for, so these hold before anything else is worth building.
Board: —
- [x] ISC-1: Channel 0 is the loopback and channel 1 the microphone — the same number in the audio, in what the provider is asked about, and in what is stored.
- [x] ISC-2: A recording is never transcribed under a profile its channel count contradicts.
- [x] ISC-3: Every instant the corpus holds is UTC to the millisecond.
- [x] ISC-4: Every length and every offset on a timeline is a whole number of milliseconds.
- [ ] ISC-5: Anti: a time value cannot enter the corpus without saying whether it is an instant or a length.
- [x] ISC-6: The rules of the corpus hold with no Windows behind them.
- [x] ISC-7: A job reaches only the states the one it is in allows, and every state says where it can go.
- [x] ISC-8: The runner starts only what is due, and never something that is waiting on a person.
- [x] ISC-9: Two speakers heard on different channels never share a label, so a name put on one never lands on the other's words.
- [x] ISC-10: The user is settled only when the microphone caught exactly one speaker; every other speaker waits for somebody to say who it is.
- [x] ISC-11: What is stored, and what a citation anchors on, is a turn — and where a turn ends is decided the same way for every meeting.
- [x] ISC-12: Every classification name is closed: a meeting cannot be filed under one the corpus does not know.
- [x] ISC-13: The thirteen meetings of `arquitectura.md` §5.3 store and are found again.
- [x] ISC-14: Anti: nothing stored can be renamed without the rename being deliberate.
- [x] ISC-15: Anti: the moment a row appeared cannot be moved once the row exists.
- [x] ISC-16: Anti: nothing under test reaches the network, so no test spends what a person has to pay for.
- [x] ISC-17: Anti: a test cannot read a provider response the fixture set does not name.
- [x] ISC-18: Anti: a test cannot open a corpus other than the one it made for itself.
- [x] ISC-19: The build carries no warning.
- [x] ISC-20: The code needs no reformatting.
- [x] ISC-21: What goes into the corpus comes back out of it as what it was.
- [x] ISC-22: A corpus is one thing — a database and the folder it sits in — and nothing can be handed half of one and half of another.
- [x] ISC-23: Anti: letting go of one corpus cannot break another that is open.
- [ ] ISC-134: Anti: no test of this repo fails because of something changed outside it.
- [ ] ISC-135: A clean clone is enough to build, test and know what this repo requires of a change, with no board behind it and no particular assistant installed.
- [x] ISC-176: Anti: nothing closes a claim in words `main` was not already carrying, so what a closure is scored against is never written by the change that closes it.
- [ ] ISC-178: Anti: a claim whose words move after it closed never keeps standing over the evidence written for the words it moved off.

### F1 · Contracts and characterisation
Why: what the Python system learned is specified in .NET before any of it is rebuilt, so the
knowledge survives without a runtime dependency on Python.
Board: 0 · Contratos y caracterización
- [x] ISC-24: Every response the fixture set holds parses into the turns it describes.
- [x] ISC-25: A fixture carries the provider's real timings, confidences and channel numbers, with every word replaced from a closed vocabulary.
- [x] ISC-26: Where this system departs from the one it replaces is written down, and each departure is held to.
- [x] ISC-27: Anti: importing never writes to the old corpus — it comes out exactly as it went in.
- [x] ISC-28: Anti: what an import cannot place is named, apart from what was left behind on purpose, and never dropped.
- [x] ISC-29: An imported meeting's derived files are produced here rather than carried over.
- [x] ISC-30: A speaker somebody resolved in the old corpus arrives on the words that speaker actually said.
- [x] ISC-31: What the old corpus extracted arrives with the run it came out of, and every decision, action and state projected from it hangs off that run.

### F2 · Deterministic core from artifacts
Why: given a paid response, everything that does not need a microphone works — parse, store,
project, rebuild — so the artifact is the only input the rest of the system needs.
Board: 1 · Núcleo .NET desde artefactos
- [x] ISC-32: A paid response becomes turns carrying their channel, their speaker and where on the timeline they were said.
- [x] ISC-33: A paid response on disk becomes a meeting, with its turns and its derived files, from the command line alone.
- [x] ISC-34: Anti: the same meeting cannot enter the corpus twice — filed or imported again, under whatever folder name, it is the one already there and it is rendered again.
- [x] ISC-35: A rebuild throws away only what it can produce again: every correction, name and classification a person gave survives it.
- [x] ISC-36: Everything derived comes back the same from the sources alone — the same projections and the same files.
- [x] ISC-37: Anti: a citation always resolves — nothing can cite a turn its meeting never had, and a turn something cites cannot be deleted out from under it.
- [x] ISC-38: Anti: a rebuild that would move a turn's position fails rather than rewriting what every stored citation points at.
- [x] ISC-39: Anti: what was paid for, or cannot be produced again, is never written over.
- [x] ISC-40: A derivative produced again replaces itself and stays one row.
- [x] ISC-41: A write cut off part-way leaves either nothing or the finished artifact, never half of one.
- [x] ISC-42: What is recorded for an artifact is the hash of the bytes that were actually written.
- [x] ISC-43: Anti: a meeting cannot write into another meeting's folder.
- [x] ISC-44: Anti: a write cannot put one kind of artifact over the path another kind already holds.
- [x] ISC-45: Anti: bytes no row of this corpus describes never reach an artifact's path.
- [x] ISC-46: A paid file the disk has lost comes back from bytes that hash to what its row already says, before anything is rendered from it.
- [x] ISC-47: A corpus that is not sound fails and names what broke.
- [x] ISC-48: Anti: a name or a correction reaches the rendered files and never the stored turn.
- [x] ISC-49: A merged turn's confidence is the mean of its parts, weighted by their length.
- [x] ISC-50: Every meeting's folder carries a card saying what the corpus now says about it — after it is filed, filed again, renamed or rebuilt.
- [x] ISC-51: A meeting is recognised from its card alone — its id, when it started, its profile, its language and its title — with nothing else read.
- [x] ISC-52: Anti: the card and the corpus never disagree — a change that cannot reach both does not happen at all.
- [x] ISC-53: The corpus's state can be read from the command line without opening the application.
- [x] ISC-54: Exactly one person is the user of this install.
- [x] ISC-55: Anti: a speaker somebody resolved is never overwritten by what the recording settled.
- [x] ISC-127: Anti: nothing one extraction produced shares a position with another of its kind, so what somebody pinned to a decision, an action or an open question cannot come to mean another one.
- [ ] ISC-174: Anti: a meeting whose turns the corpus refuses partway through producing them again is left holding the ones it already had, never none at all.
- [x] ISC-175: Anti: a meeting's derived files are never left describing two different renders of it.
- [x] ISC-177: Anti: the only thing a corpus removes without being asked is an abandoned write — never a copy of a file it holds, and never a write in progress.

### F3 · Audio engine
Why: two sources become one timeline a person can trust. This is the largest technical risk in
the product and it is settled before any UI is built on top of it.
Board: 2 · Spike y motor de audio
- [x] ISC-56: The selected microphone and the full system loopback are captured over the same stretch of time, each into its own stream.
- [x] ISC-57: A capture names each source's device and the format that device handed it.
- [x] ISC-58: A source's level is what actually arrived, so a source that heard nothing reads as silent.
- [x] ISC-59: Anti: a capture that cannot open both of its sources stops, rather than recording one of them.
- [x] ISC-60: Two sources become one aligned pair of channels.
- [x] ISC-61: A stretch of audio lands where its device says it belongs, not immediately after the stretch before it.
- [x] ISC-62: A stretch a source never delivered stays a gap of the length its device says, rather than being closed up.
- [x] ISC-63: Whatever rate, width and channel count a source arrives with, it leaves the timeline at one rate and one sample format.
- [x] ISC-64: What a source covers is counted from where its device says the audio was, and never from how much of it reached the application.
- [x] ISC-65: Audio is placed by the instant its device read it, and never by the moment the application collected it.
- [x] ISC-66: Two hours of a meeting whose two devices' clocks disagree leave the channels under 50 ms apart at every point, and not only at the end.
- [x] ISC-67: A source that opened late is a wait and not a rate: the delay is reported as the wait it was, and audio already in the right place is never pulled off it as though it had drifted.
- [x] ISC-68: Anti: a device whose frame counter and clock disagree by more than any crystal drifts stops the recording, rather than being clamped into looking aligned.
- [x] ISC-69: A source that goes quiet does not hold the rest of the meeting back.
- [x] ISC-70: A recording says how much of each source never arrived, rather than coming back shorter with nothing saying so.
- [x] ISC-71: A recording is as long as the last audio of either source, so one that stopped early does not cut the end off the meeting.
- [x] ISC-72: Anti: how late a source's audio is handed over cannot change the recording, up to the half minute after which that source is given up.
- [ ] ISC-73: Anti: a stretch of a meeting nobody played into is recorded as the silence it was.
- [x] ISC-73.1: Anti: what a silent stretch holds is silence, and not whatever the device's buffer last held.
- [ ] ISC-73.2: Anti: a silent stretch is there rather than missing, however another application is using the speakers the meeting is coming out of.
- [x] ISC-73.2.1: Anti: another application holding the speakers to itself takes no stretch out of the recording.
- [ ] ISC-73.2.2: A meeting the installed application records brings back what the machine played, including the stretches nothing was playing.
- [x] ISC-74: Anti: the recorded file never carries the microphone on channel 0.
- [x] ISC-75: A recording cut off mid-block comes back to its last whole block.
- [x] ISC-76: Finishing the same recording twice produces the same file.
- [x] ISC-77: A recording following one application that brings back no audio offers the whole machine's audio in its place, while the meeting is still running.
- [x] ISC-78: A device changing mid-recording does not end the recording.
- [x] ISC-117: A recording that follows one application carries what that application played, including what the processes it started played.
- [x] ISC-118: Anti: a recording that follows one application carries nothing another application played over it.
- [x] ISC-119: A recording says in its own folder which meeting it is, when it started and under what profile, with nothing else read.
- [x] ISC-120: A recording says in its own folder what fed each of its channels.
- [x] ISC-121: A recording whose channel 0 stopped following the program it was asked to says so in its own folder, and says when.
- [ ] ISC-161: [DROPPED 2026-08-20: written and marked closed in the same pass as the code under it, over a folder contract no card decided, and what it said is not what its probe reached; the behaviour and its test stay.]
- [x] ISC-122: Anti: what a recording's folder says about it is what was true when it started, and nothing that happens while it records rewrites it.
- [x] ISC-123: Every recording sitting in the folder recordings are written into is found again, each saying which meeting it is and what each of its sources holds.
- [x] ISC-124: A recording waiting in that folder is kept, has its audio taken out, or is thrown away — and which of the three happens is somebody's choice every time.
- [x] ISC-125: Anti: a recording waiting in that folder is removed by nothing but somebody choosing to remove it.
- [x] ISC-126: Anti: a meeting that is still being recorded is never offered as one to decide about.
- [ ] ISC-126.1: Anti: a meeting whose save is still running is never offered as one to decide about.
- [ ] ISC-126.2: A save the process died in the middle of leaves its meeting decidable again, rather than held out of reach by a mark nothing will lift.
- [x] ISC-128: A source that will not stop is given up on at a deadline, rather than waited on for as long as it takes.
- [ ] ISC-129: A source that would not stop is named when a recording stops, together with what was kept of it.
- [ ] ISC-130: A source that would not stop does not keep the recording's other source from being let go of.
- [ ] ISC-131: Anti: nothing a source that will not stop is still using is taken away from it.
- [x] ISC-132: A source whose device counts its frames more slowly than it hands them over is still recorded, rather than costing the meeting it was part of.
- [x] ISC-133: A recording says which of its sources had their own frame counter given up on, so a rate reported for one of those is never read as a rate that was measured.
- [x] ISC-136: Stopping a recording ends at a deadline even when the device being let go of never answers.
- [x] ISC-137: Starting a recording ends at a deadline even when a device never starts.
- [ ] ISC-138: A recording that did not start names the device that did not answer.
- [ ] ISC-139: Anti: nothing moves a recording from the application it is following to the whole machine's audio without somebody choosing it.
- [x] ISC-139.1: Anti: at a prompt, that choice is a key pressed after the offer was on screen, and never one typed before it.
- [x] ISC-139.2: Anti: on a screen, there is nothing to press until the offer has been made.
- [ ] ISC-140: A source whose device counts its frames in a rate of its own has its drift measured and corrected like any other source's, rather than being recorded at the rate its label claims.
- [x] ISC-163: Asking this machine what it can record from comes back at a deadline, even when the audio service never answers.
- [x] ISC-162: Anti: a question this machine has not come back from is not put to it again until it does, so a screen that looks every second costs one deadline and not one at every look.
- [x] ISC-164: Anti: a question this machine has not come back from stops no other question about its devices being asked, so a meeting following a microphone that went away is never held up by a screen looking at what the machine plays through.
- [ ] ISC-169: A meeting following one program goes on following another without the recording stopping.

### F4 · WinUI recorder
Why: the application replaces OBS. Recording, pausing, stopping and recovering happen in one
native app with no Python, no WSL and no FFmpeg anywhere behind it.
Board: 3 · Grabador WinUI
- [x] ISC-79: Opening the application after it was killed mid-recording ends in a meeting somebody can play.
- [x] ISC-80: A source that is hearing nothing is shown as silent while the meeting is still running.
- [x] ISC-81: A recording that was paused is one meeting as long as the clock says, carrying the paused stretch as the silence it was.
- [x] ISC-82: A meeting says what stage it is at and what the application would do to it next.
- [x] ISC-83: An audio file from disk becomes a meeting, whatever its channel count.
- [x] ISC-141: A recording whose device changed says so while the meeting is still running, naming what it moved to.
- [x] ISC-147: A meeting whose next stage was declined can be offered that stage again later.
- [x] ISC-148: What a meeting is waiting for survives the application closing and opening again.
- [x] ISC-149: Anti: a recording waiting to be decided about never keeps a new meeting from being recorded.
- [x] ISC-150: Somebody listening to the meeting through speakers is told, while it is still running, that the microphone is picking the other side up twice.
- [x] ISC-151: Anti: audio this application did not record is never taken as two channels of one meeting.
- [x] ISC-159: Audio nothing in its folder vouches for enters as one track, whatever shape the file itself is in.
- [ ] ISC-160: [DROPPED 2026-08-20: nobody decided the corpus is obliged to say a mix down happened; the audit line and its probe stay, the requirement never existed.]
- [x] ISC-152: Every text a person reads in the application is there in both Spanish and English.
- [x] ISC-153: The application opens in the language Windows is set to, unless somebody chose another.
- [x] ISC-156: A meeting's identity, its row in the corpus and the folder its audio goes into all exist before the first sample of it is captured.
- [ ] ISC-156.1: Anti: a meeting no sample was ever captured for does not survive as one — its row and its folder are gone the next time the application opens.
- [x] ISC-157: Anti: stopping a recording queues no work on the meeting that nobody asked for beforehand.
- [ ] ISC-157.1: What happens to a meeting when its recording ends is what the person settled beforehand, and nothing else.
- [ ] ISC-158: A meeting is recorded from end to end with nothing typed at a command line.
- [x] ISC-158.1: Which microphone a meeting records, and whether channel 0 follows one program or everything the machine plays, are chosen before it starts.
- [x] ISC-158.2: A meeting being recorded is paused, resumed and stopped without leaving the application.
- [x] ISC-158.3: A meeting recorded from the application arrives in the corpus as the same thing a meeting recorded at a prompt does.
- [x] ISC-158.4: What a meeting is expected to be spoken in is said for that meeting, and is never taken from the language the application is being read in.
- [x] ISC-158.5: Anti: a recording cannot be started before the microphone, what channel 0 follows and what will be spoken have each been said.
- [ ] ISC-158.6: A microphone connected while the application is open can be recorded with, without closing it.
- [x] ISC-158.7: The stretch between stop and the meeting being saved is a state of its own, and what saving it is doing is on screen for as long as it lasts.
- [ ] ISC-158.8: [DROPPED 2026-09-01: written and marked closed in the same pass, so it never stood as a bet the work had to clear.]
- [x] ISC-158.9: How long the meeting has been running is on screen for as long as it is being recorded.
- [x] ISC-158.10: Anti: one meeting is never given two lengths — nothing says how long it was until the length it turned out to be is known.
- [ ] ISC-165: A meeting's name is the person's to set, at any time after it was recorded.
- [x] ISC-165.1: Anti: a meeting nobody has named never reads under a name the application invented for it.
- [x] ISC-166: Who is using the application is asked once and is what the microphone's own voice resolves to from then on.
- [ ] ISC-167: Anti: playing back what a meeting recorded never requires a transcription to have been paid for.
- [x] ISC-168: A meeting whose transcription arrived has its readable files without anybody asking for them.
- [x] ISC-170: The meetings already recorded are on the screen the application opens on, with nothing to press and no second window to reach them.
- [ ] ISC-170.1: A meeting whose stage changed while the application was open reads its new stage without the application being started again.
- [x] ISC-171: The list of meetings takes the room the recorder half was using and gives it back, by one control that is in the same place either way.
- [ ] ISC-172: Anti: a meeting being recorded is never out of sight behind the meetings list — what it is doing, and the press that stops it, are on screen wherever the list is.
- [ ] ISC-173: The application is drawn from one visual system, so a screen built later looks like the ones already there.
- [ ] ISC-173.1: Every colour, text size and corner a screen uses is one of the system's named few, and never a value chosen on the screen itself.
- [ ] ISC-173.2: A screen that rearranges itself moves between the two arrangements, so somebody can tell what arrived from what was already there.
- [ ] ISC-173.3: Anti: with Windows asked for no animation, nothing on a screen moves, and nothing on it is lost for standing still.

### F5 · Deepgram BYOK
Why: a recording becomes a transcript on the user's own key, and the user is charged exactly
once for exactly what they approved.
Board: 4 · Deepgram BYOK
- [ ] ISC-84: The Deepgram key lives in Windows Credential Manager and is read from nowhere else.
- [ ] ISC-85: Anti: no Deepgram call happens without an explicit approval carrying an estimate of what that call will cost, worked out from what is actually sent and never from how long the meeting was.
- [ ] ISC-86: Transcribing again is a new version beside what was paid for, never a replacement.
- [ ] ISC-87: A job whose outcome is uncertain — a charge that may already have happened — stops on a person.
- [ ] ISC-88: What the provider returns has the shape the fixtures describe.
- [ ] ISC-154: Silence is left out of what is sent to the provider one channel at a time, so a channel that stayed quiet while the other was being spoken into is not paid for.
- [ ] ISC-155: Anti: a turn lands where it was said in the meeting, however much of the meeting was left out of what was sent.

### F6 · Summaries
Why: a meeting becomes a summary whose every claim resolves to something said, using the user's
own Claude Code credits — and the product stays whole when Claude Code is not installed.
Board: 5 · Summaries
- [ ] ISC-89: Anti: recording, transcription, rendering, search and recovery all work with Claude Code absent.
- [ ] ISC-90: A summary that fails validation is stored as a failed run, not as a summary.
- [x] ISC-91: A second extraction leaves the first one's state alone and starts its own blank.
- [ ] ISC-115: A rejected summary is handed back once, saying what was wrong with it.
- [ ] ISC-116: Anti: a statement nothing said supports can come back only without that statement — one that comes back pointing at something else for the same statement is refused.
- [ ] ISC-142: Anti: what an extraction produced without validating is never shown as the meeting's summary.
- [ ] ISC-143: A meeting left without a summary says which condition failed and on which statement.

### F7 · Local knowledge
Why: people and agents query the corpus with no server, no network and no cloud, and every
answer traces back to a turn. What an answer says still stands is maintained as meetings arrive
rather than re-derived at every question, and the corpus answers the same way whether or not a
run has been over it.
Board: 6 · Conocimiento local
- [x] ISC-92: Search costs what an index costs and not what a scan costs, however many meetings there are.
- [ ] ISC-93: Everything search promises to find is found.
- [x] ISC-94: A hit carries the meeting, its date, its title, an elided snippet and where on the timeline it was said.
- [x] ISC-95: Anti: a meeting on its way out is never something search offers.
- [x] ISC-96: Maintaining the corpus — compacting it, or throwing the indexes away and building them again — leaves search answering exactly what it answered before.
- [x] ISC-97: Anti: a query the index cannot parse is refused naming the query, never as a database error.
- [ ] ISC-98: The MCP server answers read-only over stdio and never writes.
- [ ] ISC-99: Anti: an MCP response is bounded.
- [ ] ISC-100: Every MCP request is recorded locally.
- [ ] ISC-101: Anti: what a meeting recorded is never rewritten by a later one — what changed is recorded beside it and both stay readable.
- [ ] ISC-102: Two people asking the same corpus what still stands get the same answer, whoever is reading and whatever they read first.
- [ ] ISC-103: What still stands comes back at the same cost with three hundred meetings behind it as with ten.
- [ ] ISC-104: A decision comes back with when it was settled and what has happened around it since, so "nothing contradicted it" is never read as "somebody confirmed it".
- [ ] ISC-105: Anything saying a decision no longer stands cites the turn where that was said, the way a decision cites the turn it came from.
- [ ] ISC-106: Anti: two decisions that contradict each other with nothing settling it come back as a conflict, and neither is hidden for being the older one.
- [ ] ISC-107: Anti: nothing is hidden for want of a pass having run over it — a decision stands until something says otherwise.
- [ ] ISC-108: A person's word on whether a decision stands outranks whatever the machine concluded, and survives a rebuild.
- [ ] ISC-109: Deciding what an arriving meeting changed reads a bounded part of the corpus, and what bounds it does not grow as meetings accumulate.
- [ ] ISC-144: Asking for a node brings back the statements of its meetings in the order they were said, each carrying the meeting and the date it came from.
- [ ] ISC-145: A node's answer includes the statements of everything hanging off its children.

### F8 · Distribution and backup
Why: the application installs, upgrades and comes back from a lost disk, because the corpus
holds artifacts that cannot be obtained again.
Board: 7 · Distribución y backup
- [ ] ISC-110: Anti: the corpus never lives in the MSIX package data folder.
- [x] ISC-110.1: Anti: no folder the application would open a corpus in is one the package takes with it when it is uninstalled — neither the one it falls back to nor one a person named.
- [ ] ISC-110.2: A corpus an installed build wrote is still there, whole, after the package is uninstalled.
- [ ] ISC-111: A snapshot restores to an alternate directory and comes back sound.
- [ ] ISC-112: An upgrade over an installed build leaves the corpus intact.
- [ ] ISC-113: The CLI and the MCP server are reachable by app execution alias.
- [ ] ISC-114: The corpus location is configurable and validated at startup.
- [x] ISC-114.1: With nobody having chosen where the corpus goes, the corpus is in a folder of the user's own profile.
- [x] ISC-114.2: The corpus opens wherever it was moved to, and the same one is opened again the next time the application starts.
- [x] ISC-114.3: A corpus location that cannot be opened is refused naming the folder, rather than opened.
- [x] ISC-114.4: Anti: a corpus location that cannot be opened never becomes a second, empty corpus somewhere else.
- [ ] ISC-146: The package installs and uninstalls on a machine that is not the one it was built on.

## Not yet specified

- **How a summary citation anchors.** ISC-37 says a citation always resolves; whether it stores
  the turn id or an offset into the transcript changes what happens when turns are regrouped.
  `docs/reference-behaviour.md` has the grouping rules but not this.
- **What bounds an MCP response in ISC-99.** Rows, bytes or tokens — and the answer depends on
  what an agent actually asks for, which nobody has measured yet.
- **How the corpus decides that a decision stopped standing.** ISC-101 to ISC-109 say what has to
  be true of the answer; none of them says what produces it, and four shapes are on the table with
  no evidence between them. A pass over each arriving meeting, linking a new decision to the
  standing one it replaces — the most precise, and wrong in the direction that hides something
  somebody decided. The same question asked at read time over the decisions of one node, which
  stores nothing and answers differently on two days. A person asked at the end of a meeting which
  standing decisions this one touched — the most reliable, and a chore that gets skipped. Or
  nothing inferred at all: every decision comes back with its date and what has happened since,
  and the person judges, which is ISC-104 on its own and can hide nothing. What decides between
  them is a corpus with enough meetings on one node to measure, and that does not exist yet — the
  extraction that fills `decisions` outside the importer is F6 and unbuilt. Two things would have
  to be measured before choosing: how close two statements have to be before one is offered as
  replacing the other, and how much of one node actually fits in a context, which is the number
  the read-time shape lives or dies on. Deferred deliberately on 2026-08-17 rather than left
  dim: none of the four shapes is built, and neither measurement is taken, until reading a
  node's whole history stops answering the question. Until then ISC-144 and ISC-145 are what a
  person reads and judges from, and ISC-104 is the only one of ISC-101 and ISC-104 to ISC-108
  that holds without any of them — which is also the shape that can hide nothing.

- **What the screen does with a measurement of the system's echo.** ISC-150 is the warning
  that costs nothing to be sure of — the playback endpoint saying it is speakers rather than a
  headset. Measuring how much of channel 0 comes back in on channel 1 is engine work with a
  card of its own, and what a screen should do with that number is not decided: a second level
  beside the meters, a threshold that warns once, or nothing until somebody asks. Nobody has
  seen the number, so the shape of the answer would be invented rather than chosen.

## Learning

- **conjecture** — A helper every test opens a corpus through is where a rule about corpora is
  held, so mending the process-wide pool clear inside it mends it everywhere.
- **refuted-by** — A second call sat in `CorpusSchemaTests`, in a test that opens a corpus
  read-only after a writer, and it was found by an adversarial reviewer rather than by anything
  going red. The suite passed twenty times over the half-fix, because what the call breaks is
  another test that happened to be between putting a connection back and taking it out again.
- **learned** — A rule saying what nothing may call is not held by fixing the place it was called.
  The helper concentrates how a corpus is opened, which is why the rule looked contained; it has
  no say over a suite that reaches for the provider's static method directly, and that call reads
  as harmless everywhere it appears.
- **criterion-now** — The test tree is swept for `ClearAllPools` and the sweep names the file, so
  the second one costs a red test at the moment it is written. A test lets go of its own corpus
  through `CorpusDatabase.ClearPoolsFor`, which is also the only place that knows both connection
  strings a corpus can be pooled under.

- **conjecture** — Starting a recording is bounded once the call that starts the device is, because
  that call is the one a driver wedges in.
- **refuted-by** — An audit of the finished branch read the four calls above it: opening the
  endpoint, activating its client, reading the mix format and initialising it are all synchronous
  COM into the same driver, on the thread somebody pressed record on, and the code's own comment
  said initialising is where a device most often says no. The suite was green over all of it,
  because every probe stood at the mechanism that had been bounded and none stood at the moment.
- **learned** — Bounding a wait is not bounding a moment. A deadline holds whatever runs behind it,
  and what a claim is about is the stretch a person is sitting through — so the question a probe has
  to answer is not "is this wait bounded" but "which calls happen between pressing record and coming
  back, and is every one of them inside a deadline". The three that were not looked bounded because
  they sat next to one that was.
- **criterion-now** — Everything it takes to get from a device's name to a stream runs inside one
  ask with one deadline, both ways in and for the silence played into the loopback, so a call added
  to that path is inside it by construction rather than by having been remembered. A deadline is per
  device and not per call: a driver that wedges anywhere along the way is given up on once.

- **conjecture** — A `corpus.db` that is there is either a corpus or a permissions problem, and
  SQLite's error says which.
- **refuted-by** — A file of zero bytes is neither. SQLite reads it as an empty database and
  refuses to put it into WAL, so every write against it comes back as `attempt to write a readonly
  database` — including the migration that would have made it a corpus. The directory was a dead
  end: the corpus could not be created there again, and the message sent whoever read it to look at
  permissions that were fine.
- **learned** — The state a create leaves behind when it is cut off is not "nothing" and not "a
  corpus". It is a third thing, and it is the one state where the error a component reports is
  about something other than what is wrong. Every file this program opens can be in it.
- **criterion-now** — An empty `corpus.db` is not a corpus: every command refuses it saying so, and
  `migrate` removes it, which is the only file this program deletes without being asked. A refusal
  that comes from SQLite keeps SQLite's words and adds the path, because two very different causes
  arrive under the same sentence and this program cannot tell them apart.

- **conjecture** — A timestamp column is named after the moment it holds, so `created_at` on a
  table is a description of that column and nothing more.
- **refuted-by** — Two tables kept an honest value under it and rewrote it in place: an artifact
  every time a derivative was rendered again, a speaker assignment every time somebody corrected
  what the channel had guessed. Both reads at the call site looked right, and both were storing
  when the answer was settled under a name promising when the row appeared.
- **learned** — A column present on nearly every table stops being a description and becomes the
  default place to put a time. Nothing was wrong at either call site in isolation; what was wrong
  is that neither had to name what it was recording, so the vocabulary quietly widened to mean
  "the last time anything about this row was true".
- **criterion-now** — `created_at` is read-only once its row exists, held on the model rather than
  by each writer. A timestamp that moves gets a column named for what moves it, and finding that
  out costs a failed `SaveChanges` at the moment it is written rather than a query years later.

- **conjecture** — The importer stored a resolved speaker as `speaker_0`, and that was the label,
  because nothing else in the system had an opinion about it.
- **refuted-by** — The first legacy meeting rendered through `MeetingRenderer` produced turns
  labelled `ch0:speaker_0`, so every assignment the old corpus carried matched no turn and no name
  reached the transcript. Nothing failed: an assignment for a label that does not exist is a row
  the join simply does not find.
- **learned** — A contract only holds where two sides of it meet. `SpeakerLabels.For` was written,
  documented and tested, and the importer spelled the string out by hand a few lines away — which
  is the one thing that could silently lose the most valuable rows in the old corpus, because every
  one of them is somebody having listened.
- **criterion-now** — A stored label is built by `SpeakerLabels.For` and never spelled out, and a
  writer of `speaker_assignments` is tested against a rendered turn rather than against a string
  literal. Both sides come from the same function or neither is proved.

- **conjecture** — `dotnet test --filter "FullyQualifiedName~X"` selects a test class, so a
  Test Strategy row could name the filter alone and stay short.
- **refuted-by** — Run against `TurnsTests` it executed all 439 tests across all four projects
  and exited 0. The MTP runner that xunit.v3 3.2.2 uses ignores the flag rather than rejecting
  it.
- **learned** — The probe passed while probing nothing. A green result from a filter that was
  silently discarded is indistinguishable from a real pass, which is the exact failure the
  evidence rule exists to catch — and it would have marked sixty-five claims verified on one
  command that never ran them.
- **criterion-now** — A `dotnet-test` row names its project and uses `-- --filter-class`, since
  a bare `--filter-class` makes the three non-matching projects exit non-zero on zero tests. No
  row is trusted until it has been run once and seen to select what it claims.

- **conjecture** — WASAPI's per-packet flags have no reader here, so leaving them off
  `CapturePacket` is the rule about abstractions built for callers that do not exist.
- **refuted-by** — The same pass added a check that stops a recording when a device's frame
  counter and its clock disagree. `TIMESTAMP_ERROR` is the device saying it cannot vouch for the
  pair that check reads, so the check is that flag's reader — and without it a device that flags
  one bad packet loses the whole meeting. An adversarial reviewer found it; nothing went red,
  because no fabricated packet has ever set the flag.
- **learned** — A field's caller can be added in the same pass as the field, and a check that can
  refuse work is exactly the kind of caller that appears late. The rule asks whether a caller
  exists, and the honest question is whether one exists *once this change lands*, which is not
  the same question when the change adds a failure the flag exists to suppress.
- **criterion-now** — A field carrying a device's own "do not trust this" is added when something
  in the same change would otherwise act on the untrustworthy value. `DATA_DISCONTINUITY` stays
  off the packet on the original grounds and the reason sits in the type, since a lost stretch is
  a jump in the device position and a flag nothing reads can disagree with the number beside it.

- **conjecture** — A device's reported position and the frames it hands over are counted in the
  same unit, so a position that goes backwards is a driver whose counter is broken.
- **refuted-by** — A webcam microphone on this machine, opened at the endpoint's 48 kHz mix
  format, hands over 480 frames a packet and advances its position by 160 — its own 16 kHz frames,
  while the samples arrive converted. Its first packet of 463 frames put the next expected frame
  at 463 and its second packet said 160, so an 8 s recording came back as a refusal about a
  counter going backwards. The other microphone on the same machine, which runs at 48 kHz
  natively, records fine, so the whole difference is what the device runs at.
- **learned** — Where a stretch of audio belongs is two numbers in two units, and only one of them
  is the client's. A shared-mode client is converted to the format it asked for; the position
  counter is not, so a position means nothing on the recording's timeline until it has been scaled
  by the rate that device really counts in.
- **criterion-now** — A capture is not assumed to number its frames in the format it was opened
  at, and a device whose positions cannot be laid out refuses the recording at the moment it is
  stopped, with every block still on disk and the message saying so. Recording such a device
  rather than refusing it is a board task, and it is the one thing a person with that microphone
  needs.

- **conjecture** — Recording that device means working out what rate it counts in: inferred from
  the ratio its two numbers keep, measured against the instants beside them, or read off the
  endpoint before the stream opens.
- **refuted-by** — No rate is needed and none is computed. A counter in the frames the client is
  handed advances by exactly those frames, or by more where a stretch was dropped that nobody was
  handed — never by less, which would be a device claiming it produced less than it just gave
  over. So the one reading that used to refuse the recording is already the whole detection, and
  the fabricated 16 kHz device is recorded correctly by a rule that never learns it is 16 kHz.
- **learned** — Asking why a number is wrong is a different question from asking whether it can be
  used, and only the second one had to be answered. A counter in another rate and a driver whose
  counter is simply broken are one case — this source's numbers cannot be laid out — and the
  answer to both is to stop reading them and place the source by the clock its packets already
  carry, which is what a source whose device numbers nothing has always done.
- **criterion-now** — A counter that goes back on itself is given up on rather than refused, and
  the recording says which of its sources that happened to, because giving it up switches that
  source's drift correction off and leaves its rate reading as the label. What still refuses a
  recording is a device whose frame counter and clock disagree, which is measured against the
  clock and so survives the counter being replaced — except on a source already given up on,
  where the positions are computed from that same clock and the comparison is with itself. A
  device that both counts in its own unit and runs at a rate other than its label is therefore
  recorded at its label, with the disagreement showing up as the stretch that comes back missing.
  That, and a counter running finer than the client's rate rather than coarser — which reads as a
  dropout between every packet and is refused — are both on the board rather than guessed at here.

- **conjecture** — A corpus is kept out of the folder an uninstall deletes by resolving it through
  the API that does not return that folder, and by refusing a path that is spelled like it.
- **refuted-by** — Two reviewers on the opposing model, independently. Where a path is written and
  where it leads are different things: a folder on any disk that is a link into the container was
  accepted, and a junction is something a test makes with no privilege at all. The same check was
  anchored on the local application data folder, which is the one folder a packaged process may be
  handed already inside the container — so in exactly the case the check exists for it would have
  compared the container against itself and found nothing wrong with anywhere. Every test was green
  over both.
- **learned** — A check that reads a path is a check on spelling. What an uninstall answers is where
  the path leads, and the anchor a location is judged against has to be one the thing being guarded
  against cannot move — which the profile is and the application data folder is not.
- **criterion-now** — The container is the profile's own `AppData\Local\Packages`, and every step
  from the corpus folder up to its root is asked where it leads; a folder that only reaches the
  container through a link is refused like one spelled that way, and a folder somewhere else that
  merely happens to be spelled that way is somebody's own and is left alone.

- **conjecture** — Refusing a location that cannot be opened is what keeps the application from
  making a second, empty corpus while somebody's meetings sit somewhere else.
- **refuted-by** — There is a route to it with no location to refuse. Somebody who never moved their
  corpus drags the folder in Explorer; nothing was ever written down, so resolution finds no setting,
  falls back to the folder the application would have used, and opens it exactly as it opens a first
  run. A reviewer found it while every refusal test was green.
- **learned** — A refusal only reaches the cases where something was written down. Nobody having
  chosen and what was there being gone are the same thing on disk, so the safety cannot come from
  telling them apart — it has to come from never making one silently in either.
- **criterion-now** — Resolution never answers with a folder without also saying whether a corpus is
  there yet, so making one is something whoever opens it was told about rather than something
  nothing objected to.

- **conjecture** — Keeping the corpus out of the package's own data folder is enough for it to
  outlive an uninstall, so the application data folder Windows names for an application is a safe
  place to put it as long as it is asked for the way that does not return the package's own.
- **refuted-by** — An audit of the branch, against what a packaged full-trust desktop application
  does rather than what it is handed. AppData write virtualization is on by default: the path comes
  back spelled exactly as it was asked for and the bytes land inside the container. Every check was
  green over a fallback the first uninstall would have deleted — including the one asserting that
  fallback is not inside the container, which runs unpackaged and so cannot see the redirection at
  all.
- **learned** — A folder is not safe for being spelled differently from the unsafe one. What decides
  is whose writes land in it, and that is answered by the tree it is in rather than by anything the
  folder itself reports — so the rule has to be about the tree, and the probe has to stand at a
  folder rather than at a write.
- **criterion-now** — The corpus is in the user's own profile and never under their application
  data. What is refused is every application data folder this user has — the profile's own, and
  whatever Windows answers for the roaming and local ones, which are different folders on a profile
  where somebody keeps application data elsewhere — the package container being the case an
  uninstall deletes outright rather than redirects into. A folder that only leads into one of them
  goes with it: the chain of links is followed to its end rather than one hop, since a disk somebody
  moved and then a folder somebody else moved still ends in application data.

- **conjecture** — A claim is closed once the rule it names is refused where it lives, so a probe
  standing at the engine that owns the rule is what closes it.
- **refuted-by** — ISC-126 says a meeting still being recorded is never *offered* as one to decide
  about, and it sat closed for four days on the audio engine's own tests while the two commands
  over it printed `export or discard` and then failed on a block file. ISC-114 and ISC-81 had each
  been closed the same way and each needed a second pass at the surface.
- **learned** — What a claim is about decides where its probe stands, and the verb gives it away:
  *offered*, *shown*, *said* and *refused* are about what a person meets, and no amount of evidence
  from the layer underneath reaches them. An engine that refuses correctly and a listing that
  offers the choice anyway are both true at once, which is why the green suite was not lying.
- **criterion-now** — A claim whose subject is what a person is offered or told closes on a probe
  at the surface they meet it at, and its stub names that project as well as the engine's. Three
  claims have now been closed a layer short of their own subject, so the read is on the verb
  rather than on remembering the last time.

- **conjecture** — Recording everything the machine plays means recording the endpoint it plays
  through, and an endpoint that hands over nothing while nothing plays into it has to be kept awake
  by something. Playing inaudible silence into it costs nothing anybody hears.
- **refuted-by** — It costs what recording depends on. Another application holding the speakers
  in exclusive mode refuses that playback with `AUDCLNT_E_DEVICE_IN_USE`, measured on this machine
  2026-08-20, and so would a stuck audio service or a driver that would not take the format —
  each of them turning a capture that had nothing wrong with it into a channel 0 holding only the
  stretches something happened to be playing. The virtual device Windows exposes for following one
  program never had the problem: it is handed silence when its targets play nothing, and asked for
  every process but this application's own tree it is the whole machine.
- **learned** — A workaround that has been in the tree long enough stops being read as one. The
  silence was reviewed three times — as a resource to release, as a wait to bound, as a handle
  to let go of in the right order — and never once as a dependency the recording did not need,
  because each of those asked whether the mechanism was sound and the question that dissolved it was
  what the mechanism was for. The measurement that answered it had already been made and written
  down for the other way in.
- **criterion-now** — Recording depends on being able to open a capture and on nothing else.
  Channel 0 opens no playback stream and names no endpoint either way it is obtained, so what
  another application does with the speakers cannot cost a meeting a stretch of itself.

- **conjecture** — Killing the application while a save runs is the probe for the claim that no
  mark left behind holds a meeting out of reach: the process dies with whatever says the save is
  under way, and the next start either offers that meeting or does not.
- **refuted-by** — The walk offered it and Keep filed a meeting of `0:06:00`, and nothing had been
  held. What says a save is running is an id the window hands the list and clears in a `finally`;
  a crash takes it with the process, so there was never a mark for a next start to fail to lift.
  The standing the run offered as its discriminator is reached only from a block a read refused,
  and a pour reads blocks rather than writing them, so that branch was not live either.
- **learned** — A probe that kills the process and finds the good outcome says nothing about a
  claim whose whole bet is that the bad outcome cannot happen, while the mechanism that would
  produce it does not exist. Both branches answer the same and the run cannot tell them apart.
  What it did measure is worth keeping: Keep works after a save was interrupted, the meeting comes
  back on the next start, and its audio is whole up to the kill.
- **criterion-now** — A claim about a mark being lifted closes only against a build that writes
  one, on a run where the crash strands a real mark and the next start clears it. Until then the
  claim stays open however the walk comes out, and card #86, which owns the mark, owns both halves.

## Verification
- ISC-163 — `DeviceEnquiryTests` and `.Both_questions_this_application_asks_about_devices_go_through_the_deadline` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20. Red 2026-08-20 with the question run on the caller's thread instead: the six-test run was killed at 120 s with one of them still going at 1 m 57 s, where bounded they take 21 s. Red again with the memory ignored, where `AudioDevices.Microphones()` enumerated this machine's real endpoints and answered. That a third question added beside the two would be inside the ask, and why `AudioDevices.Open` and `AudioDevices.EngineFormat` are not, is read off `AudioDevices`
- ISC-162 — `DeviceEnquiryTests.A_question_that_has_not_come_back_is_not_put_to_the_machine_again` and `.Two_lookers_that_arrive_together_pay_the_deadline_once_each_and_no_more` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20. Red 2026-08-20 with the memory ignored: 4 of the 7 failed and the second look reached the machine and waited five seconds of its own. Narrowed on 2026-08-20 from every question about this machine's devices to the one asked; `git log -- ISA.md` holds what it used to say, and ISC-164 is the half that says why. What no probe reaches is two callers of one question at once, because the product has none
- ISC-164 — `DeviceEnquiryTests.A_question_still_out_there_leaves_a_different_one_asked` and `.Two_lookers_that_arrive_together_pay_the_deadline_once_each_and_no_more` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20 and 2026-09-02, the first saying a wedged playback device leaves the microphones unrefused and `.Both_questions_this_application_asks_about_devices_go_through_the_deadline` saying `AudioDevices.Microphones()` is that call, which compose to the watcher's seam without enumerating a real endpoint. Red 2026-09-02 with the memory unscoped: that test and `Two_lookers` failed, waiting the whole deadline on the microphones. Red again 4 of 7 with the memory ignored altogether. What no probe reaches is the meeting itself, since a microphone unplugged mid-recording needs hardware
- ISC-78 — `DeviceChangeTests`, `.A_replacement_whose_first_blocks_carry_no_sound_instant_still_lands_where_it_belongs`, `.A_replacement_for_a_device_that_never_spoke_lands_where_the_recording_got_to`, `ReplacedDeviceTests`, `HandoverTests` and `SpoolStretchTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20, and the 24 s `capture --microphone fifine --then-microphone "GENERAL WEBCAM" --then-microphone-at 12` run of that day: the channel handed over live at 0:00:12, both spools ran to 0:00:24, `audio.wav` came out one file of 386118 frames, the seam 33 ms recorded as missing. Red 2026-08-20 with the stretch mark ignored: 4 of 7 device-change tests failed, the other 273 green. The lock taken out loses that race 34 to 93 goes in 5 000, and 0 in 200 000 with it in. That a source on a dead replacement says it ended is read off `CaptureSource.MoveTo`. No run reaches another format, since both endpoints here mix at 48 000 Hz, or an unplug
- ISC-141 — `RecordingMetersTests` and `.A_channel_zero_moved_to_the_whole_machine_says_what_it_was_following_and_no_words_for_what_it_is` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-20. What is argued rather than probed is the window: `LiveRegionTests` (`tests/MeetingTranscriber.App.Tests`) holds that the two lines exist, are not bound in the XAML and are told their words by the code that shows them, but reaching a WinUI tree needs a UI thread and a packaged host. The `capture --then-microphone` run of 2026-08-20 printed `ch1  Micrófono (fifine Microphone) → Micrófono (GENERAL WEBCAM)` at the second the channel handed over, off the same two values the screen reads

- ISC-1 — `AudioChannelTests` green 2026-08-07
- ISC-2 — `SourceProfileTests` green 2026-08-07
- ISC-3 — `UtcTimestampTests` green 2026-08-07
- ISC-4 — `DurationTests` green 2026-08-07
- ISC-6 — `DomainAssemblyTests.Domain_references_no_windows_assembly` green 2026-08-07
- ISC-7 — `JobStateTests.Every_state_says_where_it_can_go` green 2026-08-07
- ISC-8 — `JobStateTests.Only_the_states_the_runner_owns_are_picked_up_by_itself` green 2026-08-07
- ISC-9 — `SpeakersTests` green 2026-08-07
- ISC-10 — `SpeakersTests` green 2026-08-07
- ISC-11 — `TurnsTests` green 2026-08-07
- ISC-12 — `CorpusSchemaTests` green 2026-08-07
- ISC-13 — `ClassificationStoriesTests` green 2026-08-07
- ISC-14 — `CorpusNamingTests` green 2026-08-07, which spells out every stored table and column, so a rename that nobody meant fails the suite
- ISC-15 — `CorpusNamingTests.No_created_at_anywhere_can_be_written_over_a_row_that_exists` and `.Moving_a_created_at_on_a_stored_row_fails_instead_of_being_written` green 2026-08-07, both red with the model rule commented out
- ISC-16 — `git grep` for HTTP and socket types over `tests/` returned no match 2026-08-07
- ISC-17 — `git grep -l` for the five fixture names over `tests/**/*.cs` returned `MeetingTranscriber.Testing/DeepgramFixtures.cs` alone 2026-08-07; the other hit is the tool that builds them, which is not the test tree. `DeepgramFixtureTests.The_inventory_names_exactly_the_responses_that_are_committed` green, red with a fixture dropped from the inventory
- ISC-18 — `git grep -l "class TemporaryCorpus" -- tests/` returned `MeetingTranscriber.Testing/TemporaryCorpus.cs` alone 2026-08-07
- ISC-19 — `dotnet build --no-restore -warnaserror` 0 warnings 0 errors 2026-08-14
- ISC-20 — `dotnet format --verify-no-changes` clean 2026-08-14
- ISC-21 — `CorpusStorageTests` green 2026-08-07
- ISC-22 — `CorpusIsOneThingTests.A_corpus_says_which_folder_it_is` green 2026-08-13, and `CorpusIsOneThingTests` in `Cli.Tests` and `CorpusImport.Tests` green 2026-08-13, each red against a signature taking both in the assemblies it covers
- ISC-23 — `TemporaryCorpusTests` green 2026-08-13, and the whole suite twenty times over: `Closing_a_corpus_leaves_another_corpus_the_connection_it_had_pooled` red against `SqliteConnection.ClearAllPools` on a different handle coming back, `No_test_empties_the_pools_of_every_corpus_in_the_process` red naming `CorpusSchemaTests.cs`, which was the second call site and the one no test had ever caught
- ISC-24 — `FixtureParsingTests` green 2026-08-07
- ISC-25 — `DeepgramFixtureTests` green 2026-08-07
- ISC-26 — `ReferenceBehaviourTests` green 2026-08-07
- ISC-27 — `CorpusImporterTests.The_corpus_it_reads_comes_out_exactly_as_it_went_in` green 2026-08-07
- ISC-28 — `CorpusImporterTests.What_is_left_behind_on_purpose_is_not_mixed_with_what_had_nowhere_to_go` green 2026-08-07
- ISC-29 — `CorpusImporterTests.Importing_again_does_not_duplicate_or_rewrite_the_derivatives` green 2026-08-07
- ISC-30 — `CorpusImporterTests.A_speaker_somebody_resolved_arrives_under_the_label_the_provider_wrote` green 2026-08-07
- ISC-31 — `CorpusImporterTests.An_imported_extraction_arrives_with_the_run_it_came_out_of` and `.A_decision_and_an_action_projected_from_it_hang_off_that_run` green 2026-08-07
- ISC-32 — `DeepgramTranscriptParserTests` green 2026-08-07
- ISC-33 — `CliWalkthroughTests.A_response_becomes_a_meeting_that_renders_rebuilds_and_is_found_again` green 2026-08-07
- ISC-34 — `CliWalkthroughTests.The_same_response_imported_twice_is_one_meeting`, `CorpusImporterTests.Importing_the_same_corpus_twice_imports_it_once` and `.Importing_again_does_not_duplicate_or_rewrite_the_derivatives` green 2026-08-07; the folder-name half, `CorpusImporterTests.A_meeting_whose_folder_was_renamed_is_still_the_same_meeting` (`tests/MeetingTranscriber.CorpusImport.Tests`) green 2026-08-26; the audio door too, `AudioIntakeTests.The_same_audio_brought_in_twice_is_one_meeting` (both a single track and a pair, compared after the mix down because that is what would land) and `ImportAudioCommandTests.Bringing_the_same_audio_in_twice_is_one_meeting` (`tests/MeetingTranscriber.Recording.Tests`, `tests/MeetingTranscriber.Cli.Tests`) green 2026-08-20
- ISC-35 — `CorpusRebuildTests.Deleting_every_derived_row_and_projecting_again_leaves_every_other_table_as_it_was` green 2026-08-07, which holds the classifications and the speaker assignments a person edited as well as the rows nothing touched
- ISC-36 — `CorpusRebuildTests.Rebuilding_produces_the_same_projections_and_the_same_files` and `MeetingRendererTests.Rendering_again_leaves_the_sources_alone_and_produces_the_same_files` green 2026-08-07
- ISC-37 — `CorpusRebuildTests.A_claim_cannot_cite_a_turn_the_meeting_never_had` green 2026-08-07. That half only: the deleted-out-from-under-it half held for a rebuild that finishes and not for one refused partway, where the turns went and the claims stayed. `.A_meeting_refused_with_cited_turns_costs_that_meeting_and_not_the_run` (`tests/MeetingTranscriber.Processing.Tests`) green 2026-09-02 is what reaches it, red that day against the projection deleting before it knew the new turns would save
- ISC-38 — `CorpusRebuildTests.A_claim_still_points_at_the_turn_it_came_from` green 2026-08-07, over a rebuild that reproduces every position a claim cites. `.A_response_that_no_longer_reaches_a_cited_turn_costs_that_meeting_and_not_the_run` (`tests/MeetingTranscriber.Processing.Tests`) green 2026-09-02 is the half where it cannot, a response swapped for a shorter one: red that day at the corpus-wide commit, which took the whole run and the report naming the meeting rather than costing that meeting
- ISC-39 — `DurableWriteTests.A_source_is_never_written_over` green 2026-08-07
- ISC-40 — `DurableWriteTests.A_derivative_is_replaced_and_stays_one_row` green 2026-08-07, and `ArtifactsTests.Which_kinds_a_second_write_may_replace` for which kinds those are
- ISC-41 — `DurableWriteTests.A_write_cut_while_its_content_is_produced_leaves_nothing_at_all` green 2026-08-07
- ISC-42 — `DurableWriteTests.What_is_recorded_is_the_hash_of_the_bytes_that_were_written` green 2026-08-07
- ISC-43 — `DurableWriteTests.Another_meetings_folder_is_not_somewhere_this_meeting_may_write` green 2026-08-07
- ISC-44 — `DurableWriteTests.A_write_that_calls_a_path_something_it_is_not_is_refused_before_the_file_moves` green 2026-08-13: a card addressed at `deepgram.json` is refused and the paid bytes are still there afterwards. `ArtifactsTests.The_manifest_is_the_only_source_a_second_write_may_replace` holds the exception to one kind
- ISC-45 — `ArtifactRestoreTests.Bytes_no_row_of_this_corpus_describes_are_refused_and_nothing_is_written` and `.Bytes_the_corpus_records_elsewhere_do_not_land_where_another_row_is_missing` green 2026-08-13; the second is the one worth having, since bytes the corpus does know are the case where a wrong path is reachable at all
- ISC-46 — `ArtifactRestoreTests` and `MeetingIntakeTests.A_meeting_whose_response_is_gone_gets_it_back_when_the_same_bytes_are_filed_again` green 2026-08-13, the second red with the restore taken out of the intake: `RenderException` naming the response the row points at. `CommandLineTests.A_paid_file_the_corpus_lost_is_put_back_from_the_bytes_it_already_describes` deletes the paid response, sees `check` refuse, restores from the original and gets `Sound` out of `check --verify-contents`
- ISC-47 — `CorpusIntegrityTests.A_row_pointing_at_a_meeting_that_is_not_there_fails_and_names_the_table` green 2026-08-07
- ISC-48 — `MeetingRendererTests.A_name_and_a_correction_reach_the_transcript_and_not_the_stored_turns` green 2026-08-07
- ISC-49 — `TurnsTests.A_turns_confidence_is_the_mean_of_its_parts_weighted_by_their_length` green 2026-08-07
- ISC-50 — `MeetingManifestTests.Filing_a_response_leaves_a_card_recorded_as_a_source`, `.Filing_again_writes_the_card_as_the_corpus_now_says_it_is`, `.A_card_that_is_gone_comes_back_when_the_response_is_filed_again`, `CorpusImporterTests.An_imported_meeting_arrives_with_the_card_that_names_it`, `CorpusRebuildTests.A_rebuild_leaves_every_meeting_with_the_card_that_names_it` — starting from a corpus with no card row at all — `.A_rebuild_brings_a_card_up_to_a_title_somebody_changed_since` and `HumanLayerTests.Renaming_a_meeting_leaves_its_card_saying_the_new_title` green 2026-08-13, the last of them reading `la daily` against `la daily del equipo` before the fix
- ISC-51 — `MeetingManifestTests.A_meeting_is_recognised_from_its_card_with_nothing_else_left` green 2026-08-13: the corpus is disposed and deleted before the card is read, so only the copied file can answer
- ISC-52 — `HumanLayerTests.A_rename_whose_card_cannot_be_written_does_not_happen_at_all` green 2026-08-13: a directory standing where the card goes makes the replace fail, and the title is read back past the tracked entity to prove the corpus kept the old one
- ISC-53 — `CommandLineTests` green 2026-08-07: `status` answers for a corpus this build has moved past, and `check` names the file the corpus claims and does not have
- ISC-54 — `HumanLayerTests.Exactly_one_person_is_the_user_of_this_install` green 2026-08-07
- ISC-55 — `HumanLayerTests.A_label_the_recording_settled_does_not_overwrite_one_a_person_resolved` green 2026-08-07
- ISC-56 — `capture` runs of 8, 12 and 24 seconds on this machine 2026-08-13: the two streams opened within 32 ms of each other and their files ended within 60 ms of each other, a difference that did not grow with length (60 ms over 8 s, 10 ms over 24 s), so it is start and stop jitter and not accumulated drift, which is ISC-66's to measure. Both files parse as IEEE float WAVs, 48 kHz 2 ch 32 bit, their data chunk ending exactly at the last byte
- ISC-57 — the same runs: `ch0 device` and `ch0 format` named 'Altavoces (High Definition Audio Device)' at 48000 Hz, 2 ch, 32 bit float, and `ch1` its microphone. `StreamFormatTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13 for the extensible format WASAPI really hands over, which reads as neither integer nor float until it is reduced
- ISC-58 — `LevelsTests` and `SourceMeterTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13; the same runs metered both sources every second, between −7.5 and −65.6 dBFS. A width no block of which could be metered is refused before a device is opened rather than on its first block, which `LevelsTests.A_format_that_could_never_be_metered_is_refused_before_anything_is_recorded` holds
- ISC-59 — `AudioDevicesTests` green 2026-08-13, and three runs 2026-08-13: `--microphone "blue yeti"` refused with exit 1 and nothing opened; a channel 1 whose file was already there refused with exit 1 after channel 0 had opened; and a channel 1 whose path could not be claimed at all — a directory standing in its place — refused with exit 1 after channel 0 was already recording, left nothing of channel 0 behind, and let the next attempt succeed once the obstacle was gone
- ISC-60 — `SharedTimelineTests.Two_fabricated_sources_come_out_as_one_pair_of_channels` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-13
- ISC-61 — `SharedTimelineTests.A_packet_after_a_gap_lands_where_its_position_says` green 2026-08-13
- ISC-62 — `SharedTimelineTests.A_stretch_the_device_never_delivered_stays_a_gap_of_its_own_length` green 2026-08-13
- ISC-63 — `SharedTimelineTests.Sources_that_agree_on_nothing_come_out_at_one_rate_and_one_width` green 2026-08-13
- ISC-64 — `PacketTallyTests` green 2026-08-14, and `The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` red against a tally advancing on frames instead of positions: it read 900 ms for the second of meeting it was given. Two `capture` runs on this machine 2026-08-14, of 20 s and 14 s, covered 0:00:20 and 0:00:14 on both sources with nothing lost on any of the four
- ISC-65 — the same runs: 2015 and 1352 loopback packets reported 9 to 10 and 9 to 20 ms apart, over a loop that polls the device every 50 ms — five packets a poll, which instants stamped where the application collected them could not have spaced at all. `PacketTallyTests.Packets_are_as_far_apart_as_the_device_read_them`, `A_packet_whose_instant_the_device_would_not_vouch_for_still_counts_as_meeting` and `A_source_that_opened_with_an_unvouched_packet_still_covers_it` green 2026-08-14
- ISC-66 — `TimelineDriftTests.Two_hours_of_two_devices_that_disagree_stay_under_fifty_milliseconds_apart` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-14: two hours of a 48 kHz loopback 50 ppm slow against a 44.1 kHz microphone 200 ppm fast, each instant jittered up to a millisecond, over 120 markers a minute apart. Tightened by hand, the worst sat 0.69 ms from the other channel and 1.5 ms from the shared clock; resampling at the label rate puts the run over 50 ms at the fourth marker and 1.8 s apart by the end. `SharedTimelineTests.A_fast_clock_is_pulled_back_throughout_and_not_at_the_end` holds the shorter case, its first marker 20 ms out at 10 s with the measurement disabled. A two-hour run on hardware end to end is a board task
- ISC-67 — `TimelineDriftTests.A_source_that_opened_late_and_runs_fast_reports_the_wait_apart_from_the_rate` green 2026-08-14: a microphone opening 250 ms late and running 2000 ppm fast reports the 250 ms as waited and 48096 Hz as its rate, neither absorbing the other, and all five markers it heard land within 5 ms of where the loopback heard them — where a quarter second read as drift would have pulled every one of them off
- ISC-68 — `SharedTimelineTests.A_device_whose_two_counters_disagree_stops_the_recording_rather_than_being_clamped` and `A_source_whose_clock_goes_backwards_stops_the_recording` green 2026-08-13
- ISC-69 — `SharedTimelineTests.A_source_that_goes_quiet_does_not_hold_the_rest_of_the_meeting` and `A_source_the_recording_left_behind_is_refused_rather_than_inserted` green 2026-08-13
- ISC-70 — `PacketTallyTests.The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions` green 2026-08-14: ninety packets either way, one covering a second of meeting with a tenth of it lost and the other nine tenths of a second with nothing lost. `capture` prints the number on its own line per source, and both runs printed 0 ms
- ISC-71 — `SharedTimelineTests.A_source_that_stopped_early_does_not_cut_the_meeting_short` green 2026-08-14: a microphone that stops at 15 s of a 20 s recording leaves the recording 20 s long with 5 s reported missing, rather than ending where it stopped
- ISC-72 — `SharedTimelineTests.Handing_a_source_over_in_clumps_seconds_late_records_the_same_meeting` green 2026-08-14: the same minute delivered smoothly and in five second clumps is the same recording sample for sample, with the two deliveries first shown to differ — runs of one source of under 5 packets against over 100. `.Handing_a_source_over_later_than_the_timeline_waits_gives_that_source_up` holds the far side of the bound, where the same packets in 35 second clumps are refused
- ISC-73.1 — the 14 s run 2026-08-14 played a 440 Hz tone from second 2 to second 12 into the endpoint channel 0 was recording. Its WAV peaks at 0.167 over 5–6 s and is exactly zero over 0–1 s and from 12.5 s to the end, with 1352 packets arriving throughout and nothing lost, so the stretches nothing played into are the silence they were rather than the tone still standing in the device's buffer
- ISC-73.2.1 — four `capture` runs on this machine 2026-08-20, Windows 11 build 26200, against channel 0 obtained from the process loopback rather than from a loopback on the playback endpoint; `docs/process-capture.md` holds all four with their packet counts, levels, losses and the `AUDCLNT_E_DEVICE_IN_USE` (`0x8889000A`) the old design met. No test reaches this and none can. Every one is the `capture` command built and run out of this tree rather than the build somebody installs, which is ISC-73.2.2 and is why the parent stays open with this leaf closed
- ISC-74 — `MeetingAudioTests.The_recording_never_carries_the_microphone_on_channel_0` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16, both ways round: with only the loopback loud the recording read back off disk peaks above half scale at position 0 of every frame and exactly zero at position 1, and with only the microphone loud it is the other way — so a build that swapped the contract and the recording together still fails one of the two. The 8 s `capture` run on this machine 2026-08-16 recorded −18.5 dBFS on channel 0 with its microphone silent, which is the source that was playing landing on channel 0 on real devices
- ISC-75 — `BlockSpoolTests` (`tests/MeetingTranscriber.Audio.Tests`), `.A_block_the_disk_did_not_keep_is_dropped_and_the_ones_before_it_are_not` and `RecoveryCommandTests` (`tests/MeetingTranscriber.Cli.Tests`) green 2026-08-15, the middle one red against a reader that skipped the checksum. Four `capture` runs on this machine 2026-08-15, killed at 4, 7, 8 and 11 s of 60, came back through `recover --keep` whole, nothing discarded on any of the eight sources — 341, 636, 793 and 996 blocks on channel 0, about 90 a second either way. 1500 bytes then taken off one of those tails cost the 2376-byte block they were inside and left the 792 before it
- ISC-76 — `MeetingAudioTests.Finishing_the_same_recording_twice_produces_the_same_file` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16: the same two spools finished twice are the same bytes, the same length and the same levels. On this machine 2026-08-16 an 8 s capture's `audio.wav` and the one produced from those same blocks afterwards by what is now `recover --keep` both hashed to `03aaad46b9e9d5e96cb4c25ff52eea4f07047a31bfa6c1a7dc29184896267a6b`, 130563 frames either way
- ISC-91 — `CorpusRebuildTests.A_second_extraction_leaves_the_first_ones_state_alone_and_starts_its_own_blank` green 2026-08-07
- ISC-92 — `CorpusIntegrityTests.Search_is_the_index_answering_and_not_the_table` green 2026-08-07
- ISC-94 — `CorpusSearchTests.A_hit_carries_the_meeting_the_date_the_title_a_snippet_and_where_it_was_said` green 2026-08-07
- ISC-95 — `CorpusSearchTests.A_meeting_being_deleted_is_not_something_search_offers` green 2026-08-07
- ISC-96 — `CorpusSearchTests.Throwing_both_indexes_away_and_rebuilding_them_answers_exactly_the_same` and `CorpusIntegrityTests.Compacting_leaves_search_answering_exactly_what_it_answered_before` green 2026-08-07
- ISC-97 — `CorpusSearchTests.A_query_the_index_cannot_parse_says_so_and_names_it` green 2026-08-07
- ISC-117 — `capture --process` runs on this machine 2026-08-15. A program that played nothing itself while a process it started looped a 440 Hz tone: 10 s at −8.7 dBFS every second, covering 0:00:10 over 1002 packets 10 ms apart with nothing lost — so it is the tree and not the process. Edge and Firefox playing the same tone from a page: 6 s each at −12.0 dBFS, the name resolving to the browser process out of 17 and 10 of that name. `AudioProcessesTests` holds that resolution and `FramePositionsTests` the placement the virtual device leaves to be worked out (`tests/MeetingTranscriber.Audio.Tests`), green 2026-08-15
- ISC-118 — the same session: following a program that played nothing while another program looped the tone left channel 0 silent for all 8 s and its file loudest silent, over 802 packets with nothing lost. Eight seconds later the whole machine's loopback heard that same tone at −13.9 dBFS, so it was there to record and the followed program's file did not have it
- ISC-119 — `SpoolManifestTests.A_recording_says_which_meeting_it_is_when_it_started_and_under_what_profile` and `.A_card_missing_something_that_identifies_the_recording_is_refused` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-15: the card is read back with no corpus and no database, and one missing field refuses it rather than answering four of the five questions
- ISC-120 — `SpoolManifestTests.A_recording_says_what_fed_each_of_its_channels` and `.Which_of_the_two_channel_zero_opened_as_is_the_cards_own_field` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20, reading both sources back in channel order out of a file listing them in the other one — the microphone by its endpoint id, channel 0 by what it was and by no id, since neither shape of it is a device. This claim said *which device* until 2026-08-20 and was closed 2026-08-15 on two endpoints read back; it was already loose then, because a channel 0 following a program named no device either, and it became wrong when the whole machine stopped being an endpoint
- ISC-121 — `SpoolChangesTests` and `UnfinishedRecordingsTests.A_recording_whose_channel_was_moved_is_found_again_saying_so` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-18, and the `capture --process explorer --whole-machine-at 12` runs of 2026-08-18 and 2026-08-20, whose folders came back through `recordings --spool` naming the moment channel 0 moved, what it moved to and what it had been following, beside a card still naming the program it opened on. The 2026-08-20 run is the same folder written by a channel 0 that moved onto a process loopback rather than an endpoint
- ISC-122 — `SpoolManifestTests.What_a_recording_says_about_itself_is_written_once` green 2026-08-15, the second write refused and the first card still saying what it said. `.A_folder_that_already_says_what_it_holds_takes_no_recording` holds the same rule where it costs nothing — before a device is opened
- ISC-123 — `UnfinishedRecordingsTests.Every_recording_nobody_stopped_is_found_again_with_what_it_is_and_what_is_in_it` and `.A_recording_with_no_card_is_offered_all_the_same` green 2026-08-15, over a root holding one recording with both sources, one with a single source and a folder that is not a recording at all. `RecoveryCommandTests.What_is_waiting_says_which_meeting_it_is_and_what_each_source_holds` (`tests/MeetingTranscriber.Cli.Tests`) holds it at the surface a person meets it at
- ISC-124 — `UnfinishedRecordingsTests` and `RecoveryCommandTests` green 2026-08-15: kept, the recording is read through and every file is still there afterwards; taken out, the audio lands where it was asked for and the blocks stay; thrown away, the folder is gone. `RecoveryCommandTests.Deciding_nothing_about_a_recording_is_a_misuse` and `.Deciding_two_things_about_a_recording_is_a_misuse` hold that there is no default
- ISC-125 — `UnfinishedRecordingsTests.Nothing_but_a_decision_about_one_recording_removes_a_folder` and `.Nothing_in_the_audio_engine_removes_a_file_it_did_not_just_create` green 2026-08-15, each red 2026-08-15 against a `Directory.Delete` and a `Delete(` planted in a source file that has no business holding one
- ISC-126 — `UnfinishedRecordingsTests.A_meeting_still_being_recorded_is_said_to_be_rather_than_offered_as_one_to_decide_about`, `.None_of_the_three_outcomes_lands_on_a_meeting_that_is_still_being_recorded` and `.A_recording_found_while_it_is_being_written_refuses_the_three_by_naming_the_meeting` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-20, and at the two surfaces a person reaches it from, `CorpusRecoveryCommandTests.A_meeting_still_being_recorded_is_listed_with_none_of_the_three_offered`, `.None_of_the_three_lands_on_a_meeting_that_is_still_being_recorded`, `RecoveryCommandTests.A_folder_a_capture_is_still_writing_is_listed_with_none_of_the_three_offered` and `.None_of_the_three_lands_on_a_folder_a_capture_is_still_writing` (`tests/MeetingTranscriber.Cli.Tests`). Red 2026-08-20 with the one answer made unconditional: 10 cases failed across those three projects and the other 1211 stayed green. Both orders are held between the suites — a recording found while a capture is writing is refused on what it is, and one found before the handle was taken is refused by the file system, which is what protects the blocks
- ISC-128 — `CaptureLoopTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16, over a loop body that ignores what it is asked and blocks on a gate nothing sets. Red 2026-08-16 against the unbounded wait this replaces, where that same test was still going at 2m 27s and never finished
- ISC-132 — `MeetingAudioTests.A_microphone_that_numbers_its_frames_at_its_own_rate_becomes_the_meetings_file`, `SharedTimelineTests.A_microphone_that_numbers_its_frames_at_its_own_rate_still_records_the_meeting`, `.A_source_whose_position_goes_backwards_gives_the_counter_up_and_keeps_the_meeting` and `PacketTallyTests.A_source_numbering_its_frames_at_its_own_rate_covers_the_meeting_and_loses_none_of_it` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16 over a fabricated webcam handing over 480 frames a packet and advancing its counter by 160. Red 2026-08-16 with the one line that gives a counter up commented out: the two timeline tests refused at “placed at frame 160 after reaching 480” and the tally reported 340 ms of the second it covers, the other twenty of the class green. `SharedTimelineTests.A_source_that_lost_a_stretch_before_giving_its_counter_up_still_records_the_meeting` is the changeover itself, over its own fixture, and was written red. A counter running finer is a board task and not something this closes
- ISC-133 — `SharedTimelineTests.A_recording_says_which_of_its_sources_had_its_counter_given_up_on` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-16 and red that day against the same commented-out line, and `RecoveryCommandTests.A_rate_a_counter_was_given_up_on_is_never_reported_as_measured` (`tests/MeetingTranscriber.Cli.Tests`) green 2026-08-17 over a recovery whose microphone hands over 480 frames a packet and counts by 160, red the same day with `measured` put back on that line and the other forty-two tests of its project green over the edit
- ISC-77 — the 26 s `capture --process explorer --whole-machine-at 12` run on this machine 2026-08-18, with a Windows sound played into the endpoint from second 16: the offer came at 0:00:10, taking it at 12 moved channel 0 from `explorer (pid 24172)` to `Altavoces (High Definition Audio Device)` without the meeting stopping, both spools ran to 0:00:26, `audio.wav` came out one file of 421164 frames, and the seam is 11 ms recorded as lost, not closed up. Channel 0 reads silent for every second it followed `explorer` and −12.9 dBFS after, the change stamped 12.1 s after that source opened. `SilentProgramTests`, `SpoolChangesTests` (`tests/MeetingTranscriber.Audio.Tests`) and `WholeMachineTests` (`tests/MeetingTranscriber.Cli.Tests`) green the same day. No test reaches the move itself, which needs two real devices
- ISC-139.1 — the 16 s `capture --process explorer` run on this machine 2026-08-18: the offer came at 0:00:10, nobody took it, and the recording ended still on `ProcessLoopback`, its folder holding no changes file. Its microphone heard the room throughout, so channel 0's silence was the program's. `SilentProgramTests.A_channel_recording_the_whole_machine_is_never_the_wrong_program` (`tests/MeetingTranscriber.Audio.Tests`) green the same day is the offer never made where it would mean nothing, and `WholeMachineTests` (`tests/MeetingTranscriber.Cli.Tests`) green that day holds the gate with no device in it, under ten mutations one at a time, each red at the test named for it. No probe reaches an activation Windows refuses; that a refusal opens no second device is read off `CaptureSession`
- ISC-136 — `DeviceReleaseTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-17, over a release body that never comes back. Red 2026-08-17 three ways: the release run on the caller's thread instead, killed at 100 s having never finished; the exception boundary taken out, the test host itself dying on `System.IO.IOException`; and an `InvalidOperationException` planted where a handle closes, over the whole `MeetingTranscriber.Audio.Tests` project. That both `Dispose` paths route through it is read off `WasapiStream`
- ISC-137 — `DeviceOpenTests` and `CaptureLoopTests.A_loop_that_never_gets_underway_is_given_up_on_rather_than_waited_on` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-17. Red 2026-08-17 with the ask run on the caller's thread instead, which is what `AudioDevices.Open`, `endpoint.AudioClient`, `client.Initialize` and `output.Init`/`output.Play` all did: the run was killed at 600 s having never finished. The loop half red the same day against the unbounded gate it replaces, which came back in 0.1 ms. Naming the device that did not answer is ISC-138 and stays open. What no test reaches is that every call into a driver on the way in routes through that ask, read off `WasapiStream`: forcing it needs the wedged device ISC-129 to ISC-131 wait on
- ISC-152 — `UiTextsTests`, `UiTextTests` and `TextLineTests` (`tests/MeetingTranscriber.Presentation.Tests`) and `ScreenTextsTests` (`tests/MeetingTranscriber.App.Tests`) green 2026-08-26. What each of them holds is stated in the tests themselves; what no test reaches is the WinUI tree, so what is held instead is that no screen has words of its own to leave behind.
- ISC-153 — `UiLanguagesTests` with `.A_choice_beats_windows`, and `LanguageChoiceTests` with `.A_choice_and_windows_together_settle_what_opens` (`tests/MeetingTranscriber.Presentation.Tests`) green 2026-08-18, the last reading the choice back through a second reader, so what is proved is the file and not a field. Both classes red 2026-08-18 with the four lines that let a choice win removed: those two tests failed and the other seventeen stayed green. The one line the tests do not reach is `GlobalizationPreferences.Languages`, argued against `ApplicationLanguages` in `App.xaml.cs`
- ISC-127 — `ExtractionPositionTests` and `.A_position_in_an_extraction_belongs_to_one_row_wherever_it_is_stored` (`tests/MeetingTranscriber.Infrastructure.Tests`) green 2026-08-16 over `decisions`, `action_items`, `open_questions` and the note pinned to one alike; `.Nothing_the_model_anchors_goes_unprobed` red that day with `action_item_progress` left off the probed list, and `.A_position_that_is_not_a_position_is_refused_of_a_note_as_well` red against position zero, so it hangs on the CHECK. `CorpusRebuildTests.Rebuilding_puts_everything_an_extraction_produced_back_at_its_own_position` and `MeetingTranscriber.Processing.Tests`' `.What_the_rebuild_cannot_produce_again_it_does_not_delete` hold the other side
- ISC-81 — `PausedRecordingTests` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-18 and again 2026-08-19: a meeting paused for 35 seconds comes back 45 seconds long with the paused stretch silent and the room loud either side, through the one `RecordingPause` the recording has. Red 2026-08-19 twice: dropped packets instead of substitution fail the long pause outright, and every block let through unsubstituted brings the paused stretch back at 0.97 of full deflection. `Both_channels_pause_on_the_one_answer` holds that the pause is the recording's and not a source's. What no probe here covers is the capture callback itself, which needs a device
- ISC-156 — `MeetingRecordingsTests.A_meeting_and_its_folder_exist_before_any_of_it_is_captured`, `.A_meeting_is_identified_without_a_title_or_anything_a_provider_says` and `.The_meeting_is_recognisable_with_the_database_deleted` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-18, the row read back through a second connection and the meeting read off its card with the database deleted. That the corpus really precedes the devices in one press is `MeetingRecording.Start`'s ordering, argued there; the hand probe is `record`
- ISC-157 — `MeetingRecordingsTests.Stopping_a_recording_queues_no_work_on_the_meeting` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-09-02 against the widened sentence: after a recording is stopped and its audio written, `processing_jobs` is empty read back through a second connection and the one place that decides answered nothing. Red 2026-09-02 with that place answering `Transcribe`, this and three of the other nine failing on the refusal that catches what decides changing without the queueing being written. What it reaches is the stronger sentence, nothing at all, because nothing yet lets anybody ask for anything beforehand; the half where somebody has is ISC-157.1, open.
- ISC-149 — `WaitingRecordingsTests.A_recording_waiting_to_be_decided_about_never_keeps_a_new_meeting_from_being_recorded` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-18: a whole meeting recorded and finished over two undecided recordings, every file hashed before and after, so a build that cleared the way by tidying them up fails rather than passing the first half. What no probe here covers is a screen adding a gate of its own, because there is no recovery screen yet; the claim is the constraint on the one that comes
- ISC-110.1 — `CorpusLocationTests.A_folder_under_the_users_application_data_goes_when_the_package_does`, `.A_folder_outside_it_survives_the_package`, `.A_corpus_under_the_users_application_data_is_refused_though_it_is_there_and_whole`, `.A_folder_that_only_leads_into_the_container_goes_with_it_too`, `.A_folder_that_leads_into_the_container_through_another_link_goes_with_it_too`, `.A_loop_of_links_is_answered_rather_than_followed`, `.The_folders_refused_are_the_ones_Windows_itself_names`, `.An_application_data_folder_kept_off_the_profile_is_refused_like_any_other` and `.Nothing_the_application_is_built_out_of_asks_the_package_where_to_write` (`tests/MeetingTranscriber.Infrastructure.Tests`) green 2026-08-19, the folders built from the running profile and the junctions made with `mklink /J`. Red one test at a time 2026-08-18 and 2026-08-19 — the link walk removed, the chain cut back to one hop, the last two Windows folders dropped, the folders it is handed ignored, and `Windows.Storage.ApplicationData.Current.LocalFolder` planted in `MeetingTranscriber.App` — and red with the rule narrowed back to the package container, exactly four failing and 253 green. What no probe here reaches is a packaged process writing under a redirected path — that is ISC-110.2
- ISC-114.1 — `CorpusLocationTests.With_nobody_having_chosen_the_corpus_is_directly_under_the_users_profile`, `.No_folder_the_application_would_write_a_corpus_in_is_under_app_data` and `.A_first_corpus_is_never_put_under_app_data` green 2026-08-19, `%USERPROFILE%\MeetingTranscriber` compared as paths with nothing read off the disk. Red that day with the fallback put back under `SpecialFolder.LocalApplicationData`: the first two failed and the other 255 stayed green
- ISC-114.2 — `CorpusLocationTests.The_corpus_opens_where_the_setting_says` and `.The_same_folder_opens_again_the_next_time_the_application_starts` green 2026-08-18, the second reading it back through a second `CorpusLocation` over the same file so that what is proved is the file and not a field. `.A_corpus_moved_somewhere_else_keeps_every_path_it_recorded` moves a corpus holding a rendered file and a paid response to another folder and opens it from there: the reconciler reports nothing with every artifact hashed, and both files read back byte for byte. A second temp folder and not a second drive, which no build agent has — what the claim turns on is that a stored path is relative to whichever folder the corpus is opened as, and that is what changes
- ISC-114.3 — `CorpusLocationTests.A_folder_that_does_not_answer_is_refused_naming_it`, `.A_folder_with_no_corpus_in_it_is_refused_rather_than_filled_with_a_new_one`, `.A_corpus_file_of_no_bytes_is_not_a_corpus` and `.A_setting_saying_nothing_usable_is_refused_and_not_read_as_nobody_having_chosen` green 2026-08-18. Each carries the path it refused, and the last is five cases — blank, spaces, two relative paths and a sentence. Red 2026-08-18 with the unusable-setting refusal replaced by the fall back the language preference makes: exactly those five failed and the other twenty stayed green. A folder that is gone and one this user may not read arrive under one refusal deliberately, because Windows answers them the same way and guessing between them would send half the people who hit it to check the wrong thing
- ISC-114.4 — `CorpusLocationTests.A_folder_that_is_not_there_never_becomes_a_second_empty_corpus`, `.Somewhere_the_application_would_put_a_corpus_says_whether_one_is_there_yet` and `.A_folder_the_next_start_would_refuse_cannot_be_recorded_as_where_the_corpus_is` green 2026-08-18, the second deleting a real corpus out from under the fallback and red that day with the fallback claiming a corpus unconditionally
- ISC-139.2 — `RecorderScreenTests.The_whole_machine_cannot_be_taken_before_the_recording_has_offered_it`, `.The_whole_machine_is_takeable_once_the_recording_has_offered_it`, `.The_whole_machine_is_taken_once_and_is_not_on_offer_afterwards`, `.A_meeting_already_recording_the_whole_machine_is_never_offered_it`, `.The_whole_machine_is_not_takeable_before_a_meeting_is_running` and `.The_whole_machine_is_never_taken_while_the_meeting_is_paused` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-19; red that day with the offer dropped from the condition, exactly two failing and the other 46 green. What no probe reaches is the window itself, which needs a UI thread and a packaged host
- ISC-158.4 — `RecorderLanguageTests.A_meeting_is_not_recorded_until_what_will_be_spoken_in_it_has_been_said` and `.Nothing_a_recording_reaches_knows_what_language_the_application_is_read_in` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-19, the second walking every assembly this side of the application reaches and finding no catalogue of what a person reads; the argument is `MeetingRecordings.Open`'s own
- ISC-158.5 — `RecorderScreenTests.Recording_cannot_start_with_one_of_the_three_unanswered`, `.A_screen_opens_with_nothing_said_and_nothing_to_press` and `.Recording_starts_once_all_three_have_been_answered` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-19, one unanswered question per case and the language left blank as well as absent. Red that day with the condition replaced by an unconditional yes: those five failed and the other 43 stayed green. Why what channel 0 follows is a third answer and not the engine's two is `RecorderScreen`'s own
- ISC-82 — `MeetingStageTests.A_meeting_with_no_audio_yet_is_never_offered_for_transcription`, `.A_transcription_whose_job_landed_is_not_offered_again_because_its_file_is_missing` and `.Nothing_the_application_offers_is_work_it_could_do_for_nothing` (`tests/MeetingTranscriber.Domain.Tests`), `MeetingWorkTests` (`tests/MeetingTranscriber.Infrastructure.Tests`) and `MeetingCardTextTests` (`tests/MeetingTranscriber.App.Tests`) green 2026-08-19 — the answer, the answer read off a corpus, and the screen having a word for every answer there is. Red 2026-08-19 with the bottom rung's condition replaced by an unconditional `Recorded`, 2 of the 21 failing, and again with one stage's arm deleted from the tables `MeetingCardTextTests` reads out of source, naming the stage that had lost it. What no probe reaches is the WinUI tree, which needs a UI thread and a packaged host; what is held instead is that the answer is right and that the screen has words for all of it
- ISC-147 — `MeetingWorkTests.A_stage_declined_can_be_taken_later`, `.Declining_a_stage_leaves_the_meeting_where_it_was_with_the_same_action`, `.A_stage_asked_for_and_not_yet_run_can_be_taken_back` (`tests/MeetingTranscriber.Infrastructure.Tests`) and `MeetingStageTests.A_declined_stage_stays_where_it_was_and_keeps_its_action` green 2026-08-19; red that day with a declined stage made untakeable, 1 of 21 domain tests and 3 of 14 corpus tests failing
- ISC-148 — `MeetingWorkTests.What_a_meeting_is_waiting_for_is_the_same_after_the_application_is_reopened` (`tests/MeetingTranscriber.Infrastructure.Tests`) green 2026-08-19 over three meetings — one untouched, one turned down, one asked for — read back through a connection that never saw the one that answered. Red that day with the answer a person gave never reaching a row: it failed on the turned-down meeting's standing, and 5 others failed with it. What is argued rather than probed is that a second connection is a second run of the application: everything read is on disk and nothing is held between calls
- ISC-80 — `RecordingMetersTests.A_channel_hearing_nothing_reads_as_silent_and_one_hearing_something_does_not`, `.Both_channels_are_metered_for_as_long_as_the_meeting_is_running`, `.Only_a_meeting_that_is_running_is_metered`, `.Nothing_is_metered_when_no_meeting_is_being_recorded` and `.A_reading_carries_a_number_or_no_words_at_all` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-19, both state theories derived from the enum so a state added later is held to showing nothing. Red 2026-08-19 three times: a channel never reading as silent failed 1 of 67; the bar drawn off the peak with the state gate removed failed 9; a reading that answered with words for silence failed 1 of 69. `SourceMeterTests.Reading_it_starts_the_next_stretch` holds that reading empties the meter. What no probe reaches is the window, which needs a UI thread and a packaged host, and which source fills which reading, argued at `RecordingMeters` rather than probed
- ISC-150 — `RecordingMetersTests.A_meeting_playing_through_speakers_says_the_others_are_heard_twice` and `.Nothing_is_warned_about_when_no_meeting_is_being_recorded` (`tests/MeetingTranscriber.Recording.Tests`), and `EndpointKindTests` with `.Nothing_else_is_taken_for_a_room` (`tests/MeetingTranscriber.Audio.Tests`) green 2026-08-19. Red 2026-08-19 with the rule answering no unconditionally: 2 of 67 recording and 1 of 216 audio tests failed. `meeting-transcriber devices` run 2026-08-19 on this machine reports `form factor  Speakers — the microphone hears the others twice`, which is one endpoint of one kind on one machine
- ISC-83 — `AudioIntakeTests.More_channels_than_a_pair_still_become_a_meeting` and `.How_long_the_meeting_is_comes_off_the_audio_and_never_off_a_header` (`tests/MeetingTranscriber.Recording.Tests`), `ImportAudioCommandTests` (`tests/MeetingTranscriber.Cli.Tests`) and `AudioFilesTests.More_than_two_channels_average_the_same_way` green 2026-08-20, probed at one track, at a pair and at the six a room's microphone array hands over. The second red 2026-08-20 with the header believed instead of the bytes counted: 2 of 239 audio and 1 of 87 recording tests failed. Every fixture is `ForeignWav`'s rather than `WaveFileWriter`'s
- ISC-151 — `AudioIntakeTests.A_stereo_file_this_application_did_not_record_is_never_two_channels_of_one_meeting`, `.A_card_beside_audio_this_application_could_not_have_made_decides_nothing`, `.A_card_says_nothing_about_a_file_it_is_not_about`, `.A_folder_saying_two_sources_over_a_single_track_is_still_one_track` and `.This_applications_own_recording_arrives_as_its_two_sources` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-20, the last over a folder this product really wrote and hashed before and after; the second is the forgery an adversarial pass reproduced against the built command line, where a typed card alone filed a stereo export as two sources and `check --verify-contents` called the corpus sound. `ImportAudioCommandTests.Nobody_is_asked_what_a_channel_carries` is the claim's other end. Red 2026-08-20 with the profile inferred from the channel count: 8 of 86 recording and 1 of 89 command-line tests failed
- ISC-159 — `AudioIntakeTests.Audio_shaped_like_this_applications_own_with_nothing_saying_so_is_one_track` and `.A_card_that_does_not_read_as_a_meetings_vouches_for_nothing` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-08-20, both over files matching the interchange format field for field and the second with a browser extension's `manifest.json` beside it; the first also hashes the filed file against its row. `.A_recording_this_corpus_has_not_stopped_yet_is_not_something_to_bring_in` is the separate case, refused for where it is rather than for a card that failed to parse. Red 2026-08-20 with the refusal put back for a file nothing vouches for: 2 of 90 recording tests failed. Rewritten in place that day, and `git log -- ISA.md` holds what it said
- ISC-158.1 — the UI probe on the packaged build 2026-09-01, the application in English, one walk per answer channel 0 can follow: `see nothing-said choose MicrophonePicker fifine choose SourcePicker <what> choose SpokenPicker English wait RecordButton see answered`, with `<what>` the whole machine's own entry — `Everything this machine plays` — the first time and one program the second. Record reads `disabled` on both opening trees and on neither answered one, which is the whole of what the two pairs differ in. That a `choose` which ran found the entry it named is `Session.Choose`, reaching one the list has not drawn is `Session.Offered`, and opening at the top is `MainWindow.xaml`
- ISC-165.1 — `MeetingRecordingsTests.A_meeting_nobody_named_comes_out_of_recording_with_no_name` (`tests/MeetingTranscriber.Recording.Tests`) green 2026-09-01, red that day with a title written at `MeetingRecordings.Open`, and the UI probe on the packaged build: a meeting recorded and never named reads `Unnamed` on the list, which is the catalogue's own two words rather than anything about that meeting. Why it is asserted after stopping, and why the two doors that do carry a title are not counter-examples, is on the test
- ISC-170 — the UI probe on the packaged build 2026-09-01, the application in English: `see` as the first instruction after it was started and before anything was pressed. One window, and its tree carries both halves — the microphone and source pickers, what will be spoken and record above, and under them the meetings header, the count and a recorded meeting's row with the press its stage offers. A list arriving in a window of its own would have stopped that walk rather than passed it, for the reason `docs/ui-probe.md` gives under `Which window is the screen`
- ISC-171 — the UI probe on the packaged build 2026-09-01, the application in English: `see docked press OpennessButton wait OpennessButton see whole press OpennessButton wait OpennessButton see docked-again`. Raised, the recorder half is out of the tree rather than scrolled off it — the pickers, record, pause, carry on and stop gone — while the corpus line, the report, the status line and the packaging button stay, for the reason `MainWindow.xaml` gives, which is why the claim is the recorder's room and not the window. The control that raised it is the same element in the same place, reading `Bring the list back down` where it read `Open the whole list`, and the third tree is the first line for line apart from when it was read
- ISC-158.3 — `SavingTheMeetingTests.What_is_filed_is_the_same_whether_or_not_anybody_is_watching` (`tests/MeetingTranscriber.Recording.Tests`) and `SavingCardTests.The_application_and_the_prompt_stop_a_meeting_through_the_same_call` (`tests/MeetingTranscriber.App.Tests`) green 2026-09-01: two meetings out of identical spools finished at the same instant, one save watched and one not, equal on the audio's hash and size, the length, the run's end and every stored fact but their ids — and both entry points stopping through the recording's own call, with neither filing any part of a meeting itself. Red 2026-09-01 twice: with the window's `recording.Stop(` replaced, and with `MeetingRecordings.Finish` written into its stop handler. What no probe reaches is a window really recording one, which needs two devices and a meeting somebody sat through
- ISC-158.2 — the UI probe's script host on the packaged build 2026-09-02, a real microphone and this machine's loopback into the real corpus: `choose MicrophonePicker fifine choose SourcePicker "Everything this machine plays" choose SpokenPicker Espa`, then `press RecordButton sleep 25 see a1-recording sleep 45 see a2-recording press PauseButton sleep 25 see a3-paused` and `press ResumeButton sleep 45 see a4-resumed sleep 60 see a5-recording press StopButton`. One window and no prompt; `0:03:21` in the corpus after. Pause live and Carry on `disabled` while recording, swapped while paused, Stop live on all four. The holds record a meeting rather than wait on a press. No run reaches a device changing under a pause
- ISC-158.9 — the same walk. `TheClock` is absent before Record and reads `0:00:24`, `0:01:09`, `0:01:35`, `0:02:20`, `0:03:20` on the five trees across the meeting, the third while paused, against 25, 70, 96, 141 and 201 seconds of wall clock after the press; gone from the first tree after Stop and from every tree of the save. Five instants were observed. That it is there between them is read off `ShowTheClock` and `RecordingClock`, not sampled, and no run reaches a clock stepped back mid-meeting
- ISC-158.10 — the same walk and ISC-158.7's. A meeting recording and one saving show a size on the list and never a length — `2026-09-02 02:13 · 265.5 MB` — and the saving card carries none; the first tree after reads `2026-09-02 02:13 · 0:06:00`, the figure the report gives. ISC-79's run is sharper: a crashed one read `0:00:50 · 37.0 MB` and became a meeting of `0:00:50`. The other two are `CommandLineTests.Status_says_what_the_corpus_holds_and_never_how_long_a_meeting_was` (`tests/MeetingTranscriber.Cli.Tests`) and `TranscriptRendererTests.The_rendered_files_never_say_how_long_the_meeting_was` (`tests/MeetingTranscriber.Processing.Tests`), green 2026-09-02 over one meeting with a length and one without, red with a length added to each. `recovery` does say one off the blocks before a meeting row has it, which is the preview it exists for. Unreached: a save in flight, which `recovery` cannot tell from a crash — card #86
- ISC-158.7 — the UI probe on the packaged build 2026-09-02, a six-minute meeting, 265 MB of spool: `press StopButton see e1 see e2 see e3 sleep 1 see e4 sleep 1 see e5 sleep 2 see e6 sleep 2 see e7 sleep 3 see e8 sleep 5 see e9 sleep 10 see e10`. The recorder card is out of the tree and `Saving the meeting` in it on the first five, 0.1 to 2.5 seconds after the press returned; the sixth, at 4.6 seconds, carries `recorded: 0:06:00`. Unreached: `Letting both sources go` was done on the first tree, so the card was never seen to change; the save ended somewhere in the 2.1 seconds nothing sampled; and it never took the minutes a long meeting does
- ISC-79 — the UI probe on the packaged build 2026-09-02: `choose MicrophonePicker fifine`, then `press RecordButton sleep 50 see b1-recording kill`. Channel 1 read `-96.6 dBFS` there, a level and not the silent mark ISC-80 shows, so the microphone was open. The next start offers the recording at the top of the list — `The application closed in the middle of this recording. The audio is whole up to there.`, `0:00:50 · 37.0 MB`, Discard and Keep — and `press Keep` files a meeting of `0:00:50`. Its `audio.wav` is 16 kHz stereo, 803262 frames, 50.204 s, channel 0 at RMS 3210 of 32767. Nobody heard it: `SoundPlayer.PlaySync` returning after 50.4 s is Windows accepting the file
- ISC-168 — a real paid response filed by `import-response` and killed the moment its `manifest.json` landed, which is what `MeetingIntake` says an unfinished render leaves: the folder held that and `deepgram.json`, nothing else. Started with `sleep 20 see d1-after-launch`, nothing pressed, the folder then held `transcript.md` and `utterances.jsonl`, `status` counting 637 more turns. Run again over a second response on a build carrying `main`'s `MeetingRenderer`: 136 more turns, two more derived rows. What was probed is the launch, which today is the only path — nothing brings a response back into a running application, and that is ISC-170.1's. The `sleep 20` is a bet and not a wait: nothing announces a finished render
- ISC-176 — `IsaStructureTests` (`tests/MeetingTranscriber.Isa.Tests`) green 2026-09-02 over checks 15 and 16, on a branch against the fork point. Red 2026-09-02 twice: with `ISC-174` reworded and ticked in the tree, `.No_claim_is_closed_in_words_the_file_did_not_already_carry` named it and three of twenty-three failed; and with `ISA_TRUNK_BEFORE` set to a commit the trunk stood at in August, `.No_claim_is_written_into_the_file_already_closed` named what had closed since, where the push route had said nothing at all. `.What_a_change_is_judged_against_is_said_and_never_guessed` refuses a name there. Not closed here: words landing ahead of the branch that ticks them can still have been worded to fit — `.claude/agents/auditor.md` asks that.
- ISC-175 — `CorpusRebuildTests.A_meeting_whose_second_derived_file_cannot_be_written_keeps_both_of_the_ones_it_had` and `MeetingRendererTests.A_render_that_cannot_write_the_second_file_leaves_both_of_them_as_they_were` (`tests/MeetingTranscriber.Processing.Tests`), `DurableWriteTests.A_set_whose_second_file_cannot_be_written_leaves_the_first_one_alone` and `.A_set_whose_second_destination_cannot_be_taken_leaves_the_first_one_where_it_was` (`tests/MeetingTranscriber.Infrastructure.Tests`) green 2026-09-02, each red that day against a render that put one file in place before it began the next. Two things none of them reaches: a machine dying inside the run of renames, which no filesystem makes one act, and the stored turns, which are replaced before either file and can be a generation ahead of both
- ISC-177 — `DurableWriteTests.A_replace_that_stopped_partway_leaves_the_copy_it_set_aside` and `.A_sweep_running_beside_a_write_leaves_the_write_alone` (`tests/MeetingTranscriber.Infrastructure.Tests`) and `CommandLineTests.A_write_still_being_made_is_left_where_it_is_and_named` green 2026-09-02, each red that day on one reverted line: the copy named `.partial` again, and the temporary's handle sharing deletion. Three things outside it: the tidy-up inside a write does remove a copy, which is a write somebody asked for; the audio engine writes `.partial` files nothing holds, and no probe races a sweep against one; and neither probe schedules a sweep and a replace at once.
- ISC-166 — `WhoIsUsingThisRowTests` (`tests/MeetingTranscriber.Presentation.Tests`), `HumanLayerTests` (`tests/MeetingTranscriber.Infrastructure.Tests`) and `MeetingRendererTests.The_microphones_own_voice_reads_as_whoever_said_they_are_using_this`, `.A_meeting_rendered_before_anybody_said_who_is_using_this_names_nobody`, `.Rendering_again_after_the_answer_arrives_names_a_meeting_that_had_nobody` green 2026-09-02 over `tests/fixtures/deepgram/two-channel-one-voice-me.json`, one voice on the microphone against three on the loopback: the transcript reads `## Ada` and leaves `ch0:speaker_0` alone. Red that day with the settle taken out, three failing. Asked and kept is the UI probe on the packaged build 2026-09-02, on a corpus with no `is_me` row: the field opens empty, `Save` dead, the question under it; typed and pressed, the name is in `people` and the question gone; the next start reads it back; typed again, that row is renamed and no second person appears. Unreached: a meeting recorded here and transcribed — card #111
