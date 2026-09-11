using System.Data;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
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
    public void One_search_asks_more_than_one_index()
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

        // Every way there is of reaching that meeting: a turn, its own words, its context note,
        // what it is filed under, what is above that, and who was on it. No voice — this meeting
        // has no assignment, and the daily below is where that branch is reached from.
        foreach (var word in new[] { "presupuesto", "trimestral", "margen", "ticket", "Renata" })
        {
            CorpusSearch.Find(context, word).ShouldBeEmpty(word);
        }

        // The other meeting is where the four branches an extraction produced live, and where its
        // voices are; nothing above reaches either. They all filter on the same column and it is
        // the same mistake to make.
        Sql.Execute(context, $"""
            UPDATE meetings SET lifecycle_state = 'deleting', deleted_at = '{Corpus.When}'
            WHERE id = '{written.Daily}';
            """);

        foreach (var word in new[]
            {
                "coati", "cierre", "pizarron", "formulario", "cronograma", "Tobias", "Ines", "Bruno",
            })
        {
            CorpusSearch.Find(context, word).ShouldBeEmpty(word);
        }
    }

    /// <summary>
    /// A meeting's own words. Its title and the note somebody wrote so that a person who was not
    /// there can read it are the two things nobody infers, and they were the two search could not
    /// find.
    /// </summary>
    [Fact]
    public void A_meetings_own_words_are_something_search_finds()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        foreach (var word in new[] { "trimestral", "margen" })
        {
            var hit = CorpusSearch.Find(context, word).ShouldHaveSingleItem();

            hit.Source.ShouldBe(SearchSource.Meeting);
            hit.MeetingId.ShouldBe(written.Budget);
            hit.Snippet.ShouldContain(word);
            hit.Ordinal.ShouldBeNull();
            hit.Start.ShouldBeNull();
            hit.End.ShouldBeNull();
        }
    }

    /// <summary>What a meeting is filed under is one of the ways somebody looks for it.</summary>
    [Fact]
    public void A_meeting_is_found_by_the_thing_it_is_filed_under()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "ticket").ShouldHaveSingleItem();

        hit.Source.ShouldBe(SearchSource.Node);
        hit.MeetingId.ShouldBe(written.Budget);
        hit.Ordinal.ShouldBeNull();
    }

    /// <summary>
    /// The node branch walks the tree with a fixed number of joins, so it reaches exactly as far as
    /// the tree is deep and no further. Deepen the tree and it under-reaches in silence: a meeting
    /// filed at the new level is stored, is legal, and is never found by anything above it.
    /// </summary>
    /// <remarks>
    /// A cap read off the constant rather than a number written twice. There is nowhere to put this
    /// in the query itself — the branch is a string and the walk is spelled out in it — so this is
    /// what turns "somebody raised the cap" from a silent gap into a red test naming the file to
    /// open.
    /// </remarks>
    [Fact]
    public void The_node_branch_reaches_the_whole_tree_and_says_so_when_the_tree_grows()
    {
        Node.MaxDepth.ShouldBe(
            2,
            "The node branch of CorpusSearch.Sql walks a node, its children and its grandchildren. "
            + "The tree just got deeper, so that walk needs another OR or search stops finding "
            + "meetings filed at the bottom of it.");
    }

    /// <summary>
    /// And so is anything above it. Somebody typing an organization's name and not finding the
    /// meetings under it would be search contradicting what a listing by node already answers.
    /// </summary>
    [Fact]
    public void A_meeting_is_found_by_the_organization_above_what_it_is_filed_under()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var found = CorpusSearch.Find(context, "Orchard");

        found.Select(hit => hit.Source).Distinct().ShouldHaveSingleItem().ShouldBe(SearchSource.Node);

        // Two levels down and one level down, which is the whole reach of a three-level tree.
        found.Select(hit => hit.MeetingId).Order().ShouldBe(
            new[] { written.Budget, written.Daily }.Order());
    }

    /// <summary>
    /// Somebody the meeting names. Either way of being named counts: a person the meeting was about
    /// and never attended is exactly the one nobody would think to index.
    /// </summary>
    [Fact]
    public void A_meeting_is_found_by_somebody_it_names()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var attended = CorpusSearch.Find(context, "Renata").ShouldHaveSingleItem();
        attended.Source.ShouldBe(SearchSource.Person);
        attended.MeetingId.ShouldBe(written.Budget);

        var subject = CorpusSearch.Find(context, "Tobias").ShouldHaveSingleItem();
        subject.Source.ShouldBe(SearchSource.Person);
        subject.MeetingId.ShouldBe(written.Daily);
    }

    /// <summary>
    /// Somebody the corpus knows spoke in a meeting, which is a different thing from somebody the
    /// meeting names. Ines is on no <c>meeting_people</c> row at all — only on a voice of the daily —
    /// so before the voice branch this search answered nothing, and the one person with a name on
    /// every multichannel meeting was the one person nobody could look for.
    /// </summary>
    [Fact]
    public void A_meeting_is_found_by_the_name_somebody_gave_a_voice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "Ines").ShouldHaveSingleItem();

        hit.Source.ShouldBe(SearchSource.Voice);
        hit.MeetingId.ShouldBe(written.Daily);
        hit.Snippet.ShouldContain("Ines");

        // A label is a place in the audio and a row about it is about the whole meeting: there is
        // no turn under it to cite, and inventing one would be a citation nobody searched for.
        hit.Ordinal.ShouldBeNull();
        hit.Start.ShouldBeNull();
        hit.End.ShouldBeNull();
    }

    /// <summary>
    /// And the recording settling the microphone on its own counts the same as a person listening
    /// back and saying who it was. Filtering to the assignments somebody made by hand would make the
    /// user of this install the one person their own corpus cannot find.
    /// </summary>
    [Fact]
    public void A_voice_the_recording_settled_is_found_the_same_way_as_one_a_person_did()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        var hit = CorpusSearch.Find(context, "Bruno").ShouldHaveSingleItem();

        hit.Source.ShouldBe(SearchSource.Voice);
        hit.MeetingId.ShouldBe(written.Daily);
    }

    /// <summary>
    /// One person can hold two labels of one meeting — both sides of a conversation, or a diarized
    /// track the provider split — and both rows come from one index row, so every column of them
    /// including the score is the same and there is nothing to choose between them.
    /// </summary>
    [Fact]
    public void Somebody_heard_on_two_voices_of_one_meeting_is_one_hit()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);
        Corpus.AlsoHeardOnASecondVoiceOfTheDaily(context);

        CorpusSearch.Find(context, "Ines").ShouldHaveSingleItem();
    }

    /// <summary>
    /// An assignment whose label names no turn of the meeting is not somebody the corpus knows spoke
    /// in it. Nothing deletes such a row — a meeting transcribed again into a different set of labels
    /// leaves every old one behind — and this is the first reader that does not reach the name
    /// through the label, so without the check it would be the only place that stale row is ever
    /// seen: a meeting offered under a name its transcript never says, with no anchor to check.
    /// </summary>
    [Fact]
    public void A_voice_whose_label_names_no_turn_is_not_somebody_the_corpus_knows_spoke()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);
        Corpus.HeardOnALabelThatNamesNoTurn(context);

        // Renata still answers once, as the budget meeting naming her. What she does not answer as
        // is a voice, which is the row this just wrote.
        var hit = CorpusSearch.Find(context, "Renata").ShouldHaveSingleItem();

        hit.Source.ShouldBe(SearchSource.Person);
        hit.MeetingId.ShouldBe(written.Budget);
    }

    /// <summary>
    /// Somebody a meeting names and also heard in it answers twice for that meeting, once as each.
    /// They are two different facts — one is somebody's assertion that this person belongs on the
    /// meeting, the other is the corpus recognising their voice in it — and one row per way of
    /// reaching the meeting is what this query promises everywhere else.
    /// </summary>
    [Fact]
    public void Somebody_a_meeting_names_and_also_heard_in_it_answers_as_both()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);
        Corpus.AlsoHeardOnAVoiceOfTheDaily(context);

        var found = CorpusSearch.Find(context, "Tobias");

        found.Select(hit => hit.MeetingId).Distinct().ShouldHaveSingleItem().ShouldBe(written.Daily);
        found.Select(hit => hit.Source).Order()
            .ShouldBe([SearchSource.Person, SearchSource.Voice]);
    }

    /// <summary>
    /// Every source can be answered with. A member of <see cref="SearchSource"/> with no branch
    /// behind it compiles, formats and passes everything else — it is simply a kind of hit the
    /// corpus can never produce, and nothing else here would notice.
    /// </summary>
    [Fact]
    public void Every_source_a_hit_can_carry_is_a_source_the_corpus_can_answer_with()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var answered = Corpus.Terms
            .SelectMany(term => CorpusSearch.Find(context, term, limit: 100))
            .Select(hit => hit.Source)
            .Distinct()
            .Order();

        answered.ShouldBe(Enum.GetValues<SearchSource>().Order());
    }

    /// <summary>
    /// The three lists an extraction leaves, each found where it was said. They anchor on a turn by
    /// construction, so a hit that threw the anchor away would send somebody back to the corpus for
    /// something the row already holds.
    /// </summary>
    [Fact]
    public void A_decision_an_action_and_an_open_question_are_each_found_where_they_were_said()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);

        foreach (var (word, source) in new[]
            {
                ("pizarron", SearchSource.Decision),
                ("formulario", SearchSource.Action),
                ("cronograma", SearchSource.Question),
            })
        {
            var hit = CorpusSearch.Find(context, word).ShouldHaveSingleItem();

            hit.Source.ShouldBe(source);
            hit.MeetingId.ShouldBe(written.Daily);
            hit.Ordinal.ShouldBe(Corpus.AnchorOrdinal);
            hit.Start.ShouldBe(Duration.FromMilliseconds(Corpus.AnchorOrdinal * 1000));
            hit.End.ShouldBe(Duration.FromMilliseconds((Corpus.AnchorOrdinal + 1) * 1000));
        }
    }

    /// <summary>
    /// Only the extraction a person accepted answers, and only the last one they accepted. A second
    /// extraction otherwise puts the same decision in front of somebody twice, said slightly
    /// differently, with nothing on either to say which is current — and an unaccepted run would put
    /// sentences nobody has vouched for under the meeting's own name.
    /// </summary>
    [Fact]
    public void Only_the_extraction_a_person_accepted_last_answers()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        CorpusSearch.Find(context, "pizarron").ShouldHaveSingleItem()
            .Snippet.ShouldContain("decidido");
        CorpusSearch.Find(context, "formulario").ShouldHaveSingleItem()
            .Snippet.ShouldContain("pendiente");
        CorpusSearch.Find(context, "cronograma").ShouldHaveSingleItem()
            .Snippet.ShouldContain("resolver");
        CorpusSearch.Find(context, "cierre").ShouldHaveSingleItem()
            .Snippet.ShouldContain("sprint de coati");

        // A word only the unaccepted run's own rows carry. Nothing answers it at all, which is the
        // other half of the rule and a different path through the SQL: with no accepted run the
        // subquery is NULL, and a comparison against NULL is not a row that fails the filter but a
        // row the filter never sees. Every meeting sits in that state until somebody accepts one.
        CorpusSearch.Find(context, "aceptar").ShouldBeEmpty();
    }

    /// <summary>
    /// The rule "the last extraction a person accepted" is one spelling —
    /// <c>CorpusSearch.TheRunThatCounts</c> — reaching two readers, and they reach it by two
    /// different paths to the database: search correlates it on <c>meeting.id</c> inside a nine-way
    /// union, the meeting screen fills it with a bound <c>@meeting</c> and runs it alone. Two runs
    /// accepted in the same instant is what tells those two paths apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to hold two spellings level with each other. It now holds something narrower and
    /// still worth running: that the one spelling answers the same through a correlated column and
    /// through a bound parameter — which is where a meeting id bound in a form that does not match
    /// what EF stored would show up, and nothing else would catch that.
    /// </para>
    /// <para>
    /// Both ties, because the ordering has two steps past <c>accepted_at</c> and each is reached
    /// only by the one shape. The first meeting's two runs were created at different moments and
    /// the run created later carries the <em>lower</em> id, so an ordering that ran straight out to
    /// the id would pick the other one. The second meeting's two runs agree on both instants, which
    /// is the only way to reach the id at all.
    /// </para>
    /// <para>
    /// The assertion goes through the text and not through a run id: a <see cref="SearchHit"/>
    /// carries none by design, and the decision branch has already filtered to one run, so what
    /// says which run answered is the marker its own decision carries. Both readers naming the
    /// expected marker is them landing on the right run rather than on the same wrong one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Search_and_the_meeting_screen_break_a_tie_between_two_accepted_runs_the_same_way()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Tied.Write(context);

        var hits = CorpusSearch.Find(context, "empatado");
        hits.Count.ShouldBe(2);

        var byCreation = hits.Single(hit => hit.MeetingId == Tied.BrokenByCreation);
        var byId = hits.Single(hit => hit.MeetingId == Tied.BrokenById);

        byCreation.Snippet.ShouldContain(Tied.CreatedLast);
        byId.Snippet.ShouldContain(Tied.HighestId);

        var reading = new MeetingReading(context, TimeProvider.System);

        Decided(reading, Tied.BrokenByCreation).ShouldContain(Tied.CreatedLast);
        Decided(reading, Tied.BrokenById).ShouldContain(Tied.HighestId);
    }

    /// <summary>What the meeting screen shows as the decision of the run it decided counts.</summary>
    private static string Decided(MeetingReading reading, Guid meetingId) =>
        reading.Of(meetingId).Screen.Left.Things
            .Single(thing => thing.Kind == LeftKind.Decision)
            .Says;

    /// <summary>
    /// A meeting filed under two things under one organization is one answer for that organization,
    /// not two. Both rows come from one index row through two paths, so every column of them is the
    /// same and there is nothing to choose between them.
    /// </summary>
    [Fact]
    public void A_meeting_filed_under_two_things_under_one_organization_comes_back_once_for_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var written = Corpus.Write(context);
        Corpus.AlsoUnderTheInitiative(context);

        CorpusSearch.Find(context, "Orchard")
            .Count(hit => hit.MeetingId == written.Budget)
            .ShouldBe(1);
    }

    /// <summary>
    /// No source owns the page. An initiative with forty meetings under it, asked for by its own
    /// name, does not evict every turn where somebody actually said the word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure eight indexes have and two did not. <c>bm25</c> weighs a term against the index
    /// it was found in, so the number ranks inside a source and means nothing across sources — and
    /// the node branch makes that worse, because one index row fans out to one row per meeting and
    /// every one of them carries that same score. Measured on this corpus before the fix: twenty
    /// node rows and not one turn. After it, ten and ten.
    /// </para>
    /// <para>
    /// The rest of this fixture cannot see it, because every word in it is in exactly one index by
    /// construction — which is what lets the other tests say which index answered, and exactly what
    /// would hide this. An initiative's name is the case that is not: it is on the tree
    /// <em>and</em> people say it out loud.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_source_takes_the_whole_page_from_the_others()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);
        Corpus.ManyMoreMeetingsUnderSoporte(context, howMany: 40);

        var found = CorpusSearch.Find(context, "soporte");

        found.Count.ShouldBe(CorpusSearch.DefaultLimit);
        found.Count(hit => hit.Source == SearchSource.Node).ShouldBeGreaterThan(1);
        found.Count(hit => hit.Source == SearchSource.Turn).ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// The other half of the task, and the reason the rebuild command exists at all: every index is
    /// external content, so throwing them away loses nothing — and search has to prove it by
    /// answering identically afterwards.
    /// </summary>
    [Fact]
    public void Throwing_every_index_away_and_rebuilding_them_answers_exactly_the_same()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Corpus.Write(context);

        var before = Corpus.Everything(context);
        before.ShouldNotBeEmpty();

        // A word nothing answers would compare equal to itself across the rebuild and say nothing
        // about the index it was meant to be asking.
        foreach (var term in Corpus.Terms)
        {
            before.ShouldContain(line => line.StartsWith($"{term}: ", StringComparison.Ordinal), term);
        }

        CorpusIntegrity.RebuildSearchIndexes(context);

        Corpus.Everything(context).ShouldBe(before);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }
}

