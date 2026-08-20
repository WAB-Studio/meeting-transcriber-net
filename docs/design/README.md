# The Olivo artboards

Thirteen screens as pictures. **`../design.md` is the authority** — this folder is what it was
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

`canvas.json` is the index: it names the thirteen, lays them out on two pages — *Flujo* and
*Sistema* — and carries the note written against each screen. Those notes are the shortest statement
of why a screen is shaped the way it is, and they are mined into `../design.md`.

| Page | File | Screen |
| --- | --- | --- |
| Flujo | `Main.dc.html` | Inicio — recording and the meetings, on one screen |
| Flujo | `GrabandoVivo.dc.html` | Recording, transcribing live |
| Flujo | `GrabandoDiferido.dc.html` | Recording, transcribing at the end |
| Flujo | `NadaLlego.dc.html` | Nothing arrived from the program |
| Flujo | `Fallo.dc.html` | A source died |
| Flujo | `AlParar.dc.html` | Stopping |
| Flujo | `Configuracion.dc.html` | Settings |
| Flujo | `Recuperacion.dc.html` | Unfinished recordings |
| Flujo | `Reunion.dc.html` | The meeting |
| Flujo | `Clasificar.dc.html` | What it was about |
| Flujo | `QuienEsQuien.dc.html` | Who is who |
| Flujo | `Correcciones.dc.html` | Words that come out wrong |
| Sistema | `Sistema.dc.html` | Olivo — the system sheet |

Every artboard is 1120 wide; the flow is 720 tall except `Reunion` (860), `Correcciones` (820) and
`Configuracion` (760), and the system sheet is 1560.

## The names and the copy

The filenames and every word on these pages are Spanish, because the product is Spanish and these
are the product's own words. That is the exception `CLAUDE.md` allows for what a person reads, not
a licence for anything else in the repo.

The copy is a draft. `../design.md` names the three places the markup carries a value or a phrasing
that a later decision replaced — the voseo especially. Read a string against that section before it
becomes an entry of `UiTexts`.
