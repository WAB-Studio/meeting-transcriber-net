using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// What somebody typing into the meetings list is shown: meetings, in the order of where the word
/// is, each once.
/// </summary>
public class MeetingSearchTests
{
    // Digits only: EF writes a Guid in upper case and these rows are raw SQL, and a hex letter would
    // be a meeting EF can never find.
    private const string FilingId = "51000000-0000-0000-0000-000000000000";
    private const string ChangedId = "52000000-0000-0000-0000-000000000000";
    private const string SimilarId = "53000000-0000-0000-0000-000000000000";

    /// <summary>
    /// One meeting in each band for one word. <c>cierre</c> is a summary's in the shared fixture, so
    /// the daily is the second band; the others are made here.
    /// </summary>
    [Fact]
    public void Meetings_come_back_in_the_four_bands_in_order()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);
        AMeeting(context, FilingId, "cierre del mes");
        ATurn(context, written.Budget.ToString(), 9, "ya viene el cierre");
        AMeeting(context, SimilarId, "otra cosa");
        ATurn(context, SimilarId, 0, "cierra la puerta");

        var found = MeetingSearch.Find(context, "cierre");

        found.Select(one => (one.Meeting.Id, one.Band)).ShouldBe(
        [
            (Guid.Parse(FilingId), SearchBand.Filing),
            (written.Daily, SearchBand.Summary),
            (written.Budget, SearchBand.Transcript),
            (Guid.Parse(SimilarId), SearchBand.Similar),
        ]);
    }

    [Fact]
    public void A_meeting_comes_back_once_under_the_best_band_it_reaches()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        // Said in thirty turns of the daily and in its summary.
        var found = MeetingSearch.Find(context, "coati").ShouldHaveSingleItem();

        found.Meeting.Id.ShouldBe(written.Daily);
        found.Band.ShouldBe(SearchBand.Summary);
    }

    [Theory]
    [InlineData("C++")]
    [InlineData("\"")]
    [InlineData("AND")]
    [InlineData("presu*")]
    [InlineData("-")]
    [InlineData("   ")]
    [InlineData("(")]
    public void What_is_typed_is_never_refused(string typed)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        Should.NotThrow(() => MeetingSearch.Find(context, typed));
    }

    [Fact]
    public void Typed_text_with_no_word_in_it_finds_nothing_and_says_nothing()
    {
        MeetingSearch.AsTyped("-").ShouldBeNull();
        MeetingSearch.AsTyped("C++").ShouldBe("\"C\"");
        MeetingSearch.AsTyped("el presupuesto").ShouldBe("\"el\" \"presupuesto\"*");
    }

    [Fact]
    public void A_short_last_word_is_matched_whole_and_a_longer_one_as_a_prefix()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        MeetingSearch.Find(context, "pre").ShouldContain(one => one.Meeting.Id == written.Budget);
        MeetingSearch.Find(context, "pr").ShouldBeEmpty();
    }

    [Fact]
    public void A_word_like_the_one_typed_finds_the_fourth_band()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var found = MeetingSearch.Find(context, "presupuestos").ShouldHaveSingleItem();

        found.Meeting.Id.ShouldBe(written.Budget);
        found.Band.ShouldBe(SearchBand.Similar);
    }

    [Fact]
    public void Nothing_like_it_finds_no_fourth_band()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        MeetingSearch.Find(context, "kkkkkkkk").ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-92.1. The query goes through <c>RawSql</c>, which runs its own command, so
    /// <c>EmittedSql</c> cannot see it; what can be said is what the file names.
    /// </summary>
    [Fact]
    public void The_fourth_band_reads_the_terms_and_not_the_turns()
    {
        var source = SourceText.WithoutProse(
            RepositoryTree.At("src/MeetingTranscriber.Infrastructure/Storage/MeetingSearch.cs"));

        source.ShouldContain("utterances_fts_terms");
        source.ShouldNotContain("context.Utterances");
        source.ShouldNotContain("MisspelledWords.LikeTyped");
    }

    [Fact]
    public void A_corrected_term_finds_the_meetings_that_wrote_it_wrong()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);
        AMeeting(context, ChangedId, "otra mas");
        ATurn(context, ChangedId, 0, "hablamos del kuati");
        context.TerminologyCorrections.Add(new TerminologyCorrection
        {
            Id = Guid.NewGuid(),
            WrongText = "kuati",
            CorrectText = "Coati",
            MatchMode = TerminologyMatchMode.Exact,
            CreatedAt = Corpus.March,
        });
        context.SaveChanges();

        MeetingSearch.Find(context, "Coati").ShouldContain(one => one.Meeting.Id == Guid.Parse(ChangedId));
    }

    [Fact]
    public void A_meeting_on_its_way_out_is_not_found()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);
        Sql.Execute(context, $"UPDATE meetings SET lifecycle_state = 'deleting', deleted_at = '{Corpus.When}' WHERE id = '{written.Budget}';");

        MeetingSearch.Find(context, "presupuesto").ShouldBeEmpty();
        MeetingSearch.Find(context, "presupuestos").ShouldBeEmpty();
    }

    private static void AMeeting(CorpusDbContext context, string id, string title) =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, title, context, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{id}', '{title}', NULL, '2026-03-05T14:00:00.000Z', 'multichannel', 'es', 'active', '{Corpus.When}', '{Corpus.When}');
            """);

    private static void ATurn(CorpusDbContext context, string meeting, int ordinal, string text) =>
        Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{meeting}-{ordinal}', '{meeting}', {ordinal}, {ordinal * 1000}, {(ordinal + 1) * 1000}, 0, 'ch0:speaker_0', '{text}');
            """);
}
