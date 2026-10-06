namespace MeetingTranscriber.Presentation;

// What the application says on the screen one meeting is read from.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    public static UiText SummariseAgain { get; } = new("Resumir de nuevo", "Summarise again");

    // The name field. A meeting's name is the person's to set at any time after it was recorded,
    // and this is the one place the application offers to set it — so the field says what it is
    // for rather than sitting there as an unlabelled box holding a title.
    public static UiText TheMeetingsName { get; } = new("Título", "Title");

    // The three sections, which are fixed and are the tables the corpus already has. The AI does
    // not choose them, or the corpus stops being able to answer "every decision in August".
    public static UiText WhatWasDecided { get; } = new("Qué se decidió", "What was decided");

    public static UiText WhatIsLeftToDo { get; } = new("Qué queda por hacer", "What is left to do");

    public static UiText WhatWasLeftUnresolved { get; } =
        new("Qué quedó sin resolver", "What was left unresolved");

    // The heading over the whole transcript, which stands under what the AI left (or alone when it
    // left nothing). docs/design.md §Reunion.
    public static UiText TheTranscript { get; } = new("Transcripción", "Transcript");

    // The press on that heading, and the item on a line's right-click menu: the one verb for
    // correcting a word from where it is read (docs/design.md §One verb per act). It is not
    // *Corregir palabras*, which opens the screen of every word that keeps coming out wrong.
    public static UiText CorrectThisWord { get; } = new("Corregir", "Correct");

    // The cell of the table on the meeting's right when the meeting arrived already made and
    // carries no record of what made it. The row names its entry (`Transcribed`, `Summarised`) and
    // this says what is not known, rather than leaving the cell blank — which under a row that says
    // it was done is the screen contradicting itself.
    public static UiText NotRecorded { get; } = new("No consta", "Not recorded");

    // The data-rank label above the rows of a meeting's summaries, and the automation name of their
    // panel (docs/design.md, The flow). Read by ReadingAMeeting.
    public static UiText ThisMeetingsSummaries { get; } = new(
        "resúmenes de esta reunión",
        "this meeting's summaries");

    // Why the meeting has no summary: the last attempt was refused. One sentence naming the
    // condition, from ExtractionCondition, filled into one of these two depending on whether the
    // refusal is about one statement or about the document as a whole.
    public static UiText SummaryNotAccepted { get; } = new(
        "Resumen rechazado. {0}",
        "Summary refused. {0}");

    public static UiText SummaryNotAcceptedOn { get; } = new(
        "Resumen rechazado. {0} Sobre «{1}».",
        "Summary refused. {0} On “{1}”.");

    // The same refusal over a meeting that already has a summary, so it cannot open with "there is
    // no summary". Said in place of the sentence that only says the summary asked for again
    // failed, never beside it; asking again is already on the screen whenever these show.
    public static UiText TheLastSummaryWasNotAccepted { get; } = new(
        "El último se rechazó. {0}",
        "The last one was refused. {0}");

    public static UiText TheLastSummaryWasNotAcceptedOn { get; } = new(
        "El último se rechazó. {0} Sobre «{1}».",
        "The last one was refused. {0} On “{1}”.");

    public static UiText RefusedNotTheSchema { get; } = new(
        "Lo que devolvió no tiene la forma de un resumen.",
        "What came back is not in the shape of a summary.");

    public static UiText RefusedInputNotAsPrepared { get; } = new(
        "No se hizo sobre la transcripción de esta reunión tal como está ahora.",
        "It was not made from this meeting's transcript as it stands now.");

    public static UiText RefusedAnotherMeeting { get; } = new(
        "Dice ser de otra reunión.",
        "It says it is about another meeting.");

    public static UiText RefusedSpeakerNotInTheMeeting { get; } = new(
        "Nombra una voz que esta reunión no tiene.",
        "It names a voice this meeting does not have.");

    public static UiText RefusedNoEvidence { get; } = new(
        "Afirma algo sin citar dónde se dijo.",
        "It states something without citing where it was said.");

    public static UiText RefusedNoSuchTurn { get; } = new(
        "Cita un turno que esta reunión no tiene.",
        "It cites a turn this meeting does not have.");

    public static UiText RefusedNotTheTurnCited { get; } = new(
        "Cita un turno con un momento o una voz que no son los de ese turno.",
        "It cites a turn at a moment or in a voice that is not that turn's.");

    public static UiText RefusedQuoteNotInTheTurn { get; } = new(
        "Cita palabras que ese turno no dice.",
        "It quotes words that turn does not say.");

    public static UiText RefusedCitedAgainElsewhere { get; } = new(
        "Volvió a traer un enunciado que se le pidió quitar, citando otra cosa.",
        "It brought back a statement it was asked to remove, citing something else.");

    // How far into the meeting the playback has got. It labels the track rather than sitting
    // beside it: a slider hands its value back through a pattern of its own, and this is the name
    // that value is read under.
    public static UiText HowFarIntoTheMeeting { get; } =
        new("Por dónde va la reunión", "How far into the meeting");

    // The press on each thing the AI left. It carries the minute it was said at as its words, and
    // this is what says what pressing it does — the same act wherever a citation appears.
    public static UiText WhereThisWasSaid { get; } = new("Dónde se dijo esto", "Where this was said");

    // The two absences a screen with no player has to tell apart. The audio is a source: it was
    // never produced from anything and cannot be produced again, so a meeting recorded whose file
    // is gone is something to look at rather than something still to come.
    public static UiText ThereIsNoRecordingUnderThisMeetingYet { get; } = new(
        "Todavía no hay grabación.",
        "There is no recording yet.");

    public static UiText TheRecordingFileIsMissing { get; } = new(
        "Falta el archivo de audio.",
        "The audio file is missing.");

    // Said where the player would be. A machine with nothing to play through, or a recording whose
    // file has gone, is not something this screen can do anything about — so it says what happened
    // and leaves the rest of the meeting readable.
    public static UiText ThisMeetingWillNotPlay { get; } = new(
        "No se pudo reproducir: {0}",
        "Would not play: {0}");

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

    // The press on the meeting's card of voices: docs/design.md §One verb per act's own verb for
    // putting names on them, in the imperative the rest of the screen is.
    public static UiText SayWhoIsWho { get; } = new("Nombrar voces", "Name voices");

    // The label over who spoke, on the meeting screen's card. Mono at the data rank, like
    // *sobre qué fue* and *lo que se dijo de esto* beside it on their own screens.
    public static UiText WhoSpoke { get; } = new("quién habló", "who spoke");

    // The data-rank label over the meeting screen's card, in the register of *quién habló*.
    public static UiText WordsThatComeOutWrongHere { get; } =
        new("palabras que salen mal", "words that come out wrong");

    // The press on that card; docs/design.md §One verb per act's own verb for this act.
    public static UiText CorrectWords { get; } = new("Corregir palabras", "Correct words");

    // The player's volume slider: its name in the automation tree, and nothing is drawn beside it.
    public static UiText Volume { get; } = new("Volumen", "Volume");
}
