namespace MeetingTranscriber.Presentation;

/// <summary>
/// Everything the application says, Spanish first and English second. A screen names an entry
/// here; it never carries the words itself, which is what makes translating a screen a matter of
/// reading one file rather than of finding every literal in it.
/// </summary>
/// <remarks>
/// A catalogue in C# rather than in `.resw`: a name that does not exist is a compile error
/// instead of a blank on screen, both languages sit on the same line so one cannot quietly be
/// added without the other, and none of it needs a packaged host to be read — which is what lets
/// a plain test walk the whole catalogue. The section below is the temporary scaffold's, and it
/// goes when the scaffold does.
/// </remarks>
public static class UiTexts
{
    // ── The recording screen ─────────────────────────────────────────────────────

    public static UiText RecordAMeeting { get; } = new("Grabar una reunión", "Record a meeting");

    public static UiText Microphone { get; } = new("Micrófono", "Microphone");

    public static UiText NoMicrophoneOnThisMachine { get; } = new(
        "Esta máquina no tiene ningún micrófono.",
        "This machine has no microphone.");

    public static UiText WhatToRecordFromThisMachine { get; } =
        new("Qué grabar de esta máquina", "What to record from this machine");

    public static UiText EverythingThisMachinePlays { get; } = new(
        "Todo lo que suena en esta máquina",
        "Everything this machine plays");

    public static UiText RefreshThePrograms { get; } =
        new("Actualizar la lista", "Refresh the list");

    // Its own question, and the caption is not decoration: this screen has two language pickers on
    // it and they answer different things. A meeting filed in the language of the menu somebody
    // happens to read is a meeting transcribed in the wrong one.
    public static UiText WhatWillBeSpoken { get; } =
        new("Idioma de la reunión", "The meeting's language");

    public static UiText WhatWillBeSpokenIsAskedEveryTime { get; } = new(
        "Se pregunta por reunión. El idioma en el que se lee la aplicación no dice en qué se va a "
        + "hablar.",
        "Asked for every meeting. The language the application is read in does not say what will "
        + "be spoken in it.");

    public static UiText Record { get; } = new("Grabar", "Record");

    public static UiText Pause { get; } = new("Pausar", "Pause");

    public static UiText Resume { get; } = new("Seguir", "Carry on");

    public static UiText Stop { get; } = new("Detener", "Stop");

    public static UiText RecordTheWholeMachine { get; } =
        new("Grabar toda la máquina", "Record the whole machine");

    public static UiText NothingCameFromThatProgram { get; } = new(
        "No llegó nada de ese programa. Grabar toda la máquina en su lugar mete las "
        + "notificaciones y todas las demás aplicaciones en la grabación. La reunión sigue "
        + "corriendo de cualquier manera.",
        "Nothing at all has come from that program. Recording the whole machine instead puts "
        + "notifications and every other application in the recording. The meeting keeps running "
        + "either way.");

    public static UiText NowRecordingTheWholeMachine { get; } = new(
        "Canal 0: todo lo que suena en esta máquina.",
        "Channel 0: everything this machine plays.");

    public static UiText ReadyToRecord { get; } = new(
        "Elegí el micrófono, qué grabar de esta máquina y en qué idioma se va a hablar.",
        "Choose the microphone, what to record from this machine, and what will be spoken.");

    public static UiText RecordingMeeting { get; } =
        new("Grabando la reunión {0}.", "Recording meeting {0}.");

    public static UiText PausedAndTheClockKeepsRunning { get; } = new(
        "En pausa. El reloj de la reunión sigue corriendo, así que la pausa queda adentro como el "
        + "silencio que fue.",
        "Paused. The meeting's clock keeps running, so the pause stays in it as the silence it "
        + "was.");

    public static UiText OpeningTheDevices { get; } = new(
        "Abriendo el micrófono y el canal 0.",
        "Opening the microphone and channel 0.");

    public static UiText ThatProgramIsNoLongerRunning { get; } = new(
        "Ese programa ya no está corriendo, así que no se empezó a grabar: elegí otra vez qué "
        + "grabar de esta máquina. Su número de proceso puede ser de otra aplicación ahora.",
        "That program is no longer running, so nothing was started: choose again what to record "
        + "from this machine. Its process number may belong to another application by now.");

    public static UiText MakingTheMeeting { get; } = new(
        "Deteniendo. La reunión se está armando con lo que se grabó, y para una reunión larga eso "
        + "tarda unos minutos.",
        "Stopping. The meeting is being made out of what was recorded, and for a long meeting that "
        + "takes some minutes.");