/// <summary>
/// A corpus with something to find in it: two meetings, turns worth ranking against each other, a
/// summary so the summary index has an answer, a tree and four people so the human layer does — two
/// the meetings name and two they only heard — and three extractions of one meeting so that "the one
/// a person accepted" has something to choose.
/// </summary>
/// <remarks>
/// Every word here is in exactly one place on purpose. Nine branches over one corpus means a word
/// that appears in a title and in a turn makes two hits out of one search, and the tests that count
/// hits stop saying which branch answered. Adding a word to this fixture means checking it against
/// <see cref="Terms"/> first. Branches and not indexes, because the person branch and the voice
/// branch both read <c>people_fts</c> — so a name is in one index and can still answer twice, and
/// the two helpers that make it do are out of <see cref="Write"/> for that reason.
/// </remarks>
internal sealed class Corpus
{
    public const string When = "2026-03-04T14:00:00.000Z";

    /// <summary>An earlier acceptance, so "the last one accepted" has something to be later than.</summary>
    public const string Earlier = "2026-03-04T13:00:00.000Z";

    public const string BudgetTitle = "revision trimestral";

    public const string BudgetContext = "notas previas sobre el margen";

    public const string DailyTitle = "la diaria del equipo";

    /// <summary>Long enough that a snippet of it is visibly shorter than it is.</summary>
    public const string Haystack =
        "esto es un turno largo con mucho relleno alrededor de la palabra aguja para que el "
        + "recorte tenga algo que elidir a los dos lados y se note que no es el turno entero";

