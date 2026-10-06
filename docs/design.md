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

`docs/design/` holds the nineteen artboards — the screens as pictures, openable in a browser.
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

| Role | Value | Dark | Where | Key |
| --- | --- | --- | --- | --- |
| Papel — paper | `#FCFCFB` | `#1B1A18` | the window's background | `PaperBrush` |
| Tarjeta — card | `#F4F3EF` | `#22211F` | a block laid on the paper | `CardBrush` |
| Tinte de decisión — decision tint | `#EEF2EF` | `#1E2521` | something waiting on the person; the selected option of a set | `DecisionTintBrush` |
| Tinte de atención — attention tint | `#F8EDE6` | `#2A211C` | something lost or about to be | `AttentionTintBrush` |
| Tinta — ink | `#1C1B19` | `#ECEAE5` | text, and the fill of the principal act | `InkBrush` |
| Secundario — secondary | `#6E6C66` | `#8E8B85` | the second line of a pair; a control at the margin | `SecondaryTextBrush` |
| Terciario — tertiary | `#9B9891` | `#64615C` | data, counts, units, labels | `TertiaryTextBrush` |
| Línea — rule | `#E6E4DE` | `#2D2C29` | a 1px divider; the trough of a two-way pill | `LineBrush` |
| Pista de medidor — meter track | `#E1DED7` | `#302F2C` | the meter's empty segments | `MeterTrackBrush` |
| Zona caliente — hot zone | `#EDD5C7` | `#402D24` | the meter's segments above −12 dB | `HotZoneBrush` |
| Olivo — olive | `#4F7561` | `#6A947F` | alive and well; see above | `OliveBrush` |
| Pico — peak | `#C2683C` | `#B86038` | wants attention; see above | `PeakBrush` |
| Sin responder — unanswered | `#C3BFB6` | `#45423E` | a speaker nobody has named; an unticked box; the ring of an unchosen radio | `UnansweredBrush` |

The application is drawn in the theme chosen in the settings — *Sistema*, *Claro* or *Oscuro* —
and *Sistema* is the theme Windows is set to, followed through a switch made while it is open. Every
window the application makes is drawn in it, and the dialogue that adds a person and the *Corregir*
pill take the theme of the window that opens them. **Every key has a dark value**, in the Dark column, and `Olivo.xaml` holds both under
`ThemeDictionaries`; a screen names a key and never a value, so no screen changes with the theme.
The dark values are re-tuned and not inverted: each text and accent value holds against dark papel
the contrast its light value holds against light papel, to within about a tenth — except tinta,
which is 14.5 to 1 in dark against 16.8 to 1 in light, because full-strength light type on a dark
page glares. In dark, tarjeta is a step lighter
than papel, because lighter is what lifts a card off a dark page. The text on an olivo, pico or
tinta fill is papel in both themes. The sheet that shows the fifteen dark swatches is
`Sistema.dc.html`; no other artboard has a dark twin, because the dark theme substitutes keyed
values and changes no layout.

Speakers get their own three, and only these three:

| Speaker | Value |
| --- | --- |
| First — the user's own microphone | `#4F7561` |
| Second | `#A0567A` |
| Anybody with no name yet | `#C3BFB6` |

The first and the unnamed rows are `OliveBrush` and `UnansweredBrush`, and take their dark values
with them. The second speaker has no key yet and so no dark value until the screen that keys it
settles one.

A fourth speaker has no colour yet and nobody has decided one. Until somebody does, a third named
speaker takes the no-name grey rather than a colour invented on the spot.

A press under the pointer is veiled in its own ink, at once and without a fade: tinta at 6 % under
the pointer and 12 % pressed, and the principal act — whose fill is ink — in papel at 14 % and 28 %.
The four strengths are `PointerVeilOpacity`, `PressedVeilOpacity`, `PrincipalPointerVeilOpacity` and
`PrincipalPressedVeilOpacity`, and neither the two button templates nor the drop-down's pill hold a
`Storyboard` or a `VisualTransition`. They are not rows of either table above, which pin colours and
speakers; they are strengths of a colour a row already is.

### Decided, and not yet a key

**No screen ever writes a colour.** Not in markup, not in code, and not inside a component: every
colour a screen draws is a key of `Olivo.xaml`, and `OlivoTests` fails the build over a screen that
writes one anyway. There is no such thing as a value that lives inside a component rather than as a
token — a component is drawn on a screen, and the rule reaches it there.

So a colour this page has decided and no screen has yet drawn is listed here:

| Value | What it is |
| --- | --- |
| `#A0567A` | the second speaker |
| `#B9B5AC` | the bars of an audio clip's waveform |

Every colour written anywhere on this page is either a key of `Olivo.xaml` or a row of that table,
and `OlivoTests` fails the build over a third case. The screen that first draws one settles it, in
the change that draws it: the value leaves the table, the brush goes into `Olivo.xaml`, and the row
joins the colour table above carrying the key it settled on — which takes that table one row
longer, and `OlivoTests` pins its length, so the pin moves in the same change. It is the same act as
the ones already there — the key is this page's suggestion and the first screen to need one settles
it — happening one colour later. `#C3BFB6` left this table that way: the settings screen was the
first to draw the ring of an unchosen radio, and it took the value whole under `UnansweredBrush`,
the other two roles included. A value leaves whole: the row above lists every role it has, so the
screen that names one of them names the value, and two of those roles get two keys only on the day
one of them has to diverge.

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
who is who, the corrections, a node's story, the settings. It carries the 20/26 title and no back
button of its own: the round back button, 34px, is the app bar's, one for every sub-screen, and
Alt+Left does the same. It is shown while a sub-screen has the room, never for the raised list, and
is drawn dead while a screen refuses to be left — the corrections screen, mid-save. The screens at
the top level carry the 26/32 title.

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

