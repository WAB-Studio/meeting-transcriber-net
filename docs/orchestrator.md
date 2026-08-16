# El orquestador desatendido

Un día de trabajo sin nadie en el loop: sesiones frescas encadenadas, cada una toma una tarea del
board y la deja en un PR abierto que ya miró alguien que no lo escribió.

```powershell
.\.claude\orchestrator\run-day.ps1                       # 4 sesiones, enfriando 10 min
.\.claude\orchestrator\run-day.ps1 -MaxSessions 8 -MaxUsdDay 120
.\.claude\orchestrator\run-day.ps1 -DryRun               # imprime lo que lanzaría
```

Todo lo que produce queda en `.claude/orchestrator/log/<fecha>/`, ignorado por git.

## Las cuatro piezas y por qué son cuatro

| Pieza | Qué decide |
| --- | --- |
| `run-day.ps1` | Nada sobre el código. Cuándo lanzar, cuándo parar, cuánto gastar. |
| `next-task` | Qué tarea, cómo hacerla, qué no se puede hacer solo. |
| `audit-session` | Si lo que quedó se sostiene, leyendo el diff y no el relato. |
| `handoff` / `verdict` | La forma en que las dos primeras se hablan. |

**El orquestador no es una IA a propósito.** Elegir el momento y sumar dólares no tiene juicio
adentro, y un proceso que mirara todo el día se degradaría justo cuando más lleva acumulado:
a las seis de la tarde ya compactó lo que vio a las nueve y opina sobre un día que no recuerda.
Cada sesión arranca en cero y por eso el día no se queda sin contexto.

**La auditoría sí es una IA, y corre fresca por sesión.** Existe por un modo de falla concreto:
el worker que escribe "esto queda pendiente" en prosa y sigue. El handoff tiene un campo para eso
—`decisions_deferred`, donde `[]` es una afirmación y no un silencio— pero un schema sólo tapa al
que sabe que difirió algo. Al que no se dio cuenta de que decidió lo agarra un lector que mira el
diff, la tarea y el ISA sin pasar por el resumen del worker.

## Qué frena el día

El loop para —no reintenta— ante cualquiera de estas, y el log dice cuál:

- **Preflight**: árbol sucio, parado fuera de `main`, o `main` divergido de `origin`. Una sesión
  nueva sobre lo que otra dejó a medias es la forma más rápida de perder trabajo.
- **Handoff o veredicto mal formados.** No se interpretan con buena voluntad: una sesión que no
  terminó de decir qué hizo cuenta como una que no terminó.
- **`outcome: blocked`** del worker, o **`verdict: hold`** de la auditoría.
- **`no_tasks`**: no quedan tareas elegibles. Es un final legítimo, no una falla.
- **Techo de dólares del día**, o tres backoffs seguidos por límite de uso.

Un error de la API o el límite de las 5 horas no frenan: esperan 60 minutos y reintentan la misma
sesión, hasta tres veces.

## Los PRs quedan abiertos

La sesión termina en el PR y **el merge sigue siendo del usuario**, igual que en una sesión a mano
—`CLAUDE.md`, "How work starts and ends"—. La auditoría no cambia eso: agrega un comentario en el
PR diciendo qué revisó y qué encontró, para que a la mañana el merge se lea rápido en vez de
tenerse que reconstruir.

**La consecuencia práctica:** cada sesión del día ramifica desde el mismo `main`, así que la
sesión 3 no ve lo que hizo la 1. Mientras toquen partes distintas del sistema no pasa nada; cuando
se pisan, aparece un conflicto que resolvés al mergear. Por eso el default es 4 sesiones y no 12:
el número que conviene es el que entra entre dos merges tuyos.

La tarjeta queda en `in review` y cerrarla también es del usuario.

## Lo que ninguna sesión puede hacer

Hay tareas cuyo final no está de este lado de la CLI: una reunión real, dos placas de audio, un
dispositivo que se desenchufa a mitad de grabación, dos horas de deriva medidas sobre hardware.
Hoy es casi toda la lista `2 · Spike y motor de audio`.

El riesgo no es que el worker se trabe: es que **produzca mediciones plausibles de una reunión que
nunca existió**. Por eso `next-task` las mueve a `pending` con un comentario diciendo qué hay que
traer, y sigue con la próxima — y la auditoría revisa que ese salteo sea honesto y no una tarea
difícil sacada de encima.

Eso fija una convención del board que sólo existe por el modo desatendido: **`Open` es el pool y
`pending` significa "espera a una persona"**.

## Cosas que se rompen y no se ven

- **`PYTHONIOENCODING=utf-8`.** Sin eso el CLI de ClickUp corta con `UnicodeEncodeError` al
  imprimir acentos o flechas, porque la consola de Windows entrega cp1252. `run-day.ps1` lo fija
  y los procesos hijos lo heredan; a mano hay que ponerlo.
- **Permisos.** En `-p` no hay a quién preguntarle: un permiso que falta no pregunta, deniega, y
  la sesión sigue como si esa herramienta no existiera. Los extras del orquestador van en
  `.claude/orchestrator/settings.json`, que se pasa con `--settings` — así el `deny` del proyecto
  (`deepgram.json`, `settings.json`, force-push) sigue mandando.
- **`--max-budget-usd`** corta la sesión sola. Con suscripción el número no se factura, pero
  igual sirve de tope contra una tarea patológica.

## Que arranque solo

```powershell
$a = New-ScheduledTaskAction -Execute "powershell.exe" `
     -Argument '-NoProfile -File "C:\Users\pc\Documents\GitHub\Personal\meeting-transcriber-net\.claude\orchestrator\run-day.ps1"'
Register-ScheduledTask -TaskName "meeting-transcriber-dia" -Action $a `
     -Trigger (New-ScheduledTaskTrigger -Daily -At 9am)
```

Una vez por día, no un cron cada media hora: el loop ya se autoregula y dos corridas encimadas se
pisan en el mismo árbol.
