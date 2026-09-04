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
    // ── The application ──────────────────────────────────────────────────────────

    // What the window says it is, in the row every artboard opens with. The same either way
    // because it is the product's name and a product is not translated — and it is a placeholder:
    // `docs/design.md` §The artboards and this page says the name and the mark are the one thing on
    // those drawings that is deliberately unfinished.
    public static UiText TheApplicationsName { get; } =
        new("Meeting Transcriber", "Meeting Transcriber");

    // ── The recording screen ─────────────────────────────────────────────────────

    public static UiText RecordAMeeting { get; } = new("Grabar una reunión", "Record a meeting");

    public static UiText Microphone { get; } = new("Micrófono", "Microphone");

    // The whole line and not the parenthesis on its own, which is what makes it an entry here at
    // all: a screen handed "(predeterminado)" would still be the one deciding that a space and a
    // bracket go between it and the device's name, and where the bracket goes is as much a
    // language as the word inside it. The device's name is the maker's and is never translated,
    // which is why it is a value rather than words.
    public static UiText TheDeviceWindowsUsesByDefault { get; } =
        new("{0} (predeterminado)", "{0} (default)");

    // The two channels, as the chip and the role the artboards draw beside each picker. The chip is
    // the channel index Deepgram reports back, in mono at the data rank, and it is the same either
    // way because a number is: `docs/design.md` §Type gives mono to every number that gets compared
    // to another one, and *ch0* against *canal 0* would be two spellings of one index. The role
    // beside it is the words, and those are translated.
    //
    // Two entries where there used to be one saying both at once. The meter drew "Canal 0 · los
    // demás" over its own bar while the picker had a header of its own, and the redraw put the
    // three things the artboards draw — the chip, the role and the pill — in one row instead.
    public static UiText Channel0 { get; } = new("ch0", "ch0");

    public static UiText Channel1 { get; } = new("ch1", "ch1");

    public static UiText TheOthersRole { get; } = new("Los demás", "The others");

    public static UiText MyRole { get; } = new("Yo", "Me");

    // What transcribes the meeting, shown on the card and changed in the settings — which is what
    // #97 settled, and why it is a pill with no chevron under it. A provider's model name is what
    // that provider called it, so it is the same either way.
    public static UiText TheEngineThatTranscribes { get; } =
        new("Deepgram nova-3", "Deepgram nova-3");

    // When the meeting gets transcribed, at the foot of the card. #97 settled that this is chosen
    // here and per meeting; nothing transcribes during a recording yet, so it has one answer and
    // this is the whole of it. *En vivo* is not in the catalogue, because a word for an answer
    // nobody can give is a word waiting to be put on a control that lies.
    public static UiText TranscribedAtTheEnd { get; } = new("Al terminar", "At the end");

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

    // Its own question, because this screen has two language pickers on it and they answer
    // different things: a meeting filed in the language of the menu somebody happens to read is a
    // meeting transcribed in the wrong one. The name says which it is and carries the difference
    // on its own — the caption under it that used to explain that is gone, because
    // `docs/design.md` §The rules the design imposes says a screen gets one sentence and only
    // where something failed, and **if an option needs a line explaining it, its name is wrong**.
    public static UiText WhatWillBeSpoken { get; } =
        new("Idioma de la reunión", "The meeting's language");

    // The verb `docs/design.md` §One verb per act fixes for this. The same act is never said two
    // ways, and this one was *Grabar* on the screen against *Empezar a grabar* on the page.
    public static UiText Record { get; } = new("Empezar a grabar", "Start recording");

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

    // The four words the strip says while the meetings have the window, one per state a meeting
    // can be under way in, which `MainAbierto` draws as the one loud thing on it. They are the
    // state and not a sentence about it: the strip is read at a glance by somebody who came to the
    // list for something else, and the status line at the foot is where the same states are said
    // in full, in a quieter ink, for somebody who has stopped to read.
    //
    // Four and not two. Paused is here because a paused meeting is still being recorded and the
    // one thing somebody who just pressed pause is looking for is whether it took; saving is here
    // because stop is on the strip and a strip that went away on its own press would answer that
    // press with nothing; and opening is here because the two devices take as long as they take.
    public static UiText TheDevicesAreOpening { get; } = new("Abriendo", "Opening");

    public static UiText TheMeetingIsBeingRecorded { get; } = new("Grabando", "Recording");

    public static UiText TheMeetingIsPaused { get; } = new("En pausa", "Paused");

    public static UiText TheMeetingIsBeingSaved { get; } = new("Guardando", "Saving");

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

    /// <summary>
    /// A channel that brought back nothing at all in the second just read. Said in words beside
    /// the bar rather than left to an empty bar: an empty bar and a bar nobody has drawn yet look
    /// the same, and the case this exists for — a microphone muted in Windows — never moves it.
    /// </summary>
    public static UiText NothingIsArriving { get; } = new("nada", "nothing");

    /// <summary>
    /// The loudest this source has reached since the recording started — the meter's only memory,
    /// and the mark standing on the bar beside it. The number is data and comes in as a value;
    /// what this carries is the one word saying which of the two numbers under the bar it is.
    /// </summary>
    public static UiText TheLoudestSoFar { get; } = new("pico {0}", "peak {0}");

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
    /// One channel's device gone while the meeting carries on, naming it and the moment it went.
    /// <c>docs/design/Fallo</c> is what these are drawn from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two entries and not one with the channel as a value: what each of them costs is different,
    /// and a person deciding what to do needs to be told which half of the conversation they still
    /// have. Each ends by saying the other channel is still recording, which is the drawing's
    /// header said in the sentence — the meeting is still a two-channel recording and the one thing
    /// somebody has to know is that it did not stop.
    /// </para>
    /// <para>
    /// The device's name and the time go in as values, for the reason
    /// <see cref="TheChannelMovedToAnotherDevice"/> takes its two: a name this machine gave and a
    /// number read off a clock are the same in every language, and a catalogue that held either
    /// would be a catalogue holding this machine's answers.
    /// </para>
    /// <para>
    /// Neither says to press stop, and that is not an omission. Stopping keeps what was recorded,
    /// the way stopping any meeting does — a source that ended by itself is finished like any
    /// other, and the meeting is made and filed by that press. So there is nothing extra for either
    /// notice to promise and nothing for it to warn about, and a line about stopping would be this
    /// screen telling somebody about a press that already behaves the way they expect. Neither
    /// leans on there being a press beside it either: what is offered next to which notice is the
    /// screen's to decide and changes with what else is on it, and a sentence in the catalogue that
    /// assumed a particular button is the sentence this paragraph replaced.
    /// </para>
    /// </remarks>
    public static UiText TheOthersChannelStoppedOnItsOwn { get; } = new(
        "«{0}» dejó de responder a las {1}. Lo que dijeron los demás desde entonces no quedó y no "
        + "se recupera; el micrófono sigue grabando.",
        "‘{0}’ stopped responding at {1}. Nothing the other side said from then on was kept, and "
        + "it does not come back; the microphone is still recording.");

    public static UiText TheMicrophoneChannelStoppedOnItsOwn { get; } = new(
        "«{0}» dejó de responder a las {1}. Lo que escuchó ese micrófono desde entonces no quedó y "
        + "no se recupera; el canal 0 sigue grabando.",
        "‘{0}’ stopped responding at {1}. What that microphone heard from then on was not kept, "
        + "and it does not come back; channel 0 is still recording.");

    /// <summary>
    /// The act on the microphone's notice. <c>docs/design.md</c> §Fallo is what makes it this word:
    /// a source that is alive and silent is answered by pointing somewhere else, and a device that
    /// stopped responding is answered by trying that same device again. <em>Cambiar</em> is on that
    /// page too, as the neutral press on the left rather than the answer.
    /// </summary>
    public static UiText TryTheMicrophoneAgain { get; } = new("Reintentar", "Try again");

    /// <summary>
    /// What the meter says where the level would be, for a channel whose device is gone.
    /// </summary>
    /// <remarks>
    /// The level's place and not a line of its own, which is <c>docs/design.md</c> §The three
    /// states: a dead source has no level to put there, and the one measurement left worth making
    /// about it is when it stopped. In pico, like the peak it stands in the row with, because it is
    /// the thing on that row wanting attention.
    /// </remarks>
    public static UiText ItWasCutOffAt { get; } = new("se cortó a las {0}", "cut off at {0}");

    /// <summary>What the report says when opening the microphone again worked.</summary>
    /// <remarks>
    /// Everything the press changes is visible — the notice goes, the card comes back to full
    /// weight, the scale's two coloured numbers return, and the time it was cut off is replaced by
    /// a level. This is that said in words, for somebody reading the screen through a narrator, who
    /// sees none of it.
    /// </remarks>
    public static UiText TheMicrophoneIsRecordingAgain { get; } =
        new("El micrófono está grabando otra vez.", "The microphone is recording again.");

    /// <summary>And when it did not.</summary>
    /// <remarks>
    /// It says the meeting is still going, because that is the thing somebody who just pressed a
    /// button that failed is about to doubt. The refusal's own words are dumped under it, where the
    /// machine's English belongs.
    /// </remarks>
    public static UiText TheMicrophoneCouldNotBeOpenedAgain { get; } = new(
        "No se pudo abrir el micrófono otra vez. La reunión sigue grabándose por el canal 0.",
        "The microphone could not be opened again. The meeting is still being recorded on "
        + "channel 0.");

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
    /// <remarks>
    /// It said the first recording until 2026-09-02, and stopped being true the day saying who is
    /// using the application became something the corpus keeps: that answer is asked before the
    /// first meeting and makes the corpus if it is the first thing kept. Named after what happens
    /// rather than after which press does it, so a third thing worth keeping before a recording
    /// does not make it wrong again.
    /// </remarks>
    public static UiText TheFirstThingKeptMakesTheCorpusAt { get; } = new(
        "Las reuniones se guardan en {0}. Todavía no hay un corpus ahí: lo crea lo primero que se guarde.",
        "Meetings are kept at {0}. There is no corpus there yet: the first thing kept makes one.");

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

    // ── Who is using the application ──────────────────────────────────────────────────────────

    public static UiText WhoIsUsingTheApplication { get; } =
        new("Quién usa la aplicación", "Who is using the application");

    /// <summary>
    /// Why the field is worth filling in, shown only while nobody has. It is the whole of the
    /// asking: the row is on the screen the application opens on either way, and this is what
    /// tells the first person who sees it that it is a question and not a label.
    /// </summary>
    /// <remarks>
    /// It says what the answer does rather than pleading for one. Nothing is blocked by leaving it
    /// blank — a meeting still records and still transcribes — so a sentence that made it sound
    /// required would be false, and the true reason is better anyway: this name is what the
    /// microphone's own voice reads as afterwards, and no meeting recorded before it is answered
    /// gets it back.
    /// </remarks>
    public static UiText NobodyHasSaidWhoIsUsingThis { get; } = new(
        "Nadie lo dijo todavía. Es el nombre con el que se leerá tu propia voz en las reuniones "
        + "que se graben de acá en adelante: el micrófono es tuyo, así que cuando capta una sola "
        + "voz es la tuya y no hay a quién más preguntarle.",
        "Nobody has said yet. It is the name your own voice reads under in the meetings recorded "
        + "from here on: the microphone is yours, so when it catches a single voice it is yours "
        + "and there is nobody else to ask.");

    /// <summary>
    /// Not <c>Keep</c>, which is what a recording nobody stopped is offered. The two are one word
    /// in English and two in Spanish — conservar is rescuing something that would otherwise go,
    /// guardar is writing an answer down — and sharing the entry would have made the recovery
    /// list's own button read as this one the day either sentence moved.
    /// </summary>
    public static UiText Save { get; } = new("Guardar", "Save");

    public static UiText WhoIsUsingThisIsKept { get; } = new(
        "Listo: de acá en adelante tu voz en el micrófono se lee «{0}».",
        "Done: from here on your voice on the microphone reads ‘{0}’.");

    public static UiText WhoIsUsingThisWasNotKept { get; } = new(
        "No se pudo guardar quién usa la aplicación.",
        "Who is using the application could not be kept.");

    /// <summary>
    /// Said when the corpus would not open. It matters that it is a different sentence from the
    /// one above: an empty field reads as nobody having answered, so somebody would answer again
    /// and the answer would fail on the same corpus — and this is the line that sends them to the
    /// refusal about the folder instead.
    /// </summary>
    public static UiText WhoIsUsingThisCouldNotBeRead { get; } = new(
        "No se pudo leer quién usa la aplicación: el campo está vacío porque el corpus no abrió, "
        + "no porque nadie lo haya dicho.",
        "Who is using the application could not be read: the field is empty because the corpus "
        + "would not open, not because nobody has said.");

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

    // The frame, and the reason goes inside it. This once read as ThatDidNotGoThrough's case — a
    // machine message dropped into {0}, English either way — and it was not: what the engine hands
    // back is one of WhyNotAMeeting's members, and the five sentences under here are the
    // application's own words about a recording, which is exactly what the catalogue is for. Until
    // 2026-09-02 they were English literals on WaitingRecording.Unrecoverable, so somebody reading
    // in Spanish got half a sentence in a language they did not choose.
    public static UiText ThisCannotBecomeAMeeting { get; } = new(
        "No puede volverse una reunión: {0}",
        "This cannot become a meeting: {0}");

    public static UiText NothingHereSaysWhichMeetingItIs { get; } = new(
        "nada de lo que hay acá dice de qué reunión es",
        "nothing here says which meeting it is");

    // No machine message rides on this one, for the reason TheBlocksOfThisOneWouldNotRead gives
    // below and one more: what a torn card throws is a sentence this repository wrote, in English,
    // so dropping it into {0} would put an untranslated clause inside a translated frame — the very
    // thing these five entries exist to stop. It is on the CLI listing and the exception instead,
    // which are read while debugging, and the answer this row offers is the same either way.
    public static UiText WhatItSaysAboutItselfCannotBeRead { get; } = new(
        "no se puede leer lo que dice de sí misma",
        "what it says about itself cannot be read");

    public static UiText ItIsInAnotherMeetingsFolder { get; } = new(
        "está en '{0}', y la grabación de la reunión {1} va en una carpeta con el nombre de esa "
        + "reunión",
        "it is in '{0}', and meeting {1}'s recording belongs in a folder of that meeting's own "
        + "name");

    public static UiText ThisCorpusHasNoSuchMeeting { get; } = new(
        "este corpus no tiene la reunión {0}",
        "this corpus has no meeting {0}");

    public static UiText NotAllOfItsSourcesAreHere { get; } = new(
        "está solo {0} de sus {1} fuentes, y una reunión son las dos",
        "only {0} of its {1} sources is here, and a meeting is both");

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

    // ── The screen one meeting is read from ────────────────────────────────────────────────────

    // The way back, and it says where back is rather than "back": this screen is reached from one
    // place and returns to it, so naming the place costs a word and saves somebody wondering.
    public static UiText BackToTheMeetings { get; } =
        new("Volver a las reuniones", "Back to the meetings");

    // The name field. A meeting's name is the person's to set at any time after it was recorded,
    // and this is the one place the application offers to set it — so the field says what it is
    // for rather than sitting there as an unlabelled box holding a title.
    public static UiText TheMeetingsName { get; } = new("Nombre de la reunión", "The meeting's name");

    // The three sections, which are fixed and are the tables the corpus already has. The AI does
    // not choose them, or the corpus stops being able to answer "every decision in August".
    public static UiText WhatWasDecided { get; } = new("Qué se decidió", "What was decided");

    public static UiText WhatIsLeftToDo { get; } = new("Qué queda por hacer", "What is left to do");

    public static UiText WhatWasLeftUnresolved { get; } =
        new("Qué quedó sin resolver", "What was left unresolved");

    // Who wrote this. Said and not left to be worked out from which buttons are on screen: a
    // summary is a machine's words under a meeting's own name, and whose words they are belongs
    // beside them.
    public static UiText TranscribedBy { get; } = new(
        "La transcribió {0}, el {1}.",
        "{0} transcribed it, on {1}.");

    public static UiText SummarisedBy { get; } = new(
        "El resumen lo armó {0}, el {1}.",
        "{0} put the summary together, on {1}.");

    public static UiText NobodyHasTranscribedThisYet { get; } = new(
        "Todavía no la transcribió nadie.",
        "Nobody has transcribed it yet.");

    public static UiText NobodyHasSummarisedThisYet { get; } = new(
        "Todavía no hay resumen.",
        "There is no summary yet.");

    // A meeting that arrived here already transcribed or already summarised carries what was made
    // and no record of what made it. Said out loud rather than read as nobody having done it,
    // which under a heading that says it was done is the screen contradicting itself.
    public static UiText TheCorpusDoesNotSayWhoTranscribedIt { get; } = new(
        "El corpus no dice quién la transcribió.",
        "The corpus does not say what transcribed it.");

    public static UiText TheCorpusDoesNotSayWhoSummarisedIt { get; } = new(
        "El corpus no dice quién armó el resumen.",
        "The corpus does not say what put the summary together.");

    // The player. Hearing what a meeting recorded never costs anything and never waits on a
    // transcription, so none of these words says anything about either.
    public static UiText Play { get; } = new("Reproducir", "Play");

    // How far into the meeting the playback has got. It labels the track rather than sitting
    // beside it: a slider hands its value back through a pattern of its own, and this is the name
    // that value is read under.
    public static UiText HowFarIntoTheMeeting { get; } =
        new("Por dónde va la reunión", "How far into the meeting");

    // The press on each thing the AI left. It carries the minute it was said at as its words, and
    // this is what says what pressing it does — the same act wherever a citation appears.
    public static UiText WhereThisWasSaid { get; } = new("Dónde se dijo esto", "Where this was said");

    // The two absences a screen with no player has to tell apart. The audio is a source: it was
    // never produced from anything and cannot be produced again, so a meeting the corpus has a row
    // for and no file under is something to look at rather than something still to come.
    public static UiText ThereIsNoRecordingUnderThisMeetingYet { get; } = new(
        "Todavía no hay una grabación bajo esta reunión.",
        "There is no recording under this meeting yet.");

    public static UiText TheRecordingIsNotWhereTheCorpusSaysItIs { get; } = new(
        "El corpus dice que esta reunión tiene audio, y el archivo no está donde debería.",
        "The corpus says this meeting has audio, and the file is not where it should be.");

    // Said where the player would be. A machine with nothing to play through, or a recording whose
    // file has gone, is not something this screen can do anything about — so it says what happened
    // and leaves the rest of the meeting readable.
    public static UiText ThisMeetingWillNotPlay { get; } = new(
        "No se pudo reproducir esta reunión: {0}",
        "This meeting would not play: {0}");

    // ── Filing a meeting under what it was about ───────────────────────────────────────────────

    // Nothing in this section is a technical name and none of it may become one. The whole of what
    // #105 asks for is that a meeting is filed without the person meeting the three-level tree, the
    // closed vocabularies or anything else the corpus stores — so the columns are plain Spanish
    // about the meeting, and the words *node*, *role*, *link* and *template* appear nowhere.

    // The way back, and it says where back is: this screen is reached from one meeting and returns
    // to it, exactly as that meeting is reached from the list.
    public static UiText BackToTheMeeting { get; } = new("Volver a la reunión", "Back to the meeting");

    public static UiText WhatThisMeetingWasAbout { get; } =
        new("De qué fue esta reunión", "What this meeting was about");

    // The act, on the meeting's own screen. It is the verb docs/design.md §One verb per act gives
    // for filing a meeting under what it was about, and it is that verb everywhere.
    public static UiText Classify { get; } = new("Clasificar", "Classify");

    // The label over the filing on the meeting's screen, in the artboard's own words: mono at the
    // data rank, like *esto lo escribió* beside it.
    public static UiText WhatItWasFiledUnder { get; } = new("sobre qué fue", "what it was about");

    // What a meeting nobody filed says where its filing would be. §5.3 row 2 is the story: a casual
    // catch-up is stored with no links at all and is found by text, so this is a real state and not
    // a gap.
    public static UiText ItIsFiledUnderNothing { get; } = new("Sin clasificar", "Unclassified");

    // The fourteen chips, and the question above them. What each one fills is not explained: it is
    // seen when it is chosen, which is what #105 settled.
    public static UiText WhichOneWasItLike { get; } = new("¿A cuál se pareció?", "Which one was it like?");

    // The three columns. Plain Spanish about the meeting and never the name of a role: *es trabajo
    // de*, *del otro lado*, *trata sobre*.
    public static UiText ItIsWorkOf { get; } = new("Es trabajo de", "It is work of");

    public static UiText TheOtherSide { get; } = new("Del otro lado", "The other side");

    public static UiText ItIsAbout { get; } = new("Trata sobre", "It is about");

    // Where the pills would be while a column has none. *Agregar* stands under the column either
    // way, so a column a shape opened empty is still one somebody can fill by hand.
    public static UiText NothingElse { get; } = new("Nada más", "Nothing else");

    public static UiText Add { get; } = new("Agregar", "Add");

    // The `+` at the end of a path. A glyph with no name is nothing to a screen reader, so this is
    // what that press is called in the automation tree rather than on it.
    public static UiText AddALevel { get; } = new("Agregar un nivel", "Add a level");

    public static UiText Who { get; } = new("Quiénes", "Who");

    // The two toggles on a person's row, one per way a meeting can name somebody. Both are
    // pressable because both are things somebody has to be able to say: §5.3 row 10 is a person a
    // meeting is about who was never in the room.
    public static UiText TheyWereThere { get; } = new("estuvo", "they were there");

    // Deliberately not the artboard's *la reunión es sobre ella*. A toggle drawn beside every row
    // cannot know whose row it is on, and a gendered pronoun there is wrong for half the corpus.
    public static UiText TheMeetingIsAboutThisPerson { get; } =
        new("la reunión es sobre esta persona", "the meeting is about them");

    public static UiText AddSomebody { get; } = new("Agregar a alguien", "Add somebody");

    // Not an escape. §5.3 says a casual chat is stored with no links and is found by text, so this
    // is an answer somebody gives — it empties the screen, and *Guardar* is still what writes.
    public static UiText LeaveItUnclassified { get; } =
        new("Dejarla sin clasificar", "Leave it unclassified");

    // Walking away from a form, which is the verb docs/design.md's closed table gives for it.
    public static UiText Cancel { get; } = new("Cancelar", "Cancel");

    // The two entries every picker on this screen opens and closes with. *Ninguno* empties the pill
    // and everything to the right of it; the ellipsis is what says the last one asks a question
    // rather than answering it.
    public static UiText NoneOfThese { get; } = new("Ninguno", "None of these");

    public static UiText NameANewOne { get; } = new("Nombrar uno nuevo…", "Name a new one…");

    // What the dialogue that adds a person asks. An organization and a year are optional: a person
    // carries as many affiliations as they have, and a corpus that never learned the date has none.
    public static UiText NameOfAPerson { get; } = new("Nombre", "Name");

    public static UiText Organization { get; } = new("Organización", "Organization");

    public static UiText From { get; } = new("Desde", "From");

    // Where somebody belonged the day of the meeting, beside their name. The year and not the date:
    // what it is read against is another person's period and the meeting's own.
    public static UiText SinceTheYear { get; } = new("desde {0}", "since {0}");

    // The fourteen shapes, by name only. What each one fills is seen when it is chosen.
    public static UiText TheShapeClass { get; } = new("Clase", "Class");

    public static UiText TheShapeCasualCatchUp { get; } = new("Junta casual", "A casual catch-up");

    public static UiText TheShapeInterviewAsCandidate { get; } =
        new("Entrevista — soy el candidato", "Interview — I am the candidate");

    public static UiText TheShapeInterviewAsInterviewer { get; } =
        new("Entrevista — yo entrevisto", "Interview — I am interviewing");

    public static UiText TheShapeTwoProjects { get; } = new("Dos proyectos", "Two projects");

    public static UiText TheShapeSellingToAClient { get; } =
        new("Vendedor con cliente", "Salesperson with a client");

    public static UiText TheShapeTeamMeeting { get; } = new("Reunión de equipo", "Team meeting");

    public static UiText TheShapeConference { get; } = new("Conferencia", "Conference");

    public static UiText TheShapeBetweenTwoCompanies { get; } =
        new("Entre dos empresas", "Between two companies");

    public static UiText TheShapeHumanResources { get; } = new("Recursos humanos", "Human resources");

    public static UiText TheShapeRecurringOneToOne { get; } = new("1:1 recurrente", "Recurring 1:1");

    // The same either way, and it is named in UiTextsTests as one of those: a daily is called a
    // daily in both, the way the channel chips and the engine's name are.
    public static UiText TheShapeDaily { get; } = new("Daily", "Daily");

    public static UiText TheShapeAfterSalesSupport { get; } =
        new("Soporte post-venta", "After-sales support");

    // Not the same answer as *Junta casual*, which is why both are here. That one is «this was a
    // casual catch-up»; this one is «none of the thirteen fits and I will fill it in».
    public static UiText TheShapeFilledByHand { get; } =
        new("Ninguna — la lleno yo", "None — I will fill it in");

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