**A button that opens the question is not the button that answers it.** The next step on a
meeting's row — *Transcribir*, *Resumir*, and *Reintentar* on a transcription stopped over a
charge — opens the dialogue where the charge is actually agreed to, so it takes the **normal**
rank, and pico appears only on the act inside that dialogue. Until that dialogue exists
(`ISA.md` ISC-85), *Transcribir*, *Resumir* and *Reintentar* — and *Resumir de nuevo*, on the
meeting screen — spend at once, on this machine's Deepgram key or on the Claude account Claude Code
is signed in to, and keep the normal rank anyway: it is the rank the row goes back to the day the dialogue lands. A list of twelve meetings
with twelve orange buttons spends the colour that is supposed to mean *this one costs money*, and
once it is spent nothing on the screen can say it any more.

The normal rank has two fills for the same reason the surfaces alternate: a tarjeta button on a
tarjeta row is invisible. On papel it is tarjeta with no rule; on tarjeta it is papel with a 1px
`ControlRuleBrush` rule — **not `LineBrush`**, which is the dividing rule and against tarjeta is
four per cent of nothing, so the button reads as a stray outline rather than a thing to press.

Every button is drawn by one of two templates of `Olivo.xaml`: `PrincipalActTemplate` for the
principal act and `OlivoButtonTemplate` for every other rank. **Both answer a passing cursor and a
press at once**: a veil in the button's own ink over the whole press (§Colour has the strengths), shown
the moment the pointer is on it and gone the moment it leaves, with nothing fading either way. A dead
button differs only by ink: its words go tertiary, and the principal act's ink fill becomes the rule's
colour so that it stops being the heaviest thing on the screen. What a pointer does get is the hand:
every press and every pressable row shows it, set by the styles in `Olivo.xaml` and never by a
screen. A press that is a glyph and nothing else — the list's caret, the player's play and pause,
the export — is `IconButton`, or a style carrying its values: the margin rank at the control height
and as wide, with a 1px `EmptyControlRingBrush` rule, its name and its tooltip the words a text
press would carry. A tooltip is where a press's one short explanation lives; the press itself keeps
its one or two words, and a keyboard shortcut is never drawn beside it.

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
| Start, pause, carry on, stop a recording | *Empezar a grabar* · *Pausar* · *Seguir* · *Detener* |
| Try the same thing again | *Reintentar* |
| Point a channel somewhere else | *Cambiar* |
| Change where the meetings are kept | *Cambiar* |
| Say where Claude Code is | *Cambiar* |
| Take the meetings out of the application | *Exportar* |
| Stop a summary that is running, or a second one waiting to run | *Detener* |
| Take the whole machine instead | *Grabar toda la máquina* |
| Buy a transcription or a summary | *Transcribir* · *Resumir* |
| Ask a summarised meeting for another | *Resumir de nuevo* |
| Put names on the voices | *Decir quién es quién* |
| Correct the words that come out wrong | *Corregir palabras* |
| Correct a word, from where it is read | *Corregir* |
| Rename a name somebody typed wrong | *Renombrar* |
| Check that Claude Code answers | *Probar* |
| Leave the first step | *Empezar* |
| Answer a word that turned up by itself | *Sí, es esa* · *No* |
| File it under what it was about | *Clasificar* |
| Keep or throw away an unfinished recording | *Conservar* · *Descartar* |
| Commit a form | *Guardar* |
| Walk away from one | *Cancelar* |
| File a meeting under nothing, on purpose | *Dejarla sin clasificar* |
| Put a classification by to file with again | *Recordar* |
| Throw away a classification put by under a name | *Descartar* |
| Add a person, anywhere | *Agregar a alguien* |
| Add a path to a column | *Agregar* |
| Take the Deepgram key off this machine | *Quitar* |

A screen needing a verb that is not here either found a new act — which is a decision — or is saying
one of these in its own words, which is the thing this table exists to stop.

**A meeting has one status, and it is one word.** *Sin audio*, *Grabada*, *En cola…*,
*Transcribiendo…*, *Transcrita*, *Resumiendo…*, *Resumida*, *Ignorada* or *Detenida*, decided once
by `OwedWork.Status` and said the same on a meeting's row and on its own screen. A word that ends in
an ellipsis is work in progress. **The two that run are olivo and the rest are secondary**:
*Transcribiendo…* and *Resumiendo…* are a row that is alive and running, which is what olivo means
everywhere else in this application. *Detenida* is the one that waits on a person, and takes the
primary ink to be noticed; the others are quiet.

A drop-down is the application's own control (`DropDown`), a 34-high pill on papel with a 1px
`#E6E4DE` rule and an 11px chevron in secondary, veiled like every other press under the pointer. Its open
list opens under the pill, or over it when there is no room below, always inside the window and never
outside it: no taller than eight entries and as wide as its widest entry, and never narrower than the
pill. The pill keeps showing what is chosen while the list is open, and a press on the pill while the
list is open closes it. Where the list goes is `ListPlacement`'s, as plain arithmetic, and not the
platform's: a `ComboBox` opens a window of its own that three batches of patching never placed
reliably. No control offers a two-way choice now. When one does, it is two halves inside a `#E6E4DE`
trough with 3px of padding, the trough at radius 4 and each half at 3; the chosen one is papel with
weight 500 and the other is secondary with no fill. A set of more than two is a radio row: a 16px circle — genuinely round — olivo with a
4px papel inset when chosen and a 1.5px `#C3BFB6` ring when not, and the whole chosen row sits on
the decision tint.

A set somebody may choose several of is a tick, and it is not a radio row: an 18 by 18 box at radius 4
with the check inside it, as `Correcciones.dc.html` draws it. Unticked it is a 1.5px `#C3BFB6` ring;
ticked it is olivo with a papel check, the glyph 11px at stroke 3. The row is the radio's — 34 high,
the body size, ink — with no tint either way, because several rows are ticked at once and a tint on
each would say nothing. The platform's own check box fills from the system accent, which §And five
things this application is not forbids, so the control is `Tick` in `Olivo.xaml`.

