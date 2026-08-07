using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The check that runs before anything copies the corpus, and the compaction that is not allowed
/// to leave search answering with the wrong rows.
/// </summary>
public class CorpusIntegrityTests
{
    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string Orphan = "99999999-9999-9999-9999-999999999999";
    private const string When = "2026-08-05T14:00:00.000Z";

    [Fact]
    public void A_corpus_with_nothing_wrong_with_it_passes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 4);

        CorpusIntegrity.Check(context).ShouldBeEmpty();
        Should.NotThrow(() => CorpusIntegrity.Ensure(context));
    }

    [Fact]
    public void An_empty_corpus_passes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// The row the schema says cannot exist. It gets in the way any real one would: through a
    /// connection with foreign keys off, which is SQLite's default and therefore one forgotten
    /// pragma away from being every connection.
    /// </summary>
    [Fact]
    public void A_row_pointing_at_a_meeting_that_is_not_there_fails_and_names_the_table()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 1);
        WithoutForeignKeys(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('orphan', '{Orphan}', 99, 0, 1, 0, 'ch0:speaker_0', 'palabra suelta');
            """);

        var problem = CorpusIntegrity.Check(context).ShouldHaveSingleItem();

        problem.Table.ShouldBe("utterances");
        problem.Check.ShouldBe("foreign_key_check");
        problem.Detail.ShouldContain("meetings");
    }

    [Fact]
    public void A_corpus_that_is_not_sound_throws_saying_what_is_wrong_with_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 1);
        WithoutForeignKeys(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('orphan', '{Orphan}', 99, 0, 1, 0, 'ch0:speaker_0', 'palabra suelta');
            """);

        var failure = Should.Throw<CorpusIntegrityException>(() => CorpusIntegrity.Ensure(context));

        failure.Problems.ShouldHaveSingleItem().Table.ShouldBe("utterances");
        failure.Message.ShouldContain("utterances");
    }

    /// <summary>
    /// An index that has stopped matching the table it indexes. Search still answers, still
    /// reports nothing, and quietly leaves rows out — which is why the check has to compare the
    /// two rather than ask the index whether it feels consistent.
    /// </summary>
    [Fact]
    public void A_search_index_that_no_longer_matches_its_table_fails_and_names_the_index()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 4);
        ForgetOneTurnInTheIndex(context);

        var problem = CorpusIntegrity.Check(context).ShouldHaveSingleItem();

        problem.Table.ShouldBe("utterances_fts");
        problem.Check.ShouldBe("fts_integrity_check");
    }

    [Fact]
    public void Rebuilding_the_indexes_is_what_makes_that_corpus_sound_again()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 4);
        ForgetOneTurnInTheIndex(context);

        CorpusIntegrity.RebuildSearchIndexes(context);

        CorpusIntegrity.Check(context).ShouldBeEmpty();
        Search(context, "comun").Count.ShouldBe(4);
    }

    /// <summary>
    /// The one the whole class is here for. Compacting is allowed to renumber the rowids both
    /// indexes key on, so it is paired with the rebuild rather than left to a caller to remember,
    /// and what search returns is the same on both sides of it.
    /// </summary>
    [Fact]
    public void Compacting_leaves_search_answering_exactly_what_it_answered_before()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 200);

        // Holes in the middle, so compacting has something to close up.
        Sql.Execute(context, "DELETE FROM utterances WHERE ordinal % 2 = 0;");
        var before = Search(context, "comun");

        CorpusIntegrity.Compact(context);

        Search(context, "comun").ShouldBe(before);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// The half of compacting that is easy to leave out, pinned on its own.
    /// </summary>
    /// <remarks>
    /// The test above cannot see it: this SQLite keeps the rowids across a vacuum, so a compaction
    /// that skipped the rebuild would answer the same and pass. Starting from an index that is
    /// already wrong is what makes the rebuild the only thing that can produce the result — and
    /// it is a fair starting point, since a corpus is compacted precisely when nobody has been
    /// watching it closely.
    /// </remarks>
    [Fact]
    public void Compacting_puts_the_indexes_back_and_not_only_the_pages()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 4);
        ForgetOneTurnInTheIndex(context);

        CorpusIntegrity.Compact(context);

        CorpusIntegrity.Check(context).ShouldBeEmpty();
        Search(context, "comun").Count.ShouldBe(4);
    }

    /// <summary>
    /// Search is answered out of the index and not out of the table, so a test that compares two
    /// lists of rows would pass even with the index switched off entirely.
    /// </summary>
    [Fact]
    public void Search_is_the_index_answering_and_not_the_table()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Project(context, turns: 4);

        Search(context, "palabra0").ShouldHaveSingleItem();
        Search(context, "comun").Count.ShouldBe(4);
        Search(context, "inexistente").ShouldBeEmpty();
    }

    /// <summary>What the index would answer, which is the thing a rebuild has to leave alone.</summary>
    private static List<string> Search(CorpusDbContext context, string term) => Sql.Strings(
        context,
        $"""
        SELECT turn.text FROM utterances AS turn
        WHERE turn.rowid IN (SELECT rowid FROM utterances_fts WHERE utterances_fts MATCH '{term}')
        ORDER BY turn.ordinal;
        """);

    /// <summary>
    /// Takes one turn out of the index and leaves it in the table. It is what a vacuum that
    /// renumbered the rowids would look like from the outside, without depending on a particular
    /// SQLite build choosing to renumber them.
    /// </summary>
    private static void ForgetOneTurnInTheIndex(CorpusDbContext context) => Sql.Execute(context, """
        INSERT INTO utterances_fts (utterances_fts, rowid, text)
        VALUES ('delete', (SELECT rowid FROM utterances ORDER BY ordinal LIMIT 1),
                          (SELECT text FROM utterances ORDER BY ordinal LIMIT 1));
        """);

    /// <summary>
    /// Writes with the constraint switched off, the way a corpus acquires an orphan in the first
    /// place.
    /// </summary>
    /// <remarks>
    /// The connection is held open across both statements on purpose. The pragma is per connection,
    /// and EF takes one out of the pool per command and hands it back — so setting it and then
    /// writing lands the write on a different connection, where the interceptor has already turned
    /// foreign keys on. Which is the same reason the interceptor exists.
    /// </remarks>
    private static void WithoutForeignKeys(CorpusDbContext context, string sql)
    {
        context.Database.OpenConnection();
        try
        {
            Sql.Execute(context, "PRAGMA foreign_keys = OFF;");
            Sql.Execute(context, sql);
            Sql.Execute(context, "PRAGMA foreign_keys = ON;");
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static void Project(CorpusDbContext context, int turns)
    {
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{MeetingId}', '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');
            """);

        for (var ordinal = 0; ordinal < turns; ordinal++)
        {
            Sql.Execute(context, $"""
                INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
                VALUES ('u{ordinal}', '{MeetingId}', {ordinal}, {ordinal * 1000}, {(ordinal + 1) * 1000}, 0,
                        'ch0:speaker_0', 'palabra{ordinal} comun');
                """);
        }
    }
}
