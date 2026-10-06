namespace MeetingTranscriber.Presentation;

// What the application says on the screen where words that come out wrong are corrected, and in
// the dialogue that corrects one word from where it is read.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // Reached from the meeting screen's own card, on that meeting's doubtful words, and returning
    // to it through the app bar.
    public static UiText WordsThatComeOutWrong { get; } =
        new("Palabras que salen mal", "Words that come out wrong");

    public static UiText TheWordAsItShouldBe { get; } =
        new("La palabra como va", "The word as it should be");

    // The first choice of the scope drop-down: the correction holds in every meeting.
    public static UiText InEveryMeeting { get; } = new("en todas", "everywhere");

    // The other choices; {0} is the place the meeting is filed under, as a path.
    public static UiText OnlyIn { get; } = new("sólo en {0}", "only in {0}");

    public static UiText HowItCameOutWritten { get; } =
        new("así quedó escrita", "how it came out written");

    // One form's count line: how many times it was written ({0} in the second), then in how many
    // meetings, as OneMeeting or MeetingsCounted.
    public static UiText OnceIn { get; } = new("1 vez · {0}", "once · {0}");

    public static UiText TimesIn { get; } = new("{0} veces · {1}", "{0} times · {1}");

    // The press that saves; {0} is how many forms are ticked.
    public static UiText SaveTheTicked { get; } = new("Guardar {0}", "Save {0}");

    public static UiText MaybeTheseCameOutWrong { get; } =
        new("Posibles errores", "Possible mistakes");

    public static UiText ItIsThatOne { get; } = new("Sí, es esa", "Yes, that one");

    // A press answering a question, not the lower-case word in a sentence that No is.
    public static UiText ItIsNot { get; } = new("No", "No");

    public static UiText MoreLeft { get; } = new("{0} más", "{0} more");

    public static UiText AlreadyFixed { get; } = new("ya arregladas", "already fixed");

    public static UiText AndMore { get; } = new("y {0} más", "and {0} more");

    public static UiText NothingHereSeemsWrong { get; } =
        new("Nada para revisar", "Nothing to review");

    public static UiText NothingWrittenLikeIt { get; } = new("Sin coincidencias", "No matches");

    public static UiText SomeMeetingsCouldNotBeRead { get; } = new(
        "Algunas reuniones no se leyeron.",
        "Some meetings were not read.");

    // {0} is OneMeeting or MeetingsCounted.
    public static UiText SavedButNotYetShownIn { get; } = new(
        "Guardado; {0} aún no lo muestran.",
        "Saved; {0} do not show it yet.");

    // The title of the dialogue that corrects one word where it is read, over a line of the
    // transcript or a voice's quotation: the third dialogue docs/design.md §Notices lists. Its
    // two labels are `HowItCameOutWritten` over the word as it stands and `TheWordAsItShouldBe`
    // over the field, the same two the screen of every word that comes out wrong carries.
    public static UiText FixAWord { get; } = new("Corregir palabra", "Fix a word");

    // Said in the dialogue in place of its field when the word selected is the one a correction
    // already reaching the meeting wrote: a correction keyed on it would leave the transcript as it
    // is and close as though it had saved.
    public static UiText AlreadyCorrected { get; } = new("Ya está corregida", "Already corrected");
}