A typed field, `TypedField` or `TypedSecret`, stands at the control height with its text centred,
and a press beside one takes the control height as well, so the two read as one row.

A field for a secret — the Deepgram key, the one there is — is the typed field showing dots, `TypedSecret` in
`Olivo.xaml`, with the platform's reveal turned off: a secret on screen is a secret in whatever recorded
the screen, and a meeting is when somebody is sharing theirs. It is emptied after every press, and
nothing beside it ever says anything about what is in it beyond whether one is kept.

An optional or empty control — *add somebody*, *+*, *none of these* — has no fill and a 1px
`EmptyControlRingBrush` inset ring.

| Role | Value | Dark | Where | Key |
| --- | --- | --- | --- | --- |
| The rule of a normal button on tarjeta | `#E1DED7` | `#302F2C` | the 1px rule of a normal button on tarjeta | `ControlRuleBrush` |
| The ring of an optional or empty control | `#DEDBD4` | `#32312E` | the inset ring of an optional or empty control | `EmptyControlRingBrush` |

## Notices

Two, and none is ever a pop-up.

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

Both are the `Notice` style of `Olivo.xaml`: papel with a 1px `LineBrush` border at
`InterruptionCorner`, 12. The act sits on the right as `NormalButtonOnPaper`, the way out on the left
as `ButtonAtTheMargin`, and **no button is the default** — the platform's default button takes the
system accent, which §And five things this application is not forbids, so the style names the
default's states and leaves them empty.

### What a charge costs, asked once

The first dialogue. It exists because a charge is the one thing that cannot be undone by pressing
again, and because putting a sentence about it in the row instead — *puede que ya se haya cobrado*,
*reintentar · se cobra* — made every screen carry a sentence about money that ninety-nine readings
out of a hundred did not need.

It opens on the press, not before it. Radius 12, on the elevated surface, over a `rgba(28,27,25,.32)`
scrim. It says what is about to happen, what is sent, and offers two answers: the act, and leaving
it as it was. Nothing else — no explanation of how the figure was reached, no note about the
provider. **It never shows an amount of money**: neither provider quotes a price before a call, so
any amount would be invented.

The two are not asked the same way, because only one of them has a size that can be known in
advance:

- **Transcribing** shows how many minutes will be sent, an estimate worked out from what is sent —
  the meeting's own audio. It reads as an estimate and says so in the number's own words, not in a
  sentence beside it.
- **Summarising** shows the model that will write it and no figure. There is nothing honest to
  count, and inventing a count would be the worst thing on this page.

The number of minutes takes the stopwatch rank dropped to 40/48 — the ramp already lets it drop for
a screen an alert owns, and this is that. The dialogue's title takes the sub-screen rank, 20/26: the
panel is not a screen, and the screen title inside it reads as shouting.

### Adding a person, from wherever

The second dialogue. It exists because naming somebody is needed from more than one place —
classifying a meeting, saying who a voice is — and because a flow that sends you to another screen
to type three fields is a flow that loses what you were doing.

Same panel as the charge: the `Notice` style, radius 12, papel, the same scrim. What it asks is the name, and optionally
the organization and since when — a person carries as many affiliations as they have, each with its
own period, so this dialogue adds one and never replaces what is there. Two answers, in the two
places the grammar fixes: *Guardar* on the right, *Cancelar* on the left — the verb the closed table
above gives for walking away from a form, and the one `Persona.dc.html` draws.

While a name is typed, up to three people the corpus already holds under a name spelled nearly the
same way, or under a name the typed one is the start of or a word of, are offered under the field, each a pill with the organization they belong to; pressing one
answers the dialogue with that person and adds nobody. *Guardar* still adds the name as typed. It is
offered only while adding, never while correcting a name.

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
| Something entering or leaving — a row, a notice, a clip, a sub-screen | **250 ms** | decelerating in, accelerating out |
| The meetings drawer, and a dialogue arriving | **300 ms** | the same pair, over a distance you can follow |
| A meter's level falling back | **20 dB in 1.5 s** | a rate, not a duration — and rising is immediate |

A sub-screen takes the room at once and nothing travels for it: the recording card and the report go,
the strip stays when a meeting is under way, and the sub-screen fills everything between the app bar
and the foot, arriving by a fade. Only the raised list travels.

Entering decelerates and leaving accelerates, which is the platform's own grammar and reads as
weight rather than as an effect. Nothing eases both ways; nothing bounces; nothing overshoots.

### What never moves

- **The stopwatch.** It counts. A number that animates between two values is unreadable at exactly
  the moment somebody is reading it.
- **Anything that happens on every element.** A list whose rows arrive one after another says the
  list is important; a list of thirty says it twelve seconds late. Rows arrive together or not at
  all.
- **A fade on hover.** A press answers a pointer at once, with a veil that is there or is not, and
  nothing fades in or out under it: a fade is what read as a flicker. A screen that reacts to a
  passing cursor with anything but a press's own veil is a screen with a hundred small events in it,
  none of which somebody asked for.

### When Windows says no

Windows carries a setting for people who need the screen to stay still, and **it is obeyed**. With
animations off, every duration above is zero: the drawer is up, the notice is there, the dialogue
has the screen. Nothing is lost by turning them off — which is the test of whether a piece of
movement was carrying information or making up for a layout that did not explain itself.

## The meter

The one component that is this application's own, and the one nothing else can be copied from.

### What it is for

**It shows now, never a history.** Its job before a recording starts is to answer *which of the three
processes called Teams is the one making the sound* while the person clicks from one to the next.
That is about what it draws — the level as it stands — and not about whether it moves; how it moves
is below. A strip of the last few seconds answers for the process before this one, which is the wrong
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

