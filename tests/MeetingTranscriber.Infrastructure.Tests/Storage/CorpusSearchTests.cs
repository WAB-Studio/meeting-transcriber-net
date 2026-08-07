using System.Data;

using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// What the corpus answers when somebody asks it a question, and what it deliberately does not
/// answer with.
/// </summary>
/// <remarks>
/// A search test can pass while the index is switched off entirely — a scan of the table returns
/// the same rows — so what these look for is what only the index can produce: a snippet, a ranking,
/// and the same answers on both sides of the two indexes being thrown away and rebuilt.
/// </remarks>
public class CorpusSearchTests
{
    [Fact]
    public void A_hit_carries_the_meeting_the_date_the_title_a_snippet_and_where_it_was_said()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "presupuesto").ShouldHaveSingleItem();

        hit.MeetingId.ShouldBe(written.Budget);
        hit.StartedAt.ShouldBe(Corpus.March);
        hit.Title.ShouldBe(Corpus.BudgetTitle);
        hit.Source.ShouldBe(SearchSource.Turn);
        hit.Snippet.ShouldContain("presupuesto");
        // With the meeting, the position is what a citation anchors on, so a hit can be quoted
        // from without going back to the corpus for it.
        hit.Ordinal.ShouldBe(1);
        hit.Start.ShouldBe(Duration.FromMilliseconds(1000));
        hit.End.ShouldBe(Duration.FromMilliseconds(2000));
    }

    /// <summary>
    /// The summary index answers the same search. A summary is about the whole meeting, so it
    /// carries no offset and nothing to cite — and saying so with nulls is better than an offset of
    /// zero, which reads as "the first millisecond".
    /// </summary>
    [Fact]
    public void A_summary_answers_too_and_says_it_has_no_place_on_the_timeline()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "cierre")
            .ShouldHaveSingleItem();

        hit.Source.ShouldBe(SearchSource.Summary);
        hit.Snippet.ShouldContain("cierre");
        hit.Ordinal.ShouldBeNull();
        hit.Start.ShouldBeNull();
        hit.End.ShouldBeNull();
    }

    /// <summary>Both indexes answer one question, and both kinds of hit come back from one call.</summary>
    [Fact]
    public void One_search_asks_both_indexes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var sources = CorpusSearch.Find(context, "coati").Select(hit => hit.Source).Distinct().Order();

        sources.ShouldBe([SearchSource.Turn, SearchSource.Summary]);
    }

    /// <summary>
    /// The snippet is the index answering. A scan of the table can return the row; only the index
    /// knows which words to cut around, and it elides the rest rather than handing back the turn.
    /// </summary>
    [Fact]
    public void The_snippet_is_an_excerpt_and_not_the_whole_turn()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "aguja").ShouldHaveSingleItem();

        hit.Snippet.ShouldContain("aguja");
        hit.Snippet.ShouldContain("…");
        hit.Snippet.Length.ShouldBeLessThan(Corpus.Haystack.Length);
    }

    /// <summary>
    /// Small answers, because the caller after this one is an agent with a context window. The
    /// limit is a limit on hits and not on meetings: forty answers to "where was this said" in one
    /// long meeting are forty answers.
    /// </summary>
    [Fact]
    public void A_search_returns_at_most_what_it_was_asked_for()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        CorpusSearch.Find(context, "comun").Count.ShouldBe(CorpusSearch.DefaultLimit);
        CorpusSearch.Find(context, "comun", limit: 3).Count.ShouldBe(3);
        Should.Throw<ArgumentOutOfRangeException>(() => CorpusSearch.Find(context, "comun", limit: 0));
    }

    /// <summary>
    /// Ranked and not merely filtered. The turn that is mostly the word being looked for beats the
    /// one that mentions it once in a paragraph, which is the whole difference between a search and
    /// a LIKE.
    /// </summary>
    [Fact]
    public void The_best_match_comes_first()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var first = CorpusSearch.Find(context, "aguja").First();

        first.Snippet.ShouldContain("aguja");

        // The dense hit outranks the one buried in a long turn, whichever order they were written.
        var ranked = CorpusSearch.Find(context, "ranking").Select(hit => hit.Ordinal).ToArray();
        ranked.ShouldBe([Corpus.DenseOrdinal, Corpus.SparseOrdinal]);
    }

    /// <summary>
    /// FTS5's own syntax, because the caller worth serving first is somebody who knows what they
    /// are looking for. Bound as a parameter, so a quote in a query is a word and never SQL.
    /// </summary>
    [Theory]
    [InlineData("presupuesto AND cliente", 1)]
    [InlineData("presupuesto AND inexistente", 0)]
    [InlineData("\"el presupuesto del cliente\"", 1)]
    [InlineData("\"del presupuesto el\"", 0)]
    [InlineData("presu*", 1)]
    public void The_query_is_the_index_query_language(string query, int expected)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        CorpusSearch.Find(context, query).Count.ShouldBe(expected);
    }

    /// <summary>
    /// The cost of that syntax, paid where it can be seen. A query the index cannot parse comes
    /// back naming the query, not as a SQLite error naming a column nobody wrote.
    /// </summary>
    [Fact]
    public void A_query_the_index_cannot_parse_says_so_and_names_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var refused = Should.Throw<CorpusSearchException>(() => CorpusSearch.Find(context, "presupuesto AND"));

        refused.Query.ShouldBe("presupuesto AND");
        refused.Message.ShouldContain("presupuesto AND");
    }

    /// <summary>
    /// A search borrows the connection and hands it back. EF opens one per operation and closes it
    /// again, so a search that left its own open would pin the SQLite handle for as long as the
    /// context lives — which for the UI is the whole session, with the MCP server reading beside it.
    /// </summary>
    [Fact]
    public void A_search_leaves_the_connection_as_it_found_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);
        context.Database.GetDbConnection().State.ShouldBe(ConnectionState.Closed);

        CorpusSearch.Find(context, "presupuesto").ShouldNotBeEmpty();

        context.Database.GetDbConnection().State.ShouldBe(ConnectionState.Closed);
    }

    [Fact]
    public void A_search_for_nothing_is_not_a_search()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        Should.Throw<ArgumentException>(() => CorpusSearch.Find(context, "   "));
    }

    /// <summary>
    /// A meeting on its way out does not answer. Offering it is offering something that will not be
    /// there when somebody opens it, and the deletion is already under way.
    /// </summary>
    [Fact]
    public void A_meeting_being_deleted_is_not_something_search_offers()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        CorpusSearch.Find(context, "presupuesto").ShouldHaveSingleItem();

        Sql.Execute(context, $"""
            UPDATE meetings SET lifecycle_state = 'deleting', deleted_at = '{Corpus.When}'
            WHERE id = '{written.Budget}';
            """);

        CorpusSearch.Find(context, "presupuesto").ShouldBeEmpty();
    }

    /// <summary>
    /// The other half of the task, and the reason the rebuild command exists at all: both indexes
    /// are external content, so throwing them away loses nothing — and search has to prove it by
    /// answering identically afterwards.
    /// </summary>
    [Fact]
    public void Throwing_both_indexes_away_and_rebuilding_them_answers_exactly_the_same()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var before = Corpus.Everything(context);
        before.ShouldNotBeEmpty();

        CorpusIntegrity.RebuildSearchIndexes(context);

        Corpus.Everything(context).ShouldBe(before);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }
}

