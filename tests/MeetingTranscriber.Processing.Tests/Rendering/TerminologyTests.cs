using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// The two rules the Python renderer learned about applying corrections, and the reason they are
/// rules: each one is a way of getting it wrong that looks fine until the corpus has the word that
/// breaks it.
/// </summary>
public class TerminologyTests
{
    [Fact]
    public void A_correction_replaces_the_word_it_names()
    {
        Terminology.Apply("hablamos de quati hoy", [Correct("quati", "Coati")])
            .ShouldBe("hablamos de Coati hoy");
    }

    /// <summary>
    /// Longest first. Correcting "Coati" before "Coati Cloud" would leave the second half of the
    /// longer term stranded beside a corrected first half, and the order rows come back in is not
    /// something a renderer gets to depend on.
    /// </summary>
    [Fact]
    public void A_term_that_is_the_start_of_a_longer_one_does_not_eat_it()
    {
        var corrections = new[] { Correct("quati", "Coati"), Correct("quati cloud", "Coati Cloud") };

        Terminology.Apply("migramos a quati cloud", corrections).ShouldBe("migramos a Coati Cloud");
        Terminology.Apply("migramos a quati cloud", [.. corrections.Reverse()])
            .ShouldBe("migramos a Coati Cloud");
    }

    /// <summary>
    /// Whole words only. Without it, correcting "ml" rewrites the middle of "html", and a corpus of
    /// speech about software is full of short terms that live inside longer words.
    /// </summary>
    [Fact]
    public void A_term_inside_a_longer_word_is_left_alone()
    {
        Terminology.Apply("el ml del html es ml", [Correct("ml", "ML")]).ShouldBe("el ML del html es ML");
    }

    [Fact]
    public void Punctuation_around_a_word_is_not_part_of_it()
    {
        Terminology.Apply("(quati), quati. quati!", [Correct("quati", "Coati")])
            .ShouldBe("(Coati), Coati. Coati!");
    }

    /// <summary>
    /// A corpus of speech has the same word at the start of a sentence and in the middle of one, so
    /// the mode that ignores case is the one the legacy corpus imports under.
    /// </summary>
    [Fact]
    public void Case_is_the_correction_s_to_decide()
    {
        Terminology.Apply("Quati y quati", [Correct("quati", "Coati", TerminologyMatchMode.IgnoreCase)])
            .ShouldBe("Coati y Coati");
        Terminology.Apply("Quati y quati", [Correct("quati", "Coati")]).ShouldBe("Quati y Coati");
    }

    /// <summary>
    /// A term whose edge is punctuation — <c>gh.</c>, <c>c++</c> — has no word boundary to check on
    /// that side. Requiring one would make the correction never apply, which is worse than applying
    /// it once too often.
    /// </summary>
    [Fact]
    public void A_term_that_ends_in_punctuation_still_applies()
    {
        Terminology.Apply("corre gh. y listo", [Correct("gh.", "GitHub CLI")])
            .ShouldBe("corre GitHub CLI y listo");
    }

    [Fact]
    public void A_word_that_carries_an_accent_is_one_word()
    {
        Terminology.Apply("la sesion de hoy", [Correct("sesion", "sesión")]).ShouldBe("la sesión de hoy");
        Terminology.Apply("la sesión de hoy", [Correct("sesion", "sesión")]).ShouldBe("la sesión de hoy");
    }

    [Fact]
    public void Nothing_to_correct_leaves_the_text_as_it_was()
    {
        Terminology.Apply("sin nada que corregir", []).ShouldBe("sin nada que corregir");
    }

    private static TerminologyCorrection Correct(
        string wrong,
        string right,
        TerminologyMatchMode mode = TerminologyMatchMode.Exact) => new()
        {
            Id = Guid.NewGuid(),
            WrongText = wrong,
            CorrectText = right,
            MatchMode = mode,
            CreatedAt = UtcTimestamp.From(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero)),
        };
}
