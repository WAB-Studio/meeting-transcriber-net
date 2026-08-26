# Olivo — the visual system

Every screen of this application is drawn from what is on this page. Open it before building a
screen, and again before adding a control to one that exists.

**This page is the present tense and nothing else.** It says what the design *is*, imperatively —
never what it used to be, never what was tried and dropped, never a value beside the value that
replaced it. A rule that carries its own history teaches two rules and lets a reader pick. So
whoever changes something here **replaces** the sentence rather than adding to it: what the design
was yesterday lives in `git log -- docs/design.md` and in the commit that changed it, which is
where somebody looking for it will know to go. A reason for a rule is welcome and is what makes a
rule survive; a record of the rule it replaced is not.

`docs/design/` holds the seventeen artboards — the screens as pictures, openable in a browser.
**They are reference and this document is the authority.** Where the two disagree, this page is what
a screen is built from and the artboard is what gets corrected.

The system is called Olivo, after the one colour that carries it.

## Two colours and nothing else

This is a rule, not a palette.

- **Olivo `#4F7561`** is what is alive and well: it is being heard, it is being recorded, this is
  the thing to press.
- **Pico `#C2683C`** is what wants attention: it clipped, it was cut off, this one costs money.

**Red does not exist in this application.** Recording is olivo, because recording is not an alarm.
A screen that reaches for a third accent is a screen that has not decided which of the two it
means.

Everything else is paper, card, ink and three greys. Colour is information; it is never decoration.

**Two surfaces, and they alternate**: the window is papel, a card laid on it is tarjeta, and a
control sitting on that card is papel again. There is no third. A block nested inside a card does
not get a surface of its own — where a card genuinely holds two things, a 1px `#E6E4DE` rule
separates them, because a third tint at the same lightness as the paper is depth as decoration.

## Colour

The value and the role are fixed here. The resource key is this document's suggestion and the
first screen to need one settles it — but every screen after that uses the same key.

| Role | Value | Where | Key |
| --- | --- | --- | --- |
| Papel — paper | `#FCFCFB` | the window's background | `PaperBrush` |
| Tarjeta — card | `#F4F3EF` | a block laid on the paper | `CardBrush` |
| Tinte de decisión — decision tint | `#EEF2EF` | something waiting on the person; the selected option of a set | `DecisionTintBrush` |
| Tinte de atención — attention tint | `#F8EDE6` | something lost or about to be | `AttentionTintBrush` |
| Tinta — ink | `#1C1B19` | text, and the fill of the principal act | `InkBrush` |
| Secundario — secondary | `#6E6C66` | the second line of a pair; a control at the margin | `SecondaryTextBrush` |
| Terciario — tertiary | `#9B9891` | data, counts, units, labels | `TertiaryTextBrush` |
| Línea — rule | `#E6E4DE` | a 1px divider; the trough of a two-way pill | `LineBrush` |
| Pista de medidor — meter track | `#E1DED7` | the meter's empty segments | `MeterTrackBrush` |
| Zona caliente — hot zone | `#EDD5C7` | the meter's segments above −12 dB | `HotZoneBrush` |
| Olivo — olive | `#4F7561` | alive and well; see above | `OliveBrush` |
| Pico — peak | `#C2683C` | wants attention; see above | `PeakBrush` |

Speakers get their own three, and only these three:

| Speaker | Value |
| --- | --- |
| First — the user's own microphone | `#4F7561` |
| Second | `#A0567A` |
| Anybody with no name yet | `#C3BFB6` |

A fourth speaker has no colour yet and nobody has decided one. Until somebody does, a third named
speaker takes the no-name grey rather than a colour invented on the spot.

Three greys appear inside components rather than as tokens: `#DEDBD4` for the 1px inset ring on an
empty or optional control, `#C3BFB6` for an unticked box, and `#B9B5AC` for the bars of an audio
clip's waveform.

## Type

