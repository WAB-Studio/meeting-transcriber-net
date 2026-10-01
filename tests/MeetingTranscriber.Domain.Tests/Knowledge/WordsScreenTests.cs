using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Knowledge;

/// <summary>What the window that corrects words decides from its controls, decided without the window.</summary>
public class WordsScreenTests
{
    private static readonly UtcTimestamp Early = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");
    private static readonly UtcTimestamp Late = UtcTimestamp.Parse("2026-09-26T09:00:00.000Z");

    /// <summary>
    /// The line <c>Correcciones.dc.html</c> draws: <c>nubeco</c> is ticked and <c>nuveco</c> is not.
    /// It goes red with the line at 0.6, where the second would start ticked.
    /// </summary>
    [Fact]
    public void A_form_written_nearly_as_typed_starts_ticked()
    {
        WordsScreen.StartsTicked(new WrittenForm("nubeco", 4, 3, 0.83)).ShouldBeTrue();
        WordsScreen.StartsTicked(new WrittenForm("nuveco", 1, 1, 0.67)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("", 2, false)]
    [InlineData("   ", 2, false)]
    [InlineData("Nubeko", 0, false)]
    [InlineData("Nubeko", 1, true)]
    public void Nothing_typed_or_nothing_ticked_is_not_saved(string typed, int ticked, bool saved)
    {
        WordsScreen.MayBeSaved(typed, ticked).ShouldBe(saved);
    }

    /// <summary>
    /// The same right word under a node and everywhere are two rows. It goes red with the scope left
    /// out of the grouping, which would merge them into one.
    /// </summary>
    [Fact]
    public void The_corrections_of_one_word_in_one_place_are_one_row_newest_first()
    {
        var node = Guid.NewGuid();
        var made = new[]
        {
            Made("nubeco", "Nubeko", null, Early),
            Made("nube co", "Nubeko", null, Late),
            Made("nubeco", "Nubeko", node, Early),
            Made("deepgram", "Deepgram", null, Early),
        };

        var rows = WordsScreen.Corrected(made);

        rows.Count.ShouldBe(3);
        rows[0].Right.ShouldBe("Nubeko");
        rows[0].Under.ShouldBeNull();
        rows[0].Wrong.ShouldBe(["nube co", "nubeco"]);
        rows[0].Latest.ShouldBe(Late);

        // Equal instants fall back to the right word, ordinally.
        rows[1].Right.ShouldBe("Deepgram");
        rows[2].Right.ShouldBe("Nubeko");
        rows[2].Under.ShouldBe(node);
        rows[2].Wrong.ShouldBe(["nubeco"]);
    }

    private static TerminologyCorrection Made(string wrong, string right, Guid? node, UtcTimestamp at) => new()
    {
        Id = Guid.NewGuid(),
        WrongText = wrong,
        CorrectText = right,
        NodeId = node,
        CreatedAt = at,
    };
}
