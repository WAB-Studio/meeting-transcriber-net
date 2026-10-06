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
    /// The first correction to replace a word leaves nothing for a later one of the same form, so
    /// the narrower place has to go first or a correction made under a node loses to one made
    /// everywhere, depending on which row the query returned first.
    /// </summary>
    [Fact]
    public void The_narrower_place_wins_whichever_order_the_rows_came_in()
    {
        // Fixed ids, the everywhere row the smaller: with the place rank gone the id decides, and the
        // wrong row would win every time instead of half of them.
        var everywhere = Correct("quati", "Coati");
        everywhere.Id = new Guid("00000000-0000-0000-0000-000000000001");
        var underANode = Correct("quati", "Kwati");
        underANode.Id = new Guid("00000000-0000-0000-0000-000000000002");
        underANode.NodeId = Guid.NewGuid();

        Terminology.Apply("hablamos de quati", [everywhere, underANode]).ShouldBe("hablamos de Kwati");
        Terminology.Apply("hablamos de quati", [underANode, everywhere]).ShouldBe("hablamos de Kwati");
    }

    /// <summary>
    /// Of two node corrections of one word the deeper node is the more specific: a word somebody
    /// fixed under a project beats the one fixed for its organization, inside that project. The
    /// organization's row takes the smaller id, so without the depth the id would put it first.
    /// </summary>
    [Fact]
    public void The_deeper_node_wins_whichever_order_the_rows_came_in()
    {
        var organization = Correct("quati", "Coati");
        organization.Id = new Guid("00000000-0000-0000-0000-000000000001");
        organization.NodeId = Guid.NewGuid();
        var project = Correct("quati", "Kwati");
        project.Id = new Guid("00000000-0000-0000-0000-000000000002");
        project.NodeId = Guid.NewGuid();
        var depths = new Dictionary<Guid, int> { [organization.NodeId.Value] = 0, [project.NodeId.Value] = 1 };

        Terminology.Apply("hablamos de quati", [organization, project], depths).ShouldBe("hablamos de Kwati");
        Terminology.Apply("hablamos de quati", [project, organization], depths).ShouldBe("hablamos de Kwati");
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
    /// the mode that ignores case is the one the Python corpus's own corrections were written under.
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
    public void A_correction_reaches_a_text_only_where_it_would_replace_a_whole_word()
    {
        Terminology.Reaches("el html del sitio", Correct("ml", "ML")).ShouldBeFalse();
        Terminology.Reaches("el ml del html", Correct("ml", "ML")).ShouldBeTrue();
        Terminology.Reaches("dijo Nubeco", Correct("nubeco", "Nubeko", TerminologyMatchMode.IgnoreCase)).ShouldBeTrue();
        Terminology.Reaches("dijo Nubeco", Correct("nubeco", "Nubeko")).ShouldBeFalse();
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

    /// <summary>
    /// Each mark is a span of the corrected text, so what it covers there is what the correction
    /// wrote, and what it says was there before is what the stored turn held.
    /// </summary>
    [Fact]
    public void Every_word_a_correction_changed_is_marked_where_it_stands()
    {
        var read = Terminology.ApplyMarked(
            "hablamos de quati y de ml hoy",
            [Correct("quati", "Coati"), Correct("ml", "machine learning")]);

        read.Text.ShouldBe("hablamos de Coati y de machine learning hoy");
        read.Marks.Count.ShouldBe(2);
        read.Marks.ShouldAllBe(mark => read.Text.Substring(mark.Start, mark.Length) == mark.After);
        read.Marks.Select(mark => mark.Before).ShouldBe(["quati", "ml"]);
    }

    /// <summary>
    /// Longest first, so "coati" is replaced before "ml" and it is the replacement earlier in the
    /// line, changing the length, that has to carry the mark it did not make.
    /// </summary>
    [Fact]
    public void A_mark_moves_when_an_earlier_replacement_changes_the_length()
    {
        var read = Terminology.ApplyMarked(
            "ml y coati",
            [Correct("ml", "machine learning"), Correct("coati", "Coati")]);

        read.Text.ShouldBe("machine learning y Coati");
        var coati = read.Marks.Single(mark => mark.After == "Coati");
        read.Text.Substring(coati.Start, coati.Length).ShouldBe("Coati");
        read.Marks.Count.ShouldBe(2);
    }

    [Fact]
    public void A_correction_inside_one_already_made_is_one_mark()
    {
        var read = Terminology.ApplyMarked(
            "migramos a quati cloud",
            [Correct("quati cloud", "Coati Cloud"), Correct("cloud", "Nube", TerminologyMatchMode.IgnoreCase)]);

        read.Text.ShouldBe("migramos a Coati Nube");
        var mark = read.Marks.ShouldHaveSingleItem();
        mark.Before.ShouldBe("quati cloud");
        read.Text.Substring(mark.Start, mark.Length).ShouldBe("Coati Nube");
    }

    [Fact]
    public void A_correction_across_a_marks_edge_is_one_mark_over_both()
    {
        var read = Terminology.ApplyMarked(
            "usamos quati cloud en casa",
            [Correct("quati cloud", "Coati Cloud"), Correct("Cloud en", "Nube")]);

        read.Text.ShouldBe("usamos Coati Nube casa");
        var mark = read.Marks.ShouldHaveSingleItem();
        mark.Before.ShouldBe("quati cloud en");
        read.Text.Substring(mark.Start, mark.Length).ShouldBe("Coati Nube");
    }

    [Fact]
    public void A_replacement_that_changes_nothing_leaves_no_mark()
    {
        Terminology.ApplyMarked("la sesión", [Correct("sesión", "sesión")]).Marks.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_reads_what_ApplyMarked_reads()
    {
        var corrections = new[]
        {
            Correct("quati", "Coati"),
            Correct("quati cloud", "Coati Cloud"),
            Correct("ml", "machine learning"),
            Correct("cloud", "Nube", TerminologyMatchMode.IgnoreCase),
        };

        foreach (var text in new[] { "migramos a quati cloud con ml", "html y ml", "nada", "quati" })
        {
            Terminology.Apply(text, corrections).ShouldBe(Terminology.ApplyMarked(text, corrections).Text);
        }
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