    /// <summary>The dense hit and the buried one, which is what ranking is asked to tell apart.</summary>
    public const int DenseOrdinal = 3;

    public const int SparseOrdinal = 4;

    /// <summary>
    /// The turn every claim below cites. It has to be one of the daily's own turns: a citation is a
    /// foreign key onto (meeting_id, ordinal), not a number somebody made up.
    /// </summary>
    public const int AnchorOrdinal = 5;

    /// <summary>
    /// The daily's other two voices, past the block of turns the limit test counts: the second
    /// speaker the provider found on the loopback, and the microphone.
    /// </summary>
    private const int LoopbackOrdinal = 5 + CorpusSearch.DefaultLimit + 5;

    private const int MicrophoneOrdinal = LoopbackOrdinal + 1;

    /// <summary>Two more of the daily's loopback voices, written only by the helpers that need them.</summary>
    private const int SplitLoopbackOrdinal = MicrophoneOrdinal + 1;

    private const int TobiasOrdinal = SplitLoopbackOrdinal + 1;

    public static readonly UtcTimestamp March = UtcTimestamp.Parse(When);

    /// <summary>
    /// Every word this corpus can be asked about, at least one per index. It is what the rebuild
    /// comparison walks: a single term would leave most of the index untouched and the comparison
    /// would hold whatever the rebuild did to the rest.
    /// </summary>
    public static readonly string[] Terms =
    [
        "presupuesto", "coati", "aguja", "ranking", "cierre", "comun", "turno7",
        "trimestral", "margen", "orchard", "soporte", "ticket", "renata", "tobias",
        "pizarron", "formulario", "cronograma", "ines", "bruno",
    ];

