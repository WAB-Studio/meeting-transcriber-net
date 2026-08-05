# meeting-transcriber-net

Aplicación Windows nativa que graba reuniones, las transcribe con Deepgram y las convierte
en conocimiento local consultable. Todo el corpus vive en el equipo del usuario: SQLite para
lo consultable, filesystem para audio y artefactos. El diseño completo está en
[`arquitectura.md`](arquitectura.md).

## El contrato

Estas tres reglas las asume todo el resto del sistema. Romper cualquiera corrompe reuniones
ya grabadas y artefactos ya pagados, así que no se negocian por conveniencia de un módulo.

### Canal 0 es la reunión, canal 1 sos vos

En cualquier audio capturado por la aplicación:

```text
canal 0 = otros   el proceso seleccionado, o loopback completo como fallback
canal 1 = yo      el micrófono seleccionado
```

El número **es** el contrato: es el orden de interleaving que se escribe en el WAV y el
índice de canal que Deepgram devuelve en la respuesta. Invertirlo pone las palabras de una
persona en boca de otra, y las citas de los summaries apuntan a evidencia falsa.

Nadie lee un índice de canal a mano. `CapturedAudio` es el único lugar que traduce
`AudioChannel` a una posición; el resto pide el canal por nombre.

### Dos perfiles de fuente, y cada uno tiene su número de canales

```text
multichannel   audio capturado por la app       2 canales   speakers deterministas
diarize        archivo importado de una pista   1 canal     labels hasta que una persona asigne
```

Un perfil que no coincide con el número de canales de su audio es un error, no algo a
adivinar: `SourceProfile.EnsureChannelCount` corta. Los nombres en minúscula son la forma
persistida y la que se manda al proveedor, no un detalle de presentación.

### Los timestamps son UTC y las duraciones son milisegundos enteros

Hay dos tipos y no se mezclan con `DateTime` ni `TimeSpan` sueltos:

- `UtcTimestamp` — un instante, siempre UTC y siempre al milisegundo. Un `DateTime` local se
  convierte; uno con `Kind` sin especificar se rechaza, porque no nombra ningún instante y
  adivinarle uno es como una grabación termina con horas de diferencia.
- `Duration` — una longitud en milisegundos enteros, nunca negativa. Es también cómo se
  expresa un offset dentro de una timeline: la distancia desde su origen.

Dónde vive todo esto:

```text
src/MeetingTranscriber.Domain/
  Audio/  AudioChannel · SourceProfile · SourceProfiles · CapturedAudio
  Time/   UtcTimestamp · Duration
```

Los tests de `tests/MeetingTranscriber.Domain.Tests/` fallan si se invierte el orden de
canales o si un perfil no coincide con su número de canales.

## Desarrollo

Windows nativo. WinUI 3, el Windows App SDK y WASAPI no compilan ni corren en WSL, y el repo
no se clona en `\\wsl$\`: sobre el filesystem cruzado MSBuild va lento y Hot Reload no
detecta cambios.

Hace falta Visual Studio 2026 Community con los workloads **Desarrollo de escritorio de .NET**
y **Desarrollo de aplicaciones de Windows**; el SDK de .NET 10 LTS viene incluido.

Lo mismo que corre el CI:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
```

Los warnings frenan en CI, no en la máquina de desarrollo.
