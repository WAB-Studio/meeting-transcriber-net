namespace MeetingTranscriber.Presentation;

// What the application says on the settings screen.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
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

    /// <summary>
    /// What a picker refusing to open says. The Windows App SDK picker used here does not throw
    /// the older picker's <c>COMException</c>, but an <c>async void</c> handler still cannot let
    /// anything it throws escape, so this is what is said instead of the application going away.
    /// </summary>
    public static UiText TheFolderPickerDidNotOpen { get; } = new(
        "Windows no abrió el selector de carpetas.", "Windows did not open the folder picker.");

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

    // The one question on this screen that spends money, and the whole reason it exists: it is
    // asked once, here, rather than per meeting.
    public static UiText WhenARecordingEnds { get; } =
        new("Cuando termina una grabación", "When a recording ends");

    // The three answers. *Preguntarme cada vez* is not among them and is not missing: nothing in
    // this application asks, so the third answer is the meeting sitting in the list until somebody
    // presses something on its row.
    public static UiText TranscribeItAndSummariseIt { get; } =
        new("Transcribirla y resumirla", "Transcribe it and summarise it");

    public static UiText OnlyTranscribeIt { get; } = new("Sólo transcribirla", "Only transcribe it");

    public static UiText DoNothingWhenItEnds { get; } = new("No hacer nada", "Do nothing");

    // The two engines, each on its own card. Neither is a picker: there is one
    // of each, so what is drawn is the answer and not a choice.
    public static UiText TranscribeWith { get; } = new("Transcribir con", "Transcribe with");

    public static UiText SummariseWith { get; } = new("Resumir con", "Summarise with");

    // What a maker called its own model, so it is the same either way — the same answer
    // `TheEngineThatTranscribes` gives.
    public static UiText TheEngineThatSummarises { get; } = new("Claude", "Claude");

    // Over the line naming the folder. Not *Dónde se guardan las reuniones*: what is kept there is
    // the corpus, which is the recordings, the responses already paid for and everything read out
    // of them, and the sentence under it already says the word *reuniones*.
    public static UiText WhereItIsKept { get; } = new("Dónde se guarda", "Where it is kept");

    //
    // Nothing here ever carries the key or any part of it: the screen says whether one is kept and
    // what happened to a paste, and that is the whole of what it knows.
    public static UiText DeepgramKeyHeader { get; } = new("Clave de Deepgram", "Deepgram key");

    // The line under the field. A label and not a sentence: whether a key is kept is a state, and
    // the one sentence this block gets is the one that says a paste failed.
    public static UiText ADeepgramKeyIsKept { get; } =
        new("Guardada en esta máquina", "Kept on this machine");

    public static UiText NoDeepgramKeyIsKept { get; } =
        new("Ninguna guardada en esta máquina", "None kept on this machine");

    // Windows would not say. Not a guess either way: *Quitar* stays offered, since a key may be
    // there to take away.
    public static UiText WhetherADeepgramKeyIsKeptIsUnknown { get; } = new(
        "Windows no dijo si hay una guardada en esta máquina",
        "Windows would not say whether one is kept on this machine");

    /// <summary>
    /// The accessible name of the press that keeps a pasted key. It still shows <see cref="Save"/>,
    /// and so does the press beside who is using the application, so a screen reader is told which.
    /// </summary>
    public static UiText SaveTheDeepgramKey { get; } =
        new("Guardar la clave de Deepgram", "Save the Deepgram key");

    public static UiText RemoveTheDeepgramKey { get; } =
        new("Quitar la clave de Deepgram de esta máquina", "Remove the Deepgram key from this machine");

    public static UiText TheDeepgramKeyIsKept { get; } = new(
        "Listo: la clave de Deepgram quedó guardada en esta máquina.",
        "Done: the Deepgram key is kept on this machine.");

    public static UiText TheDeepgramKeyIsRemoved { get; } = new(
        "Listo: esta máquina ya no guarda ninguna clave de Deepgram.",
        "Done: this machine no longer keeps a Deepgram key.");

    public static UiText TheDeepgramKeyWasNotRemoved { get; } = new(
        "No se quitó: Windows no dejó quitar la clave de Deepgram de esta máquina.",
        "Not removed: Windows would not take the Deepgram key off this machine.");

    // The two ways a paste is refused, one per refusal `Keep` can make. Neither quotes what was
    // pasted, and neither says what Windows said: the line under the field, read again afterwards,
    // is what says whether a key is still kept.
    public static UiText ThatIsNotADeepgramKey { get; } = new(
        "No se guardó: la clave está vacía. Nada cambió en esta máquina.",
        "Not kept: the key is empty. Nothing changed on this machine.");

    public static UiText ThisMachineWouldNotKeepTheKey { get; } = new(
        "No se guardó: esta máquina no aceptó guardar la clave de Deepgram.",
        "Not kept: this machine would not store the Deepgram key.");

    public static UiText ClaudeCodeIsNotOnThisMachine { get; } = new(
        "Claude Code no está en esta máquina, así que no se puede resumir.",
        "Claude Code is not on this machine, so nothing can be summarised.");

    public static UiText ClaudeCodeDidNotAnswer { get; } = new(
        "Claude Code está, pero no respondió: {0}",
        "Claude Code is there, and did not answer: {0}");

    public static UiText TheFilePickerDidNotOpen { get; } = new(
        "Windows no abrió el selector de archivos.", "Windows did not open the file picker.");

    /// <summary>
    /// The accessible name of the press that changes where the corpus is kept. It still *shows*
    /// <see cref="Change"/> (<em>Cambiar</em>), which keeps its visible word on every
    /// <em>Cambiar</em> press on the settings screen; this is what a screen reader announces for
    /// this one, since two presses named <em>Cambiar</em> read the same to somebody who cannot see
    /// which row they are beside.
    /// </summary>
    public static UiText ChangeWhereTheCorpusIsKept { get; } =
        new("Cambiar dónde se guarda el corpus", "Change where the corpus is kept");

    /// <summary>
    /// The accessible name of the press that changes where Claude Code is.
    /// See <see cref="ChangeWhereTheCorpusIsKept"/>.
    /// </summary>
    public static UiText ChangeWhereClaudeCodeIs { get; } =
        new("Cambiar dónde está Claude Code", "Change where Claude Code is");

    // Over the four ticks. *Qué se lleva* and not *Qué exportar*: the person is choosing what goes
    // with them, which is the sentence the card's own decision is written in.
    public static UiText WhatAnExportTakes { get; } =
        new("Qué se lleva una exportación", "What an export takes");

    // The four ticks. Each is a kind of thing and not a file: a person choosing *Transcripciones*
    // means both the paid response and the text somebody can read.
    public static UiText ExportsAudio { get; } = new("Audio", "Audio");

    public static UiText ExportsTranscripts { get; } = new("Transcripciones", "Transcripts");

    public static UiText ExportsSummaries { get; } = new("Resúmenes", "Summaries");

    public static UiText ExportsHandCorrections { get; } =
        new("Correcciones a mano", "Corrections by hand");

    // The press, at the normal rank: taking the corpus out is not the act the screen is for.
    public static UiText Export { get; } = new("Exportar", "Export");

    /// <summary>
    /// The accessible name of the press that exports. It shows <see cref="Export"/>; this is what a
    /// screen reader announces, so the press says what is exported and where it goes.
    /// </summary>
    public static UiText ExportTheCorpusToAFolder { get; } =
        new("Exportar el corpus a una carpeta", "Export the corpus to a folder");

    // On the press while an export runs, and nowhere else: there is no success sentence, because
    // the line below it changing is what says it worked.
    public static UiText Exporting { get; } = new("Exportando…", "Exporting…");

    // {0} is one line of data — when, how many meetings, and which kinds — and {1} is the folder.
    public static UiText LastExport { get; } =
        new("Última exportación: {0}, en {1}", "Last export: {0}, into {1}");

    // Said after an export that finished without everything the corpus records. The file it names
    // is the export's own index, which lists each one.
    public static UiText OneFileWasNotThere { get; } = new(
        "1 archivo que el corpus registra no estaba en el disco y quedó fuera; está nombrado en {0}.",
        "1 file the corpus records was not on the disk and was left out; it is named in {0}.");

    public static UiText SomeFilesWereNotThere { get; } = new(
        "{0} archivos que el corpus registra no estaban en el disco y quedaron fuera; están nombrados en {1}.",
        "{0} files the corpus records were not on the disk and were left out; they are named in {1}.");
}
