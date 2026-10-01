using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// A classification filled by hand and put by under a name, against a real corpus: what comes back
/// when the corpus is opened again, what choosing it files, and that nothing done to it reaches a
/// meeting.
/// </summary>
/// <remarks>
/// Every read goes through a fresh context on the same corpus, for the reason
/// <see cref="MeetingClassifyingTests"/> gives: the context that wrote is the change tracker, and
/// asserting on it passes with nothing on disk.
/// </remarks>
public class KeptClassificationTests
{
    /// <summary>
    /// What the screen is handed on opening: the classification, and the filing choosing it fills.
    /// It is not the evidence for the screen showing the chip, which is a walk.
    /// </summary>
    [Fact]
    public void A_classification_put_by_under_a_name_is_read_back_after_the_corpus_is_opened_again()
    {
        using var corpus = new TemporaryCorpus();
        var (a, _, _) = Written(corpus);

        using var reading = corpus.Open();
        var kept = new MeetingClassifying(reading, TimeProvider.System).Of(a.Meeting).Kept.ShouldHaveSingleItem();

        kept.Template.Name.ShouldBe("Soporte TechSed");
        kept.Fills.WorkOf.Select(path => path.Deepest).ShouldBe([a.Ticket]);
        kept.Fills.Counterpart.Select(path => path.Deepest).ShouldBe([a.Company]);
        kept.Fills.Somebody.ShouldBe([new ChosenPerson(a.Person, Attended: true, Subject: true)]);
    }

    /// <summary>ISC-202.2: choosing it files another meeting the way the first one was filed.</summary>
    [Fact]
    public void Choosing_a_classification_put_by_files_a_meeting_the_way_the_one_it_was_put_by_from_was_filed()
    {
        using var corpus = new TemporaryCorpus();
        var (a, b, _) = Written(corpus);

        MeetingFiling filled;
        using (var reading = corpus.Open())
        {
            var classifying = new MeetingClassifying(reading, TimeProvider.System);
            var read = classifying.Of(b);
            filled = read.Chosen.FilledFrom(read.Kept.Single().Fills);
        }

        using (var writing = corpus.Open())
        {
            new MeetingClassifying(writing, TimeProvider.System).Save(b, filled);
        }

        using var after = corpus.Open();
        Links(after, b).ShouldBe(Links(after, a.Meeting), ignoreOrder: true);
        Namings(after, b).ShouldBe(Namings(after, a.Meeting), ignoreOrder: true);
        Links(after, b).ShouldNotBeEmpty();
    }

    /// <summary>
    /// ISC-202.3: putting by again, renaming and throwing away leave every meeting as it was, down
    /// to the instant it was last written.
    /// </summary>
    [Fact]
    public void Changing_renaming_or_discarding_a_classification_put_by_leaves_every_meeting_as_it_was()
    {
        using var corpus = new TemporaryCorpus();
        var (a, b, _) = Written(corpus);
        var before = Everything(corpus, a.Meeting, b);

        using (var context = corpus.Open())
        {
            var human = new HumanLayer(context, TimeProvider.System);
            human.Keep("Soporte TechSed", new MeetingFiling(null, [new ChosenPath([a.Company])], [], [], []));
            Everything(corpus, a.Meeting, b).ShouldBe(before, "keeping the same name again");

            var renamed = human.Rename(context.Templates.Single(), "Soporte");
            renamed.ShouldNotBeNull();
        }

        Everything(corpus, a.Meeting, b).ShouldBe(before, "renaming it");

        using (var context = corpus.Open())
        {
            new HumanLayer(context, TimeProvider.System).Discard(context.Templates.Single());
        }

        Everything(corpus, a.Meeting, b).ShouldBe(before, "throwing it away");

        using var after = corpus.Open();
        after.Templates.Count().ShouldBe(0);
        after.TemplateNodes.Count().ShouldBe(0);
        after.TemplatePeople.Count().ShouldBe(0);
    }

    [Fact]
    public void Putting_by_under_a_name_already_used_replaces_what_it_holds()
    {
        using var corpus = new TemporaryCorpus();
        var (a, _, _) = Written(corpus);

        using var context = corpus.Open();
        var human = new HumanLayer(context, TimeProvider.System);
        var first = context.Templates.Single();

        var again = human.Keep("  Soporte TechSed  ", new MeetingFiling(null, [new ChosenPath([a.Company])], [], [], []));

        again.Id.ShouldBe(first.Id);
        context.Templates.Count().ShouldBe(1);
        context.TemplateNodes.ToArray().Select(row => (row.NodeId, row.Role))
            .ShouldBe([(a.Company, MeetingNodeRole.WorkOf)]);
        context.TemplatePeople.Count().ShouldBe(0);
    }