    public static UiText TheMeetingIsRecorded { get; } = new(
        "Reunión {0} grabada: {1} de audio en {2}.",
        "Meeting {0} recorded: {1} of audio at {2}.");

    // Said out loud every time, because it is the promise and not an omission.
    public static UiText NothingWasQueued { get; } = new(
        "No se puso nada en cola: transcribir es otro botón.",
        "Nothing was queued: transcribing is a separate press.");

    public static UiText TheRecordingCouldNotStart { get; } =
        new("No se pudo empezar a grabar.", "The recording could not be started.");

    public static UiText TheMeetingCouldNotBeMade { get; } = new(
        "La grabación terminó, pero la reunión no se pudo armar. Lo grabado sigue en su carpeta.",
        "The recording ended, but the meeting could not be made. What was recorded is still in its "
        + "folder.");

    // ── The meters, while a meeting is being recorded ────────────────────────────

    // The channel number is in the name a person reads, and not decoration. It is the number the
    // provider reports back and the number a citation is anchored by, so somebody looking at a
    // transcript later and somebody watching this meter are reading the same two numbers.
    public static UiText Channel0TheOthers { get; } =
        new("Canal 0 · los demás", "Channel 0 · the others");

    public static UiText Channel1Me { get; } = new("Canal 1 · yo", "Channel 1 · me");

    /// <summary>
    /// A channel that brought back nothing at all in the second just read. Said in words beside
    /// the bar rather than left to an empty bar: an empty bar and a bar nobody has drawn yet look
    /// the same, and the case this exists for — a microphone muted in Windows — never moves it.
    /// </summary>
    public static UiText NothingIsArriving { get; } = new("nada", "nothing");

    /// <summary>
    /// ISC-150. What it says about itself matters as much as what it says: somebody who reads it
    /// as a measurement of their echo will go looking for one, and there is none — this is what
    /// kind of device Windows says the meeting is being played through, and nothing more.
    /// </summary>
    public static UiText TheOthersAreHeardTwice { get; } = new(
        "Estás escuchando la reunión por parlantes, así que el micrófono capta a los demás una "
        + "segunda vez. Lo dice el tipo de dispositivo de reproducción y no una medición del eco. "
        + "Con auriculares no pasa.",
        "You are listening to this meeting through speakers, so the microphone is picking the "
        + "other side up a second time. That is what kind of playback device this is, not a "
        + "measurement of the echo. A headset avoids it.");

    /// <summary>
    /// One channel's device gone while the meeting carries on. Two entries and not one with the
    /// channel as a value: what each of them costs is different, and a person deciding what to do
    /// needs to be told which half of the conversation they still have.
    /// </summary>
    /// <remarks>
    /// Neither says to press stop, and that is not an omission. Stopping a meeting one of whose
    /// sources ended by itself does not make a meeting — the source says so as it is let go of, and
    /// the recording comes back refused — so what is on disk is dealt with afterwards rather than
    /// by a press. Saying "stop and keep what it has" would be this screen promising an outcome the
    /// press does not produce, which is worse than saying nothing.
    /// </remarks>
    public static UiText TheOthersChannelStoppedOnItsOwn { get; } = new(
        "El canal 0 dejó de grabar solo: se desconectó el dispositivo o Windows lo cerró. Lo que "
        + "digan los demás desde acá no queda en la grabación; el micrófono sigue grabando. Lo que "
        + "ya se grabó está en su carpeta y no se pierde.",
        "Channel 0 stopped recording on its own — the device was unplugged, or Windows closed it. "
        + "Nothing the other side says from here on is being recorded; the microphone still is. "
        + "What was already recorded is in its folder and is not lost.");

    public static UiText TheMicrophoneChannelStoppedOnItsOwn { get; } = new(
        "El canal 1 dejó de grabar solo: se desconectó el micrófono o Windows lo cerró. Lo que "
        + "digas desde acá no queda en la grabación; el canal 0 sigue grabando. Lo que ya se grabó "
        + "está en su carpeta y no se pierde.",
        "Channel 1 stopped recording on its own — the microphone was unplugged, or Windows closed "
        + "it. Nothing you say from here on is being recorded; channel 0 still is. What was "
        + "already recorded is in its folder and is not lost.");

    public static UiText MeetingsAreKeptAt { get; } =
        new("Las reuniones se guardan en {0}", "Meetings are kept at {0}");