    private const string BudgetId = "11111111-1111-1111-1111-111111111111";
    private const string DailyId = "22222222-2222-2222-2222-222222222222";
    private const string JobId = "33333333-3333-3333-3333-333333333333";
    private const string RunId = "44444444-4444-4444-4444-444444444444";
    private const string OldJobId = "66666666-6666-6666-6666-666666666666";
    private const string OldRunId = "77777777-7777-7777-7777-777777777777";
    private const string UnacceptedJobId = "88888888-8888-8888-8888-888888888888";
    private const string UnacceptedRunId = "99999999-9999-9999-9999-999999999999";
    private const string OrchardId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string SoporteId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string TicketId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string RenataId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    private const string TobiasId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    private const string InesId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
    private const string BrunoId = "ffffffff-ffff-ffff-ffff-fffffffffffe";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    private Corpus()
    {
    }

    public Guid Budget => Guid.Parse(BudgetId);

    public Guid Daily => Guid.Parse(DailyId);

    public static Corpus Write(CorpusDbContext context)
    {
        Meeting(context, BudgetId, BudgetTitle, BudgetContext);
        Meeting(context, DailyId, DailyTitle, note: null);

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

        // The daily is a multichannel recording, so it has more than one voice in it: the loopback
        // diarized into two, and the microphone. Every label an assignment below hangs off is one a
        // turn of this meeting actually carries, which is the state the corpus is in after a render
        // and the only state a voice hit is supposed to answer from.
        Turn(context, DailyId, LoopbackOrdinal, "turno del otro lado del loopback", "ch0:speaker_1");
        Turn(context, DailyId, MicrophoneOrdinal, "turno dicho por el microfono", "ch1:speaker_0", channel: 1);

        // The tree, and the two meetings hung off it at different depths: the budget meeting on a
        // ticket two levels down, the daily on the initiative one level down. Searching the
        // organization has to reach both.
        Node(context, OrchardId, "organization", "Orchard", depth: 0);
        Node(context, SoporteId, "initiative", "Soporte", depth: 1, parent: OrchardId, parentKind: "organization");
        Node(context, TicketId, "topic", "ticket 4312", depth: 2, parent: SoporteId, parentKind: "initiative");
        Filed(context, BudgetId, TicketId, "work_of");
        Filed(context, DailyId, SoporteId, "work_of");

        // One who was there and one the meeting was about, because the second is the one nobody
        // would think to index.
        Person(context, RenataId, "Renata");
        Person(context, TobiasId, "Tobias");
        Named(context, BudgetId, RenataId, "attended");
        Named(context, DailyId, TobiasId, "subject");

        // Two the meeting never names. Ines is somebody who listened back and put a name on a
        // voice; Bruno is the microphone the recording settled on its own, so he is `is_me` — that
        // is the only person `SettleTheMicrophone` ever writes a `channel` row for, and he is the
        // user of this install on every multichannel meeting. Neither is on `meeting_people`, which
        // is the whole of what this pair is here for.
        Person(context, InesId, "Ines");
        Person(context, BrunoId, "Bruno", isMe: true);
        Voice(context, DailyId, "ch0:speaker_1", InesId, "person");
        Voice(context, DailyId, "ch1:speaker_0", BrunoId, "channel");

        // Three extractions of one meeting: the one a person accepted last, one accepted before it,
        // and one nobody accepted. Only the first answers.
        Run(context, JobId, RunId, acceptedAt: When, createdAt: When);
        Run(context, OldJobId, OldRunId, acceptedAt: Earlier, createdAt: Earlier);
        Run(context, UnacceptedJobId, UnacceptedRunId, acceptedAt: null, createdAt: When);

        Summary(context, "55555555-5555-5555-5555-555555555555", RunId,
            "el cierre del sprint de coati", "coati queda listo para el cierre");
        Summary(context, "55555555-5555-5555-5555-555555555556", OldRunId,
            "el cierre del sprint anterior", "asi quedaba el cierre en la corrida vieja");
        Summary(context, "55555555-5555-5555-5555-555555555557", UnacceptedRunId,
            "el cierre sin aceptar", "nadie firmo este cierre");

        Left(context, "a", RunId, "lo del pizarron queda decidido", "queda pendiente el formulario",
            "sin resolver el cronograma");
        Left(context, "b", OldRunId, "el pizarron se decidia distinto", "otro formulario del run viejo",
            "el cronograma segun el run viejo");
        Left(context, "c", UnacceptedRunId, "el pizarron sin aceptar", "formulario sin aceptar",
            "cronograma sin aceptar");

        return new Corpus();
    }