**Space Grotesk** 400/500/600 for text. **JetBrains Mono** 400/500 for every number that gets
compared to another one: clock times, durations, decibels, sizes, counts, and dates in small caps.
Text never goes in mono. A number that is part of a sentence is text.

Sizes are written `size/line-height` in px.

| Rank | Face | Size | Weight | Tracking |
| --- | --- | --- | --- | --- |
| Stopwatch | mono | 62/72 | 500 | `-.03em` |
| Screen title | Space Grotesk | 26/32 | 600 | `-.02em` |
| Sub-screen title | Space Grotesk | 20/26 | 600 | `-.02em` |
| Section | Space Grotesk | 17/22 | 600 | `-.015em` |
| Transcript | Space Grotesk | 15/26 | 400 | — |
| Body | Space Grotesk | 14/20 | 400 | — |
| Data | mono | 11/18 | 400 | — |

Seven ranks, and a screen uses no size that is not one of them. A second line that is quieter is
quieter by colour: the three inks carry that, and there is no rank between Body and Data to reach
for.

**A label is never set in capitals and never tracked out.** Spaced small capitals as a data label
is the most borrowed gesture in software of the last five years, and mono at 11 already reads as a
number without raising its voice.

The stopwatch is 62/72 on a screen it owns. Where a live transcript or an alert owns the screen
instead, it drops to 40/48 and keeps everything else.

A **sub-screen** is one reached from another and returning to it — the meeting, classifying it,
who is who, the corrections, the settings. It carries the 20/26 title and a 34px round back
button; the screens at the top level carry the 26/32 title and no back button.

The fallbacks are `'Segoe UI', system-ui, sans-serif` for text and `'Cascadia Mono', Consolas,
monospace` for numbers, so a machine without the two fonts still reads.

## Radii

Four values, and they are the platform's:

- Anything pressed — **4**
- A card, or a row in a list — **8**
- The one thing that interrupts the screen — **12**
- A circle, where the thing is genuinely round: the back button, a speaker's dot, a radio

**999 does not exist here, and neither does a fifth value.** A design where nothing has a straight
corner is a design where shape carries no information: the eye has nothing to catch on and the whole
thing reads as generated rather than drawn.

Four and eight are what Windows itself uses, and what every well-built application on it uses. That
is the grammar of the platform and it is not what makes any application ugly.

**What tells a control from a container is not its radius.** It is fill, weight and height — the
ranks below. A pressable thing is filled or ruled and stands 34 to 46 tall; a container is flat and
holds things. Somebody scanning a screen knows what to press and, in the same glance, what is
important — which is the half a page of identical pills cannot say.

## Spacing

- Screen — **30** vertical, **40** horizontal
- Large card — **20–24**
- Small card — **13–16**
- Between blocks — **18–20**
- Inside a card — **10–14**

## Heights

Height is half of what says a thing is pressable, so each of these is a value and not a range:

- The principal act — **46**
- A normal button — **42**
- A control — **34**
- A small button inside a row — **30**
- The round back button — **34**

## Controls

Five ranks, and a screen has at most one of the first.

| Rank | Fill | Text | For |
| --- | --- | --- | --- |
| The principal act | tinta `#1C1B19` | papel | the one thing this screen is for: start recording, stop, save |
| Recommended | olivo | papel | the cheap or safe way out of a decision |
| Has a consequence | pico | papel | it costs money, or something is lost |
| Normal | tarjeta `#F4F3EF` on papel; papel with a 1px rule on tarjeta | tinta | everything else |
| At the margin | none | secundario | dismiss, cancel, leave it as it was |

**A button that opens the question is not the button that answers it.** The next step on a meeting's
row — *Transcribir*, *Resumir* — costs nothing to press: it opens the dialogue where the charge is
actually agreed to. So it takes the **normal** rank, and pico appears only on the act inside that
dialogue. A list of twelve meetings with twelve orange buttons spends the colour that is supposed to
mean *this one costs money*, and once it is spent nothing on the screen can say it any more.