### Its ballistics

**The level rises at once and falls back slowly.** Both halves are the rule and they are not the
same rule.

Rising is immediate: the bar is at the new level on the next frame it is drawn. Easing the rise is
the one thing here that is actually wrong — a slow attack under-reports a transient, and this meter's
job includes saying *saturando*, so a peak it smoothed away is a clip nobody was told about.

Falling is smooth, and its rate is fixed rather than a duration: **20 dB in a second and a half**,
which on this scale is a third of the bar. A level that drops instantly flickers at every gap
between words and is unreadable; every meter ever built falls slowly for that reason, and the
professional ones fall slower still — a broadcast peak meter takes between 1.7 and 2.8 seconds to
drop 20 dB.

The retained peak is the other half of the same idea and needs no rate: it stands where the loudest
moment was and does not decay at all.

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

The meter is **pinned to the control that chooses its source** — the program picker for the others,
the microphone picker for you. Pick, look, pick the next one. Separating them turns the answer into
a memory test.

Two meters, always: **the others and you.** They are fixed and there are two, each named by what it
hears (*Los demás*, *Yo*) and never by a number. This is never a list.

## The rules the design imposes

These do not show in the markup and are as load-bearing as any colour.

- **Labels are one or two words.** A label or a press is imperative where it asks for an act. A
  status is a word with an ellipsis while something runs — *Transcribiendo…*, *En cola…*. A sentence
  appears only where something failed, and says it in one short sentence; the machine's own words
  stay after it.
- **No vocabulary from the domain reaches the screen.** Nothing named `work_of`, `counterpart`,
  `meeting_people`, `ch0:speaker_1`, and nothing that names the application's insides: not
  *corpus*, not a channel number (*canal 0*, `ch0`), not a process id, not a meeting id, not a folder
  of ids, not a migration. The folder the meetings live in is *la carpeta de reuniones*. The stored
  speaker label is not what a person reads: the voices are called *Tu micrófono*, *Voz 1*,
  *Voz 2*. Nothing about the three-level tree is named as a tree; it is *Es trabajo de* and a
  chevron between two pills.
- **Nothing explanatory about how the application works inside.** If a line exists to explain the
  mechanism, it goes. The order of a list can *be* the rule without stating it: the meeting still
  running is at the top of the recovery screen and offers none of the three choices, and that is the
  whole of "there is nothing to decide yet".
- **Neutral Spanish, no voseo**, in everything a person reads. *Escuchá*, *cambialo*, *mirá*,
  *apretés*, *decís* are wrong; *escuche*, *cámbielo*, *mire*, *lo pulse*, *dice* are right.
- **No screen shows an amount of money.** Neither provider quotes a price before a call, and the real
  one comes off the person's own account, so an amount written on a screen or in a string would be
  invented. What a charge shows is minutes, and the settings screen shows no figure at all.
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
  Nothing draws from the platform's accent family: `Olivo.xaml` sets the seven `SystemAccentColor`
  keys to olivo in both themes, so a slider's fill, a text selection and every control the platform
  draws itself take olivo and not the colour of the person's Windows.
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

## The nineteen screens

`docs/design/` holds one file per screen. Eighteen are the flow and the nineteenth is the system
sheet. `canvas.json` carries their layout and the notes, each naming the artboard it was written
against. Two artboards have none, and a note nobody has written is not one this index invents.

### The flow — recording

**`Main`** · *Grabar una reunión*. The top-level screen: the recording card above, the meetings
below. The app bar is the window's title bar: it carries the mark and the name, and the round back button
while a sub-screen has the room, and the window is dragged by the mark, the name and the empty width
of the row — never by the back button. Windows' caption buttons stand at its right in the chosen
theme, and the bar keeps clear of them. The foot carries *Configuración* at the left, as a press at the
margin and a word, never an icon, and the packaging press at the right; it is not on screen while a
sub-screen has the room. The program picker and the microphone picker are each pinned to their meter
— this is the three-Teams case — and the strips are named by what they hear, *Los demás* and *Yo*,
never by a channel number. The program picker offers *Todo el audio* first and then one entry per
application, the way a recorder lists windows: twenty processes of one browser are one entry, and a
program that only plays is offered under its own name. Both pickers read the machine again when they
are opened, so nothing on the screen says *refrescar*.

**The meters listen before a meeting.** While the recording card is on screen and the window is the
one in front, the chosen microphone and source are opened for listening only — nothing is written —
and drawn on the same two meters, so a source is chosen against a level that moves. They are let go
of when *Empezar a grabar* opens the devices, when a sub-screen or the raised list takes the room,
and when the window is not in front, and a source that ends by itself is not reported as one that
stopped. **During a meeting both pickers stay live**, recording or paused: a pick moves that
channel to it without stopping the meeting, and *Cambiar* on the silent-program notice opens the
same program picker. **The last choices are offered again.** After a stop the window keeps what will
be spoken and what channel 0 follows, checked against the sources as they are now; at launch what
will be spoken is the language of the most recent meeting that has a recording, when the picker
offers it, and nothing is taken from the language the application is read in. Recording still waits
on all three answers, and each picker still waiting draws its rule in pico. Language and transcription engine are pills at
the top right of the card; there is no *al terminar* pill, because nothing transcribes live and a
two-way that offers one answer reads as broken. *Empezar a grabar* is the principal act at the bottom
right. Under it, the meetings list, and above the list anything waiting on a decision.

**A meeting's row is two lines**: the name, which is the press that opens it, and under it when and
how long in the data rank and its status. Its presses stand at the right and span both lines; a
failure stands under the data line. Rows stand 6 apart. A list with no meetings shows one quiet line
where the rows would be. The list's header raises it to the window and lowers it again with a
caret, named and given its tooltip by *Ver todas* and *Ver menos*.

