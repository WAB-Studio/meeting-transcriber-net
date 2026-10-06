namespace MeetingTranscriber.Presentation;

// What the application says on the screen where voices are given names.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // The screen that names voices, reached from the meeting screen's card. The verb is
    // docs/design.md §One verb per act's own choice for QuienEsQuien.
    public static UiText WhoIsWho { get; } = new("Quién es quién", "Who is who");

    // What a voice is called until somebody names it, and what it stays called on the microphone's
    // own card either way. It goes without a picker only once a Channel row has settled it
    // (HumanLayer.SettleTheMicrophone), which needs the install to know its user — until then it is
    // offered a picker like any other voice. Every other voice on the recording is a number, never
    // the label the corpus stores it under.
    public static UiText YourMicrophone { get; } = new("Tu micrófono", "Your microphone");

    public static UiText VoiceNumbered { get; } = new("Voz {0}", "Voice {0}");

    // How much a voice said, on its own card and on the meeting screen's beside it. Singular and
    // plural are two entries because Spanish and English both need the split.
    public static UiText OneTurn { get; } = new("1 turno", "1 turn");

    public static UiText TurnsSaid { get; } = new("{0} turnos", "{0} turns");

    // The tag on the microphone's own card, drawn either way (docs/design.md §QuienEsQuien's own
    // words). Whether it also carries a picker turns on whether a Channel row has settled it, which
    // needs the install to know its user — not on this tag.
    public static UiText OnlyOneVoice { get; } = new("una sola voz", "only one voice");

    // The label over the further clips of a voice that said little (docs/design.md §QuienEsQuien).
    // Read by SayingWhoIsWho.
    public static UiText OtherStretches { get; } = new("otros fragmentos", "other stretches");

    // The label where the clip of a voice somebody always talked over would be: it has no stretch
    // heard alone to bring (docs/design.md §QuienEsQuien). Read by SayingWhoIsWho.
    public static UiText NeverHeardAlone { get; } = new(
        "siempre habló con alguien encima",
        "somebody always spoke over this voice");

    // Said over the whole screen, in place of every voice's clip, for a meeting with no audio to
    // play a stretch of: a voice is still named by what it said, and this is the one sentence
    // saying why no card on this screen offers to play anything.
    public static UiText ThisMeetingHasNoAudioToListenTo { get; } = new(
        "Esta reunión no tiene audio para escuchar: las voces se nombran por lo que dijeron.",
        "This meeting has no audio to listen to: the voices are named by what they said.");
}
