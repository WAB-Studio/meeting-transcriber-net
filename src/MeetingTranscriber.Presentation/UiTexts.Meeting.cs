namespace MeetingTranscriber.Presentation;

// What the application says on the screen one meeting is read from.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    public static UiText SummariseAgain { get; } = new("Resumir de nuevo", "Summarise again");

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

    // The data-rank label above the rows of a meeting's summaries, and the automation name of their
    // panel (docs/design.md, The flow). Read by ReadingAMeeting.
    public static UiText ThisMeetingsSummaries { get; } = new(
        "resúmenes de esta reunión",
        "this meeting's summaries");

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

    // Why the meeting has no summary: the last attempt was refused. One sentence naming the
    // condition, from ExtractionCondition, filled into one of these two depending on whether the
    // refusal is about one statement or about the document as a whole.
    public static UiText SummaryNotAccepted { get; } = new(
        "No hay resumen: el último que llegó no se aceptó. {0}",
        "There is no summary: the last one that came back was not accepted. {0}");

    public static UiText SummaryNotAcceptedOn { get; } = new(
        "No hay resumen: el último que llegó no se aceptó. {0} Sobre «{1}».",
        "There is no summary: the last one that came back was not accepted. {0} On “{1}”.");

    // The same refusal over a meeting that already has a summary, so it cannot open with "there is
    // no summary". Said in place of the sentence that only says the summary asked for again
    // failed, never beside it; asking again is already on the screen whenever these show.
    public static UiText TheLastSummaryWasNotAccepted { get; } = new(
        "El último que llegó no se aceptó. {0}",
        "The last one that came back was not accepted. {0}");

    public static UiText TheLastSummaryWasNotAcceptedOn { get; } = new(
        "El último que llegó no se aceptó. {0} Sobre «{1}».",
        "The last one that came back was not accepted. {0} On “{1}”.");

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

    public static UiText SayWhoIsWho { get; } = new("Decir quién es quién", "Say who is who");

    // The label over who spoke, on the meeting screen's card. Mono at the data rank, like
    // *sobre qué fue* and *lo que se dijo de esto* beside it on their own screens.
    public static UiText WhoSpoke { get; } = new("quién habló", "who spoke");

    // The data-rank label over the meeting screen's card, in the register of *quién habló*.
    public static UiText WordsThatComeOutWrongHere { get; } =
        new("palabras que salen mal", "words that come out wrong");

    // The press on that card; docs/design.md §One verb per act's own verb for this act.
    public static UiText CorrectWords { get; } = new("Corregir palabras", "Correct words");
}
