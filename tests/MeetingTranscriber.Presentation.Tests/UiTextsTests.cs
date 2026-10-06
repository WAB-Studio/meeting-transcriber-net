using System.Reflection;

namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-152, over the catalogue. <see cref="UiTextTests"/> proves a text missing a language cannot
/// be built; this walks every text the application actually has and proves none of them is
/// anything else.
/// </summary>
public class UiTextsTests
{
    /// <summary>
    /// Every entry, by the name a screen calls it. Reading them is also what runs every one of
    /// their constructors, so the guards in <see cref="UiText"/> are guards over this file and
    /// not only over the type.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, UiText Text)> Catalogue =
        typeof(UiTexts)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(UiText))
            .Select(property => (property.Name, (UiText)property.GetValue(null)!))
            .ToArray();

    public static TheoryData<string> Names() => [.. Catalogue.Select(entry => entry.Name)];

    [Fact]
    public void The_catalogue_holds_what_the_application_says()
    {
        Catalogue.ShouldNotBeEmpty();

        // Nothing else on the type: a helper or a constant among the texts would be a text the
        // walk below never sees, which is the one way an entry gets to skip these checks.
        typeof(UiTexts)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType != MemberTypes.Method)
            .Select(member => member.Name)
            .ShouldBe(Catalogue.Select(entry => entry.Name), ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_text_exists_in_both_languages(string name)
    {
        var text = Catalogue.Single(entry => entry.Name == name).Text;

        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            text.In(language).ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void No_two_entries_are_the_same_text()
    {
        // Two names for one thing is how a translation gets changed in one place and not in the
        // other. Sharing the entry is what keeps them saying the same thing.
        Catalogue
            .GroupBy(entry => entry.Text.Spanish, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.Name))}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Reading_in_one_language_leaves_nothing_in_the_other()
    {
        // The catalogue's half of "changing the language leaves no text in the previous one":
        // every entry whose two versions are different words really does come back different.
        // The screen's half is ScreenTextsTests — that no screen carries words of its own.
        // These read the same either way, and each is named here rather than allowed by a rule,
        // because "the two versions happen to be equal" is also what a translation nobody got round
        // to looks like. One more costs a red test until somebody says which it is.
        //
        // The two language names are equal so a picker stays findable by somebody who cannot read
        // the language the application is in; "no" is the same word in both. The two the redrawn
        // front door kept are one answer said twice: **what a machine, a provider or a maker
        // called something is not translated.** The engine is what Deepgram called that model, and
        // the product's name is the product's. The channel chips went with the channel numbers
        // they said: nothing on a screen names a channel index any more.
        // `TheShapeDaily` is the same answer once more: a daily is called a daily in
        // both, and the word came into Spanish from the ceremony rather than being translated out
        // of it.
        //
        // The export's tick for the recording is `Audio` in both: the word is the same one.
        //
        // The settings screen added `TheEngineThatSummarises`, the maker's answer a fifth time —
        // Anthropic called that model Claude.
        //
        // `ItIsNot`: a press answering a question with the one word both languages spell alike.
        //
        // The pill that offers somebody already there is `{0} · {1}`: the person's name and the
        // organization are the corpus's and the separator is a mark and not a word.
        //
        // The summary models are the maker's names.
        string[] sameEitherWayOnPurpose =
        [
            nameof(UiTexts.EnglishName),
            nameof(UiTexts.ExportsAudio),
            nameof(UiTexts.ItIsNot),
            nameof(UiTexts.No),
            nameof(UiTexts.SomebodyAndWhereTheyBelong),
            nameof(UiTexts.SpanishName),
            nameof(UiTexts.SummaryModelFable),
            nameof(UiTexts.SummaryModelHaiku),
            nameof(UiTexts.SummaryModelOpus),
            nameof(UiTexts.SummaryModelSonnet),
            nameof(UiTexts.TheApplicationsName),
            nameof(UiTexts.TheEngineThatSummarises),
            nameof(UiTexts.TheEngineThatTranscribes),
            nameof(UiTexts.TheShapeDaily),
        ];

        var sameEitherWay = Catalogue
            .Where(entry => string.Equals(entry.Text.Spanish, entry.Text.English, StringComparison.Ordinal))
            .Select(entry => entry.Name)
            .ToArray();

        sameEitherWay.ShouldBe(sameEitherWayOnPurpose, ignoreOrder: true);

        foreach (var (name, text) in Catalogue.Where(entry => !sameEitherWayOnPurpose.Contains(entry.Name)))
        {
            text.In(UiLanguage.Spanish).ShouldNotBe(text.In(UiLanguage.English), name);
        }
    }
}