The normal rank has two fills for the same reason the surfaces alternate: a tarjeta button on a
tarjeta row is invisible. On papel it is tarjeta with no rule; on tarjeta it is papel with a 1px
`#E1DED7` rule — **not `#E6E4DE`**, which is the dividing rule and against tarjeta is four per cent
of nothing, so the button reads as a stray outline rather than a thing to press.

### Two places, and they mean the same thing everywhere

Every row, every notice, every screen foot lays its buttons out the same way. The positions carry
the meaning, so the eye learns them once and never reads a button to find out what kind it is.

- **Left is the neutral one** — open it, see it, leave it as it was. Never filled, always secondary
  ink. It is the same button on every screen and only the word changes.
- **Right is the act** — the one thing this row, notice or screen is asking for. Always filled,
  always one verb. Its rank is the rank of what carries it: a row or a notice takes the normal
  rank, and a whole screen whose one purpose is that act takes the principal one — *Guardar* on a
  form, *Empezar a grabar*, *Detener*. The position never changes; only the fill says which it is.
- **And nothing else**, except where something is lost or charged. That one sits **to the left of
  the left one, past a gap**, at the margin, so it cannot be pressed by reflex.

A screen that gives each row its own button count reads as an application assembled rather than
designed, however defensible each row is on its own.

**A row that seems to have three answers is a row asking two questions.** An unfinished recording
is answered by *do I keep this?* — keep or discard. Exporting it is a copy and not a decision: the
recording is still waiting afterwards, so it is not an answer, and it does not sit in an answer's
place.

**How many shapes a row of this list has is not settled**, and the number of them is the thing to
watch: whether *Ver la reunión* is always there, or the row is itself the thing you press, is open.
It changes no engine and costs nothing to change later.

### One verb per act

The same act is never said two ways. Pointing a channel at another source is *Cambiar* wherever it
happens, and the notice it sits in says which source — a verb does not carry the noun a screen has
already named.

| The act | The verb |
| --- | --- |
| Open a meeting | *Ver la reunión* |
| Start, pause, stop a recording | *Empezar a grabar* · *Pausar* · *Detener* |
| Try the same thing again | *Reintentar* |
| Point a channel somewhere else | *Cambiar* |
| Take the whole machine instead | *Grabar toda la máquina* |
| Buy a transcription or a summary | *Transcribir* · *Resumir* |
| Put names on the voices | *Decir quién es quién* |
| File it under what it was about | *Clasificar* |
| Keep or throw away an unfinished recording | *Conservar* · *Descartar* |
| Commit a form | *Guardar* |
| Walk away from one | *Cancelar* |
| File a meeting under nothing, on purpose | *Dejarla sin clasificar* |
| Add a person, anywhere | *Agregar a alguien* |

A screen needing a verb that is not here either found a new act — which is a decision — or is saying
one of these in its own words, which is the thing this table exists to stop.

**One status is olivo and the rest are secondary.** *transcribiendo* is a row that is alive and
running, which is what olivo means everywhere else in this application. Everything else a row can
say about itself — *necesita revisión*, *en cola* — is quiet.

A drop-down is a 34-high control on papel with a 1px `#E6E4DE` rule and an 11px chevron in
secondary. A two-way choice is two halves inside a `#E6E4DE` trough with 3px of padding, the trough
at radius 4 and each half at 3; the chosen one is papel with weight 500 and the other is secondary
with no fill. A set of more than two is a radio row: a 16px circle — genuinely round — olivo with a
4px papel inset when chosen and a 1.5px `#C3BFB6` ring when not, and the whole chosen row sits on
the decision tint.

An optional or empty control — *add somebody*, *+*, *none of these* — has no fill and a 1px
`#DEDBD4` inset ring.

## Notices

Two, and neither is ever a pop-up.

**Nothing stops the screen except these two, and this list is closed:**

1. **A charge**, at the moment somebody asks for it. Below.
2. **Adding a person**, from wherever a flow needs one.

