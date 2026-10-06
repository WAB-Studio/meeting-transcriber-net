namespace MeetingTranscriber.Presentation;

// What the application says on the main window, the channel strips and the meters.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    public static UiText RecordAMeeting { get; } = new("Grabar una reunión", "Record a meeting");

    public static UiText Microphone { get; } = new("Micrófono", "Microphone");

    public static UiText Back { get; } = new("Volver", "Back");

    // The two strips are named by what they hear and never by a number: the channel index is the
    // provider's and belongs to the files it sends back. The role stands beside each picker, and a
    // sentence that has to name the loopback says *los demás*, or *the others*.
    public static UiText TheOthersRole { get; } = new("Los demás", "The others");

    public static UiText MyRole { get; } = new("Yo", "Me");

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
        "Windows no avisa cuando cambian los dispositivos; la lista de micrófonos se actualiza al abrirla.",
        "Windows will not say when devices change; the microphone list updates when it is opened.");

    // Said out loud because the picker emptying itself is the sort of change somebody notices
    // afterwards. The recording is not startable until another one is picked, which is the point.
    public static UiText TheMicrophoneChosenIsNoLongerThere { get; } = new(
        "El micrófono elegido ya no está.",
        "The microphone you chose is gone.");

    public static UiText TheWholeMachineCouldNotBeRecorded { get; } = new(
        "No se pudo grabar todo el audio.",
        "Recording all audio could not start.");

    public static UiText WhatToRecordFromThisMachine { get; } = new("Programa", "Program");

    public static UiText EverythingThisMachinePlays { get; } = new("Todo el audio", "All audio");

    // The verb `docs/design.md` §One verb per act fixes for this. The same act is never said two
    // ways, and this one was *Grabar* on the screen against *Empezar a grabar* on the page.
    public static UiText Record { get; } = new("Empezar a grabar", "Start recording");

    public static UiText Resume { get; } = new("Seguir", "Carry on");

    public static UiText RecordTheWholeMachine { get; } = new("Grabar todo el audio", "Record all audio");

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
    public static UiText ChangeWhatTheOthersFollow { get; } = new(
        "Cambiar el programa de los demás", "Change the others' program");

    public static UiText AnotherProgramCouldNotBeFollowed { get; } = new(
        "No se pudo seguir a {0}; todo sigue como estaba.",
        "{0} could not be followed; nothing changed.");

    public static UiText ThatProgramStoppedBeforeTheOthersMoved { get; } = new(
        "{0} ya no está corriendo; todo sigue como estaba.",
        "{0} is no longer running; nothing changed.");

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

    public static UiText ThatProgramIsNoLongerRunning { get; } = new(
        "Ese programa ya no está corriendo, así que no se empezó a grabar. Elija otro.",
        "That program is no longer running, so nothing was started. Choose another.");

    //
    // The heading the recorder half takes while a meeting is being saved, and one line per step
    // that save is going to run. What decides which of them are on screen is not here: a step is
    // shown because the save runs it, so this file holds words for steps and never the list.
    public static UiText SavingTheMeeting { get; } = new("Guardando…", "Saving…");

    public static UiText LettingBothSourcesGo { get; } = new("Cerrando la grabación…", "Closing the recording…");

    public static UiText SavingTheAudioOfBothChannels { get; } = new("Guardando el audio…", "Saving the audio…");

    // The two marks beside a step. They are what a narrator reads out where somebody looking sees
    // a tick or a ring, so they are texts and not decoration; a step still to come carries neither,
    // because there is nothing yet to say about it.
    public static UiText ThisStepIsDone { get; } = new("listo", "done");

    public static UiText ThisStepIsUnderWay { get; } = new("en curso", "under way");

    public static UiText TheRecordingCouldNotStart { get; } =
        new("No se pudo empezar a grabar.", "The recording could not be started.");

    public static UiText TheMeetingCouldNotBeMade { get; } = new(
        "No se pudo guardar la reunión. Lo grabado sigue en su carpeta.",
        "The meeting could not be saved. What was recorded is still in its folder.");

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
        "«{0}» dejó de responder a las {1}. Lo que dijeron los demás desde entonces no se recupera; el micrófono sigue grabando.",
        "‘{0}’ stopped responding at {1}. What the others said from then on is lost; the microphone is still recording.");

    public static UiText TheMicrophoneChannelStoppedOnItsOwn { get; } = new(
        "«{0}» dejó de responder a las {1}. Lo que escuchó desde entonces no se recupera; los demás siguen grabándose.",
        "‘{0}’ stopped responding at {1}. What it heard from then on is lost; the others are still recording.");

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

    // The one success the report still says, because nothing else would say it in words: the
    // notice goes and the meter comes back, and somebody reading this screen through a narrator
    // sees none of that.
    public static UiText TheMicrophoneIsRecordingAgain { get; } = new("Micrófono de vuelta", "Microphone back");

    /// <summary>And when it did not.</summary>
    /// <remarks>
    /// It says the meeting is still going, because that is the thing somebody who just pressed a
    /// button that failed is about to doubt. The refusal's own words are dumped under it, where the
    /// machine's English belongs.
    /// </remarks>
    public static UiText TheMicrophoneCouldNotBeOpenedAgain { get; } = new(
        "No se pudo abrir el micrófono otra vez; lo demás sigue grabándose.",
        "The microphone could not be opened again; the rest is still recording.");

    // The meters before a meeting listen to what was chosen, and a source Windows refuses is said once.
    // The refusal's own words are dumped under it, where the machine's English belongs.
    public static UiText TheSourcesCouldNotBeListenedTo { get; } = new(
        "No se pudo escuchar antes de grabar.", "Could not listen before recording.");

    // Pausing and carrying on write a line beside the recording before they change anything, which a
    // folder can refuse. Nothing changed: the meeting is in the state it was in.
    public static UiText ThePauseCouldNotBeChanged { get; } = new(
        "No se pudo cambiar la pausa; todo sigue como estaba.",
        "The pause could not be changed; nothing changed.");

    // Changing the microphone while a meeting records. Nothing changed: channel 1 is where it was.
    public static UiText TheMicrophoneCouldNotBeChanged { get; } = new(
        "No se pudo cambiar el micrófono.", "The microphone could not be changed.");

    // What a channel that changed device mid meeting says, for either channel and either way it moves:
    // what it records now, and what it did before. The names go in as values, which keeps a device's name
    // out of the catalogue.
    public static UiText TheChannelMovedToAnotherDevice { get; } = new(
        "Ahora graba «{1}» en lugar de «{0}»; lo del medio queda como hueco.",
        "Now recording ‘{1}’ instead of ‘{0}’; what happened between is a gap.");

    // The one sentence the status line keeps: a recording with nowhere to put it. It sends somebody to
    // the settings, where the folder card is.
    public static UiText ChangeWhereTheCorpusIsFromSettings { get; } = new(
        "La carpeta de reuniones no se pudo abrir. Cámbiela en Configuración.",
        "The meetings folder could not be opened. Change it in Settings.");

    public static UiText PackagingChecks { get; } =
        new("Comprobaciones de empaquetado", "Packaging checks");

    public static UiText LanguageNotRemembered { get; } = new(
        "No se pudo recordar el idioma; la próxima vez se abrirá en el de Windows.",
        "The language could not be remembered; next time it opens in Windows' language.");
}
