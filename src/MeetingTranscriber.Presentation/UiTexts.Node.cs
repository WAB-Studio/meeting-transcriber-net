namespace MeetingTranscriber.Presentation;

// What the application says on the screen that tells one node's story.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // What this screen is about, over the path. Mono at the data rank, like *sobre qué fue* on the
    // meeting's own screen: it names the block rather than heading a part of the screen.
    public static UiText WhatWasSaidAboutThis { get; } =
        new("lo que se dijo de esto", "what was said about this");

    // A node nothing has been said about. A real state and not a gap: a node somebody made on the
    // classification screen before any meeting was filed under it has no story yet, and saying so is
    // the answer.
    public static UiText NothingHasBeenSaidAboutThisYet { get; } =
        new("Todavía no se dijo nada de esto", "Nothing has been said about this yet");

    // Under the last card, when the read came back full — and alone, in the empty arm, when the
    // first card alone already filled the read and none was drawn at all: that arm's other text,
    // NothingHasBeenSaidAboutThisYet, would say nothing was ever said, and this is the one there
    // that says the truer thing, that the screen was cut instead. How much more is not said either
    // way: what the screen knows is that it was cut and never by how much.
    public static UiText ThereIsMoreThanThisScreenShows { get; } =
        new("Hay más de lo que entra en esta pantalla", "There is more here than this screen shows");
}
