using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// Walking a <see cref="JsonNode"/> tree by a dotted, bracketed path string, for a test that needs
/// to break one field of an otherwise valid extraction at a time.
/// </summary>
/// <remarks>
/// Shared by <see cref="ExtractionReaderTests"/> and <see cref="ExtractionCheckTests"/> rather than
/// carried once in each: it knows nothing about <c>decisions</c>, <c>evidence</c> or any other field
/// name, and two copies of the same path-walker are exactly the kind of thing that drifts the first
/// time somebody fixes a bug in one and not the other.
/// </remarks>
internal static class JsonNodeMutations
{
    public static byte[] Utf8(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    /// <summary>Removes the field at <paramref name="path"/> from an object, leaving it absent.</summary>
    public static void Remove(JsonNode root, string path)
    {
        var parent = ParentOf(root, path, out var leaf);
        parent.AsObject().Remove(leaf);
    }

    /// <summary>Sets the field at <paramref name="path"/> to <paramref name="value"/>, adding it if absent.</summary>
    public static void Set(JsonNode root, string path, JsonNode? value)
    {
        var parent = ParentOf(root, path, out var leaf);
        parent[leaf] = value;
    }

    private static JsonNode ParentOf(JsonNode root, string path, out string leaf)
    {
        var parts = path.Split('.');
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = Step(current, parts[index]);
        }

        leaf = parts[^1];
        return current;
    }

    private static JsonNode Step(JsonNode node, string part)
    {
        var bracket = part.IndexOf('[', StringComparison.Ordinal);
        if (bracket < 0)
        {
            return node[part]!;
        }

        var name = part[..bracket];
        var index = int.Parse(part[(bracket + 1)..^1], CultureInfo.InvariantCulture);
        return node[name]![index]!;
    }
}
