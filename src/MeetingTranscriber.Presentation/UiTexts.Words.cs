namespace MeetingTranscriber.Presentation;

// What the application says on the screen where words that come out wrong are corrected.
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
    public static UiText SaveTheTicked { get; } =
        new("Guardar las {0} marcadas", "Save the {0} ticked");

    public static UiText MaybeTheseCameOutWrong { get; } =
        new("Puede que estas hayan salido mal", "These may have come out wrong");

    public static UiText ItIsThatOne { get; } = new("Sí, es esa", "Yes, that one");

    // A press answering a question, not the lower-case word in a sentence that No is.
    public static UiText ItIsNot { get; } = new("No", "No");

    public static UiText MoreLeft { get; } = new("Quedan {0} más.", "{0} more left.");

    public static UiText AlreadyFixed { get; } = new("ya arregladas", "already fixed");

    public static UiText AndMore { get; } = new("y {0} más", "and {0} more");

    public static UiText NothingHereSeemsWrong { get; } = new(
        "Nada de esta reunión parece haber salido mal.",
        "Nothing in this meeting seems to have come out wrong.");

    public static UiText NothingWrittenLikeIt { get; } =
        new("Ninguna reunión escribió algo parecido.", "No meeting wrote anything like it.");

    public static UiText SomeMeetingsCouldNotBeRead { get; } = new(
        "Algunas reuniones no se pudieron leer y quedaron afuera.",
        "Some meetings could not be read and were left out.");

    // {0} is OneMeeting or MeetingsCounted.
    public static UiText SavedButNotYetShownIn { get; } = new(
        "Guardado. {0} todavía no lo muestran; se vuelven a escribir la próxima vez que se abra la aplicación.",
        "Saved. {0} do not show it yet; they are written again the next time the application opens.");
}