**The report under the card says only what failed.** A stop that worked says nothing: the meeting is
in the list and that is the answer. A failure is one sentence with the machine's own words under it.
The report is not selectable, so the cursor never turns into a text cursor over it, and it takes no
room while it holds no line. The status line at the foot keeps one sentence, the refusal of a
recording with nowhere to put it; every other state is already said by the stopwatch, the strip, the
saving card and the presses.

**`GrabandoVivo`** · Recording, transcribing live. Stopwatch at 40/48, the pause glyph and the stop glyph
(named and tooltipped *Pausar* and *Detener*), both
meters compressed to one row each, and the live transcript filling the rest. Text arrives word by
word and the tail is grey: **the grey is the provisional part the provider is still correcting, and
grey is not what gets stored.** A 2px olive caret follows it. Speakers are a name, a coloured dot
and the time, right-aligned in a 96px gutter; the line itself is capped at 62ch.

**A pause is cut out of the meeting.** *Pausar* and carrying on are one press, drawn as the pause
glyph while recording and as the play glyph while paused, its name and tooltip *Pausar* and *Seguir*;
a press that is refused says so in a sentence and changes nothing. The clock on screen stands still
while paused, and the stretch is left out of the audio at the end, so the meeting's length and every
time in it are the time actually recorded. *Detener* is a glyph too — the square, beside the pause
glyph and the same size, named and tooltipped *Detener* — because both steer a recording that is already
running; the principal act, *Empezar a grabar*, stays words.

**No recording screen names the meeting.** A meeting is named by its summary, or by hand from its
own screen afterwards, so while one is running there is nothing to put there and nothing is
invented. The header carries what is true at that moment: a recording is running, and it is
capturing two channels. That stays true on the two screens where a source has failed — the
recording is still a two-channel recording, and the meter below is what says which one broke.
`AlParar` is the same moment later and puts nothing in that place either: *Guardando la reunión*
stands where a name would, and nothing stands beside it.

**`GrabandoDiferido`** · Recording, transcribing at the end. **The screen is quieter on purpose.**
With no transcript, the stopwatch takes the full 62/72 and the meters take the space rather than
being padded out with invented filler. The foot of the card says what will happen and how much has
been written.

**`NadaLlego`** · Nothing arrived from the program. The others' meter reads *sin señal*, and the notice sits
directly under it because that meter is the evidence. The act on the right is *Grabar toda la
máquina*, because it is the one press that makes audio arrive; *Cambiar* is the neutral one on the
left, since it opens a picker rather than answering the notice — a button that opens the question is
not the button that answers it. Neither is pico: taking the whole machine costs nothing and loses
nothing. Your meter goes on reading normally underneath, which is what says the recording is fine.
In the window the meter says *sin señal* where its level was, and the sentence and its two presses
stand in the row under the card with the fault lines, so they are on screen and in the automation
tree whichever way the window is arranged. The same row stands, with a sentence of its own, when the program being followed goes away
mid-meeting: it says that program closed, the two presses are the same, and where both reports
stand the went-away sentence is the one said, being the cause. *Sin señal* is the recording's
verdict that nothing arrives from the program — it never did, or it stopped when the program went
away; *nada* is the last second's reading, which a meeting between sentences says all the time, and
both stay.

**`Fallo`** · A source died. Its act is *Reintentar* and *Cambiar* is the neutral one on the left,
which is the mirror of `NadaLlego` and the whole reason both exist: a source that is alive and silent
is answered by pointing somewhere else, so taking the whole machine is the act there; a device that
stopped responding is answered by trying that same device again, so retrying is the act here.
Pointing the channel somewhere else is offered on both and answers neither — a replacement plugged in
mid-meeting is reached without stopping the recording.
**Losing the microphone does not kill the meeting**: the others' strip goes on
and is visibly going on. Yours dims to the dead state described above, and the notice says what
was observed, then that what was said into that microphone from that moment is gone and does not
come back.

**`AlParar`** · Stopping. *Guardando la reunión*, a progress bar in olivo, and three steps —
finished, running, not started — as a tick, a spinner and an empty circle at 45%. **There is no
"do not close the application" sign**: if it is closed it is recovered next time. The meeting is
already in the list below, saving.

**No length on this screen.** A meeting's length is what pouring the spools onto a timeline
produces, and while the save is running that has not happened: the devices are still draining and
the drift the timeline exists to take out is still in it, so any number readable here is short of
the one the report states minutes later. One meeting, one length, and this is not where it is said.

**Unfinished recordings have no screen of their own.** A recording the application was killed in the
middle of has its row in the corpus before its first sample was captured, so it is a row in the
meetings list like any other: on the decision tint, at the top, *Conservar* on the right and
*Descartar* apart on the left. One that cannot be read sits on the attention tint and offers only
*Descartar*. The one still running sits above them and offers nothing at all, because there is
nothing to decide about it yet — the order is the statement and no line says it.

The list carries no sentence. Nothing is deleted until somebody chooses it, and saying so is the
application reassuring the reader about a thing it was never going to do.

### The flow — afterwards

