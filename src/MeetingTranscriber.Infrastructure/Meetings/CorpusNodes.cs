using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>The classification tree, whole, for a reader that has to name a node before it can ask
/// about one.</summary>
/// <remarks>
/// <para>
/// The tree stops at three levels and belongs to one person, so the whole of it is the cheapest read
/// in the schema — which is the argument <c>MeetingClassifying.Filing</c> already makes for reading
/// every node rather than the linked ones and their ancestors.
/// </para>
/// <para>
/// The third reader of this table and the first that is not one screen's.
/// <c>MeetingClassifying.Of</c> reads it by name for a picker and <c>MeetingClassifying.Filing</c>
/// reads it into a dictionary, which is an index with no order at all; both are about one meeting.
/// This is the corpus-wide plural, the way <see cref="CorpusStatements"/> is
/// <c>MeetingReading.Left</c>'s. Root first and then by name, because what a reader does with this
/// is find a node by the name a person gave it, and a tree printed in creation order is one nobody
/// can walk.
/// </para>
/// </remarks>
public static class CorpusNodes
{
    public static IReadOnlyList<Node> All(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Nodes
            .AsNoTracking()
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.Name)
            .ToArray();
    }
}
