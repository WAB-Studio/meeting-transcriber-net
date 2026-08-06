# El corpus en SQLite, y el problema que arreglé

## 1. El esquema completo

Cada tabla lleva de qué lado de la línea está: **fuente** (no se recupera de nada que quede en la
máquina) o **derivada** (se puede volver a producir desde las fuentes). Esa distinción es de la que
cuelgan el backup, el borrado y el rebuild.

```mermaid
erDiagram
    meetings ||--o{ artifacts : "cascade"
    meetings ||--o{ capture_runs : "cascade"
    meetings ||--o{ processing_jobs : "cascade"
    meetings ||--o{ transcription_runs : "cascade"
    meetings ||--o{ extraction_runs : "cascade"
    meetings ||--o{ utterances : "cascade"
    meetings ||--o{ summaries : "cascade"
    meetings ||--o{ decisions : "cascade"
    meetings ||--o{ action_items : "cascade"
    meetings ||--o{ meeting_participants : "cascade"
    meetings ||--o{ speaker_assignments : "cascade"
    meetings ||--o{ terminology_corrections : "cascade"
    meetings |o--o{ audit_events : "set null"
    companies |o--o{ projects : "set null"
    companies |o--o{ people : "set null"
    projects |o--o{ meetings : "set null"
    projects ||--o{ terminology_corrections : "cascade"
    people ||--o{ meeting_participants : "cascade"
    people ||--o{ speaker_assignments : "cascade"
    people |o--o{ decisions : "set null"
    people |o--o{ action_item_progress : "set null"
    processing_jobs |o--o{ transcription_runs : "set null"
    processing_jobs |o--o{ extraction_runs : "set null"
    artifacts |o--o{ transcription_runs : "set null"
    artifacts |o--o{ extraction_runs : "set null"
    extraction_runs ||--o{ summaries : "cascade"
    extraction_runs ||--o{ decisions : "cascade"
    extraction_runs ||--o{ action_items : "cascade"
    extraction_runs ||--o{ action_item_progress : "cascade"
    utterances |o--o{ decisions : "cita por meeting_id y ordinal, no cascade"
    utterances |o--o{ action_items : "cita por meeting_id y ordinal, no cascade"
    action_items ||..o| action_item_progress : "join por extraction_run_id y ordinal"

    meetings {
        TEXT id PK
        TEXT legacy_id UK
        TEXT project_id FK
        TEXT title "capa humana"
        TEXT started_at
        INTEGER duration_ms
        TEXT source_profile "multichannel o diarize"
        TEXT language
        TEXT lifecycle_state "active, deleting o deleted"
        TEXT deleted_at
    }
    artifacts {
        TEXT id PK
        TEXT meeting_id FK
        TEXT kind
        TEXT origin "source o derived, con CHECK"
        TEXT relative_path
        INTEGER byte_size
        TEXT sha256 "64 chars"
    }
    capture_runs {
        TEXT id PK
        TEXT meeting_id FK
        TEXT others_device_id "canal 0"
        TEXT me_device_id "canal 1"
        INTEGER sample_rate
        INTEGER drift_ms
        INTEGER recovered
    }
    processing_jobs {
        TEXT id PK
        TEXT meeting_id FK
        TEXT kind "capture, finalize, transcribe, extract, render, backup"
        TEXT state
        INTEGER attempt
        TEXT idempotency_key UK
        TEXT next_attempt_at
    }
    transcription_runs {
        TEXT id PK
        TEXT meeting_id FK
        TEXT job_id FK
        TEXT response_artifact_id FK
        TEXT audio_sha256
        TEXT billable_config_hash
        INTEGER estimated_cost_micros
        TEXT approved_at
    }
    extraction_runs {
        TEXT id PK
        TEXT meeting_id FK
        TEXT job_id FK
        TEXT output_artifact_id FK
        TEXT prompt_version
        TEXT schema_version
        TEXT input_hash
        TEXT accepted_at
    }
    utterances {
        TEXT id PK
        TEXT meeting_id FK
        INTEGER ordinal
        INTEGER start_ms
        INTEGER end_ms
        INTEGER channel "0 reunion, 1 usuario, NULL si es diarize"
        TEXT speaker_label
        TEXT text
    }
    summaries {
        TEXT id PK
        TEXT meeting_id FK
        TEXT extraction_run_id FK
        TEXT abstract
        TEXT body
    }
    decisions {
        TEXT id PK
        TEXT meeting_id FK
        TEXT extraction_run_id FK
        TEXT statement
        TEXT decided_by_person_id FK
        INTEGER utterance_ordinal FK "con meeting_id, el turno citado"
        TEXT quoted_text
        TEXT source_artifact_sha256
    }
    action_items {
        TEXT id PK
        TEXT meeting_id FK
        TEXT extraction_run_id FK
        INTEGER ordinal "posicion en la extraccion"
        TEXT statement
        TEXT due_date
        INTEGER utterance_ordinal FK "con meeting_id, el turno citado"
        TEXT quoted_text
        TEXT source_artifact_sha256
    }
    action_item_progress {
        TEXT extraction_run_id PK
        INTEGER ordinal PK
        TEXT state "open, done o dropped"
        TEXT owner_person_id FK
        TEXT updated_at
    }
    people {
        TEXT id PK
        TEXT display_name
        INTEGER is_me
    }
    projects {
        TEXT id PK
        TEXT name UK
    }
    meeting_participants {
        TEXT meeting_id PK
        TEXT person_id PK
        TEXT role
    }
    speaker_assignments {
        TEXT meeting_id PK
        TEXT speaker_label PK
        TEXT person_id FK
        TEXT assigned_by "channel o person"
    }
    terminology_corrections {
        TEXT id PK
        TEXT project_id FK
        TEXT meeting_id FK
        TEXT wrong_text
        TEXT correct_text
        TEXT match_mode
    }
    settings {
        TEXT key PK
        TEXT value
    }
    audit_events {
        INTEGER id PK
        TEXT occurred_at
        TEXT actor "user, app o agent"
        TEXT action
        TEXT meeting_id FK
    }
```

