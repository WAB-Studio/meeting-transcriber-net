using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The screen that files a meeting, against a real corpus: what it reads, and what pressing
/// <em>Guardar</em> leaves on disk.
/// </summary>
/// <remarks>
/// <para>
/// The first test is #105's own Proof — each of the thirteen filed from the screen and the corpus
/// read back — and it is held to exactly what <c>ClassificationStoriesTests</c> already stores, so
/// what the screen writes and what ISC-13 closed on are the same rows.
/// </para>
/// <para>
/// Every assertion after a save reads through a <em>fresh context on the same corpus</em>, and that
/// is not tidiness. <c>HumanLayer.Link</c> adds the entity and then saves; when a transaction rolls
/// back the rows are gone from the database and the added entities are still tracked, so asserting
/// on the context that threw reads the change tracker and passes with no transaction at all.
/// </para>
/// </remarks>
public class MeetingClassifyingTests
{
    /// <summary>
    /// Each of the thirteen, filed from the screen rather than written into the corpus, and the
    /// corpus holding exactly what the stories hold.
    /// </summary>
    [Fact]
    public void Each_of_the_thirteen_saved_from_the_screen_leaves_the_corpus_holding_what_the_stories_hold()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);

        using var reading = corpus.Open();

        reading.MeetingNodes.Count().ShouldBe(Stories.Links.Count);
        reading.MeetingPeople.Count().ShouldBe(Stories.Named.Count);

        foreach (var title in Stories.All)
        {
            var expected = Stories.Links
                .Where(link => link.Meeting == title)
                .Select(link => (link.Node, link.Role));
            var stored = reading.MeetingNodes
                .Where(link => link.MeetingId == stories.MeetingId(title))
                .ToArray()
                .Select(link => (Node: stories.NodeName(link.NodeId), link.Role));

            stored.ShouldBe(expected, ignoreOrder: true, customMessage: title);

            var named = Stories.Named
                .Where(row => row.Meeting == title)
                .Select(row => (row.Person, row.Role));
            var namings = reading.MeetingPeople
                .Where(row => row.MeetingId == stories.MeetingId(title))
                .ToArray()
                .Select(row => (Person: stories.PersonName(row.PersonId), row.Role));

            namings.ShouldBe(named, ignoreOrder: true, customMessage: title);
        }
    }

    /// <summary>
    /// The person using this install is drawn on every meeting and stored on none.
    /// </summary>
    /// <remarks>
    /// A row saying the owner of the corpus was at their own meeting says nothing, and thirteen of
    /// them would move the counts ISC-13 closed on.
    /// </remarks>
    [Fact]
    public void The_person_using_this_install_is_no_row_on_any_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);

        using var reading = corpus.Open();
        var me = new HumanLayer(reading, TimeProvider.System).Me().ShouldNotBeNull();

        me.DisplayName.ShouldBe("Renée");
        reading.MeetingPeople.Any(row => row.PersonId == me.Id).ShouldBeFalse();
        stories.PersonId("Renée").ShouldBe(me.Id);
    }

    /// <summary>
    /// Somebody carries the affiliation that held the day the meeting happened, and never the one
    /// that holds today.
    /// </summary>
    /// <remarks>
    /// The card's sixth decision and the reason <see cref="Affiliation"/> has a period. All
    /// thirteen stories start at <c>Stories.Now</c>, by which time the candidate has been hired, so
    /// the interview day is not in the fixture and this writes a meeting of its own on it rather
    /// than moving any of the thirteen.
    /// </remarks>
    [Fact]
    public void A_person_carries_the_affiliation_that_held_the_day_the_meeting_happened()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);
        var theInterview = TheDayOfTheInterview(context);

        var read = new MeetingClassifying(context, TimeProvider.System).Of(theInterview);
        var vikram = read.Everybody.Single(person => person.Person.Id == stories.PersonId("Vikram"));

        vikram.Belonged.Select(at => at.Organization.Name).ShouldBe(["Northwind"]);
    }

    [Fact]
    public void Somebody_at_two_places_at_once_carries_both()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);

        var read = new MeetingClassifying(context, TimeProvider.System)
            .Of(stories.MeetingId(Stories.Team));

        read.Everybody
            .Single(person => person.Person.Id == stories.PersonId("Sam"))
            .Belonged
            .Select(at => at.Organization.Name)
            .ShouldBe(["Orchard", "TechSed"], ignoreOrder: true);
    }

    /// <summary>
    /// A path is read from the deepest node up to its root, which is what a column draws.
    /// </summary>
    /// <remarks>
    /// The support story is the one that reaches three levels, so it is the one where returning
    /// the linked node alone — a ticket with no company above it — reads as a whole answer.
    /// </remarks>
    [Fact]
    public void A_path_is_read_from_the_deepest_node_up_to_its_root()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);

        using var reading = corpus.Open();
        var filing = new MeetingClassifying(reading, TimeProvider.System)
            .Filing(stories.MeetingId(Stories.Support));

        filing
            .Single(filed => filed.Role is MeetingNodeRole.WorkOf)
            .Path.Nodes.Select(node => node.Name)
            .ShouldBe(["TechSed", "Soporte", "ticket #4312"]);

        filing
            .Single(filed => filed.Role is MeetingNodeRole.Counterpart)
            .Path.Nodes.Select(node => node.Name)
            .ShouldBe(["Orchard"]);
    }

    [Fact]
    public void What_the_screen_reads_back_is_the_same_paths_the_meeting_screen_draws()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);

        using var reading = corpus.Open();
        var classifying = new MeetingClassifying(reading, TimeProvider.System);
        var support = stories.MeetingId(Stories.Support);

        var drawn = classifying.Filing(support)
            .Where(filed => filed.Role is MeetingNodeRole.WorkOf)
            .Select(filed => filed.Path.Nodes.Select(node => node.Id).ToArray());

        var held = classifying.Of(support).Chosen.WorkOf.Select(path => path.Nodes.ToArray());

        held.ShouldBe(drawn);
    }

    /// <summary>
    /// Saving the same classification twice writes nothing the second time, which ISC-15 is the
    /// claim about.
    /// </summary>
    /// <remarks>
    /// Read off <c>CreatedAt</c>, which is what a delete-and-re-add would move: <c>HumanLayer.Link</c>
    /// finds the row first and hands back the one that is there.
    /// </remarks>
    [Fact]
    public void Saving_the_same_classification_twice_writes_nothing_the_second_time()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);

        var first = Rows(corpus);

        using (var again = corpus.Open())
        {
            Refile(again, stories);
        }

        Rows(corpus).ShouldBe(first);
    }

    [Fact]
    public void Taking_one_link_off_leaves_the_others()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);
        var twoProjects = stories.MeetingId(Stories.TwoProjects);

        using (var editing = corpus.Open())
        {
            new MeetingClassifying(editing, TimeProvider.System).Save(
                twoProjects,
                MeetingFiling.Nothing with
                {
                    WorkOf = [new ChosenPath([stories.NodeId("Coati")])],
                });
        }

        using var reading = corpus.Open();

        reading.MeetingNodes
            .Where(link => link.MeetingId == twoProjects)
            .ToArray()
            .Select(link => stories.NodeName(link.NodeId))
            .ShouldBe(["Coati"]);
    }

    /// <summary>
    /// Nothing is filed when somebody on the screen is not in the corpus.
    /// </summary>
    /// <remarks>
    /// This is the test the transaction exists for, and its value is entirely in the order inside
    /// <c>Save</c>: the link is written and saved before the people are resolved, so without the
    /// boundary the node link stays on disk and this meeting ends up half filed. Read through a
    /// fresh context, for the reason this class's own remark gives.
    /// </remarks>
    [Fact]
    public void Nothing_is_filed_when_somebody_on_the_screen_is_not_in_the_corpus()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);
        var daily = stories.MeetingId(Stories.Daily);

        var filing = MeetingFiling.Nothing with
        {
            WorkOf = [new ChosenPath([stories.NodeId("Coati")])],
            Somebody = [new ChosenPerson(Guid.NewGuid(), Attended: true, Subject: false)],
        };

        Should.Throw<ArgumentException>(
            () => new MeetingClassifying(context, TimeProvider.System).Save(daily, filing));

        using var reading = corpus.Open();
        reading.MeetingNodes.Count(link => link.MeetingId == daily).ShouldBe(0);
    }

    /// <summary>
    /// A node the corpus does not hold is refused rather than linked.
    /// </summary>
    /// <remarks>
    /// Green with or without the transaction — the refusal happens before any write — so it is the
    /// one above and not this one that proves the boundary. What this holds is the refusal itself:
    /// resolved with a <c>FirstOrDefault</c>, the null would reach <c>HumanLayer.Link</c> and throw
    /// an <see cref="ArgumentNullException"/> that says nothing about which node.
    /// </remarks>
    [Fact]
    public void A_node_the_corpus_does_not_hold_is_refused_rather_than_linked()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);
        var daily = stories.MeetingId(Stories.Daily);

        var filing = MeetingFiling.Nothing with { WorkOf = [new ChosenPath([Guid.NewGuid()])] };

        Should.Throw<ArgumentException>(
            () => new MeetingClassifying(context, TimeProvider.System).Save(daily, filing));

        using var reading = corpus.Open();
        reading.MeetingNodes.Count(link => link.MeetingId == daily).ShouldBe(0);
    }

    /// <summary>
    /// Leaving a meeting unclassified takes everything off that meeting and nothing off any other.
    /// </summary>
    /// <remarks>
    /// There is no <c>Unclassify</c> to test: an empty filing through the same walk is it, which is
    /// what keeps one rule in one place.
    /// </remarks>
    [Fact]
    public void Leaving_it_unclassified_takes_every_link_and_every_naming_off_that_meeting_and_no_other()
    {
        using var corpus = new TemporaryCorpus();
        var stories = Fill(corpus);
        var oneToOne = stories.MeetingId(Stories.OneToOne);

        using (var editing = corpus.Open())
        {
            new MeetingClassifying(editing, TimeProvider.System).Save(oneToOne, MeetingFiling.Nothing);
        }

        using var reading = corpus.Open();

        reading.MeetingNodes.Count(link => link.MeetingId == oneToOne).ShouldBe(0);
        reading.MeetingPeople.Count(row => row.MeetingId == oneToOne).ShouldBe(0);

        reading.MeetingNodes.Count().ShouldBe(
            Stories.Links.Count(link => link.Meeting != Stories.OneToOne));
        reading.MeetingPeople.Count().ShouldBe(
            Stories.Named.Count(row => row.Meeting != Stories.OneToOne));
    }

    [Fact]
    public void A_meeting_this_corpus_does_not_have_is_said_so_rather_than_read_as_empty()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Stories.WriteWithNothingFiled(context);

        Should.Throw<MeetingStageException>(
            () => new MeetingClassifying(context, TimeProvider.System).Of(Guid.NewGuid()));
    }

    [Fact]
    public void A_meeting_nothing_was_filed_under_reads_as_nothing_rather_than_refusing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);

        var classifying = new MeetingClassifying(context, TimeProvider.System);
        var casual = stories.MeetingId(Stories.Casual);

        classifying.Filing(casual).ShouldBeEmpty();
        classifying.Of(casual).Chosen.IsUnclassified.ShouldBeTrue();
    }

    /// <summary>
    /// A corpus holding the thirteen, every one of them filed through the screen's own save rather
    /// than written in.
    /// </summary>
    private static Stories Fill(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
        var stories = Stories.WriteWithNothingFiled(context);

        Refile(context, stories);
        return stories;
    }

    /// <summary>Every one of the thirteen, saved from a filing built the way the screen builds it.</summary>
    private static void Refile(CorpusDbContext context, Stories stories)
    {
        var classifying = new MeetingClassifying(context, TimeProvider.System);

        foreach (var title in Stories.All)
        {
            classifying.Save(stories.MeetingId(title), AsTheScreenWouldHoldIt(title, stories));
        }
    }

    /// <summary>
    /// One story as the screen would be holding it: a path per link and a slot per person, with
    /// somebody named twice standing on one row carrying both toggles.
    /// </summary>
    private static MeetingFiling AsTheScreenWouldHoldIt(string title, Stories stories)
    {
        IReadOnlyList<ChosenPath> Column(MeetingNodeRole role) =>
        [
            .. Stories.Links
                .Where(link => link.Meeting == title && link.Role == role)
                .Select(link => new ChosenPath([stories.NodeId(link.Node)])),
        ];

        return new MeetingFiling(
            null,
            Column(MeetingNodeRole.WorkOf),
            Column(MeetingNodeRole.Counterpart),
            Column(MeetingNodeRole.About),
            [
                .. Stories.Named
                    .Where(named => named.Meeting == title)
                    .GroupBy(named => named.Person)
                    .Select(person => new ChosenPerson(
                        stories.PersonId(person.Key),
                        person.Any(named => named.Role is MeetingPersonRole.Attended),
                        person.Any(named => named.Role is MeetingPersonRole.Subject))),
            ]);
    }

    /// <summary>Every link and naming in the corpus with the instant it was written at.</summary>
    private static string[] Rows(TemporaryCorpus corpus)
    {
        using var context = corpus.Open();

        return
        [
            .. context.MeetingNodes
                .ToArray()
                .Select(link => $"{link.MeetingId} {link.NodeId} {link.Role} {link.CreatedAt}")
                .Concat(context.MeetingPeople
                    .ToArray()
                    .Select(row => $"{row.MeetingId} {row.PersonId} {row.Role} {row.CreatedAt}"))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A meeting of this test's own, held the day of the interview, with the candidate on it.
    /// </summary>
    /// <remarks>
    /// The thirteen all start at <c>Stories.Now</c>, and moving one of them to make this case exist
    /// would change the fixture ISC-13 closed against.
    /// </remarks>
    private static Guid TheDayOfTheInterview(CorpusDbContext context)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "la entrevista, el día que fue",
            StartedAt = Stories.Interviewed,
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            CreatedAt = Stories.Interviewed,
            UpdatedAt = Stories.Interviewed,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();
        return meeting.Id;
    }
}
