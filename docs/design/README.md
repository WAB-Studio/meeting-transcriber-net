# The Olivo artboards

Eighteen screens as pictures. **`../design.md` is the authority** — this folder is what it was
written from, and where the two disagree that document says so and wins.

## Opening one

Double-click it. Each `.dc.html` is a self-contained page with everything inline; a browser renders
it with no build step and nothing to install.

Three things about what you will see:

- **The two fonts come from Google Fonts over the network.** Offline, the page falls back to Segoe
  UI and Consolas and the proportions shift a little. Nothing else changes.
- **`support.js` is not here and is not coming.** These were drafted in a canvas editor and each
  file still asks for its runtime in the `<head>` and defines a `Component` class at the foot. The
  browser fails that request and renders the markup anyway, which is all that is wanted here.
- **`QuienEsQuien` is the one that loses something.** Its three audio clips are drawn by an
  `<sc-for>` loop that the missing runtime would have expanded, so the little waveforms come out
  empty. Everything around them renders.

Nothing under `src/` reads these files. They are read by people.

## What is here

`canvas.json` is the index: it names the eighteen, lays them out on two pages — *Flujo* and
*Sistema* — and carries the notes written against them. Those notes are the shortest statement
of why a screen is shaped the way it is, and they are mined into `../design.md`.

**A note names what it is about and is never placed.** It carries `file` and is drawn against that
artboard wherever the artboard ends up, or it carries `page` and is about the page as a whole —
exactly one of the two, and no coordinates of its own. It used to carry only a position, and five
notes had already come loose from the artboards they were written about by the time a row grew.
`ReunionTranscrita` and `Sistema` have no note; that is a sentence nobody has written, not an entry
missing from this file.

| Page | Row | File | Screen |
| --- | --- | --- | --- |
| Flujo | Recording | `Main.dc.html` | Inicio — recording and the meetings, on one screen |
| Flujo | Recording | `MainAbierto.dc.html` | Inicio, with the meetings drawer raised |
| Flujo | Recording | `GrabandoVivo.dc.html` | Recording, transcribing live |
| Flujo | Recording | `GrabandoDiferido.dc.html` | Recording, transcribing at the end |
| Flujo | Recording | `NadaLlego.dc.html` | Nothing arrived from the program |
| Flujo | It ended or broke | `Fallo.dc.html` | A source died |
| Flujo | It ended or broke | `AlParar.dc.html` | Stopping |
| Flujo | It ended or broke | `Configuracion.dc.html` | Settings |
| Flujo | Afterwards | `Reunion.dc.html` | The meeting |
| Flujo | Afterwards | `ReunionCruda.dc.html` | The meeting, recorded and nothing else |
| Flujo | Afterwards | `ReunionTranscrita.dc.html` | The meeting, transcribed and not yet summarised |
| Flujo | Afterwards | `Clasificar.dc.html` | What it was about |
| Flujo | Afterwards | `QuienEsQuien.dc.html` | Who is who |
| Flujo | Afterwards | `Correcciones.dc.html` | Words that come out wrong |
| Flujo | Across the flow | `Primera.dc.html` | The first time it opens — who is using it |
| Flujo | Across the flow | `Costo.dc.html` | What a charge costs, asked once |
| Flujo | Across the flow | `Persona.dc.html` | Adding somebody, from wherever |
| Sistema | — | `Sistema.dc.html` | Olivo — the system sheet |

Every artboard is 1120 wide; the flow is 720 tall except `Reunion` (860), `Correcciones` (820) and
`Configuracion` (760), and the system sheet is 3920. The *Afterwards* row reads left to right in
the order a meeting gains things: recorded, transcribed, summarised.

## The names and the copy

The filenames and every word on these pages are Spanish, because the product is Spanish and these
are the product's own words. That is the exception `CLAUDE.md` allows for what a person reads, not
a licence for anything else in the repo.

Read a string against `../design.md` before it becomes an entry of `UiTexts`: one sentence per
screen and only where something failed, neutral Spanish with no voseo, no line that exists to
explain how the application works inside, and one verb per act from the closed table there.