Closed means closed. A screen that wants a third does not get to decide it has a good reason —
that is a decision somebody takes deliberately and writes on this list, and until they have, the
answer is no. A rule with an exception and a criterion is a rule that grows a fourth dialogue in
six months because each one, on its own, looked justified.

Everything else is a line or a row where the thing itself is: a source that died, a program that
brought nothing back, a recording waiting to be decided about, a render that failed.

**Something is waiting on a decision** sits on the decision tint `#EEF2EF`, in the list, the height
of a row. Title 14/20 600, second line 14/20 in secondary, and its answers sit in the two places the
grammar fixes: the act on the right, the neutral one on the left.

**Something was lost or is about to be** sits on the attention tint `#F8EDE6` with an 18px warning
triangle in pico. It says **what was observed before what it means** — "The Yeti Nano stopped
responding at 08:12", and then what that costs, never the other way round — and its answers sit
where every other answer on every other screen sits.

### What a charge costs, asked once

The one dialogue in the application. It exists because a charge is the one thing that cannot be
undone by pressing again, and because putting the price in the row instead — *puede que ya se haya
cobrado*, *reintentar · se cobra* — made every screen carry a sentence about money that ninety-nine
readings out of a hundred did not need.

It opens on the press, not before it. Radius 12, on the elevated surface, over a `rgba(28,27,25,.32)`
scrim. It says what is about to happen, what it costs, and offers two answers: the act, and leaving
it as it was. Nothing else — no explanation of how the price was reached, no note about the
provider.

The two are not asked the same way, because only one of them can be known in advance:

- **Transcribing** carries a figure worked out from the meeting's own length. It reads as an
  estimate and says so in the number's own words, not in a sentence beside it.
- **Summarising** carries the model's published price and nothing worked out at all. There is no
  honest estimate for it, and inventing one would be the worst thing on this page.

Both go as `[costo]` until a real run produces a number.

The amount takes the stopwatch rank dropped to 40/48 — the ramp already lets it drop for a screen
an alert owns, and this is that. The dialogue's title takes the sub-screen rank, 20/26: the panel is
not a screen, and the screen title inside it reads as shouting.

### Adding a person, from wherever

The second and last dialogue. It exists because naming somebody is needed from more than one place —
classifying a meeting, saying who a voice is — and because a flow that sends you to another screen
to type three fields is a flow that loses what you were doing.

Same panel as the charge: radius 12, papel, the same scrim. What it asks is the name, and optionally
the organization and since when — a person carries as many affiliations as they have, each with its
own period, so this dialogue adds one and never replaces what is there. Two answers, in the two
places the grammar fixes: *Guardar* on the right, *Dejarlo como está* on the left.

## Movement

**Every screen ships moving.** Motion is not a polish pass that arrives once the screens work — a
screen built still and animated afterwards animates whatever it happens to have, which is how an
application ends up with the same fade on everything. It is decided with the screen, like colour.

And like colour, **it is information and never decoration.** Movement answers exactly two questions,
and a screen that moves for any other reason is a screen that has not decided what it means:

- **Where did this come from, and where did it go?** The drawer rising is what says it is the same
  screen and not another one. A row leaving the list is what says it is the row you just decided
  about.
- **Did something just change that I was not looking at?** A notice arriving beside a meter, a
  dialogue taking the screen, a status line turning over.

### What moves

| What | How long | How |
| --- | --- | --- |
| A control answering the press — fill, ring, tick | **150 ms** | straight in, no easing worth naming |
| Something entering or leaving — a row, a notice, a clip | **250 ms** | decelerating in, accelerating out |
| The meetings drawer, and a dialogue arriving | **300 ms** | the same pair, over a distance you can follow |

Entering decelerates and leaving accelerates, which is the platform's own grammar and reads as
weight rather than as an effect. Nothing eases both ways; nothing bounces; nothing overshoots.

### What never moves

- **The meter.** It is instantaneous by definition, and a level that eases toward its value is a
  level that is wrong for a quarter of a second, every quarter of a second. Interpolating it does
  not smooth it — it lies about it.