    /// <summary>
    /// The common edit: the same name again with one more node, so every key the first one held is
    /// written again after being removed.
    /// </summary>
    [Fact]
    public void Putting_by_again_with_everything_it_held_and_more_keeps_every_row()
    {
        using var corpus = new TemporaryCorpus();
        var (a, _, initiative) = Written(corpus);

        using var context = corpus.Open();
        var human = new HumanLayer(context, TimeProvider.System);
        var kept = new MeetingClassifying(context, TimeProvider.System).Of(a.Meeting).Kept.Single().Fills;

        human.Keep("Soporte TechSed", kept with { About = [new ChosenPath([initiative])] });

        context.TemplateNodes.Count().ShouldBe(3);
        context.TemplatePeople.Count().ShouldBe(2);
    }

    [Fact]
    public void A_classification_put_by_is_not_renamed_to_a_name_another_one_carries()
    {
        using var corpus = new TemporaryCorpus();
        var (a, _, _) = Written(corpus);

        using var context = corpus.Open();
        var human = new HumanLayer(context, TimeProvider.System);
        human.Keep("Otra", new MeetingFiling(null, [new ChosenPath([a.Company])], [], [], []));

        Should.Throw<ClassificationException>(
            () => human.Rename(context.Templates.Single(row => row.Name == "Otra"), "Soporte TechSed"))
            .Message.ShouldContain("already a classification put by under 'Soporte TechSed'");
    }

    [Fact]
    public void A_classification_with_nothing_on_it_is_not_put_by()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, TimeProvider.System);

        Should.Throw<ClassificationException>(() => human.Keep("nada", MeetingFiling.Nothing))
            .Message.ShouldContain("would keep nothing");

        context.Templates.Count().ShouldBe(0);
    }

    [Fact]
    public void A_node_the_corpus_does_not_hold_is_refused_before_anything_is_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, TimeProvider.System);

        Should.Throw<ArgumentException>(
            () => human.Keep("nada", new MeetingFiling(null, [new ChosenPath([Guid.NewGuid()])], [], [], [])));

        context.Templates.Count().ShouldBe(0);
    }

    private sealed record First(Guid Meeting, Guid Company, Guid Ticket, Guid Person);

    /// <summary>
    /// Meeting A, filed under a ticket and a company and naming one person both ways, put by as
    /// <em>Soporte TechSed</em>; and meeting B, which nobody has filed.
    /// </summary>
    private static (First A, Guid B, Guid Initiative) Written(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var company = human.Root(NodeKind.Organization, "TechSed");
        var initiative = human.Under(company, NodeKind.Initiative, "Soporte");
        var ticket = human.Under(initiative, NodeKind.Topic, "ticket #4312");
        var ada = human.Add("Ada");
        var meetingA = fixture.Meeting("A");
        var meetingB = fixture.Meeting("B");

        human.Link(meetingA.Id, ticket, MeetingNodeRole.WorkOf);
        human.Link(meetingA.Id, company, MeetingNodeRole.Counterpart);
        human.Name(meetingA.Id, ada, MeetingPersonRole.Attended);
        human.Name(meetingA.Id, ada, MeetingPersonRole.Subject);

        var read = new MeetingClassifying(context, TimeProvider.System).Of(meetingA.Id);
        human.Keep("Soporte TechSed", read.Chosen);

        return (new First(meetingA.Id, company.Id, ticket.Id, ada.Id), meetingB.Id, initiative.Id);
    }

    private static (Guid Node, MeetingNodeRole Role)[] Links(CorpusDbContext context, Guid meeting) =>
    [
        .. context.MeetingNodes.Where(row => row.MeetingId == meeting)
            .ToArray()
            .Select(row => (row.NodeId, row.Role)),
    ];

    private static (Guid Person, MeetingPersonRole Role)[] Namings(CorpusDbContext context, Guid meeting) =>
    [
        .. context.MeetingPeople.Where(row => row.MeetingId == meeting)
            .ToArray()
            .Select(row => (row.PersonId, row.Role)),
    ];

    /// <summary>Every row of both meetings the discarding could reach, with its instant.</summary>
    private static string[] Everything(TemporaryCorpus corpus, Guid a, Guid b)
    {
        using var context = corpus.Open();

        return
        [
            .. context.Meetings
                .Where(row => row.Id == a || row.Id == b)
                .ToArray()
                .OrderBy(row => row.Title, StringComparer.Ordinal)
                .Select(row => $"meeting {row.Title} updated={row.UpdatedAt}"),
            .. Links(context, a).Concat(Links(context, b)).Select(link => $"link {link}"),
            .. Namings(context, a).Concat(Namings(context, b)).Select(naming => $"naming {naming}"),
        ];
    }
}
