using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Domain.Tests.Knowledge;

/// <summary>Finding the words a transcript gets wrong from what the corpus already holds.</summary>
public class MisspelledWordsTests
{
    private static readonly Guid One = Guid.NewGuid();
    private static readonly Guid Two = Guid.NewGuid();
    private static readonly Guid Three = Guid.NewGuid();

    [Fact]
    public void A_typed_word_comes_back_with_each_way_it_was_written_closest_first_and_how_often()
    {
        var turns = new List<TurnText>
        {
            new(One, "nubeco y nubeco"),
            new(Two, "Nubeco"),
            new(Three, "nubeco"),
            new(One, "nuveco"),
        };
        turns.AddRange(Enumerable.Repeat(new TurnText(Two, "nubes"), 9));

        var forms = MisspelledWords.LikeTyped("Nubeko", turns);

        // Four occurrences over three meetings — counting turns would say three — and ahead of
        // the nine "nubes", which resemble it less.
        forms[0].Text.ShouldBe("nubeco");
        forms[0].Times.ShouldBe(4);
        forms[0].Meetings.ShouldBe(3);
        forms.Select(form => form.Text).ShouldBe(["nubeco", "nubes", "nuveco"]);
    }

    [Fact]
    public void A_word_typed_as_one_is_found_written_as_two()
    {
        var forms = MisspelledWords.LikeTyped("Nubeko", [new TurnText(One, "la nube co ayer")]);

        forms.Select(form => form.Text).ShouldContain("nube co");
    }

    [Fact]
    public void A_word_with_a_short_word_beside_it_is_not_offered_as_a_phrase()
    {
        var forms = MisspelledWords.LikeTyped(
            "Nubeko",
            [
                new TurnText(One, "la nubeco"),
                new TurnText(One, "nubeco y"),
                new TurnText(Two, "de nubeco"),
                new TurnText(Two, "en nubeco"),
            ]);

        forms.Select(form => form.Text).ShouldBe(["nubeco"]);
        forms[0].Times.ShouldBe(4);
    }

    [Fact]
    public void Every_way_it_was_written_comes_back()
    {
        const string typed = "abcdefghij";
        var variants = new List<string>();
        for (var at = 0; at < typed.Length; at++)
        {
            foreach (var instead in "xy")
            {
                variants.Add(typed[..at] + instead + typed[(at + 1)..]);
            }
        }

        variants.AddRange(Enumerable.Range(0, 5).Select(at => typed[..at] + "z" + typed[(at + 1)..]));

        var forms = MisspelledWords.LikeTyped(typed, variants.Select(variant => new TurnText(One, variant)));

        variants.Distinct().Count().ShouldBe(25);
        forms.Count.ShouldBe(25);
    }

    [Fact]
    public void The_spelling_that_was_typed_is_not_offered()
    {
        MisspelledWords.LikeTyped("Nubeko", [new TurnText(One, "Nubeko")]).ShouldBeEmpty();
    }

    [Fact]
    public void A_term_with_no_word_in_it_finds_nothing()
    {
        MisspelledWords.LikeTyped(" ... ", [new TurnText(One, "nubeco")]).ShouldBeEmpty();
    }

    [Fact]
    public void A_doubtful_word_that_comes_up_often_and_resembles_a_far_commoner_one_is_offered()
    {
        var suspects = MisspelledWords.Unprompted(Heard(), []);

        var suspect = suspects.ShouldHaveSingleItem();
        suspect.Word.ShouldBe("dipgram");
        suspect.LooksLike.ShouldBe("deepgram");
        suspect.LooksLikeTimes.ShouldBe(40);
    }

    [Fact]
    public void A_word_heard_with_confidence_is_not_offered()
    {
        MisspelledWords.Unprompted(Heard(dipgramConfidence: 0.9), []).ShouldBeEmpty();
    }

    [Fact]
    public void A_word_heard_too_rarely_is_not_offered()
    {
        MisspelledWords.Unprompted(Heard(dipgramTimes: 2), []).ShouldBeEmpty();
    }

    [Fact]
    public void A_word_too_short_to_judge_is_not_offered()
    {
        MisspelledWords.Unprompted([new("dip", 5, 0.4), new("deep", 40, 0.95)], []).ShouldBeEmpty();
    }

    [Fact]
    public void A_word_heard_exactly_as_often_as_is_asked_and_exactly_as_far_outnumbered_is_offered()
    {
        MisspelledWords.Unprompted(Heard(dipgramTimes: MisspelledWords.AtLeast, deepgramTimes: MisspelledWords.AtLeast * MisspelledWords.FarMoreOften), [])
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void A_word_whose_lookalike_is_not_far_commoner_is_not_offered()
    {
        MisspelledWords.Unprompted(Heard(deepgramTimes: 24), []).ShouldBeEmpty();
    }

    [Fact]
    public void A_word_already_corrected_is_not_offered()
    {
        MisspelledWords.Unprompted(Heard(), ["Dipgram"]).ShouldBeEmpty();
    }

    [Fact]
    public void Words_heard_are_counted_case_blind_and_without_the_punctuation_around_them()
    {
        var tally = MisspelledWords.Tally(
        [
            new WordAsHeard("Lento.", 0.9),
            new WordAsHeard("lento", 0.7),
            new WordAsHeard("e-mail", 0.5),
        ]);

        var lento = tally.Single(word => word.Word == "lento");
        lento.Times.ShouldBe(2);
        lento.MeanConfidence.ShouldBe(0.8, 1e-12);
        tally.Select(word => word.Word).ShouldBe(["e", "lento", "mail"]);
    }

    [Fact]
    public void A_meetings_suspects_are_the_ones_it_heard_most_heard_first()
    {
        var suspects = new[]
        {
            new SuspectWord("dipgram", 9, 0.4, "deepgram", 90),
            new SuspectWord("nubeco", 5, 0.4, "nubeko", 50),
            new SuspectWord("quatii", 3, 0.4, "quati", 30),
        };
        var thisMeeting = new[]
        {
            new HeardWord("nubeco", 3, 0.4),
            new HeardWord("dipgram", 1, 0.4),
            new HeardWord("hola", 20, 0.99),
        };

        MisspelledWords.HeardIn(suspects, thisMeeting).Select(suspect => suspect.Word)
            .ShouldBe(["nubeco", "dipgram"]);
    }

    private static HeardWord[] Heard(
        double dipgramConfidence = 0.40, int dipgramTimes = 5, int deepgramTimes = 40) =>
    [
        new("dipgram", dipgramTimes, dipgramConfidence),
        new("deepgram", deepgramTimes, 0.95),
        new("hola", 300, 0.99),
    ];
}
