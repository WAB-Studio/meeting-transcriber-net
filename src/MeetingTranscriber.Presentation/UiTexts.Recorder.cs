namespace MeetingTranscriber.Presentation;

// What the application says on the main window, the channel strips and the meters.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    public static UiText RecordAMeeting { get; } = new("Grabar una reunión", "Record a meeting");

    public static UiText Microphone { get; } = new("Micrófono", "Microphone");

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

    public static UiText Resume { get; } = new("Seguir", "Carry on");

    public static UiText RecordTheWholeMachine { get; } =
        new("Grabar toda la máquina", "Record the whole machine");

    // One sentence naming the program, read as a live region, so it is the same every second it
    // stands: a count running in it would be read out every second. Taking the whole machine costs
    // nothing it does not say on its own press, and the meeting keeps running either way.
    public static UiText NothingCameFromThatProgram { get; } = new(
        "No llegó nada de {0}.", "Nothing has come from {0}.");

    // The notice's other sentence, in the same row and read as the same live region: the program
    // channel 0 follows ended while the meeting was being recorded.
    public static UiText ThatProgramWentAway { get; } = new(
        "{0} se cerró y desde entonces no llega nada de los demás.",
        "{0} closed, and nothing from the other side has arrived since.");

    // What channel 0's level reads while the notice stands: the recording's verdict that nothing
    // arrives from this program (it never did, or it went away), and not *nada*, which is the last second's reading and is
    // true of a meeting between sentences. docs/design.md §NadaLlego.
    public static UiText NoSignal { get; } = new("sin señal", "no signal");

    // The accessible name of the notice's Cambiar, which shows the one word every press that
    // points something elsewhere shows (Change). Two presses named Cambiar read the same to
    // somebody who cannot see which one they are beside.
    public static UiText ChangeWhatChannel0Follows { get; } = new(
        "Cambiar qué programa sigue el canal 0", "Change which program channel 0 follows");

    public static UiText NowFollowingAnotherProgram { get; } =
        new("Canal 0: {0}.", "Channel 0: {0}.");

    public static UiText AnotherProgramCouldNotBeFollowed { get; } = new(
        "El canal 0 no se pudo pasar a {0}; sigue donde estaba.",
        "Channel 0 could not be moved onto {0}; it is still where it was.");

    public static UiText ThatProgramStoppedBeforeChannel0Moved { get; } = new(
        "{0} ya no está corriendo, así que el canal 0 sigue donde estaba.",
        "{0} is no longer running, so channel 0 is still where it was.");

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
        "Elija el micrófono, qué grabar de esta máquina y en qué idioma se va a hablar.",
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
        "Ese programa ya no está corriendo, así que no se empezó a grabar: elija otra vez qué "
        + "grabar de esta máquina. Su número de proceso puede ser de otra aplicación ahora.",
        "That program is no longer running, so nothing was started: choose again what to record "
        + "from this machine. Its process number may belong to another application by now.");

    public static UiText MakingTheMeeting { get; } = new(
        "Deteniendo. La reunión se está armando con lo que se grabó, y para una reunión larga eso "
        + "tarda unos minutos.",
        "Stopping. The meeting is being made out of what was recorded, and for a long meeting that "
        + "takes some minutes.");

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

    // Said out loud every time, because what a stop did about money is not something to leave
    // somebody working out from a list. Which of the two is said is read off what was really
    // written, never off what the settings say — a stop that decided a transcription and found one
    // already queued says the first of these, because that is what happened.
    public static UiText NothingWasQueued { get; } = new(
        "No se puso nada en cola: transcribir es otro botón.",
        "Nothing was queued: transcribing is a separate press.");

    public static UiText TranscribingWasQueued { get; } = new(
        "Quedó en cola transcribirla, que es lo que pedía la configuración.",
        "Transcribing it is queued, which is what the settings asked for.");

    public static UiText TheRecordingCouldNotStart { get; } =
        new("No se pudo empezar a grabar.", "The recording could not be started.");

    public static UiText TheMeetingCouldNotBeMade { get; } = new(
        "La grabación terminó, pero la reunión no se pudo armar. Lo grabado sigue en su carpeta.",
        "The recording ended, but the meeting could not be made. What was recorded is still in its "
        + "folder.");

    /// <summary>
    /// What the biggest number on the screen is a number of. The digits themselves carry no entry
    /// of this catalogue — a length reads the same in every language — so this is the whole of
    /// what a narrator has to go on, and it says the measurement rather than the control: somebody
    /// hearing "clock" would look for a time of day.
    /// </summary>
    public static UiText HowLongTheMeetingHasBeenRunning { get; } = new(
        "Hace cuánto que se está grabando",
        "How long the meeting has been running");

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

    // It said choosing another folder was not done from this screen yet until #148 built the press
    // that does it — on Configuración, beside the same refusal — so this sentence sends somebody
    // there now rather than pretending nothing can be done about it.
    public static UiText ChangeWhereTheCorpusIsFromSettings { get; } = new(
        "El corpus no se pudo abrir. Para cambiar dónde se guarda, vaya a Configuración.",
        "The corpus could not be opened. To change where it is kept, go to Settings.");

    public static UiText PackagingChecks { get; } =
        new("Comprobaciones de empaquetado", "Packaging checks");

    public static UiText LanguageNotRemembered { get; } = new(
        "El idioma cambió, pero no se pudo recordar la elección: la próxima vez la aplicación "
        + "abrirá en el idioma de Windows.",
        "The language changed, but the choice could not be remembered: next time the application "
        + "will open in Windows' language.");
}