- **The stopwatch.** It counts. A number that animates between two values is unreadable at exactly
  the moment somebody is reading it.
- **Anything that happens on every element.** A list whose rows arrive one after another says the
  list is important; a list of thirty says it twelve seconds late. Rows arrive together or not at
  all.
- **Anything on hover.** A screen that reacts to a passing cursor is a screen with a hundred small
  events in it, none of which somebody asked for.

### When Windows says no

Windows carries a setting for people who need the screen to stay still, and **it is obeyed**. With
animations off, every duration above is zero: the drawer is up, the notice is there, the dialogue
has the screen. Nothing is lost by turning them off — which is the test of whether a piece of
movement was carrying information or making up for a layout that did not explain itself.

## The meter

The one component that is this application's own, and the one nothing else can be copied from.

### What it is for

**Instantaneous, never historical.** Its job before a recording starts is to answer *which of the
three processes called Teams is the one making the sound* while the person clicks from one to the
next. A strip of the last few seconds answers for the process before this one, which is the wrong
answer at the moment it is read. **Never a strip of the last few seconds.**

Its job during a recording is smaller and the same: this source is still arriving.

### The scale

Linear in dBFS from **−60 at the left to 0 at the right**. Everything else follows:

    x = (dB + 60) / 60      clamped to 0…1

The scale sits under the bar in the mono data rank — 11/18 — reading `−60 −40 −20 −12 0` at 0%,
33.3%, 66.7%, 80% and 100%. The numbers under the bar are data like any other, and there is no rank
below the one data has.

The **−12 is in pico** and the **0 is in ink**; the rest is tertiary.

**The zero is always there.** Without the scale under it, the bars are decoration: nothing says
whether −16 is close to clipping or nowhere near it.

**Anything that shows a level has square ends.** The meter and a progress bar are the same kind of
thing — a quantity drawn as a length — so the four-value radius scale does not reach them: they get
zero. A rounded cap on a bar that is six pixels tall is a cap that lies about where the level is.

### The four layers, bottom to top

All four use the same 3px-on, 3px-off segment pattern, so the segments of every layer line up:

    repeating-linear-gradient(90deg, <colour> 0 3px, transparent 3px 6px)

1. **Track** — `#E1DED7`, the full width.
2. **Hot zone** — `#EDD5C7`, from 80% (−12 dB) to the right edge. **Visible even when nothing is
   arriving**, so the colour is not something that appears out of nowhere on the day it clips.
3. **Level** — `#4F7561`, from the left to the level. Not a solid fill: the same pattern clipped,
   so the segments keep their phase instead of shifting under the level.
4. **Above −12** — `#C2683C`, from 80% to the level, painted over the olive. It exists only when
   the level is past −12.

Clip each layer rather than sizing it. A sized layer re-tiles its own pattern and the segments walk
as the level moves.

### The retained peak

A **2px vertical bar in pico**, standing 4px proud of the track top and bottom. It sits at the
loudest the source has reached. **It is the meter's only memory** — nothing else on the component
remembers anything. The number beside it — `pico −9.4`, 11px mono — carries the same colour, so the
two read as one thing.

### The three states

| State | The bar | What it says |
| --- | --- | --- |
| **Being heard** | track, hot zone, olive level, peak | *se escucha* in olivo, or the level in mono |
| **Clipping** | the above, plus pico from 80% to the level | *saturando* in pico |
| **No signal** | track and hot zone only — no level, no peak | *sin señal* in pico |

There is a fourth condition that is not a meter state: a source that **died**. Its whole card drops
to 62% opacity, the bar keeps the bare track with **no hot zone**, the scale loses its two coloured
numbers, and the level is replaced by *se cortó a las 08:12* in pico. That difference is the point:
no signal is a source that is still there and hearing nothing; a dead source is not there.

### Where it goes

The meter is **pinned to the control that chooses its source** — the program picker for channel 0,
the microphone picker for channel 1. Pick, look, pick the next one. Separating them turns the
answer into a memory test.

