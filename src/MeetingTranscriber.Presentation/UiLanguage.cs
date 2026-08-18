namespace MeetingTranscriber.Presentation;

/// <summary>
/// The languages the application itself is written in. Closed on purpose: every text exists in
/// all of them or the code does not compile, which is what keeps the two from drifting apart.
/// </summary>
/// <remarks>
/// Not the language a meeting was spoken in. That one is a property of a recording, is whatever
/// the provider supports, and never reaches this type — `arquitectura.md` §6.5 calls it `idioma`
/// too and means something else entirely.
/// </remarks>
public enum UiLanguage
{
    Spanish,
    English,
}
