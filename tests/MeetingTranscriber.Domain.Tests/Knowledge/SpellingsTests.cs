using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Domain.Tests.Knowledge;

/// <summary>The one measure of how alike two spellings are, and the one rule for what a word is.</summary>
public class SpellingsTests
{
    [Fact]
    public void Case_accents_and_spaces_do_not_make_two_spellings_less_alike()
    {
        Spellings.Resemblance("Nube co", "nubeco").ShouldBe(1);
        Spellings.Resemblance("sesión", "sesion").ShouldBe(1);
        Spellings.Resemblance("NUBECO", "nubeco").ShouldBe(1);
    }

    [Fact]
    public void A_swapped_pair_of_letters_costs_one_edit()
    {
        Spellings.Resemblance("nubeco", "nubeoc").ShouldBe(1 - (1.0 / 6), 1e-12);
    }

    [Fact]
    public void One_letter_off_resembles_more_than_two_letters_off()
    {
        Spellings.Resemblance("nubeko", "nubeco")
            .ShouldBeGreaterThan(Spellings.Resemblance("nubeko", "nuveco"));
    }

    [Fact]
    public void The_same_edit_costs_less_in_a_longer_word()
    {
        Spellings.Resemblance("abcdefghij", "abcdefghik")
            .ShouldBeGreaterThan(Spellings.Resemblance("ab", "ac"));
    }

    [Fact]
    public void Two_empty_spellings_are_the_same_and_an_empty_one_resembles_nothing()
    {
        Spellings.Resemblance(" ", string.Empty).ShouldBe(1);
        Spellings.Resemblance("nubeco", string.Empty).ShouldBe(0);
    }

    [Fact]
    public void Resemblance_is_symmetric()
    {
        Spellings.Resemblance("dipgram", "deepgram").ShouldBe(Spellings.Resemblance("deepgram", "dipgram"));
    }

    [Fact]
    public void A_word_is_cut_where_a_correction_cuts_it()
    {
        // Decomposed on purpose: the mark that rides on the o is a character of its own, and a
        // rule that left it out would cut "sesión" in two.
        var decomposed = "(quati), c++ y sesión.";

        Spellings.WordsOf(decomposed).ShouldBe(["quati", "c", "y", "sesión"]);
    }

    [Theory]
    [InlineData("Resident Evil,", "Resident Evil")]
    [InlineData("  \"Deepgram\".", "Deepgram")]
    [InlineData("sesión", "sesión")]
    [InlineData("¿Qué dijo Nubeco?", "Qué dijo Nubeco")]
    public void A_selection_is_trimmed_at_both_ends_and_kept_whole_between(string selection, string words) =>
        Spellings.TrimToWords(selection).ShouldBe(words);

    [Theory]
    [InlineData("  .")]
    [InlineData("")]
    [InlineData(null)]
    public void A_selection_with_no_word_in_it_stands_for_nothing(string? selection) =>
        Spellings.TrimToWords(selection).ShouldBeNull();
}