Fuera del diagrama quedan `utterances_fts` y `summaries_fts`: son tablas virtuales FTS5 de
contenido externo, mantenidas por triggers, y no tienen foreign keys. EF no las modela.

### Los tres grupos

| Grupo | Tablas | Qué pasa si las borro |
| --- | --- | --- |
| **Derivadas** | `utterances`, `summaries`, `decisions`, `action_items`, ambos FTS5 | Nada. Se reproyectan desde `deepgram.json` y las extracciones aceptadas. |
| **Capa humana** | `people`, `projects`, `meeting_participants`, `speaker_assignments`, `terminology_corrections`, `action_item_progress`, más el título y la clasificación de `meetings` | Se pierde para siempre. No sale de ningún artefacto. |
| **Registro** | `artifacts`, `capture_runs`, `processing_jobs`, `transcription_runs`, `extraction_runs` | Se pierde qué se pagó y en qué estado quedó cada cosa. |

---

## 2. El problema

`action_items` estaba declarada derivada — el rebuild la borra entera y la vuelve a proyectar — pero
guardaba dos columnas que ninguna extracción produce:

- `state`: si la acción está abierta, hecha o descartada. Lo mueve una persona.
- `owner_person_id`: quién la tomó. Lo decide una persona.

O sea que la tabla era mitad derivada y mitad fuente, y el rebuild se llevaba la mitad que no lo
era.

Y encima había una segunda vía de pérdida. La cita de cada decisión y cada acción apunta a la
`utterance` de donde salió, y esa foreign key era `ON DELETE CASCADE`. Un rebuild **empieza**
borrando los turnos. Resultado: `DELETE FROM utterances` se llevaba decisiones y acciones enteras,
sin error, sin aviso, sin filas de vuelta.

```mermaid
flowchart TB
    subgraph antes["Antes"]
        direction TB
        A1["DELETE FROM utterances"]
        A2["action_items<br/>id, statement, state='done', owner='Ada'"]
        A3["0 filas, sin error"]
        A4["se perdio el 'hecha'<br/>y se perdio la accion entera"]
        A1 -->|"cascade por la cita"| A2
        A2 --> A3
        A3 --> A4
    end
    subgraph ahora["Ahora"]
        direction TB
        B1["DELETE FROM utterances"]
        B2["error: FOREIGN KEY constraint failed"]
        B3["el rebuild borra en orden:<br/>action_items, decisions, summaries, utterances"]
        B4["action_item_progress<br/>extraction_run_id + ordinal, state, owner"]
        B5["reproyecta y vuelve a unir"]
        B1 --> B2
        B3 -.->|"no la toca"| B4
        B3 --> B5
        B4 --> B5
    end
```

