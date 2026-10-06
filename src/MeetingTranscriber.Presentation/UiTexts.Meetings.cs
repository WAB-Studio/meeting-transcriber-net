namespace MeetingTranscriber.Presentation;

// What the application says on the meetings list and the words a meeting is described in.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    public static UiText Meetings { get; } = new("Reuniones", "Meetings");

    public static UiText NoMeetingsHereYet { get; } = new("Sin reuniones", "No meetings");

    // The drawer's one press, which always offers the position it is not in.
    public static UiText OpenTheMeetingsWhole { get; } = new("Ver todas", "Show all");

    public static UiText BringTheMeetingsBackDown { get; } = new("Ver menos", "Show less");

    // These stand in for the status line rather than sitting beside it. A recording still waiting
    // in the corpus is exactly the meeting *Sin audio* is about, and the list can now say which of
    // that status's two halves is true of it instead of offering somebody both.
    public static UiText ItIsBeingRecordedRightNow { get; } = new("Grabando…", "Recording…");

    // What was observed before what it means, which is what anything on the attention tint owes
    // the reader. The blocks are whole up to the packet the machine died in, and saying so is what
    // keeps this from reading as a recording that broke.
    public static UiText TheApplicationClosedInTheMiddleOfThisOne { get; } = new(
        "La aplicación se cerró a mitad de esta grabación; el audio está entero hasta ahí.",
        "The application closed in the middle of this recording; the audio is whole up to there.");

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
        "no dice de qué reunión es",
        "it does not say which meeting it is");

    // No machine message rides on this one, for the reason TheBlocksOfThisOneWouldNotRead gives
    // below and one more: what a torn card throws is a sentence this repository wrote, in English,
    // so dropping it into {0} would put an untranslated clause inside a translated frame — the very
    // thing these five entries exist to stop. It is on the CLI listing and the exception instead,
    // which are read while debugging, and the answer this row offers is the same either way.
    public static UiText WhatItSaysAboutItselfCannotBeRead { get; } = new(
        "no se puede leer su descripción",
        "its description cannot be read");

    public static UiText ItIsInAnotherMeetingsFolder { get; } = new(
        "está en la carpeta de otra reunión",
        "it is in another meeting's folder");

    public static UiText ThisCorpusHasNoSuchMeeting { get; } = new(
        "la carpeta de reuniones no tiene la reunión {0}",
        "the meetings folder has no meeting {0}");

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
        "No se pudo leer esta grabación, así que no se sabe cuánto duró.",
        "This recording would not read, so how long it is cannot be said.");

    // The two answers, and there are two. Taking the audio out to a folder is a copy and not an
    // answer — the recording is still waiting afterwards — so it is not a button on this row.
    // *Descartar* is also how a classification put by is thrown away, on the classification screen:
    // the same act, throwing away what somebody kept, applied to something else.
    public static UiText Keep { get; } = new("Conservar", "Keep");

    // Keeping a recording pours its blocks onto a timeline and hashes the result, which for a long
    // meeting is minutes. Said while it runs, because a press that goes quiet for minutes is one
    // somebody presses again.
    public static UiText TheRecordingIsBeingKept { get; } = new(
        "Conservando… puede tardar unos minutos.",
        "Keeping… it can take a few minutes.");

    public static UiText ItIsAMeetingNow { get; } =
        new("Listo: quedó como reunión.", "Done: it is a meeting now.");

    public static UiText TheRecordingIsGone { get; } =
        new("Listo: la grabación se borró.", "Done: the recording is gone.");

    // Where a meeting is, one word for each `MeetingStatus` and the one `MeetingWords.Status`
    // reads. A word with an ellipsis is something running or waiting its turn.
    public static UiText NoAudio { get; } = new("Sin audio", "No audio");

    public static UiText Recorded { get; } = new("Grabada", "Recorded");

    public static UiText Queued { get; } = new("En cola…", "Queued…");

    public static UiText Transcribing { get; } = new("Transcribiendo…", "Transcribing…");

    public static UiText Transcribed { get; } = new("Transcrita", "Transcribed");

    public static UiText Summarising { get; } = new("Resumiendo…", "Summarising…");

    public static UiText Summarised { get; } = new("Resumida", "Summarised");

    public static UiText Ignored { get; } = new("Ignorada", "Ignored");

    public static UiText StoppedForAPerson { get; } = new("Detenida", "Stopped");

    // Why a transcription or a summary failed for good, one per JobFailure, chosen from the kind
    // and never from LastError's own English. Above the press, on the meeting's own row.
    public static UiText NotSentNoKeyOnThisMachine { get; } = new(
        "No se envió: falta la clave de Deepgram. No se cobró nada.",
        "Not sent: the Deepgram key is missing. Nothing was charged.");

    public static UiText DeepgramRefusedTheKey { get; } = new(
        "Deepgram no aceptó la clave. No se cobró nada.",
        "Deepgram refused the key. Nothing was charged.");

    public static UiText DeepgramAccountOutOfCredit { get; } = new(
        "Deepgram rechazó la cuenta: sin crédito. No se transcribió nada.",
        "Deepgram refused the account: out of credit. Nothing was transcribed.");

    public static UiText DeepgramAccountOverItsRate { get; } = new(
        "Deepgram pidió esperar: demasiados pedidos. No se transcribió nada.",
        "Deepgram asked to wait: too many requests. Nothing was transcribed.");

    public static UiText DeepgramRefusedTheRequest { get; } = new(
        "Deepgram rechazó el pedido. No se transcribió nada.",
        "Deepgram refused the request. Nothing was transcribed.");

    public static UiText DeepgramWasNotReached { get; } = new(
        "No se pudo llegar a Deepgram. No se envió ni se cobró nada.",
        "Deepgram could not be reached. Nothing was sent or charged.");

    public static UiText NotSentTheAudioIsMissing { get; } = new(
        "No se envió: falta el audio. No se cobró nada.",
        "Not sent: the audio is missing. Nothing was charged.");

    public static UiText NotSentTheCorpusRefused { get; } = new(
        "No se envió: no se pudo guardar el intento. No se cobró nada.",
        "Not sent: the attempt could not be saved. Nothing was charged.");

    public static UiText TheSummaryWasNotAccepted { get; } = new(
        "El resumen no se aceptó. Se puede pedir otro.",
        "The summary was not accepted. Another can be asked for.");

    public static UiText NotSentNoSummariserOnThisMachine { get; } = new(
        "No se envió: Claude Code no está o no respondió. No se gastó nada.",
        "Not sent: Claude Code is missing or did not answer. Nothing was spent.");

    public static UiText TheSummariserDidNotAnswer { get; } = new(
        "Claude Code no devolvió un resumen. Se puede pedir otro.",
        "Claude Code returned no summary. Another can be asked for.");

    public static UiText NotSentAMemoryFileWasInTheWay { get; } = new(
        "No se envió: Claude Code habría leído en el resumen un CLAUDE.md de una carpeta por encima de donde se preparan los resúmenes. No se gastó nada. Muévalo o bórrelo y pida otro.",
        "Not sent: Claude Code would have read into the summary a CLAUDE.md from a folder above where summaries are prepared. Nothing was spent. Move or delete it and ask for another.");

    public static UiText NotSentMoveThisMemoryFile { get; } = new(
        "No se envió: Claude Code habría leído {0} en el resumen. No se gastó nada. Muévalo o bórrelo y pida otro.",
        "Not sent: Claude Code would have read {0} into the summary. Nothing was spent. Move or delete it and ask for another.");

    // The two answers, and what comes back of them.
    public static UiText Transcribe { get; } = new("Transcribir", "Transcribe");

    public static UiText Summarise { get; } = new("Resumir", "Summarise");

    public static UiText ItIsInTheQueueNow { get; } =
        new("Listo: quedó en cola.", "Done: it is in the queue.");

    public static UiText ItIsIgnoredForNow { get; } =
        new("Listo: ignorada por ahora.", "Done: ignored for now.");
}