Two meters, always: **channel 0 is the others and channel 1 is you.** They are fixed and there are
two. This is never a list.

## The rules the design imposes

These do not show in the markup and are as load-bearing as any colour.

- **No vocabulary from the domain reaches the screen.** Nothing named `work_of`, `counterpart`,
  `meeting_people`, `ch0:speaker_1`. The stored speaker label is not what a person reads: the voices
  are called *Tu micrófono*, *Voz 1*, *Voz 2*. Nothing about the three-level tree is named as a
  tree; it is *Es trabajo de* and a chevron between two pills.
- **Nothing explanatory about how the application works inside.** If a line exists to explain the
  mechanism, it goes. The order of a list can *be* the rule without stating it: the meeting still
  running is at the top of the recovery screen and offers none of the three choices, and that is the
  whole of "there is nothing to decide yet".
- **Neutral Spanish, no voseo**, in everything a person reads. *Escuchá*, *cambialo*, *mirá*,
  *apretés*, *decís* are wrong; *escuche*, *cámbielo*, *mire*, *lo pulse*, *dice* are right.
- **Amounts of money are `[costo]` until a run produces one.** The real price comes off the person's
  own account and is never invented on a mockup or in a string.
- **A screen gets one sentence, and only where something failed.** Everything else on it is a
  label. A second explanatory line under every option and every notice is the voice of something
  being helpful at somebody rather than an application saying what it is. **If an option needs a
  line explaining it, its name is wrong**, and the name gets fixed instead.

### And five things this application is not

Taking the platform's geometry is right and taking its skin is not. What makes Windows' own settings
ugly is none of the radii — every well-built application on the system uses the same four and eight —
so each of these is named so that a later pass cannot arrive at it by drifting:

- **Never an icon inside a rounded square.** It is the single most recognisable gesture of a
  settings list and it turns forty different things into forty of the same thing.
- **Never mica or acrylic.** Papel is opaque. A translucent window is one where nothing weighs
  anything.
- **Never the system accent colour.** This application has two colours and they are on this page.
- **Never a list whose rows all weigh the same.** The stopwatch is 62 and a datum is 11 because one
  of them matters more. A screen where everything is the same size is a screen that decided nothing.
- **Never hierarchy made only of bold.** Size, colour and space carry it; weight is the last
  resort, not the first.

And what the code already required and still does:

- **No literal in the XAML.** Every text a person reads names an entry of `UiTexts`, and
  `ScreenTextsTests` fails the build if a literal comes back — as an attribute, as an element's own
  text, or as an assignment in code-behind.
- **A notice that arrives on its own is announced from the code-behind, never bound.** A source that
  died, nothing arriving: a bound live region is not announced by a screen reader.
- **Which control is alive is decided by `RecorderScreen` and `OwedWork`, not by the window.** The
  window sets every control from one of those and asks again inside each handler, so a click already
  in flight when a control was disabled meets the same refusal. A new screen asks; it does not
  decide.
- **The application has no spare screens.** Recording and the meetings are one screen. The only
  thing that lives apart is the settings.

## The seventeen screens

`docs/design/` holds one file per screen. Sixteen are the flow and the seventeenth is the system
sheet. `canvas.json` carries their layout and the note that was written against each.

### The flow — recording

**`Main`** · *Grabar una reunión*. The top-level screen: the recording card above, the meetings
below. The program picker and the microphone picker are each pinned to their meter — this is the
three-Teams case. Language and transcription engine are pills at the top right of the card; live or
at-the-end is the two-way pill at the bottom left; *Empezar a grabar* is the principal act at the
bottom right. Under it, the meetings list, and above the list anything waiting on a decision.

**`GrabandoVivo`** · Recording, transcribing live. Stopwatch at 40/48, *Pausar* and *Detener*, both
meters compressed to one row each, and the live transcript filling the rest. Text arrives word by
word and the tail is grey: **the grey is the provisional part the provider is still correcting, and
grey is not what gets stored.** A 2px olive caret follows it. Speakers are a name, a coloured dot
and the time, right-aligned in a 96px gutter; the line itself is capped at 62ch.