    /// <summary>
    /// Files the budget meeting under the initiative as well, so it reaches the organization two
    /// ways at once — which is what <c>DISTINCT</c> in the node branch is for.
    /// </summary>
    public static void AlsoUnderTheInitiative(CorpusDbContext context) =>
        Filed(context, BudgetId, SoporteId, "about");

    /// <summary>
    /// Ines on a second voice of the same meeting, so one name reaches one meeting two ways at
    /// once — which is what <c>DISTINCT</c> in the voice branch is for. Out of <see cref="Write"/>
    /// for the same reason the node one is: the tests that count hits expect one each.
    /// </summary>
    /// <remarks>
    /// Both labels are on channel 0. The provider splitting the loopback into more speakers than
    /// there are people is the case this is for, and it is the only one available: channel 1 is the
    /// microphone, and a second label on it is exactly what stops <c>Speakers.Resolve</c> settling
    /// anything at all.
    /// </remarks>
    public static void AlsoHeardOnASecondVoiceOfTheDaily(CorpusDbContext context)
    {
        Turn(context, DailyId, SplitLoopbackOrdinal, "otro turno del loopback partido", "ch0:speaker_3");
        Voice(context, DailyId, "ch0:speaker_3", InesId, "person");
    }

    /// <summary>
    /// Tobias, whom the daily already names as its subject, also heard on one of its voices — so one
    /// name reaches one meeting as two different kinds of fact at once.
    /// </summary>
    public static void AlsoHeardOnAVoiceOfTheDaily(CorpusDbContext context)
    {
        Turn(context, DailyId, TobiasOrdinal, "y otro turno mas del loopback", "ch0:speaker_2");
        Voice(context, DailyId, "ch0:speaker_2", TobiasId, "person");
    }

