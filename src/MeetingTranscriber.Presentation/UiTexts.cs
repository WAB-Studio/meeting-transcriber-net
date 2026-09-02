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

    // These three are said before the machine's own words go in the report and never instead of
    // them: what comes back off an exception is English either way, and a report that opened with
    // it would be this application talking in a language nobody chose. Dump's remark on the
    // recording screen is where that rule is written down. One per question rather than one for
    // all of them, because the sentence a person reads has to be about what was asked — a line
    // about devices over an answer about programs is a report that misreports.
    public static UiText WindowsDidNotSayWhatMicrophonesThereAre { get; } = new(
        "Windows no dijo qué micrófonos hay en esta máquina.",
        "Windows did not say what microphones this machine has.");

    public static UiText WindowsDidNotSayWhatIsPlaying { get; } = new(
        "Windows no dijo qué programas están sonando.",
        "Windows did not say which programs are playing.");

    // What is lost is only that the list stops keeping up on its own, so it says exactly that and
    // does not read as a machine with no microphone: everything already on screen still records.
    public static UiText WindowsWillNotSayWhenTheDevicesChange { get; } = new(
        "Windows no avisa cuando cambian los dispositivos: la lista de micrófonos queda como "
        + "está hasta que se vuelva a abrir la aplicación.",
        "Windows will not say when the devices change: the list of microphones stays as it is "
        + "until the application is opened again.");

    // Said out loud because the picker emptying itself is the sort of change somebody notices
    // afterwards. The recording is not startable until another one is picked, which is the point.
    public static UiText TheMicrophoneChosenIsNoLongerThere { get; } = new(
        "El micrófono elegido ya no está en esta máquina.",
        "The microphone that was chosen is no longer on this machine.");

    public static UiText TheWholeMachineCouldNotBeRecorded { get; } = new(
        "No se pudo pasar a grabar toda la máquina.",
        "Recording the whole machine could not be taken up.");

    public static UiText WhatToRecordFromThisMachine { get; } =
        new("Qué grabar de esta máquina", "What to record from this machine");

    public static UiText EverythingThisMachinePlays { get; } = new(
        "Todo lo que suena en esta máquina",
        "Everything this machine plays");

    // One press, both pickers. The microphones keep up on their own, so this is what a session
    // where Windows refused to say when devices change has instead — and it is the only thing that
    // ever re-reads the programs, since nothing tells an application that a meeting was just
    // started in a browser tab.
    public static UiText RefreshTheList { get; } =
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

    // ── Saving the meeting, which is a state of the screen and not a moment ──────
    //
    // The heading the recorder half takes while a meeting is being saved, and one line per step
    // that save is going to run. What decides which of them are on screen is not here: a step is
    // shown because the save runs it, so this file holds words for steps and never the list.

    public static UiText SavingTheMeeting { get; } =
        new("Guardando la reunión", "Saving the meeting");

    public static UiText LettingBothSourcesGo { get; } = new(
        "Soltando las dos fuentes",
        "Letting both sources go");

    public static UiText SavingTheAudioOfBothChannels { get; } = new(
        "Guardando el audio de los dos canales",
        "Saving the audio of both channels");

    // The two marks beside a step. They are what a narrator reads out where somebody looking sees
    // a tick or a ring, so they are texts and not decoration; a step still to come carries neither,
    // because there is nothing yet to say about it.
    public static UiText ThisStepIsDone { get; } = new("listo", "done");

    public static UiText ThisStepIsUnderWay { get; } = new("en curso", "under way");

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

    // ── The clock, while a meeting is being recorded ─────────────────────────────

    /// <summary>
    /// What the biggest number on the screen is a number of. The digits themselves carry no entry
    /// of this catalogue — a length reads the same in every language — so this is the whole of
    /// what a narrator has to go on, and it says the measurement rather than the control: somebody
    /// hearing "clock" would look for a time of day.
    /// </summary>
    public static UiText HowLongTheMeetingHasBeenRunning { get; } = new(
        "Hace cuánto que se está grabando",
        "How long the meeting has been running");

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

    /// <summary>
    /// What a channel that changed device mid meeting says, naming the device it moved to.
    /// </summary>
    /// <remarks>
    /// One entry for either channel and for either way a channel moves, because what a person has
    /// to be told is the same in all of them: this channel is no longer on what the recording
    /// started on, the recording did not stop, and here is what it is on now. It says nothing about
    /// why, deliberately — somebody choosing the whole machine's audio and Windows taking a
    /// microphone away are the same news to whoever is in the meeting, and a sentence that named
    /// the cause would be two sentences one of which is usually wrong.
    /// <para>
    /// The two names go in as values, which is what keeps a device's name — a name this machine
    /// gave, and the same in every language — out of the catalogue.
    /// </para>
    /// </remarks>
    public static UiText TheChannelMovedToAnotherDevice { get; } = new(
        "Este canal ya no graba «{0}»: desde que cambió graba «{1}». La grabación no se cortó, y "
        + "lo que haya pasado entre los dos queda dicho como el hueco que fue.",
        "This channel is no longer recording ‘{0}’: since it changed it is recording "
        + "‘{1}’. The recording did not stop, and whatever happened between the two is said "
        + "as the gap it was.");

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

    public static UiText NoMeetingsHereYet { get; } = new(
        "Todavía no hay ninguna reunión en este corpus.",
        "There is no meeting in this corpus yet.");

    public static UiText SomeAreWaitingToBeTold { get; } = new(
        "{0} esperan una respuesta.",
        "{0} are waiting to be told.");

    // ISC-165.1. Two words and no more, because there is nothing here to say: the application has
    // not thought of a name and is not pretending to. What it must never read as is a name — a
    // date, a folder, the first thing said in the meeting — since a person scanning this list
    // cannot tell a title they wrote from one that was made up for them.
    public static UiText AMeetingNobodyHasNamed { get; } = new("Sin nombre", "Unnamed");

    // The drawer's one press, which always offers the position it is not in.
    public static UiText OpenTheMeetingsWhole { get; } =
        new("Abrir la lista entera", "Open the whole list");

    public static UiText BringTheMeetingsBackDown { get; } =
        new("Volver a bajar la lista", "Bring the list back down");

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

    // Not a stage: it is the one meeting the recorder above is saving right now, said on its row
    // so the list and the half above it agree about what is happening to it. Every other meeting
    // on the list reads its stage out of the corpus, which is where this one will read from too
    // the moment the save is over.
    public static UiText ThisOneIsBeingSaved { get; } = new(
        "Guardándose ahora.",
        "Being saved right now.");

    // ── A recording the application never finished ────────────────────────────────────────────

    // These stand in for the stage line above rather than sitting beside it. A recording still
    // waiting in the corpus is exactly the meeting NoAudioYet is about, and the list can now say
    // which of that line's two halves is true of it instead of offering somebody both.

    public static UiText ItIsBeingRecordedRightNow { get; } = new(
        "Se está grabando ahora.",
        "It is being recorded right now.");

    // What was observed before what it means, which is what anything on the attention tint owes
    // the reader. The blocks are whole up to the packet the machine died in, and saying so is what
    // keeps this from reading as a recording that broke.
    public static UiText TheApplicationClosedInTheMiddleOfThisOne { get; } = new(
        "La aplicación se cerró en el medio de esta grabación. El audio está entero hasta ahí.",
        "The application closed in the middle of this recording. The audio is whole up to there.");

    // The reason goes on the line as the machine's own words, for the reason ThatDidNotGoThrough
    // gives: what comes back off the engine is English either way, and a sentence built around it
    // would be half-translated for good.
    public static UiText ThisCannotBecomeAMeeting { get; } = new(
        "No puede volverse una reunión: {0}",
        "This cannot become a meeting: {0}");

    // Its own sentence and not ThisCannotBecomeAMeeting's, although the two rows offer the same
    // one answer. That one says the corpus and the folder disagree about a recording; this says
    // the blocks themselves would not come back, which is what somebody would otherwise read as
    // the application having nothing to say about a recording it plainly has. No machine message
    // rides on it: what a torn spool throws names a file and an offset, and the answer this row
    // offers is the same whichever offset it was.
    public static UiText TheBlocksOfThisOneWouldNotRead { get; } = new(
        "No se pudieron leer los bloques de esta grabación, así que no se puede decir cuánto duró.",
        "The blocks of this recording would not read, so how long it is cannot be said.");

    // The two answers, and there are two. Taking the audio out to a folder is a copy and not an
    // answer — the recording is still waiting afterwards — so it is not a button on this row.
    public static UiText Keep { get; } = new("Conservar", "Keep");

    public static UiText Discard { get; } = new("Descartar", "Discard");

    // Keeping a recording pours its blocks onto a timeline and hashes the result, which for a long
    // meeting is minutes. Said while it runs, because a press that goes quiet for minutes is one
    // somebody presses again.
    public static UiText TheRecordingIsBeingKept { get; } = new(
        "Conservando la grabación. Puede tardar unos minutos.",
        "Keeping the recording. It can take a few minutes.");

    public static UiText ItIsAMeetingNow { get; } =
        new("Listo: quedó como reunión.", "Done: it is a meeting now.");

    public static UiText TheRecordingIsGone { get; } =
        new("Listo: la grabación se borró.", "Done: the recording is gone.");

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
