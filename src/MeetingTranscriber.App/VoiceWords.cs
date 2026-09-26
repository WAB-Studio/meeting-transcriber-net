using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Presentation;

namespace MeetingTranscriber.App;

/// <summary>
/// What the screen that names voices, and the meeting screen's card beside it, say about one
/// <see cref="Voice"/> — each already read in the language it is asked for.
/// </summary>
/// <remarks>
/// Held apart from <see cref="Voice"/> itself, which is <c>Domain</c> and carries no language: a
/// screen turns what it decided into words, and never the other way round.
/// </remarks>
internal static class VoiceWords
{
    /// <summary>
    /// What a voice is called until somebody has named it, and what it stays called on the pill
    /// that offers it: <em>Tu micrófono</em> for the microphone's own voice, <em>Voz N</em> for
    /// every other one.
    /// </summary>
    public static string Handle(Voice voice, UiLanguage language)
    {
        ArgumentNullException.ThrowIfNull(voice);

        return voice.IsTheMicrophonesOwn
            ? UiTexts.YourMicrophone.In(language)
            : UiTexts.VoiceNumbered.In(language, voice.Number);
    }

    /// <summary>What a voice reads as: their name once somebody has answered, and its handle while
    /// nobody has.</summary>
    public static string ReadsAs(Voice voice, UiLanguage language)
    {
        ArgumentNullException.ThrowIfNull(voice);

        return voice.PersonName ?? Handle(voice, language);
    }

    /// <summary>How many turns a voice said, as the count a person reads.</summary>
    public static string TurnsSaid(int turns, UiLanguage language) => turns == 1
        ? UiTexts.OneTurn.In(language)
        : UiTexts.TurnsSaid.In(language, turns);
}
