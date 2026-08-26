# Olivo — the visual system

Every screen of this application is drawn from what is on this page. Open it before building a
screen, and again before adding a control to one that exists.

`docs/design/` holds the thirteen artboards this was written from — the screens as pictures,
openable in a browser. **They are reference and this document is the authority.** Where the two
disagree, the disagreement is listed at the bottom and this page wins; the artboards were drawn
over several passes and some carry a value a later pass replaced.

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

## Colour

The value and the role are fixed here. The resource key is this document's suggestion and the
first screen to need one settles it — but every screen after that uses the same key.

| Role | Value | Where | Key |
| --- | --- | --- | --- |
| Papel — paper | `#FCFCFB` | the window's background | `PaperBrush` |
| Tarjeta — card | `#F4F3EF` | a block laid on the paper | `CardBrush` |
| Tarjeta interior — inner card | `#FCFCFB` | a block inside a card, and every control sitting on one | `CardInnerBrush` |
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
| Secondary | Space Grotesk | 13/19 | 400 | — |
| Data | mono | 11/18, small caps | 400 | — |
| Micro | mono | 9–10 | 400 | `.08–.09em` when it is a label |

The stopwatch is 62/72 on a screen it owns. Where a live transcript or an alert owns the screen
instead, it drops to 40/48 and keeps everything else.

A **sub-screen** is one reached from another and returning to it — the meeting, classifying it,
who is who, the corrections, the settings. It carries the 20/26 title and a 34px round back
button; the screens at the top level carry the 26/32 title and no back button.

The fallbacks are `'Segoe UI', system-ui, sans-serif` for text and `'Cascadia Mono', Consolas,
monospace` for numbers, so a machine without the two fonts still reads.

## Radii

- Large card — **18**
- Small card, or a row in a list — **13–14**
- Inner card — **11–12**
- **Everything that gets pressed — 999**

The rule underneath: **pills for what gets pressed, rounded rectangles only for containing.** A
rectangle is never a control and a pill is never a container. Somebody scanning a screen should be
able to tell what is pressable without reading a word of it.

## Spacing

- Screen — **30** vertical, **40** horizontal
- Large card — **20–24**
- Small card — **13–16**
- Between blocks — **18–20**
- Inside a card — **10–14**

## Heights

- The principal act — **46**
- A normal button — **42**
- A control pill — **33–36**
- A small pill — **30–32**
- The round back button — **34**

## Controls

Five ranks, and a screen has at most one of the first.

| Rank | Fill | Text | For |
| --- | --- | --- | --- |
| The principal act | tinta `#1C1B19` | papel | the one thing this screen is for: start recording, stop, save |
| Recommended | olivo | papel | the cheap or safe way out of a decision |
| Has a consequence | pico | papel | it costs money, or something is lost |
| Normal | tarjeta `#F4F3EF` | tinta | everything else |
| At the margin | none | secundario | dismiss, cancel, leave it as it was |

A drop-down is a pill on the card's inner colour with a 11px chevron in secondary. A two-way choice
is two pills inside a `#E6E4DE` trough with 3px of padding; the chosen one is papel with weight 500
and the other is secondary with no fill. A set of more than two is a radio row: a 16px circle,
olivo with a 4px papel inset when chosen and a 1.5px `#C3BFB6` ring when not, and the whole chosen
row sits on the decision tint.

An optional or empty control — *add somebody*, *+*, *none of these* — is a pill with no fill and a
1px `#DEDBD4` inset ring.

## Notices

Two, and neither is ever a pop-up.

**Something is waiting on a decision** sits on the decision tint `#EEF2EF`, in the list, the height
of a row. Title 14/20 600, second line 13/19 secondary, and its answers are small pills on the
right — the recommended one first, the one that costs money beside it saying so.

**Something was lost or is about to be** sits on the attention tint `#F8EDE6` with an 18px warning
triangle in pico. It says **what was observed before what it means**, and it carries the way out
next to it. "The Yeti Nano stopped responding at 08:12" and then what that costs, not the other way
round.

## The meter

The one component that is this application's own, and the one nothing else can be copied from.

### What it is for

**Instantaneous, never historical.** Its job before a recording starts is to answer *which of the
three processes called Teams is the one making the sound* while the person clicks from one to the
next. A strip of the last few seconds answers for the process before this one, which is the wrong
answer at the moment it is read. **This was tried and dropped — do not reinvent it.**

Its job during a recording is smaller and the same: this source is still arriving.

### The scale

Linear in dBFS from **−60 at the left to 0 at the right**. Everything else follows:

    x = (dB + 60) / 60      clamped to 0…1

The scale sits under the bar in 9px mono: `−60 −40 −20 −12 0`, at 0%, 33.3%, 66.7%, 80% and 100%.
The **−12 is in pico** and the **0 is in ink**; the rest is tertiary.

**The zero is always there.** Without the scale under it, the bars are decoration: nothing says
whether −16 is close to clipping or nowhere near it.

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
  *apretés*, *decís* are wrong; *escuche*, *cámbielo*, *mire*, *lo pulse*, *dice* are right. Several
  artboards still carry the voseo the rule replaced — see the bottom of this page.
- **Amounts of money are `[costo]` until a run produces one.** The real price comes off the person's
  own account and is never invented on a mockup or in a string.

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

## The thirteen screens

`docs/design/` holds one file per screen. Twelve are the flow and the thirteenth is the system
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

**`GrabandoDiferido`** · Recording, transcribing at the end. **The screen is quieter on purpose.**
With no transcript, the stopwatch takes the full 62/72 and the meters take the space rather than
being padded out with invented filler. The foot of the card says what will happen and how much has
been written.