    /// <summary>
    /// Renata on a label the budget meeting has no turn for, which is what a corpus is left holding
    /// when a meeting is transcribed again into a different set of labels. Nothing deletes the row.
    /// </summary>
    public static void HeardOnALabelThatNamesNoTurn(CorpusDbContext context) =>
        Voice(context, BudgetId, "ch1:speaker_0", RenataId, "person");

    /// <summary>
    /// Many more meetings filed under the initiative, each saying its name out loud once.
    /// </summary>
    /// <remarks>
    /// The one collision the rest of this fixture refuses to have, and it is a helper rather than
    /// part of <see cref="Write"/> for that reason: every word above is in exactly one index so the
    /// tests can say which index answered, and that is also what would hide one source taking the
    /// whole page. A node's name is the realistic collision — it is on the tree and people say it.
    /// </remarks>
    public static void ManyMoreMeetingsUnderSoporte(CorpusDbContext context, int howMany)
    {
        for (var n = 0; n < howMany; n++)
        {
            var id = $"7{n:0000000}-0000-0000-0000-000000000000";
            Meeting(context, id, $"reunion {n}", note: null);
            Filed(context, id, SoporteId, "work_of");
            Turn(context, id, 0, $"hablamos de soporte en la reunion {n}");
        }
    }

    /// <summary>
    /// Everything every index can answer, as text, for comparing search against itself across a
    /// rebuild.
    /// </summary>
    /// <remarks>
    /// Sorted, because it is compared for equality and the ordering does not fully order it: two
    /// node hits on one search can agree on the score, the date, the source and the ordinal, and
    /// nothing after that decides which comes first. Comparing them in whatever order SQLite
    /// happened to produce would be a test that fails on a day nobody changed anything.
    /// </remarks>
    public static List<string> Everything(CorpusDbContext context) =>
    [
        .. Terms.SelectMany(term => CorpusSearch.Find(context, term, limit: 100)
                .Select(hit => $"{term}: {hit.Source} {hit.MeetingId} {hit.Ordinal} {hit.Snippet}"))
            .Order(StringComparer.Ordinal),
    ];

