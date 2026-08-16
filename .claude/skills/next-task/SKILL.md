---
name: next-task
description: >-
  Toma la próxima tarea del board y la lleva hasta el PR abierto, sin nadie en el loop. Es la
  sesión worker del orquestador desatendido: elige, trabaja, prueba, abre PR y entrega un handoff
  estructurado. Triggers: "next task", "próxima tarea", "seguí con el board".
---

# next-task — una sesión de trabajo desatendida

Una corrida de esta skill es **una tarea del board llevada hasta el PR abierto**, y nada más.
No hay nadie mirando, así que las dos cosas que normalmente resuelve una persona presente —
decidir cuando el trabajo se bifurca, y no hacer lo que no se puede hacer — están escritas acá.

`CLAUDE.md` gobierna el trabajo en sí. Esto sólo agrega lo que cambia por estar solo.

El orquestador te pasa la ruta del handoff como argumento. Si no te pasó ninguna, usá
`.claude/orchestrator/log/handoff.json`.

## 0 · Antes de tocar nada

```powershell
$S = "$env:USERPROFILE\.claude\skills\clickup\clickup.py"
$env:PYTHONIOENCODING = "utf-8"   # sin esto el CLI crashea al imprimir acentos y flechas
```

El árbol tiene que estar limpio y estar parado en `main` al día. Si no lo está, no arregles nada:
`outcome: "blocked"`, escribí el handoff y terminá. Una sesión desatendida que "acomoda" lo que
otra dejó a medias es la forma más rápida de perder trabajo.

## 1 · Elegir

El pool es lo que está en `Open` en el space `MeetingTranscriber`. `pending` no es pool: acá
significa *espera a una persona* (ver §2).

Recorré las listas **en orden de fase** y quedate con la primera que tenga una tarea elegible:

```
0 · Contratos y caracterización     4 · Deepgram BYOK
1 · Núcleo .NET desde artefactos    5 · Summaries
2 · Spike y motor de audio          6 · Conocimiento local
3 · Grabador WinUI                  7 · Distribución y backup
```

```powershell
python $S tasks --list "0 · Contratos y caracterización" --status Open
```

Dentro de la lista que ganó, ordená por prioridad: `urgente` → `alta` → `normal` → `baja`.

Si ninguna lista tiene una tarea elegible: `outcome: "no_tasks"`, handoff, y terminá. Eso es un
final legítimo, no una falla.

## 2 · Lo que no se puede hacer solo

**Una tarea es inelegible cuando terminarla necesita algo que no está de este lado de la CLI.**
Hoy eso es, sobre todo, la fase 2: una reunión real, dos placas de audio, un dispositivo que se
desenchufa a mitad de grabación, dos horas de deriva medidas sobre hardware. También cuenta una
tarea cuya decisión es del producto y no del código — dónde vive la capa humana, qué promete un
contrato — que `CLAUDE.md` manda preguntar.

El riesgo real no es trabarse: es que produzcas mediciones plausibles de una reunión que nunca
existió. **Un número que no salió de una corrida no se escribe**, ni en el código, ni en el ISA,
ni en un comentario del board.

Cuando una tarea es inelegible:

```powershell
python $S move <id> --status pending
python $S comment <id> --text "Necesita <qué exactamente> — <qué tiene que hacer o proveer una persona>."
```

Anotala en `skipped[]` del handoff y seguí con la siguiente candidata. El comentario tiene que
decir qué necesita, no que vos no pudiste: quien lo lee mañana necesita saber qué traer.

## 3 · Trabajar

Como cualquier tarea, con `CLAUDE.md` mandando. Lo que no cambia por estar solo:

- Los claims existen en `ISA.md` **antes** de construir. Si no están, se escriben primero — la
  skill `isa` es la que sabe cómo. La tarea los nombra con `Cierra: ISC-n`.
- `python $S move <id> --status "in progress"` al arrancar.
- Los cuatro comandos, cada uno en su línea, y un rojo frena todo:
  `dotnet restore`, `dotnet format --verify-no-changes`,
  `dotnet build --no-restore -warnaserror`, `dotnet test --no-build`.
- Un diff de más de 50 líneas no-comentario corre `/adversarial-review` y **lo que el veredicto
  confirma se arregla en la misma pasada**.
- Un claim cierra sobre un probe que corrió. Si el probe falla, la pregunta es si está mal el
  código o el claim, y las dos respuestas son válidas — pero la que elijas va al handoff.

Si el trabajo se traba de un modo que no podés resolver, `outcome: "blocked"` con
`blocked_reason` diciendo qué lo trabó. No abras un PR a medias para mostrar avance.

## 4 · Entregar

Rama, commit, `gh pr create`. El PR **no se mergea acá** — de eso se encarga la auditoría, que
mira el diff con ojos que no son los que lo escribieron. Después del PR:

```powershell
python $S move <id> --status "in review"
python $S comment <id> --text "<evidencia: probes que corrieron, veredicto del review, PR>"
```

Volvé a `main` con el árbol limpio.

## 5 · El handoff

Escribí el JSON en la ruta que te pasaron, y que tu **último mensaje sea ese mismo JSON**. La
forma está en `handoff.schema.json`, al lado de este archivo.

Tres campos son el punto de todo esto, así que leelos despacio:

- **`decisions_deferred`** — toda bifurcación que resolviste sin que nadie te lo confirmara, y
  toda que dejaste abierta. `[]` no es silencio: **es la afirmación de que no hubo ninguna**, y
  alguien la va a chequear contra el diff. Si escribiste "queda pendiente" en cualquier lado —
  el PR, un comentario del board, el código — eso es una entrada acá. `blocks_the_pr` es
  verdadero cuando la decisión, resuelta al revés, invalidaría lo que acabás de subir.
- **`left_out`** — lo que la tarea pedía y no entregaste. Bajar el alcance es del usuario, no
  tuyo; si lo bajaste igual, acá se dice.
- **`probes`** — qué corrió de verdad, con su resultado. No lo que debería haber corrido.

Un handoff honesto que dice `blocked` vale más que uno prolijo que dice `pr_opened` sobre trabajo
a medias. Lo segundo se detecta y frena el día entero.
