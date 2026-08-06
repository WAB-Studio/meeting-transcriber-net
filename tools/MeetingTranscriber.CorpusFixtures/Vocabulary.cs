using System.Text;

namespace MeetingTranscriber.CorpusFixtures;

/// <summary>
/// The closed list of words a fixture may contain, and the mapping from a real word onto it.
/// </summary>
/// <remarks>
/// The mapping is a hash rather than a table so it needs no per corpus maintenance, and it is
/// FNV-1a rather than <see cref="string.GetHashCode()"/> because that one is randomised per
/// process: the same corpus has to produce the same fixture bytes on any machine, or every run
/// of this tool shows up as a diff.
/// </remarks>
internal sealed class Vocabulary
{
    private readonly string[] words;

    private Vocabulary(string[] words) => this.words = words;

    public static Vocabulary Load(string path)
    {
        var words = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        if (words.Length == 0)
        {
            throw new InvalidOperationException($"'{path}' holds no words.");
        }

        return new Vocabulary(words);
    }

    /// <summary>
    /// The word that stands in for <paramref name="token"/>, always the same one. Collisions are
    /// deliberate: what the fixtures have to keep is that a word repeated in the meeting is still
    /// repeated, not that the substitution can be undone.
    /// </summary>
    public string WordFor(string token) => words[Fnv1a(token.ToLowerInvariant()) % (uint)words.Length];

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