    private static void Meeting(CorpusDbContext context, string id, string title, string? note) =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, title, context, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{id}', '{title}', {(note is null ? "NULL" : $"'{note}'")}, '{When}', 'multichannel', 'es',
                    'active', '{When}', '{When}');
            """);

    /// <summary>
    /// One turn. The label carries its channel, so <paramref name="channel"/> moves with it: channel
    /// 0 is the loopback and channel 1 is the microphone, and a row saying one while its label says
    /// the other is a corpus nothing could have recorded.
    /// </summary>
    private static void Turn(
        CorpusDbContext context,
        string meeting,
        int ordinal,
        string text,
        string label = "ch0:speaker_0",
        int channel = 0) =>
        Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{meeting}-{ordinal}', '{meeting}', {ordinal}, {ordinal * 1000}, {(ordinal + 1) * 1000}, {channel},
                    '{label}', '{text}');
            """);

    /// <summary>
    /// A node of the tree, written through raw SQL like everything else here, so the CHECKs are what
    /// decides whether it is a legal one: a root carries no parent and depth 0, and a child carries
    /// its parent's kind and its parent's depth.
    /// </summary>
    private static void Node(
        CorpusDbContext context,
        string id,
        string kind,
        string name,
        int depth,
        string? parent = null,
        string? parentKind = null) =>
        Sql.Execute(context, $"""
            INSERT INTO nodes (id, kind, name, depth, parent_id, parent_kind, parent_depth, created_at, updated_at)
            VALUES ('{id}', '{kind}', '{name}', {depth},
                    {(parent is null ? "NULL" : $"'{parent}'")},
                    {(parentKind is null ? "NULL" : $"'{parentKind}'")},
                    {(parent is null ? "NULL" : $"{depth - 1}")},
                    '{When}', '{When}');
            """);

    private static void Filed(CorpusDbContext context, string meeting, string node, string role) =>
        Sql.Execute(context, $"""
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{meeting}', '{node}', '{role}', '{When}');
            """);

    /// <summary>
    /// Somebody the corpus knows. <paramref name="isMe"/> is the user of this install, and it is on
    /// exactly the person a <c>channel</c> assignment can point at: <c>SettleTheMicrophone</c>
    /// settles the microphone onto <c>Me()</c> and onto nobody else.
    /// </summary>
    private static void Person(CorpusDbContext context, string id, string name, bool isMe = false) =>
        Sql.Execute(context, $"""
            INSERT INTO people (id, display_name, is_me, created_at, updated_at)
            VALUES ('{id}', '{name}', {(isMe ? 1 : 0)}, '{When}', '{When}');
            """);

    private static void Named(CorpusDbContext context, string meeting, string person, string role) =>
        Sql.Execute(context, $"""
            INSERT INTO meeting_people (meeting_id, person_id, role, created_at)
            VALUES ('{meeting}', '{person}', '{role}', '{When}');
            """);

    /// <summary>
    /// Somebody the corpus knows spoke in a meeting. <paramref name="assignedBy"/> is the stored
    /// form of <c>SpeakerAssignmentSource</c> — <c>person</c> where somebody listened back and said
    /// who it was, <c>channel</c> where the recording gave it for free.
    /// </summary>
    /// <remarks>
    /// The last column is <c>assigned_at</c> and not <c>created_at</c>: <c>ConfirmedAndAssigned</c>
    /// renamed it, because the value moves whenever somebody overrules what the channel guessed and
    /// so never said the row's own age.
    /// </remarks>
    private static void Voice(
        CorpusDbContext context, string meeting, string label, string person, string assignedBy) =>
        Sql.Execute(context, $"""
            INSERT INTO speaker_assignments (meeting_id, speaker_label, person_id, assigned_by, assigned_at)
            VALUES ('{meeting}', '{label}', '{person}', '{assignedBy}', '{When}');
            """);

    /// <summary>An extraction of the daily, and the job it ran under.</summary>
    private static void Run(
        CorpusDbContext context, string job, string run, string? acceptedAt, string createdAt) =>
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{job}', '{DailyId}', 'extract', 'succeeded', 'extract/{DailyId}/{job}', '{createdAt}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{run}', '{DailyId}', '{job}', 'claude_code', '1', '1', '{Sha256}',
                    {(acceptedAt is null ? "NULL" : $"'{acceptedAt}'")}, '{createdAt}');
            """);

    private static void Summary(
        CorpusDbContext context, string id, string run, string summary, string body) =>
        Sql.Execute(context, $"""
            INSERT INTO summaries (id, meeting_id, extraction_run_id, abstract, body, created_at)
            VALUES ('{id}', '{DailyId}', '{run}', '{summary}', '{body}', '{When}');
            """);

    /// <summary>
    /// What one extraction left: a decision, an action and an open question, all three citing the
    /// same turn. <paramref name="tag"/> is one hex digit and it is what keeps three runs' rows
    /// apart — the ids are made from it rather than handed in one at a time.
    /// </summary>
    private static void Left(
        CorpusDbContext context,
        string tag,
        string run,
        string decision,
        string action,
        string question) =>
        Sql.Execute(context, $"""
            INSERT INTO decisions (id, meeting_id, extraction_run_id, statement, ordinal,
                                   utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                   source_artifact_sha256, created_at)
            VALUES ('{tag}0000000-0000-0000-0000-000000000001', '{DailyId}', '{run}', '{decision}', 0,
                    {AnchorOrdinal}, {AnchorOrdinal * 1000}, {(AnchorOrdinal + 1) * 1000}, 'ch0:speaker_0',
                    'turno{AnchorOrdinal} comun de coati', '{Sha256}', '{When}');
            INSERT INTO action_items (id, meeting_id, extraction_run_id, statement, ordinal,
                                      utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                      source_artifact_sha256, created_at)
            VALUES ('{tag}0000000-0000-0000-0000-000000000002', '{DailyId}', '{run}', '{action}', 0,
                    {AnchorOrdinal}, {AnchorOrdinal * 1000}, {(AnchorOrdinal + 1) * 1000}, 'ch0:speaker_0',
                    'turno{AnchorOrdinal} comun de coati', '{Sha256}', '{When}');
            INSERT INTO open_questions (id, meeting_id, extraction_run_id, question, ordinal,
                                        utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                        source_artifact_sha256, created_at)
            VALUES ('{tag}0000000-0000-0000-0000-000000000003', '{DailyId}', '{run}', '{question}', 0,
                    {AnchorOrdinal}, {AnchorOrdinal * 1000}, {(AnchorOrdinal + 1) * 1000}, 'ch0:speaker_0',
                    'turno{AnchorOrdinal} comun de coati', '{Sha256}', '{When}');
            """);
}

/// <summary>
/// Two meetings whose extractions were accepted in the same instant, one for each step the
/// ordering has left after that: the moment the run was created, and the run's own id.
/// </summary>
/// <remarks>
/// Separate from <see cref="Corpus"/> and not folded into it. That fixture is built so every word
/// is in exactly one index and one run of one meeting answers; a tie is the opposite arrangement —
/// two runs a search has to choose between — and putting it there would change what half the tests
/// above are counting.
/// </remarks>
internal static class Tied
{
    /// <summary>The instant both runs of both meetings were accepted at.</summary>
    public const string Accepted = "2026-04-09T11:00:00.000Z";

    /// <summary>Every decision here says this, so one search reaches both meetings.</summary>
    public const string Term = "empatado";

    /// <summary>The marker on the decision of the run created last, which is the one that counts.</summary>
    public const string CreatedLast = "marcaposterior";

    /// <summary>The marker on the decision of the higher id, which is what the last step decides.</summary>
    public const string HighestId = "marcamayor";

    private const string Earlier = "2026-04-09T10:00:00.000Z";

    /// <remarks>
    /// Digits and no letters, in every id this fixture writes. These rows go in through raw SQL and
    /// come back out through EF, which binds a <see cref="Guid"/> as upper-case text — so an id
    /// with a letter in it goes in one case and is looked up in the other, and the row is not found.
    /// </remarks>
    private const string CreationId = "31111111-1111-1111-1111-111111111111";

    private const string IdId = "32222222-2222-2222-2222-222222222222";

    /// <summary>The run created last, and the lower of the two ids — which is what makes it a tie.</summary>
    private const string CreatedLastRun = "41000000-0000-0000-0000-000000000001";

    private const string CreatedFirstRun = "42000000-0000-0000-0000-000000000002";

    private const string LowerRun = "51000000-0000-0000-0000-000000000001";

    private const string HigherRun = "52000000-0000-0000-0000-000000000002";

    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The meeting whose tie only <c>created_at</c> can break.</summary>
    public static Guid BrokenByCreation => Guid.Parse(CreationId);

    /// <summary>The meeting whose tie only the id can break.</summary>
    public static Guid BrokenById => Guid.Parse(IdId);

    public static void Write(CorpusDbContext context)
    {
        Meeting(context, CreationId, "la reunion del empate por fecha");
        Meeting(context, IdId, "la reunion del empate por id");

        // The run created later carries the lower id, so an ordering that ran straight out to the
        // id would answer with the other one. That is what makes this meeting the created_at step.
        Run(context, CreationId, CreatedLastRun, createdAt: Accepted);
        Run(context, CreationId, CreatedFirstRun, createdAt: Earlier);
        Decision(context, CreationId, CreatedLastRun, CreatedLast);
        Decision(context, CreationId, CreatedFirstRun, "marcaanterior");

        // Both instants equal, so nothing but the id is left.
        Run(context, IdId, LowerRun, createdAt: Accepted);
        Run(context, IdId, HigherRun, createdAt: Accepted);
        Decision(context, IdId, LowerRun, "marcamenor");
        Decision(context, IdId, HigherRun, HighestId);
    }

    private static void Meeting(CorpusDbContext context, string id, string title) =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, title, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{id}', '{title}', '{Accepted}', 'multichannel', 'es', 'active', '{Accepted}', '{Accepted}');
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{id}-0', '{id}', 0, 0, 1000, 0, 'ch0:speaker_0', 'lo que se dijo en la reunion');
            """);

    /// <summary>An extraction accepted at <see cref="Accepted"/>, and the job it ran under.</summary>
    private static void Run(CorpusDbContext context, string meeting, string run, string createdAt) =>
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{run}', '{meeting}', 'extract', 'succeeded', 'extract/{run}', '{createdAt}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{run}', '{meeting}', '{run}', 'claude_code', '1', '1', '{Sha256}', '{Accepted}', '{createdAt}');
            """);

    /// <summary>
    /// One decision per run, saying the word both meetings share and the marker that says which run
    /// wrote it.
    /// </summary>
    private static void Decision(
        CorpusDbContext context, string meeting, string run, string marker) =>
        Sql.Execute(context, $"""
            INSERT INTO decisions (id, meeting_id, extraction_run_id, statement, ordinal,
                                   utterance_ordinal, start_ms, end_ms, speaker_label, quoted_text,
                                   source_artifact_sha256, created_at)
            VALUES ('{run}', '{meeting}', '{run}', 'quedo {Term} {marker}', 0,
                    0, 0, 1000, 'ch0:speaker_0', 'lo que se dijo en la reunion', '{Sha256}', '{Accepted}');
            """);
}