    /// <summary>
    /// Where the meetings will go, when there is nothing there yet. One entry and not
    /// <see cref="MeetingsAreKeptAt"/> with this stuck on the end of it: a screen that joined two
    /// entries would be choosing the punctuation between them, which is a word of its own in a
    /// language it picked.
    /// </summary>
    public static UiText TheFirstRecordingMakesTheCorpusAt { get; } = new(
        "Las reuniones se guardan en {0}. Todavía no hay un corpus ahí: la primera grabación lo crea.",
        "Meetings are kept at {0}. There is no corpus there yet: the first recording makes one.");

    public static UiText TheSettingSaysNothingUsable { get; } = new(
        "El archivo que dice dónde está el corpus no dice nada que se pueda usar: {0}. No se graba "
        + "hasta que eso se resuelva, para no arrancar un segundo corpus vacío en otro lado.",
        "The file that says where the corpus is says nothing that can be used: {0}. Nothing is "
        + "recorded until that is settled, rather than starting a second, empty corpus somewhere "
        + "else.");

    public static UiText TheCorpusFolderDidNotAnswer { get; } = new(
        "La carpeta {0} no responde: no está, o este usuario no puede leerla.",
        "The folder {0} does not answer: it is not there, or this user may not read it.");

    public static UiText ThereIsNoCorpusInThatFolder { get; } = new(
        "En {0} no hay ningún corpus. No se crea uno nuevo ahí: lo habitual es que esa ruta ya no "
        + "llegue al corpus al que llegaba.",
        "There is no corpus in {0}. One is not made there: the usual cause is a path that no "
        + "longer reaches the corpus it used to.");

    public static UiText TheCorpusFolderGoesWhenThePackageDoes { get; } = new(
        "{0} se borra cuando se desinstala la aplicación, y ahí adentro quedarían las respuestas "
        + "que ya se pagaron.",
        "{0} goes when the application is uninstalled, and the responses already paid for would go "
        + "with it.");

    public static UiText ChoosingAnotherFolderIsNotHereYet { get; } = new(
        "Elegir otra carpeta todavía no se hace desde esta pantalla.",
        "Choosing another folder is not done from this screen yet.");

    // ── What the application owes each meeting ────────────────────────────────────────────────

    public static UiText Meetings { get; } = new("Reuniones", "Meetings");

    public static UiText OneCardPerMeeting { get; } = new(
        "Una ficha por reunión: la etapa en la que está y lo que falta hacerle.",
        "One card per meeting: the stage it is at and what is left to do to it.");

    public static UiText NoMeetingsHereYet { get; } = new(
        "Todavía no hay ninguna reunión en este corpus.",
        "There is no meeting in this corpus yet.");

    public static UiText SomeAreWaitingToBeTold { get; } = new(
        "{0} esperan una respuesta.",
        "{0} are waiting to be told.");

    public static UiText AMeetingWithoutATitle { get; } =
        new("Reunión sin título", "Untitled meeting");

    public static UiText ReadTheMeetingsAgain { get; } = new("Actualizar", "Refresh");

    // The rendered files are the one thing that will never be a press, so the screen says why
    // rather than leaving their absence looking like a button somebody lost.
    public static UiText TheRenderedFilesAreNeverAskedAbout { get; } = new(
        "Los archivos del transcript nunca se preguntan: no cuestan nada y se pueden volver a "
        + "producir, así que no son un botón.",
        "The transcript's files are never something you are asked about: they cost nothing and "
        + "can be produced again, so they are not a button.");

    // What a meeting has got to.

    public static UiText NoAudioYet { get; } = new(
        "Todavía no hay audio: se está grabando, o la grabación no llegó a terminar.",
        "No audio yet: it is being recorded, or its recording never finished.");

    public static UiText Recorded { get; } = new("Grabada.", "Recorded.");

    public static UiText Transcribed { get; } = new("Transcrita.", "Transcribed.");

    public static UiText Summarised { get; } = new(
        "Resumida. La aplicación no le debe nada más.",
        "Summarised. The application owes it nothing more.");

    // What is happening about the part it has not got to.

    public static UiText WaitingToBeTold { get; } =
        new("Esperando una respuesta.", "Waiting to be told.");

    public static UiText AlreadyInTheQueue { get; } = new(
        "Ya está en cola. Todavía no corrió nada, así que ignorarla la saca.",
        "Already queued. Nothing has run yet, so ignoring it takes it back out.");

    public static UiText StoppedWaitingForAPerson { get; } = new(
        "Detenida esperando a una persona: puede haber un cobro que ya ocurrió, así que no se "
        + "reintenta sola.",
        "Stopped waiting for a person: there may be a charge that already happened, so nothing "
        + "retries it on its own.");

