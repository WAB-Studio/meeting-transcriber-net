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
