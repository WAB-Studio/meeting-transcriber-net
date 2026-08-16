---
name: audit-session
description: >-
  Audita lo que dejó una sesión worker desatendida: lee su handoff y lo contrasta contra el diff, el
  PR, la tarea y el ISA para decidir si el día sigue o se frena. Abre las tarjetas del trabajo que
  quedó nombrado. Triggers: "auditar la sesión", "audit session".
---

# audit-session — el lector independiente

Una sesión worker se autoevalúa, y ese es el problema. Un worker que **sabe** que difirió una
decisión la declara; uno que no se dio cuenta de que decidió llena `decisions_deferred: []` con
total honestidad. Esta skill existe para el segundo caso, así que la regla que la ordena es una:

**Nunca juzgues por el relato del worker. Juzgá por el diff, el PR, la tarea y el ISA.**
El handoff dice dónde mirar, no qué vas a encontrar.

El orquestador te pasa la ruta del handoff y la del veredicto como argumentos.

```powershell
$S = "$env:USERPROFILE\.claude\skills\clickup\clickup.py"
$env:PYTHONIOENCODING = "utf-8"
```

## 1 · Salidas cortas

Leé el handoff. Si `outcome` es `no_tasks` o `blocked`, no hay diff que auditar:
`verdict: "nothing_to_review"`, `continue_day: false`, escribí el veredicto y terminá. `blocked`
además deja un comentario en la tarjeta con `blocked_reason`, para que mañana se lea en el board
y no en un log.

## 2 · La evidencia, sin pasar por el worker

```powershell
gh pr view <n> --json title,body,files,additions,deletions
gh pr diff <n>
gh pr checks <n> --watch      # el CI tarda unos 5 minutos
python $S task <id>           # la descripción de la tarea y sus comentarios
```

Y `ISA.md` para los claims. Leé el diff **entero** si entra; si es enorme, leé los archivos que
cargan la lógica y no los generados.

## 3 · Los cinco chequeos

1. **¿Hizo lo que la tarea pedía?** Descripción de la tarea contra el diff — no contra el body
   del PR, que lo escribió el mismo que hizo el trabajo.
2. **¿Hay decisiones que no declaró?** El chequeo central. Buscá en el diff y en el body: un
   `TODO`, un "por ahora", "queda pendiente", "se puede mejorar", un caso manejado de una forma
   defendible pero no obvia, un default elegido sin fundamento a la vista, una firma que promete
   menos de lo que la tarea pedía. Cada una que no esté en `decisions_deferred` va a
   `unreported_decisions`. **Acá es donde esta skill se paga sola.**
3. **¿Los claims cierran de verdad?** Cada `isc_closed` tiene que estar `[x]` en `ISA.md`, con su
   línea en `## Verification` nombrando un probe que corrió — y ese probe tiene que estar en
   `probes` con `passed: true`. Un claim marcado sobre un test que no corrió es un hallazgo grave:
   es la única propiedad que sostiene todo el resto del repo.
4. **¿`left_out` coincide con el diff?** Si la tarea pedía tres cosas, el diff trae dos y
   `left_out` está vacío, el worker bajó el alcance sin decirlo.
5. **¿Los `skipped` son honestos?** Una tarea movida a `pending` tiene que necesitar de verdad una
   persona o hardware. Si es sólo difícil, el worker se la sacó de encima: devolvela a `Open` y
   anotalo.

## 4 · El veredicto

**`hold`** — el día se frena acá. Cualquiera de estas alcanza: CI en rojo; una
`decisions_deferred` con `blocks_the_pr`; una `unreported_decisions` que, resuelta al revés,
invalidaría el diff; un claim cerrado sin probe; el diff hace algo que la tarea no pedía y toca
`Domain/Audio/`, `Domain/Time/` o `Domain/Jobs/`.

**`pass_with_followup`** — el diff se sostiene, pero quedó trabajo nombrado: decisiones que no
bloquean, `left_out`, hallazgos tuyos que no invalidan nada.

**`pass`** — nada de lo anterior.

## 5 · Qué hacés y qué no

**El PR queda abierto y la tarjeta queda en `in review`, siempre.** Integrar el PR y cerrar la
tarjeta son del usuario — `CLAUDE.md` lo dice y esta skill no es la excepción. Lo que dejás no es
un merge: es un PR que el usuario puede leer sabiendo que alguien que no lo escribió ya lo miró.

En los tres veredictos, comentá en el PR qué revisaste y qué encontraste. En `hold`, comentalo
también en la tarjeta: es lo que se lee a la mañana.

Para cada followup, una tarjeta en la misma lista de fase que la tarea de origen:

```powershell
python $S create --list "<lista>" --name "<nombre>" --priority normal
python $S link <id-nuevo> --needs <id-origen>
```

El nombre abre con `BUG - ` sólo si es algo que ya está mal. La descripción dice qué hay que hacer
y cómo se sabe que está listo — **no** cómo lo encontraste, que va como comentario. Una decisión
que corresponde al usuario y no al código se escribe como la pregunta que hay que contestarle,
no como una tarea de implementación.

`continue_day` es verdadero en `pass` y `pass_with_followup`, falso en `hold`.

## 6 · La salida

JSON en la ruta que te pasaron, y tu último mensaje es ese mismo JSON. La forma está en
`verdict.schema.json`, al lado de este archivo. `actions_taken` lista lo que hiciste de verdad —
cada tarjeta con su ID, cada comentario — porque es lo único que el usuario va a leer a la mañana
para saber qué pasó mientras no estaba.
