namespace MeetingTranscriber.Presentation;

/// <summary>
/// Everything the application says, Spanish first and English second. A screen names an entry
/// here; it never carries the words itself, which is what makes translating a screen a matter of
/// reading one class rather than of finding every literal in it. The class is split across
/// files, one per screen, and `UiTexts.cs` holds what more than one screen says.
/// </summary>
/// <remarks>
/// A catalogue in C# rather than in `.resw`: a name that does not exist is a compile error
/// instead of a blank on screen, both languages sit on the same line so one cannot quietly be
/// added without the other, and none of it needs a packaged host to be read — which is what lets
/// a plain test walk the whole catalogue. `UiTexts.PackagingChecks.cs` is the temporary scaffold's,
/// and it goes when the scaffold does.
/// </remarks>
public static partial class UiTexts
{
    // What the window says it is, in the row every artboard opens with. The same either way
    // because it is the product's name and a product is not translated — and it is a placeholder:
    // `docs/design.md` §The artboards and this page says the name and the mark are the one thing on
    // those drawings that is deliberately unfinished.
    public static UiText TheApplicationsName { get; } =
        new("Meeting Transcriber", "Meeting Transcriber");

    // The whole line and not the parenthesis on its own, which is what makes it an entry here at
    // all: a screen handed "(predeterminado)" would still be the one deciding that a space and a
    // bracket go between it and the device's name, and where the bracket goes is as much a
    // language as the word inside it. The device's name is the maker's and is never translated,
    // which is why it is a value rather than words.
    public static UiText TheDeviceWindowsUsesByDefault { get; } =
        new("{0} (predeterminado)", "{0} (default)");

    // What transcribes the meeting, shown on the card and named again in the settings. Nothing
    // changes it: there is one engine, which is why it is a pill with no chevron under it. A
    // provider's model name is what that provider called it, so it is the same either way.
    public static UiText TheEngineThatTranscribes { get; } =
        new("Deepgram nova-3", "Deepgram nova-3");

    public static UiText Pause { get; } = new("Pausar", "Pause");

    public static UiText Stop { get; } = new("Detener", "Stop");

    /// <summary>
    /// The one word for trying the same thing again, wherever it is offered: a microphone that
    /// stopped responding (<c>docs/design.md</c> §Fallo), and a meeting stopped on a person. One
    /// entry, because two entries with one word are how a translation changes in one place and not
    /// the other.
    /// </summary>
    public static UiText TryAgain { get; } = new("Reintentar", "Try again");

    /// <summary>
    /// The one verb for pointing something somewhere else (<c>docs/design.md</c> §One verb per
    /// act), named for no press in particular. The word is the artboard's —
    /// <c>docs/design/Configuracion.dc.html</c> draws <em>Cambiar</em> on the corpus row — and not
    /// a sentence of its own, because the line beside it has already said what the row is about.
    /// Each press carries its own automation name, since two presses named <em>Cambiar</em> read
    /// the same to somebody who cannot see which row they are beside.
    /// </summary>
    public static UiText Change { get; } = new("Cambiar", "Change");

    /// <summary>
    /// Not <c>Keep</c>, which is what a recording nobody stopped is offered. The two are one word
    /// in English and two in Spanish — conservar is rescuing something that would otherwise go,
    /// guardar is writing an answer down — and sharing the entry would have made the recovery
    /// list's own button read as this one the day either sentence moved.
    /// </summary>
    public static UiText Save { get; } = new("Guardar", "Save");

    // ISC-165.1. Two words and no more, because there is nothing here to say: the application has
    // not thought of a name and is not pretending to. What it must never read as is a name — a
    // date, a folder, the first thing said in the meeting — since a person scanning this list
    // cannot tell a title they wrote from one that was made up for them.
    public static UiText AMeetingNobodyHasNamed { get; } = new("Sin nombre", "Unnamed");

    public static UiText Discard { get; } = new("Descartar", "Discard");

    public static UiText Ignore { get; } = new("Ignorar", "Ignore");

    public static UiText ThatDidNotGoThrough { get; } =
        new("No se pudo: {0}", "That did not go through: {0}");

    // Not a failure. It is what the re-read before every write is for: the screen was drawn
    // before somebody answered the same question somewhere else, and the answer on disk won.
    public static UiText ThatIsNoLongerHowItWas { get; } = new(
        "Eso ya no está como estaba. La lista se volvió a leer.",
        "That is no longer as it was. The list has been read again.");

    // The corpus is not reachable, so an empty list would be a lie. Which refusal it was is the
    // settings screen's to say, and it has the whole table for it — it moved there with the line
    // about where the corpus is kept, which is the one place that names the folder.
    public static UiText TheCorpusCouldNotBeOpened { get; } = new(
        "No se pudo abrir el corpus, así que esta lista no dice nada. La pantalla de "
        + "configuración dice por qué: {0}",
        "The corpus could not be opened, so this list says nothing. The settings screen says "
        + "why: {0}");

    // The player. Hearing what a meeting recorded never costs anything and never waits on a
    // transcription, so none of these words says anything about either.
    public static UiText Play { get; } = new("Reproducir", "Play");

    // Walking away from a form, which is the verb docs/design.md's closed table gives for it.
    public static UiText Cancel { get; } = new("Cancelar", "Cancel");

    // The two entries every picker on this screen opens and closes with. *Ninguno* empties the pill
    // and everything to the right of it; the ellipsis is what says the last one asks a question
    // rather than answering it.
    public static UiText NoneOfThese { get; } = new("Ninguno", "None of these");

    public static UiText NameANewOne { get; } = new("Nombrar uno nuevo…", "Name a new one…");

    // What every picker on that screen offers for whatever already stands in it — a pill over the
    // tree and a row of people alike, because it is one act and one act reads as one entry. The
    // ellipsis says it asks a question rather than answering one, the way *Nombrar uno nuevo…* does.
    // It is also what the line of a classification put by offers over its name.
    public static UiText CorrectThisName { get; } = new("Corregir este nombre…", "Correct this name…");

    // The placeholder on a voice's picker, over Everybody rather than over Ninguno: a voice
    // nobody has named yet reads better as an invitation than as the answer that empties it.
    public static UiText ChooseSomebody { get; } = new("Elegir a alguien", "Choose somebody");

    // What the gear on the front door opens, and the screen's own title. The gear says this out
    // loud rather than only drawing a cog: nothing else on that row is a press, so a shape with no
    // name is the one control there a screen reader would announce as nothing.
    public static UiText Settings { get; } = new("Configuración", "Settings");

    // Taking the key off this machine, which is an act of its own in `docs/design.md`'s verb table.
    public static UiText Remove { get; } = new("Quitar", "Remove");

    public static UiText OneMeeting { get; } = new("1 reunión", "1 meeting");

    public static UiText MeetingsCounted { get; } = new("{0} reuniones", "{0} meetings");

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
}