**No recording screen names the meeting.** A meeting is named by its summary, or by hand from its
own screen afterwards, so while one is running there is nothing to put there and nothing is
invented. The header carries what is true at that moment: a recording is running, and it is
capturing two channels. That stays true on the two screens where a source has failed — the
recording is still a two-channel recording, and the meter below is what says which one broke.
`AlParar` is the same moment later and carries the duration alone.

**`GrabandoDiferido`** · Recording, transcribing at the end. **The screen is quieter on purpose.**
With no transcript, the stopwatch takes the full 62/72 and the meters take the space rather than
being padded out with invented filler. The foot of the card says what will happen and how much has
been written.

**`NadaLlego`** · Nothing arrived from the program. Channel 0 reads *sin señal*, and the notice sits
directly under it because that meter is the evidence. The act on the right is *Grabar toda la
máquina*, because it is the one press that makes audio arrive; *Cambiar* is the neutral one on the
left, since it opens a picker rather than answering the notice — a button that opens the question is
not the button that answers it. Neither is pico: taking the whole machine costs nothing and loses
nothing. Channel 1 goes on reading normally underneath, which is what says the recording is fine.

**`Fallo`** · A source died. Its act is *Reintentar*, not *Cambiar*, and the difference from
`NadaLlego` is the whole reason both exist: a source that is alive and silent is answered by pointing
somewhere else, and a device that stopped responding is answered by trying that same device again.
**Losing the microphone does not kill the meeting**: channel 0 goes on
and is visibly going on. Channel 1 dims to the dead state described above, and the notice says what
was observed, then that what was said into that microphone from that moment is gone and does not
come back.

**`AlParar`** · Stopping. *Guardando la reunión*, a progress bar in olivo, and three steps —
finished, running, not started — as a tick, a spinner and an empty circle at 45%. **There is no
"do not close the application" sign**: if it is closed it is recovered next time. The meeting is
already in the list below, saving.

**Unfinished recordings have no screen of their own.** A recording the application was killed in the
middle of has its row in the corpus before its first sample was captured, so it is a row in the
meetings list like any other: on the decision tint, at the top, *Conservar* on the right and
*Descartar* apart on the left. One that cannot be read sits on the attention tint and offers only
*Descartar*. The one still running sits above them and offers nothing at all, because there is
nothing to decide about it yet — the order is the statement and no line says it.

The list carries no sentence. Nothing is deleted until somebody chooses it, and saying so is the
application reassuring the reader about a thing it was never going to do.

### The flow — afterwards

**`Reunion`** · The meeting. **The read-the-transcript screen does not exist** — nobody opens an
application to read 148 turns. What is there instead is what the AI left: the abstract on the
decision tint, then *Qué se decidió*, *Qué queda por hacer*, *Qué quedó sin resolver*, each item
carrying a timestamp pill that opens the transcript **in place**, not on another screen. Decisions
take an olive bullet and open questions a pico one. The right column is who spoke with their share,
what it was about, and who wrote this. The player runs along the bottom: **the coloured marks on the
track are the summary's citations**, so where each thing falls across the hour is visible.

**`Clasificar`** · What it was about. The templates are the thirteen meetings of `arquitectura.md`
§5.3 **by name only** — what each one fills in is not explained, it is seen on choosing.
*Es trabajo de* is pills with a chevron between them, no tree drawn anywhere. *Del otro lado* and
*Trata sobre* start empty and say so. *Quiénes* carries a person, optionally the badge saying the
meeting is about them, and their affiliation and since when. No role with a technical name, no help
panel.

**`QuienEsQuien`** · Who is who. **The voices are called *Tu micrófono*, *Voz 1*, *Voz 2*** and
never the label they are stored under. Each brings a quotation, a small waveform and a clip to
listen to before deciding — *two people look more alike in writing than they sound*. The microphone
that caught exactly one voice is settled already and says so. **One that spoke little brings three
clips instead of one**, because one is not enough to recognise somebody by. Nothing on it failed,
so the screen carries no sentence at all: the clips being there are the instruction.

