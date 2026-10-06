namespace MeetingTranscriber.Presentation;

// What the application says on the settings screen.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // The title of the first-time arrangement and the press that leaves it. The first step is this
    // screen with three blocks and nothing else, so the words are the screen's own.
    public static UiText GetStarted { get; } = new("Primeros pasos", "Get started");

    public static UiText Start { get; } = new("Empezar", "Start");

    // The headings of the blocks that have no entry of their own.
    public static UiText You { get; } = new("Tú", "You");

    public static UiText Folder { get; } = new("Carpeta", "Folder");

    // The card for how the application itself looks and reads, and its two pickers. The theme's
    // three answers are one per member of `AppTheme`; *Sistema* is "whichever Windows is set to".
    public static UiText App { get; } = new("Aplicación", "App");

    public static UiText Theme { get; } = new("Tema", "Theme");

    public static UiText ThemeSystem { get; } = new("Sistema", "System");

    public static UiText ThemeLight { get; } = new("Claro", "Light");

    public static UiText ThemeDark { get; } = new("Oscuro", "Dark");

    // Said when the pick was applied to this window and could not be written down, so the next
    // launch will not have it. The language's own sentence is the same shape.
    public static UiText TheThemeWasNotRemembered { get; } = new(
        "No se pudo recordar el tema; la próxima vez se abrirá con el de Windows.",
        "The theme could not be remembered; next time it opens in Windows' theme.");

    // Under *Resumir con*: how that engine is paid. Summaries run on the person's own Claude plan,
    // because the application strips every API key before it starts one, so no card says a price.
    public static UiText OnYourClaudePlan { get; } =
        new("Con tu plan de Claude", "On your Claude plan");

    // The picker for how much reasoning a summary is asked to spend, one answer per member of
    // `SummaryEffort`.
    public static UiText Effort { get; } = new("Esfuerzo", "Effort");

    public static UiText EffortHigh { get; } = new("Alto", "High");

    public static UiText EffortMedium { get; } = new("Medio", "Medium");

    public static UiText EffortLow { get; } = new("Bajo", "Low");

    // The press that asks Claude Code whether it answers, and what it says when it does. Said only
    // after that press: opening the screen says nothing about an engine that is there.
    public static UiText Test { get; } = new("Probar", "Test");

    public static UiText ClaudeCodeAnswers { get; } = new("Funciona", "Works");

    // The models a summary can be asked of, one per member of `SummaryModel`. Each carries a word
    // that differs in the two languages, so none is a same-either-way entry.
    public static UiText SummaryModelSonnet { get; } = new("Sonnet equilibrado", "Balanced Sonnet");

    public static UiText SummaryModelOpus { get; } = new("Opus potente", "Powerful Opus");

    public static UiText SummaryModelHaiku { get; } = new("Haiku rápido", "Fast Haiku");

    public static UiText TheSettingSaysNothingUsable { get; } = new(
        "El archivo que dice dónde están las reuniones no dice nada usable: {0}. No se graba "
        + "hasta resolverlo.",
        "The file that says where the meetings are kept says nothing usable: {0}. Nothing is "
        + "recorded until that is settled.");

    public static UiText TheCorpusFolderDidNotAnswer { get; } = new(
        "La carpeta {0} no responde: no está, o este usuario no puede leerla.",
        "The folder {0} does not answer: it is not there, or this user may not read it.");

    public static UiText ThereIsNoCorpusInThatFolder { get; } = new(
        "No hay reuniones en {0}. Esa ruta puede haber dejado de llegar a donde estaban.",
        "There are no meetings in {0}. That path may no longer reach where they were.");

    public static UiText TheCorpusFolderGoesWhenThePackageDoes { get; } = new(
        "{0} se borra al desinstalar la aplicación, con las respuestas ya pagadas.",
        "{0} goes when the application is uninstalled, with the responses already paid for.");

    /// <summary>
    /// What a picker refusing to open says. The Windows App SDK picker used here does not throw
    /// the older picker's <c>COMException</c>, but an <c>async void</c> handler still cannot let
    /// anything it throws escape, so this is what is said instead of the application going away.
    /// </summary>
    public static UiText TheFolderPickerDidNotOpen { get; } = new(
        "Windows no abrió el selector de carpetas.", "Windows did not open the folder picker.");

    // The folder card's tooltip: what is kept where the card says.
    public static UiText WhatTheFolderKeeps { get; } = new(
        "Aquí se guardan tus reuniones: audio, transcripciones y resúmenes.",
        "Your meetings are kept here: audio, transcripts and summaries.");

    // Moving the meetings to an empty folder: the line that offers it, the tick that also removes
    // the old copy, the act, and what the screen says while it works.
    public static UiText TheMeetingsMoveTo { get; } = new(
        "Mover las reuniones a {0}", "Move the meetings to {0}");

    public static UiText RemoveTheOldCopy { get; } = new("Borrar la copia anterior", "Delete the old copy");

    public static UiText Move { get; } = new("Mover", "Move");

    public static UiText Moving { get; } = new("Moviendo…", "Moving…");

    // One sentence per member of `CorpusMoveRefusal`, said before anything is written.
    public static UiText TheFolderIsNotEmpty { get; } = new(
        "La carpeta no está vacía.", "The folder is not empty.");

    public static UiText ThatFolderGoesOnUninstall { get; } = new(
        "Esa carpeta se borra al desinstalar.", "That folder is deleted on uninstall.");

    public static UiText OneFolderIsInsideTheOther { get; } = new(
        "Una carpeta está dentro de la otra.", "One folder is inside the other.");

    public static UiText WorkMustFinishFirst { get; } = new(
        "Hay trabajo en curso; espere a que termine.", "Work is under way; wait for it to finish.");

    public static UiText ARecordingIsWaitingToBeDecided { get; } = new(
        "Hay una grabación sin decidir.", "A recording is waiting to be decided.");

    // Said while a meeting is being recorded or saved, which the move waits out. The reason goes
    // inside the second one: it is what the disk or SQLite said, the evidence somebody quotes.
    public static UiText AMeetingIsBeingRecorded { get; } = new(
        "Hay una reunión en curso; termina de grabarla antes de mover las reuniones.",
        "A meeting is being recorded; finish it before moving the meetings.");

    public static UiText TheMeetingsCouldNotBeMoved { get; } = new(
        "No se pudieron mover las reuniones: {0}", "The meetings could not be moved: {0}");

    public static UiText TheOldCopyWasNotRemoved { get; } = new(
        "No se borró la copia anterior.", "The old copy was not deleted.");

    public static UiText WhoIsUsingTheApplication { get; } = new("Tu nombre", "Your name");

    public static UiText WhoIsUsingThisIsKept { get; } = new(
        "Listo: tu voz en el micrófono se lee «{0}».",
        "Done: your voice on the microphone reads ‘{0}’.");

    public static UiText WhoIsUsingThisWasNotKept { get; } = new(
        "No se pudo guardar tu nombre.", "Your name could not be saved.");

    /// <summary>
    /// Said when the meetings would not open. It matters that it is said at all: an empty field
    /// reads as nobody having answered, so somebody would answer again and the answer would fail
    /// the same way.
    /// </summary>
    public static UiText WhoIsUsingThisCouldNotBeRead { get; } = new(
        "No se pudo leer tu nombre.", "Your name could not be read.");

    // The one question on this screen that spends money, and the whole reason it exists: it is
    // asked once, here, rather than per meeting.
    public static UiText WhenARecordingEnds { get; } = new("Después de grabar", "After recording");

    // The three answers. *Preguntarme cada vez* is not among them and is not missing: nothing in
    // this application asks, so the third answer is the meeting sitting in the list until somebody
    // presses something on its row.
    public static UiText TranscribeItAndSummariseIt { get; } =
        new("Transcribir y resumir", "Transcribe and summarise");

    public static UiText OnlyTranscribeIt { get; } = new("Solo transcribir", "Transcribe only");

    public static UiText DoNothingWhenItEnds { get; } = new("Nada", "Do nothing");

    // The two engines, each on its own card. Transcribing has one; summarising has one engine and
    // a choice of the model it is asked of.
    public static UiText TranscribeWith { get; } = new("Transcribir con", "Transcribe with");

    public static UiText SummariseWith { get; } = new("Resumir con", "Summarise with");

    // What a maker called its own model, so it is the same either way — the same answer
    // `TheEngineThatTranscribes` gives. It also heads the Claude Code card.
    public static UiText TheEngineThatSummarises { get; } = new("Claude", "Claude");

    // The Deepgram card's heading and the secret field's name.
    // Nothing here ever carries the key or any part of it: the screen says whether one is kept and
    // what happened to a paste, and that is the whole of what it knows.
    public static UiText DeepgramKeyHeader { get; } = new("Clave de Deepgram", "Deepgram key");

    // The line under the field. A label and not a sentence: whether a key is kept is a state, and
    // the one sentence this block gets is the one that says a paste failed.
    public static UiText ADeepgramKeyIsKept { get; } = new("Guardada", "Saved");

    public static UiText NoDeepgramKeyIsKept { get; } = new("Sin clave", "No key");

    // Windows would not say. Not a guess either way: *Quitar* stays offered, since a key may be
    // there to take away.
    public static UiText WhetherADeepgramKeyIsKeptIsUnknown { get; } = new(
        "Windows no dijo si hay clave", "Windows would not say if there is a key");

    /// <summary>
    /// The accessible name of the press that keeps a pasted key. It still shows <see cref="Save"/>,
    /// and so does the press beside the name, so a screen reader is told which.
    /// </summary>
    public static UiText SaveTheDeepgramKey { get; } =
        new("Guardar la clave de Deepgram", "Save the Deepgram key");

    public static UiText RemoveTheDeepgramKey { get; } =
        new("Quitar la clave de Deepgram de esta máquina", "Remove the Deepgram key from this machine");

    public static UiText TheDeepgramKeyWasNotRemoved { get; } = new(
        "No se quitó: Windows no dejó quitar la clave.",
        "Not removed: Windows would not take the key off.");

    // The two ways a paste is refused, one per refusal `Keep` can make. Neither quotes what was
    // pasted, and neither says what Windows said: the line under the field, read again afterwards,
    // is what says whether a key is still kept.
    public static UiText ThatIsNotADeepgramKey { get; } = new(
        "No se guardó: la clave está vacía.", "Not saved: the key is empty.");

    public static UiText ThisMachineWouldNotKeepTheKey { get; } = new(
        "No se guardó: esta máquina no aceptó la clave.",
        "Not saved: this machine would not take the key.");

    public static UiText ClaudeCodeIsNotOnThisMachine { get; } = new(
        "Claude Code no está en esta máquina.", "Claude Code is not on this machine.");

    public static UiText ClaudeCodeDidNotAnswer { get; } = new(
        "Claude Code no respondió: {0}", "Claude Code did not answer: {0}");

    public static UiText TheFilePickerDidNotOpen { get; } = new(
        "Windows no abrió el selector de archivos.", "Windows did not open the file picker.");

    /// <summary>
    /// The accessible name of the press that changes the meetings folder. It still *shows*
    /// <see cref="Change"/> (<em>Cambiar</em>), which keeps its visible word on every
    /// <em>Cambiar</em> press on the settings screen; this is what a screen reader announces for
    /// this one, since two presses named <em>Cambiar</em> read the same to somebody who cannot see
    /// which row they are beside.
    /// </summary>
    public static UiText ChangeWhereTheCorpusIsKept { get; } =
        new("Cambiar la carpeta de reuniones", "Change the meetings folder");

    /// <summary>
    /// The accessible name of the press that changes where Claude Code is.
    /// See <see cref="ChangeWhereTheCorpusIsKept"/>.
    /// </summary>
    public static UiText ChangeWhereClaudeCodeIs { get; } =
        new("Cambiar dónde está Claude Code", "Change where Claude Code is");

    // The four ticks. Each is a kind of thing and not a file: a person choosing *Transcripciones*
    // means both the paid response and the text somebody can read.
    public static UiText ExportsAudio { get; } = new("Audio", "Audio");

    public static UiText ExportsTranscripts { get; } = new("Transcripciones", "Transcripts");

    public static UiText ExportsSummaries { get; } = new("Resúmenes", "Summaries");

    public static UiText ExportsHandCorrections { get; } =
        new("Correcciones a mano", "Corrections by hand");

    // The press, and the heading of its card: taking the meetings out is not the act the screen is
    // for.
    public static UiText Export { get; } = new("Exportar", "Export");

    /// <summary>
    /// The accessible name of the press that exports. It shows <see cref="Export"/>; this is what a
    /// screen reader announces, so the press says what is exported and where it goes.
    /// </summary>
    public static UiText ExportTheCorpusToAFolder { get; } =
        new("Exportar las reuniones a una carpeta", "Export the meetings to a folder");

    // The tooltip of the export's glyph: the press shows no word, so what it does is said on hover.
    public static UiText WhatAnExportIsFor { get; } = new(
        "Guarda el audio en WAV y las transcripciones en una carpeta, por ejemplo para pasarlas a otra PC.",
        "Saves the audio as WAV and the transcripts to a folder, for instance to move them to another PC.");

    // On the press while an export runs, and nowhere else: there is no success sentence, because
    // the line below it changing is what says it worked.
    public static UiText Exporting { get; } = new("Exportando…", "Exporting…");

    // {0} is one line of data — when, how many meetings, and which kinds — and {1} is the folder.
    public static UiText LastExport { get; } =
        new("Última exportación: {0}, en {1}", "Last export: {0}, into {1}");

    // Said after an export that finished without everything the meetings record. The file it names
    // is the export's own index, which lists each one.
    public static UiText OneFileWasNotThere { get; } = new(
        "1 archivo no estaba en el disco y quedó fuera; está nombrado en {0}.",
        "1 file was not on the disk and was left out; it is named in {0}.");

    public static UiText SomeFilesWereNotThere { get; } = new(
        "{0} archivos no estaban en el disco y quedaron fuera; están nombrados en {1}.",
        "{0} files were not on the disk and were left out; they are named in {1}.");
}