/// <summary>
/// A corpus with something to find in it: two meetings, turns worth ranking against each other, and
/// a summary so both indexes have an answer.
/// </summary>
internal sealed class Corpus
{
    public const string When = "2026-03-04T14:00:00.000Z";

    public const string BudgetTitle = "revision de presupuesto con Orchard";

    /// <summary>Long enough that a snippet of it is visibly shorter than it is.</summary>
    public const string Haystack =
        "esto es un turno largo con mucho relleno alrededor de la palabra aguja para que el "
        + "recorte tenga algo que elidir a los dos lados y se note que no es el turno entero";

    /// <summary>The dense hit and the buried one, which is what ranking is asked to tell apart.</summary>
    public const int DenseOrdinal = 3;

    public const int SparseOrdinal = 4;

    public static readonly UtcTimestamp March = UtcTimestamp.Parse(When);

    private const string BudgetId = "11111111-1111-1111-1111-111111111111";
    private const string DailyId = "22222222-2222-2222-2222-222222222222";
    private const string JobId = "33333333-3333-3333-3333-333333333333";
    private const string RunId = "44444444-4444-4444-4444-444444444444";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    private Corpus()
    {
    }

    public Guid Budget => Guid.Parse(BudgetId);

    public static Corpus Write(CorpusDbContext context)
    {
        Meeting(context, BudgetId, BudgetTitle);
        Meeting(context, DailyId, "daily de Coati");

        // What the searches above look for, each one there for a reason.
        Turn(context, BudgetId, 0, "arrancamos la reunion");
        Turn(context, BudgetId, 1, "el presupuesto del cliente sube este trimestre");
        Turn(context, BudgetId, 2, Haystack);
        Turn(context, BudgetId, DenseOrdinal, "ranking ranking ranking");
        Turn(context, BudgetId, SparseOrdinal, "una sola mencion de ranking en medio de mucho texto "
            + "de relleno que no dice nada mas y sigue y sigue para bajar la densidad del termino");

        // Enough turns that the default limit is reached and visibly caps the answer.
        for (var ordinal = 5; ordinal < 5 + CorpusSearch.DefaultLimit + 5; ordinal++)
        {
            Turn(context, DailyId, ordinal, $"turno{ordinal} comun de coati");
        }

        Summary(context, DailyId, "el cierre del sprint de coati", "coati queda listo para el cierre");

        return new Corpus();
    }