    public static UiText IgnoredForNow { get; } = new(
        "Ignorada por ahora. Se puede pedir cuando quieras.",
        "Ignored for now. It can be asked for whenever you like.");

    // The two answers, and what comes back of them.

    public static UiText Transcribe { get; } = new("Transcribir", "Transcribe");

    public static UiText Summarise { get; } = new("Resumir", "Summarise");

    public static UiText Ignore { get; } = new("Ignorar", "Ignore");

    public static UiText ItIsInTheQueueNow { get; } =
        new("Listo: quedó en cola.", "Done: it is in the queue.");

    public static UiText ItIsIgnoredForNow { get; } =
        new("Listo: ignorada por ahora.", "Done: ignored for now.");

    public static UiText ThatDidNotGoThrough { get; } =
        new("No se pudo: {0}", "That did not go through: {0}");

    // Not a failure. It is what the re-read before every write is for: the screen was drawn
    // before somebody answered the same question somewhere else, and the answer on disk won.
    public static UiText ThatIsNoLongerHowItWas { get; } = new(
        "Eso ya no está como estaba. La lista se volvió a leer.",
        "That is no longer as it was. The list has been read again.");

    // The corpus is not reachable, so an empty list would be a lie. Which refusal it was is the
    // recording screen's to say, and it has the whole table for it.
    public static UiText TheCorpusCouldNotBeOpened { get; } = new(
        "No se pudo abrir el corpus, así que esta lista no dice nada. La pantalla de grabación "
        + "dice por qué: {0}",
        "The corpus could not be opened, so this list says nothing. The recording screen says "
        + "why: {0}");

    // ── The packaging checks scaffold ──────────────────────────────────────────────────────────

    public static UiText PackagingChecks { get; } =
        new("Comprobaciones de empaquetado", "Packaging checks");

    public static UiText PackagingChecksAreScaffolding { get; } = new(
        "Andamio temporal: se borra cuando estas dos comprobaciones dejen de hacer falta.",
        "Temporary scaffold: it goes when these two checks stop being needed.");

    public static UiText Language { get; } = new("Idioma", "Language");

    // The two entries below say the same thing in both languages, and that is the claim rather
    // than a translation nobody got to. Somebody who opened the application in a language they
    // cannot read is looking for the one word on screen they do recognise, so a picker that
    // translated the names would hide the way back out. Here rather than in a `switch` so the
    // walk over the catalogue sees them like every other word a person reads.
    //
    // The recording screen names what will be spoken in a meeting with these same two, and that
    // is not reuse for its own sake: a language read as its own name is the one spelling nobody
    // has to translate back, which is the same reason as above arriving at the same answer. What
    // tells the two pickers apart on that screen is their headers, which do translate.
    public static UiText SpanishName { get; } = new("Español", "Español");

    public static UiText EnglishName { get; } = new("English", "English");

    public static UiText Yes { get; } = new("sí", "yes");

    public static UiText No { get; } = new("no", "no");

    // No room for the exception: what a runtime throws is written in whatever language Windows
    // is installed in, and putting it inside the sentence would make the sentence half-translated
    // for good. It goes on the line below as the data it is.
    public static UiText Failed { get; } = new("FALLÓ:", "FAILED:");

    public static UiText LanguageNotRemembered { get; } = new(
        "El idioma cambió, pero no se pudo recordar la elección: la próxima vez la aplicación "
        + "abrirá en el idioma de Windows.",
        "The language changed, but the choice could not be remembered: next time the application "
        + "will open in Windows' language.");

    public static UiText Package { get; } = new("Paquete: {0}", "Package: {0}");

    public static UiText InstalledAt { get; } = new("Instalado en: {0}", "Installed at: {0}");

    public static UiText Process { get; } = new("Proceso: {0}", "Process: {0}");

    public static UiText SaveReport { get; } = new("Guardar informe", "Save report");

    public static UiText ReportSavedAt { get; } = new("Informe en {0}", "Report at {0}");

    public static UiText ReportNotSaved { get; } =
        new("No se pudo guardar: {0}", "Could not save: {0}");

    // ── Check 1: what a child process inherits ────────────────────────────────────────────────

    public static UiText ChildEnvironmentCheck { get; } =
        new("1 · Entorno del proceso hijo", "1 · Child process environment");