**`NadaLlego`** · Nothing arrived from the program. Channel 0 reads *sin señal*, and the notice sits
directly under it because that meter is the evidence. *Cambiar de programa* is beside it; *Grabar
toda la máquina* is the consequential answer. Channel 1 goes on reading normally underneath, which
is what says the recording is fine. Note carried over from the design: changing source without
cutting the recording is engine work the recorder cannot do yet, tracked on its own card.

**`Fallo`** · A source died. **Losing the microphone does not kill the meeting**: channel 0 goes on
and is visibly going on. Channel 1 dims to the dead state described above, and the notice says what
was observed, then that what was said into that microphone from that moment is gone and does not
come back.

**`AlParar`** · Stopping. *Guardando la reunión*, a progress bar in olivo, and three steps —
finished, running, not started — as a tick, a spinner and an empty circle at 45%. **There is no
"do not close the application" sign**: if it is closed it is recovered next time. The meeting is
already in the list below, saving.

**`Recuperacion`** · Unfinished recordings. *Nada se borra hasta que decidas.* The one still running
is always at the top on the decision tint and offers none of the three, because there is nothing to
decide about it yet. Each of the others offers *Conservar* (principal), *Exportar* (normal),
*Descartar* (at the margin). One that cannot be read sits on the attention tint, says *Dañada* and
offers only *Descartar*.

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
clips instead of one**, because one is not enough to recognise somebody by. The foot says that a
name here changes every citation in the meeting.

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

## Where the artboards and this document disagree

Every line here is a place the markup carries something a later decision replaced. **The artboards
have not been redrawn against them** — that is a design pass of its own, and this list is what it
works from. Until then this document wins and nobody copies the stale value out of the markup.

### Values and copy an earlier pass left behind

1. **`GrabandoVivo` paints the second speaker `#8E7CC3`.** The speaker palette is `#4F7561`,
   `#A0567A` and `#C3BFB6`; `Reunion`, `Clasificar` and `QuienEsQuien` all use `#A0567A`. Use
   `#A0567A`.
2. **Several artboards write their copy in voseo** — *Escuchá el fragmento*, *cambialo acá abajo y
   mirá el medidor*, *sin que lo apretés vos*, *algo que decís seguido*. The neutral-Spanish rule is
   newer than that copy. **Do not lift a string out of an artboard into `UiTexts` without reading it
   against that rule first.**
3. **`Fallo` runs the meter's above-−12 layer out to the retained peak rather than to the level**,
   which puts orange 9% past where the level actually is. `Sistema` is the component's own sheet and
   runs it from 80% to the level; that is the rule.
4. **`Recuperacion` explains the mechanism** — *«Todavía no hay nada que decidir: algo sigue
   escribiendo esta grabación»*. The rule above is that the order **is** the statement: the one
   still running sits at the top and offers none of the three, and that is the whole of it. The
   line goes.
5. **Two notes in `canvas.json` are stale.** `Fallo`'s says changing microphone mid-recording is
   engine debt — it is merged (ISC-78). `NadaLlego`'s says the same of changing program — that was
   decided on 2026-08-25 and is now work with a card, not a refusal.

### What the grill of 2026-08-25 changed

The sitting that defined the twelve screens settled these against the markup. Each names the card
that carries it.

1. **`Main` · the two-way *En vivo / Al terminar* pill stays and is chosen per meeting** (#97).
   `Configuracion` never mentions live or deferred; it owns which engine and what happens at the
   end.
2. **`Main` · a meeting with no name reads *Sin nombre*** (#97). The summary gives it one when it
   arrives, and a pencil beside the name edits it by hand at any time.
3. **`Main` · a row's small status line is one text, not four** (#97). *transcribiendo*, *148
   turnos*, *3 voces sin nombre* and *en cola* collapse into a single discreet line — *necesita
   revisión* and the like — and a meeting with nothing pending shows none.
4. **`Main` · a row carries *Ver la reunión* and the next step's button** (#97) — transcribe,
   summarise, name the voices. A meeting already transcribing is not offered transcribing again.
5. **`Main` · the meetings list is a drawer** (#97). Small it scrolls; a press raises it to the
   full height of the window. No search field yet — that is #127, with the index behind it — and
   no paging: the whole list loads.
6. **`Configuracion` · *Preguntarme cada vez* becomes *No hacer nada*** (#108). Nothing runs on its
   own; transcribing and summarising are asked for from the meetings list. Nothing asks anything.
7. **`Configuracion` · a row saying who is using the application** (new card), with the name and
   optionally the company and since when.
8. **A first-run moment that does not exist yet** (new card): the first time the application opens,
   it asks who is using it. That is where the microphone's resolved voice gets a person's name.
9. **`Reunion` needs its small version** (#104): recorded and not transcribed is the player, the
   date, the duration and *Transcribirla*, and nothing else. With *No hacer nada* as an option that
   is the ordinary case, not an edge.
10. **`Clasificar` · *Ninguna* and *Dejarla sin clasificar* are two different things** (#105).
    *Ninguna* is «none of the thirteen fits, I fill the fields myself» and is the shell a later card
    grows into templates of one's own; *Dejarla sin clasificar* is the meeting stored with no links
    at all, found by text, which is what §5.3 says of a casual chat.
11. **The product's name and mark stay placeholder** (#96). *Meeting Transcriber* and any SVG until
    somebody decides otherwise; it is a component like everything else and changing it is one file.

## Opening the artboards

`docs/design/README.md` says what the files are and what a browser does and does not render.