    /// <summary>
    /// Everything both indexes can answer, as text, for comparing search against itself across a
    /// rebuild. Several searches rather than one, because a single term would leave most of the
    /// index untouched and the comparison would hold whatever the rebuild did to the rest.
    /// </summary>
    public static List<string> Everything(CorpusDbContext context) =>
    [
        .. new[] { "presupuesto", "coati", "aguja", "ranking", "cierre", "comun", "turno7" }
            .SelectMany(term => CorpusSearch.Find(context, term, limit: 100)
                .Select(hit => $"{term}: {hit.Source} {hit.MeetingId} {hit.Ordinal} {hit.Snippet}")),
    ];

    private static void Meeting(CorpusDbContext context, string id, string title) => Sql.Execute(context, $"""
        INSERT INTO meetings (id, title, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
        VALUES ('{id}', '{title}', '{When}', 'multichannel', 'es', 'active', '{When}', '{When}');
        """);

    private static void Turn(CorpusDbContext context, string meeting, int ordinal, string text) =>
        Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{meeting}-{ordinal}', '{meeting}', {ordinal}, {ordinal * 1000}, {(ordinal + 1) * 1000}, 0,
                    'ch0:speaker_0', '{text}');
            """);

    private static void Summary(CorpusDbContext context, string meeting, string summary, string body) =>
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{JobId}', '{meeting}', 'extract', 'succeeded', 'extract/{meeting}', '{When}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{RunId}', '{meeting}', '{JobId}', 'claude_code', '1', '1', '{Sha256}', '{When}', '{When}');
            INSERT INTO summaries (id, meeting_id, extraction_run_id, abstract, body, created_at)
            VALUES ('55555555-5555-5555-5555-555555555555', '{meeting}', '{RunId}', '{summary}', '{body}', '{When}');
            """);
}