### Por qué la clave no es el id de la acción

Ésta es la parte que decide todo lo demás. Un rebuild borra las filas y las vuelve a insertar, y
las filas nuevas tienen **ids nuevos**. Si `action_item_progress` apuntara a `action_items.id`, la
fila humana quedaría huérfana en cuanto se reproyecta — y si esa referencia fuera una foreign key
de verdad, se borraría con cascade, que es exactamente la pérdida de datos original con un
constraint arriba.

La clave tiene que ser algo que la reproyección **reproduzca igual**. Lo único que cumple eso es de
dónde salió la acción y en qué posición venía dentro de esa extracción:

```
PRIMARY KEY (extraction_run_id, ordinal)
```

El `extraction_run_id` es fuente: la extracción aceptada no se reescribe nunca. El `ordinal` es la
posición en el JSON de esa extracción, así que leer el mismo archivo dos veces da el mismo número.
En `action_items` ese par es UNIQUE, porque si dos acciones compartieran posición el estado de una
persona quedaría ambiguo, que es peor que quedar mal.

Lo que **no** hace esta clave: sobrevivir a una re-extracción. Si el LLM corre de nuevo, sale un
`extraction_run_id` nuevo y sus acciones arrancan abiertas — mientras las viejas siguen ahí con su
estado, porque una extracción nunca edita a la anterior. Decidir que la acción nueva "es la misma"
que la vieja es trabajo de una persona al aceptar, no de una regla que compare textos, y eso quedó
como tarea de Fase 5.

### Por qué la cita tampoco apunta al id del turno

Es la misma trampa un piso más abajo. Los ids de los turnos también los reparte la proyección, y el
JSON de la extracción se guardaba el `utterance_id` adentro. Un rebuild borra los turnos y los
reinserta con ids nuevos, así que al reproyectar las decisiones y las acciones desde ese mismo JSON
la cita apuntaba a un turno que ya no existía y el insert fallaba. No era un caso de borde: pasaba
en todos los rebuilds.

La cita ancla ahora sobre el par que la proyección sí reproduce:

```
FOREIGN KEY (meeting_id, utterance_ordinal) REFERENCES utterances (meeting_id, ordinal)
```

El `meeting_id` no se guarda dos veces: es la columna que la decisión o la acción ya tenía, y la
cita la comparte. Por eso no hay manera de citar un turno de otra reunión — no hay dónde escribir
el otro id.

Un id determinista derivado de ese mismo par también hubiera funcionado, y hubiera dejado el
esquema intacto. Se descartó porque promete otra cosa: dice "identidad del turno" cuando significa
"reunión y posición", y obliga a mantener para siempre una función de derivación que, si cambia,
rompe en silencio toda extracción ya guardada. El par lo dice, y el JSON queda legible al lado de
`utterances.jsonl`.

**Lo que costó:** `utterances` tiene que declarar ese par UNIQUE, porque SQLite no acepta una
foreign key contra columnas que no lo sean. SQLite no agrega constraints en el lugar, así que EF
rehace la tabla entera — y con ella se van los triggers FTS5, que cuelgan de la tabla y no del
esquema que el modelo ve, y los rowids se renumeran, que es sobre lo que `utterances_fts` indexa.
Van dos migraciones y no una: EF emite el SQL suelto **antes** del rebuild que tiene pendiente, así
que los triggers se recrean en la siguiente. Lo cazaron los tests de búsqueda que ya estaban.

### Por qué la cita ahora es NO ACTION y no RESTRICT

Los dos fallan al borrar turnos sueltos, que es lo que quiero. La diferencia es cuándo se chequean:

- **RESTRICT** salta apenas se toca la fila padre. Borrar una reunión dispara cascades a
  `utterances` y a `action_items` a la vez, y SQLite no promete en qué orden — si los turnos caen
  primero, RESTRICT aborta el borrado de la reunión entera.
- **NO ACTION** se chequea al final de la sentencia. Para entonces los dos cascades ya corrieron y
  no queda ninguna acción citando un turno muerto, así que la reunión se borra limpia. Y un
  `DELETE FROM utterances` a secas sí falla, porque ahí las acciones siguen en pie.

Hay un test por cada mitad de eso, justamente porque son la misma decisión mirada desde los dos
lados.
