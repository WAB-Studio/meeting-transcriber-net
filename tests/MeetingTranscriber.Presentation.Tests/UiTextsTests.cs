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
        // the language the application is in; "no" is the same word in both. The four the redrawn
        // front door added are one answer said four times: **what a machine, a provider or a maker
        // called something is not translated.** A channel's chip is the index the provider reports
        // back, and `docs/design.md` §Type puts every number that gets compared to another one in
        // mono; the engine is what Deepgram called that model; and the product's name is the
        // product's. The last one is the same answer once more: a daily is called a daily in both,
        // and the word came into Spanish from the ceremony rather than being translated out of it.
        string[] sameEitherWayOnPurpose =
        [
            nameof(UiTexts.Channel0),
            nameof(UiTexts.Channel1),
            nameof(UiTexts.EnglishName),
            nameof(UiTexts.No),
            nameof(UiTexts.SpanishName),
            nameof(UiTexts.TheApplicationsName),
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
