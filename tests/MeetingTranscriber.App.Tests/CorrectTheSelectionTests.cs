namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The press that stands at the end of selected words, held to what no build agent can run: that it
/// trims the selection by the Domain's one rule, that it is the application's own and not Windows'
/// menu, and that both screens that offer it hand the window the same thing.
/// </summary>
/// <remarks>
/// What a selection is trimmed to — <c>Resident Evil,</c> is <c>Resident Evil</c>, and a selection
/// with no word in it is nothing — is <c>SpellingsTests</c>, which runs: the rule lives beside the one
/// for what a word is, in a project this one can reference. A popup needs a window, which is what
/// this reads source instead of.
/// </remarks>
public class CorrectTheSelectionTests
{
    private static readonly string Offer =
        Path.Combine("MeetingTranscriber.App", "CorrectTheSelection.cs");

    [Fact]
    public void The_selection_is_trimmed_by_the_domains_rule_and_never_cut_at_a_space()
    {
        var source = File.ReadAllText(AppSources.At(Offer).FullName);

        source.ShouldContain("Spellings.TrimToWords(words.SelectedText)");
        source.ShouldNotContain("WordsOf(");
        source.ShouldNotContain("Split(");
    }

    [Fact]
    public void The_press_is_the_applications_own_and_never_a_menu_item()
    {
        var source = File.ReadAllText(AppSources.At(Offer).FullName);

        source.ShouldContain("new Popup");
        source.ShouldContain("ShouldConstrainToRootBounds = true");
        source.ShouldContain("AllowFocusOnInteraction = false");
        source.ShouldContain("press.RequestedTheme = words.ActualTheme");
        source.ShouldNotContain("MenuFlyout");
        source.ShouldNotContain("ContextFlyout");
    }

    [Fact]
    public void Both_screens_that_offer_it_hand_the_window_the_same_event()
    {
        foreach (var screen in new[] { "ReadingAMeeting.xaml.cs", "SayingWhoIsWho.xaml.cs" })
        {
            var source = File.ReadAllText(AppSources.At(Path.Combine("MeetingTranscriber.App", screen)).FullName);

            source.ShouldContain("CorrectTheSelection.OfferOver(");
            source.ShouldContain("EventHandler<WordsToCorrect>? CorrectWords");
        }
    }
}