**`Correcciones`** · Words that come out wrong. **The problem was never applying a correction, it
was finding one** — nobody reads a corpus looking for what went wrong. So: the person types the word
as it should be, and the corpus answers with how it actually got written, ranked, with the close
ones pre-ticked. Below, on the attention tint, the ones that **turned up by themselves**: they
sounded uncertain and they resemble something said often. The right column shows one applied in
context and what has already been fixed. **Neither list needs a model to have read the meetings.**

**`Configuracion`** · Settings, the one screen that lives apart. **Choosing between transcribing and
summarising stopped being a screen per meeting and became a preference set once.** The two engines
are separate choices with a separate cost each, and *a model on this machine* is one option among
them rather than a special case. Amounts go as `[costo]`.

### Across the flow

**`Costo`** · The dialogue. Two of them on one artboard, side by side, because the difference
between them is the whole point: transcribing carries a figure worked out from the meeting's length
and says it is an estimate; summarising carries the model's published price and estimates nothing.
The only thing in this application that stops the screen.

**`ReunionCruda`** · The meeting, recorded and nothing else. **It is `Reunion` with the middle
missing** — the same header, the same columns, the same player along the bottom. What the AI has not
left yet is simply not there, and *Transcribir* is the act on the right. The screen looks sparse,
and that is honest: a meeting nobody has bought anything for has little in it. A screen drawn for
the empty case would be a second blueprint for one screen, which is where a design starts
disagreeing with itself.

**`Primera`** · The first time the application opens. **Three questions, and there are only three
because everything else has a right answer already**: the language comes from Windows, the corpus
has a folder in the user's own profile, and there is one transcription engine and one summariser, so
neither is a choice. What is left is the name of the person using it — nobody can work that out, and
every citation of every meeting rests on it — what should happen when a recording ends, which is the
only one of the four that spends money, and where the corpus goes, which is asked here because the
corpus holds artifacts that cannot be obtained again and moving it later means moving all of them.

No affiliation is asked for. A person has as many as they have, each with its own period, and the
first screen of an application is not where somebody enumerates their jobs.

**`MainAbierto`** · The main screen with the meetings drawer raised to the full height of the
window. The same screen, not another one: the recording card is still above and slides out of the
way. The list scrolls whole — no paging, and no search field until the index behind it exists.

**`Persona`** · Adding somebody, over whatever screen asked. The second of the two dialogues, and
the last: name, and optionally an organization and since when. It adds an affiliation and never
replaces one.

### The system sheet

**`Sistema`** · Olivo laid out visually — the type ramp, the swatches, the meter with its anatomy
written beside it, the control ranks, the two notices and the rules. It is this document as a
picture. If the two ever disagree, this document is what a screen is built from.

## Every screen is built out of components

**Nothing on a screen is drawn twice.** A button, the meter, a meeting's row, a notice, a picker
pinned to its meter: each is one control with its own file, used from wherever it is needed. A
screen is a composition of them and holds no drawing of its own.

The reason is not tidiness. This design will be iterated on — the person who owns it says so — and
an iteration that has to be applied twelve times is an iteration that gets applied nine times and
forgotten in three. One place per thing is what makes a redesign a diff instead of a sweep.

Which pieces earn a component is the builder's judgement, and the floor is: **anything the same on
two screens is one component before it is on the second one.**

## The artboards and this page

The artboards are this page drawn. Where one of them and a sentence here ever disagree, **this page
is what a screen is built from** and the artboard is what gets corrected — not the other way, and
not both kept.

Two things on them are deliberately unfinished: the product's name and mark are a placeholder, and
`Sistema` is this page as a picture, so it is the one artboard that has to be re-read whenever this
page changes.

## Opening the artboards

`docs/design/README.md` says what the files are and what a browser does and does not render.
