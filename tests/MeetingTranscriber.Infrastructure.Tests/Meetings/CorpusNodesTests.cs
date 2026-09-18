using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>The classification tree, whole — the corpus-wide plural, and the first reader of the
/// tree that is not one screen's.</summary>
public class CorpusNodesTests
{
    private static readonly UtcTimestamp August =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// The whole tree comes back root first, and then by name — never in creation order, which is
    /// what a reader looking for a name a person gave it needs.
    /// </summary>
    /// <remarks>Red with <c>OrderBy(Depth)</c> dropped.</remarks>
    [Fact]
    public void The_whole_tree_comes_back_root_first()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayer(context, August);

        var second = human.Root(NodeKind.Organization, "zeta");
        var first = human.Root(NodeKind.Organization, "acme");

        // Under the second root, a child whose name sorts before the first root's own name — so
        // depth-before-name is what the order says, and not name across the whole tree.
        var child = human.Under(second, NodeKind.Initiative, "aaa");

        CorpusNodes.All(context)
            .Select(node => node.Name)
            .ShouldBe(["acme", "zeta", "aaa"]);

        CorpusNodes.All(context).Select(node => node.Id).ShouldBe([first.Id, second.Id, child.Id]);
    }
}