    public static UiText ChildEnvironmentHeading { get; } =
        new("1 · ENTORNO DEL PROCESO HIJO", "1 · CHILD PROCESS ENVIRONMENT");

    public static UiText VariablesInTheApp { get; } =
        new("Variables en la app: {0}", "Variables in the app: {0}");

    public static UiText VariablesInTheChild { get; } =
        new("Variables en el hijo: {0}", "Variables in the child: {0}");

    public static UiText OnlyInTheChild { get; } =
        new("Sólo en el hijo ({0}):", "Only in the child ({0}):");

    public static UiText OnlyInTheApp { get; } =
        new("Sólo en la app ({0}):", "Only in the app ({0}):");

    public static UiText TheChildEnvironmentInFull { get; } =
        new("Entorno completo del hijo:", "The child's environment in full:");

    public static UiText ChildWriteToLocalAppData { get; } = new(
        "Escritura del hijo en %LOCALAPPDATA%:",
        "The child's write to %LOCALAPPDATA%:");

    public static UiText StampWritten { get; } = new("  sello escrito: {0}", "  stamp written: {0}");

    public static UiText InTheContainer { get; } =
        new("  en el contenedor: {0}  {1}", "  in the container: {0}  {1}");

    public static UiText ChildInheritsTheRedirection { get; } = new(
        "  => el hijo HEREDA la redirección: su %LOCALAPPDATA% no es el del usuario.",
        "  => the child INHERITS the redirection: its %LOCALAPPDATA% is not the user's.");

    public static UiText ChildWritesOutsideTheContainer { get; } = new(
        "  => el hijo escribe FUERA del contenedor. Confirmar a mano en {0}",
        "  => the child writes OUTSIDE the container. Confirm by hand at {0}");

    public static UiText CompareTheEnvironmentByHand { get; } = new(
        "PASO MANUAL: correr `cmd /c set` desde el Explorador y comparar contra el bloque de "
        + "arriba. La diferencia es lo que inyecta o tapa el contenedor.",
        "BY HAND: run `cmd /c set` from Explorer and compare it against the block above. The "
        + "difference is what the container injects or hides.");

    public static UiText ChildEnvironmentCheckDone { get; } =
        new("Comprobación 1 lista.", "Check 1 done.");

    public static UiText ChildEnvironmentCheckFailed { get; } =
        new("La comprobación 1 falló.", "Check 1 failed.");

    // ── Check 2: where a write actually lands ─────────────────────────────────────────────────

    public static UiText WritePathCheck { get; } = new("2 · Ruta de escritura", "2 · Write path");

    public static UiText WritePathHeading { get; } = new("2 · RUTA DE ESCRITURA", "2 · WRITE PATH");

    public static UiText NoFolderWasChosen { get; } =
        new("Cancelado: no se eligió carpeta.", "Cancelled: no folder was chosen.");

    public static UiText ChosenPath { get; } = new("Ruta elegida: {0}", "Chosen path: {0}");

    public static UiText ExistsThere { get; } = new("Existe ahí: {0}", "Exists there: {0}");

    public static UiText ContentsReadBack { get; } =
        new("Contenido leído: {0}", "Contents read back: {0}");

    public static UiText ContentsMatch { get; } = new("Coincide: {0}", "Matches: {0}");

    public static UiText LocalAppDataResolvedTo { get; } =
        new("LOCALAPPDATA resuelto a: {0}", "LOCALAPPDATA resolved to: {0}");

    public static UiText WrittenAt { get; } = new("Escrito en: {0}", "Written at: {0}");

    public static UiText PackageRoot { get; } = new("Raíz del paquete: {0}", "Package root: {0}");

    public static UiText PackageRootExists { get; } =
        new("Existe la raíz: {0}", "The root exists: {0}");

    public static UiText RedirectedCopy { get; } =
        new("  copia redirigida: {0}", "  redirected copy: {0}");

    public static UiText CheckTheWritePathByHand { get; } = new(
        "PASO MANUAL: abrir {0} en el Explorador y confirmar que {1} está ahí de verdad y no "
        + "sólo desde adentro de la app.",
        "BY HAND: open {0} in Explorer and confirm {1} is really there and not only from inside "
        + "the app.");

    public static UiText WritePathCheckDone { get; } =
        new("Comprobación 2 lista.", "Check 2 done.");

    public static UiText WritePathCheckCancelled { get; } =
        new("Comprobación 2 cancelada.", "Check 2 cancelled.");

    public static UiText WritePathCheckFailed { get; } =
        new("La comprobación 2 falló.", "Check 2 failed.");
}