**`Reunion`** · The meeting. **The header is one title line and one data line.** The title is the
name at the sub-screen rank, or *Sin nombre* in secondary ink where nobody has named it, with
*Renombrar* beside it: pressing it swaps the text for the typed field holding the name, selected;
Enter or leaving the field writes it and swaps back, and Escape swaps back and writes nothing. Under
it stands one data line: when, how long, and the status word. What is there first is what the AI left: the abstract on the decision
tint with the summary's longer account under it, then *Qué se decidió*, *Qué queda por hacer*,
*Qué quedó sin resolver*, each item carrying a timestamp pill that opens the turns around it **in
place**, not on another screen. Decisions take an olive bullet and open questions a pico one.
**Under them, once the meeting is transcribed, stands the whole transcript**, alone when nothing has
been left: one line per turn — who said it (the voice's name once it has one, its handle until then),
the minute as a press that seeks the player, and the words as the rendered files carry them, every
correction included. It draws only the lines in view, so a long meeting costs no more than a short
one. Selecting words on a line shows a small *Corregir* pill at the end of the selection, the
application's own and never Windows' menu; pressing it opens `Correcciones` with those words loaded.

**The screen says what the meeting is, in the one word the list says.** While a job for it is queued
or running it asks again every couple of seconds and redraws when the word changes, so a summary that
finishes is on the screen without leaving it and coming back, and the player is not touched. While
work is queued or running, a small progress ring and the status word — olivo for the two that run —
stand where the act offered and *Ignorar* were; *Detener* and *Resumir de nuevo* keep their own
conditions and stay.

The right column is a compact table: who spoke with their share, what it was about, and who
transcribed and who summarised and when. It scrolls apart from the left. The player runs along the
bottom: a play and pause glyph, the time reached, the track, the length and a speaker glyph for the
volume. The glyph follows the level — muted, low, middle, high — and a press on it mutes and brings
the last level back. Its slider is out of the way until the pointer is over the glyph or either has
the keyboard, and then it opens to the glyph's left, from nothing to twice the recording's own level
and starting at its own level; a drag that wanders off it keeps it open.
**The coloured marks on the track are the summary's citations**, so where each thing falls
across the hour is visible. The track's tooltip shows a time and never milliseconds. Both sides are
folded in at their own level, then the volume is applied, then a soft limiter, so one side speaking
is heard as loud as it was said and neither both loud at once nor the volume at twice clips. A
one-track recording and *Quién es quién*'s voice clips play through the same order. A
meeting given more than one summary lists every one of them in the card that says who wrote this,
under the label *resúmenes de esta reunión*, newest accepted first: a radio row each, saying who
wrote it and when it was accepted. The chosen row is the summary on the screen, and choosing
another puts that one back — for every reading of the meeting, search included, and after the
application is closed and opened again. It is a radio row and never the two-way trough, even at
two, because the set only grows. A meeting with one summary has nothing to choose and draws no
rows.

**`Clasificar`** · The screen is titled *Clasificar* — the words of the press that opens it, one entry
used twice — and not by a question. Under the chips stands one line kept for the lit chip, two lines of
body text high whether or not a chip is lit, so nothing below it moves: the chip's own tooltip sentence,
then what it opens, each path's levels joined by *›* and the paths and the places for somebody by *·* —
*Una clase o curso · Universidad › Materia · Profesor*. *Ninguna — la lleno yo* reads its sentence
alone, and with no chip lit the line is empty. The templates are the fourteen chips — the thirteen meetings
of `arquitectura.md` §5.3 **by name**, and *Ninguna — la lleno yo* — each with a one-phrase tooltip,
and what each one fills in is not explained, it is seen on choosing. **A lit chip names its places in
that kind of meeting's own words**: *Clase* opens *Universidad › Materia* and a *Profesor*;
*Conferencia* opens *Organizador › Conferencia*; *Entre dos empresas* two *Empresa* columns; *Soporte
post-venta* *Empresa › Proyecto › Caso* beside a *Cliente*. With no chip lit, or *Ninguna*, the generic
names stand — *Organización › Proyecto › Asunto* in every column and *Persona* for somebody — and what
is stored is the same either way. **The columns are headed by their first level's name**, *Es trabajo
de* and *Del otro lado* being no heading at all; the other side is *Cliente*, *Empresa* or *Otra
organización* by the chip, and the column about a topic keeps *Trata sobre*. A column is drawn where
the lit chip opens it, where the draft already holds a path in it, and always the work column; with
no chip, each absent column is one optional press — *Otra organización…* and *Trata sobre…* — so
every crossing is still fileable by hand. Paths are pills with a chevron between them, no tree drawn
anywhere, and a path may stop at any level. **A pill offers what its level's name holds**: under
*Universidad*, *Empresa*, *Cliente* or *Organizador* it lists organizations and offers *Organización
nueva…*; under *Proyecto*, *Materia* or *Equipo* it lists what is under the pill to its left and
offers *Proyecto nuevo…* at the top, or *Nuevo…* below; the two generic first names list root
organizations and root work alike. Every list is ordered by how it has been used — nodes by the
meetings that link them under that role, people by the meetings that name them, and on *Quién es
quién* the meeting's own people first — and the end of a path is a press reading the next level's
name. *Quiénes* carries a person, optionally the badge saying the meeting is about them, and their
affiliation and since when; the places for somebody are named by the chip (*Candidato*, *Contacto*,
*Entrevistador*). No role with a technical name, no help panel. Filing with *Guardar* goes straight
on to *Quién es quién* when the meeting has a voice nobody named that the recording did not settle,
and its way back returns to the meeting read again; the meeting keeps its own press for the voices.
A name typed wrong is corrected where it was typed, and every picker on the screen says so
the same way: *Renombrar*, over a pill and over a person alike. On a pill it opens the
field a new name is typed into, holding the old name. On a person it opens the notice that adds one,
retitled *Sobre esta persona*, with the name already in it and neither the organization nor the year
asked — where somebody belongs is about a person across years and this screen is about one meeting.
A name something beside it already carries is refused in words rather than by the database, because
the correction somebody makes most often is the second half of a name typed twice. The top of the
tree offers two ways to name something new, *una organización nueva* and *un trabajo que no es de
nadie en particular*, which is how work belonging to no organization gets into the tree without the
screen ever saying the word *iniciativa*.

Under the fourteen, a classification filled by hand can be put by: a name typed beside
*Recordar* puts by what the columns and *Quiénes* hold, and it comes back on every meeting as
a chip after the fourteen, drawn like them. Choosing it adds what it holds beside whatever is
already answered and never takes an answer away, as a shape does. *Recordar* under the name
of one already put by replaces what that one holds. With one lit, *Renombrar…*
opens its name in the same field and Enter corrects it, and *Descartar*, past the gap at the
margin, throws it away. None of these reaches a meeting already filed with it: a meeting
holds what it is filed under and never which chip filled it.

**`QuienEsQuien`** · Who is who. **The voices are called *Tu micrófono*, *Voz 1*, *Voz 2*** and
never the label they are stored under. Each brings a quotation, a small waveform and a clip to
listen to before deciding — *two people look more alike in writing than they sound*. The microphone
that caught exactly one voice is settled already and says so. **One that spoke little brings three
clips instead of one**, because one is not enough to recognise somebody by. Nothing on it failed,
so the screen carries no sentence at all: the clips being there are the instruction. A
voice somebody always talked over has no clip to bring, and says so in a label where its clip
would be. The quotation is where a wrong word is seen, so selecting words in it offers the same *Corregir*
pill, which takes the room over the voices; the way back returns to them with what was typed kept.

**`Correcciones`** · Words that come out wrong. **The problem was never applying a correction, it
was finding one** — nobody reads a corpus looking for what went wrong. So: the person types the word
as it should be, and the corpus answers with how it actually got written, ranked, with the close
ones pre-ticked. Below, on the attention tint, the ones that **turned up by themselves**: they
sounded uncertain and they resemble something said often. The right column shows one applied in
context and what has already been fixed. **Neither list needs a model to have read the meetings.** It opens from the meeting's own screen,
on that meeting's doubtful words, and every correction says whether it holds everywhere or only in
one place the meeting is filed under or above. **Opened from selected words** — the *Corregir* pill
on a transcript line or on a voice's quotation — it opens with those words, trimmed of what is not a
word at both ends, in the field, selected, and pinned at the top as a ticked form whatever the
search finds; a selection that is already the output of a correction reaching the meeting says
*Ya está corregida* and pins nothing.

**`Configuracion`** · Settings, the one screen that lives apart. **Choosing between transcribing and
summarising stopped being a screen per meeting and became a preference set once.** The two engines
are separate choices with a separate cost each, and *a model on this machine* is one option among
them rather than a special case. No amount of money is on it: neither provider quotes a price before a
call. **Every block is a card**: after a recording; the two engines; Deepgram; you; the application; the
folder the meetings are kept in; the export; and Claude Code, with where it is, *Cambiar* and
*Probar*, which runs the same check the screen runs when it opens and says the result either way,
*Funciona* included. *Después de grabar* decides what happens to a recording without being asked
again: under *Transcribir y resumir* the summary is queued the moment the transcription is filed.
The summariser's card says how it is paid — *Con tu plan de Claude*, because summaries run on the
person's own plan and the card shows no figure — and holds its model, *Sonnet*, *Opus* or *Haiku*,
and its *Esfuerzo*, *Alto*, *Medio* or *Bajo*, each chosen once and read when a summary is sent.
*Tú* holds the name and its *Guardar* and nothing else; the language the application is read in and
the theme it is drawn in — *Sistema*, *Claro* or *Oscuro* — are the two pickers of *Aplicación*.
**The folder card has *Cambiar* always, and its tooltip says what the folder keeps.** A folder that
already holds meetings is switched to. An empty one is offered a move: one line names it, a tick
*Borrar la copia anterior* stands unticked, *Cancelar* sits at the margin and *Mover* is the act.
The move is refused, in one sentence each, while a meeting is being recorded or saved, while any
work is queued or running, and while a recording waits to be decided; it copies everything under
the old folder, finds every file the meetings list there whole, and only then switches — saying
*Moviendo…* and refusing to be left meanwhile. Anything missing leaves the old folder as it was, and
the old copy is removed only when the tick was set, after the new one was found whole again. The screen fills everything between the app bar and the foot and arrives by a
fade. It writes one thing at a time and disables nothing while it does: a choice made while a write
is running is written after it, and the last one wins. Under the folder there are
four ticks for what an export takes — the audio, the transcripts, the summaries and what somebody
corrected by hand — with *Exportar* beside them at the normal rank, and one line saying when the last
export was made, what it took and where it went. Under the two engines is the Deepgram key the first
of them spends: a secret field with *Guardar* beside it at the normal rank, and under it one label
saying whether a key is kept on this machine, with *Quitar* beside it while one is: a press of
the row's rank at the margin with a 1px ring, because it loses something. *Exportar* is a glyph with
a tooltip saying what it does. The
artboard does not draw it; it is here because without it a person who installed the application
could record and never transcribe.

**`Historia`** · A node's story. **The fourth kind of screen the product has**: every other one is
about one meeting or about the list of them, and this one is about a thing several meetings hang
off. It is reached by pressing what a meeting is filed under, on the meeting's own screen, which is
what makes reading a project's history something you go and do. The header is the path down the
tree as pills, and every pill above the last opens its own story — which is how *read the whole of
this client* gets asked, and it is the one thing a panel inside a meeting could never answer. Under
it, **one card per meeting, oldest first**, because a history is read forward: the meeting's date
and its name are the card's heading and are the press that opens it, and inside it every decision,
every action and every open question of that meeting in the order it was said, each with the minute
it was said at. Decisions take an olive bullet and open questions a pico one, which is `Reunion`'s
pair and means the same thing here; an action takes the rule's own grey. **There is no player and
no transcript on it.** What this screen is for is reading across meetings; the minute is what says
which meeting to open to hear one, and the transcript unfolds there. A node nothing has been said
about says so in one line, and a node with more in it than the screen read says that in one line
under the last card.

### Across the flow

**`Costo`** · The dialogue. Two of them on one artboard, side by side, because the difference
between them is the whole point: transcribing shows how many minutes will be sent, worked out from
what is sent, and says it is an estimate; summarising shows the model and no figure. Neither shows
money. The first of the two things in this application that stop the screen.

**`ReunionCruda`** · The meeting, recorded and nothing else. **It is `Reunion` with the middle
missing** — the same header, the same columns, the same player along the bottom. What the AI has not
left yet, and the transcript the meeting does not have yet, are simply not there, and *Transcribir*
is the act on the right. The screen looks sparse,
and that is honest: a meeting nobody has bought anything for has little in it. A screen drawn for
the empty case would be a second blueprint for one screen, which is where a design starts
disagreeing with itself.

**`ReunionTranscrita`** · The meeting, transcribed and not yet summarised. The third of the three
and the reason the other two are not two designs: it is the same screen again with more of it
filled in. The left column holds the transcript alone, because nothing has read the meeting yet, and the
right one has gained what a transcription buys — who spoke and their share, and *Resumir* as the act.
**The player has no marks on its track**, and that is the picture's whole point: the marks are the
summary's citations, so a meeting with no summary has none, and the track is where a reader sees
that the difference between these three is what has been bought rather than which screen they are
looking at.

**`Primera`** · The first time the application opens. **It is the settings screen in its first-time
arrangement and not a screen of its own**: a second drawing of three blocks the settings screen
already has would be where a design starts disagreeing with itself. It opens exactly when the
meetings can be reached and nobody has said who is using this, titled *Primeros pasos*, with three
blocks and *Empezar* as the principal act, which leaves it. **Three questions, and there are only
three because everything else has a right answer already**: the language comes from Windows, the
meetings have a folder in the user's own profile, and there is one transcription engine and one
summariser, so neither engine is a choice; both cards stand in the step, so what ends a recording is never
answered without the engines it spends on in view, and the summariser's model and effort sit on its
card with answers already chosen. What is left is the name of the person using it — nobody can
work that out, and every citation of every meeting rests on it — the Deepgram key, without which a
recording is never transcribed, and what should happen when a recording ends, which is the only one
of the three that spends money. Where the meetings are kept, the language and the theme are not asked: the default stands and
*Configuración* changes it. The back button in the app bar leaves it too and nothing is lost; it
opens again at the next launch until a name is saved. A folder that cannot be read does not open it,
because every block would be dead there and the card that fixes the folder is on the full screen.

No affiliation is asked for. A person has as many as they have, each with its own period, and the
first screen of an application is not where somebody enumerates their jobs.

**`MainAbierto`** · The main screen with the meetings drawer raised to the full height of the
window. The same screen, not another one: the recording card is still above and slides out of the
way. The list scrolls whole — no paging, and no search field until the index behind it exists.

**A meeting under way keeps a strip whenever the room below has the window** — the list raised into
it, or a meeting being read in it — because what it is doing and the press that stops it are never
off screen. It is the recording card's stand-in and never a second opinion beside it: the two are
never both up, and both are drawn from the same clock.

The strip carries three things and nothing else — not the meters, not the stopwatch, not the
meeting's name, and none of the presses but one:

- **the state word, in olivo.** It is the only thing there in all four states a meeting under way
  can be in — opening its devices, recording, paused, saving — and it is olivo in all four, because
  on the strip olivo says a meeting is under way rather than, as in a list, which row is the live
  one.
- **one line of data**: how long the meeting has been running, which program is being followed and which
  microphone is on it. The length only where there is one — opening the devices and saving the
  meeting have no clock, and a strip showing the last reading through either would be a screen
  saying a meeting is still being recorded. Elapsed time is spelled the way it is spelled
  everywhere, through `ScreenNumbers.Long`, so it reads `0:08:12` and not `08:12`.
- ***Detener***, the square glyph at the normal rank and not the principal one. The screen's one principal act is
  *Empezar a grabar*; a press that steers a recording already running is normal, here as on the
  card. It is the one press that cannot wait for somebody to lower the list, so it is the only one
  the strip has: *Pausar*, which is *Seguir* while paused, waits, and lowering the list is what
reaches it.

**`Persona`** · Adding somebody, over whatever screen asked. The second of the two dialogues: name, and optionally an organization and since when. It adds an affiliation and never
replaces one, and while a name is typed it offers up to three people already there under a nearly
identical name, or under a name the typed one is the start of or a word of, which a press answers
with and adds nobody.

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

An artboard and a closed claim of `ISA.md` disagree the same way, and the claim wins: it says what
has to be true of the product and it closed on a probe that ran, which a drawing never does. The
artboard is corrected and this page gains the sentence the drawing was carrying alone.

**A screen that is already merged and a sentence here disagree the third way, and the screen is the
fact.** This page gains the sentence it was missing and the artboard is drawn to both. That is the
only direction that leaves anything true: a sentence here describing a screen that does not exist
corrects nothing, and re-drawing the artboard to it would put two drawings of one screen in the
repo. It is a correction and never a licence — building a screen to something this page does not
say is still skipping this page, and it is what puts the work of writing the missing sentence on
whoever comes after.

Two things on them are deliberately unfinished: the product's name and mark are a placeholder, and
`Sistema` is this page as a picture, so it is the one artboard that has to be re-read whenever this
page changes.

Outside the window the mark is `src/MeetingTranscriber.App/Assets/Mark.svg`: every logo the package
names, both taskbar variants and the program's `.ico` are rendered from it, in Olivo's colours, by
running `tools/MeetingTranscriber.Icons`. Redrawing the mark is that file, the app bar's two paths in
`MainWindow.xaml` and one run, and `ApplicationIconTests` fails while the two drawings disagree or
the images were rendered from anything but what both say now.

## Opening the artboards

`docs/design/README.md` says what the files are and what a browser does and does not render.
