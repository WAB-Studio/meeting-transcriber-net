using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// ISA.md read as the shape `.claude/skills/isa/references/format.md` describes: claims, the
/// feature blocks that group them, and the provenance stubs that close them.
/// </summary>
/// <remarks>
/// Parsing is deliberately literal — line prefixes, not a markdown library. What the gate is
/// checking is that a human wrote the shape down correctly, so a parser that repairs a sloppy
/// line on the way in would be answering its own question.
/// </remarks>
internal sealed partial class IsaDocument
{
    private const string FogSection = "## Not yet specified";

    private const string LearningSection = "## Learning";

    private const string VerificationSection = "## Verification";

    private IsaDocument(string[] lines)
    {
        Lines = lines;
        Frontmatter = ReadFrontmatter(lines);
        Features = ReadFeatures(lines);
        Claims = [.. Features.SelectMany(feature => feature.Claims)];
        StrayFeatureBullets = [.. Features.SelectMany(feature => feature.StrayBullets)];
        Stubs = ReadStubs(lines);
        StrayVerificationLines = [.. ReadSectionBody(lines, VerificationSection)
            .Where(line => !StubLine().IsMatch(line))];
        LearningLabels = ReadLearningLabels(lines);
        Fog = ReadSectionBody(lines, FogSection);
    }

    public string[] Lines { get; }

    public IReadOnlyDictionary<string, string> Frontmatter { get; }

    public IReadOnlyList<Feature> Features { get; }

    public IReadOnlyList<Claim> Claims { get; }

    /// <summary>
    /// Bullets sitting inside a feature block that are not claim lines. A section is only as
    /// trustworthy as the reader's certainty that everything in it was parsed, so what the shape
    /// does not describe is collected rather than skipped.
    /// </summary>
    public IReadOnlyList<string> StrayFeatureBullets { get; }

    /// <summary>
    /// The provenance stubs under `## Verification`, one per closed claim, each split into the
    /// claim it closes and the prose left over once its pointers are taken out.
    /// </summary>
    public IReadOnlyList<Stub> Stubs { get; }

    /// <summary>Non-blank lines under `## Verification` that are not provenance stubs.</summary>
    public IReadOnlyList<string> StrayVerificationLines { get; }

    /// <summary>
    /// The label of each bullet under `## Learning` — `conjecture`, `refuted-by` and the rest —
    /// in the order they appear. A bullet carrying no label reads as an empty string instead of
    /// being dropped, because a bullet the shape does not describe is what this is here to catch.
    /// </summary>
    public IReadOnlyList<string> LearningLabels { get; }

    /// <summary>Body of `## Not yet specified`, empty when the section is absent.</summary>
    public IReadOnlyList<string> Fog { get; }

    /// <summary>
    /// The file lives at the repo root, and the root is found from this source file's compile-time
    /// path — the same way <c>DeepgramFixtures</c> finds the fixture folder. A path built from the
    /// working directory would depend on where the runner was launched from.
    /// </summary>
    public static FileInfo Path([CallerFilePath] string thisFile = "") => new(
        System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisFile)!, "..", "..", "ISA.md")));

    public static IsaDocument Read() => new(File.ReadAllLines(Path().FullName));

    private static Dictionary<string, string> ReadFrontmatter(string[] lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lines.Length == 0 || lines[0] != "---")
        {
            return fields;
        }

        foreach (var line in lines.Skip(1).TakeWhile(line => line != "---"))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return fields;
    }

    private static List<Feature> ReadFeatures(string[] lines)
    {
        var features = new List<Feature>();
        Feature? current = null;

        foreach (var line in lines)
        {
            var heading = FeatureHeading().Match(line);
            if (heading.Success)
            {
                current = new Feature(heading.Groups["id"].Value, heading.Groups["name"].Value.Trim());
                features.Add(current);
                continue;
            }

            // A `## ` heading ends the feature run; anything after it is another section.
            if (current is not null && line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("Why:", StringComparison.Ordinal))
            {
                current.Why = line["Why:".Length..].Trim();
            }
            else if (line.StartsWith("Board:", StringComparison.Ordinal))
            {
                current.Board = line["Board:".Length..].Trim();
            }
            else
            {
                var claim = ClaimLine().Match(line);
                if (claim.Success)
                {
                    current.Claims.Add(new Claim(
                        claim.Groups["id"].Value,
                        claim.Groups["mark"].Value == "x",
                        claim.Groups["text"].Value.Trim(),
                        current.Id));
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    current.StrayBullets.Add(line);
                }
            }
        }

        return features;
    }

    /// <summary>
    /// Read from the section rather than from the whole file: a stub is evidence, and a line that
    /// happens to carry the shape somewhere else is not evidence of anything.
    /// </summary>
    private static List<Stub> ReadStubs(string[] lines)
    {
        var stubs = new List<Stub>();

        foreach (var line in ReadSectionBody(lines, VerificationSection))
        {
            var match = StubLine().Match(line);
            if (match.Success)
            {
                // What is left after the `- ISC-N — ` prefix and the backticked spans come out is
                // the prose. A stub is one physical line — a wrapped one would fail the stray-line
                // check above it — so the whole stub is here and nothing has to be joined first.
                var evidence = line[match.Length..];
                stubs.Add(new Stub(
                    match.Groups["id"].Value,
                    evidence,
                    Pointer().Replace(evidence, string.Empty)));
            }
        }

        return stubs;
    }

    private static List<string> ReadLearningLabels(string[] lines) =>
    [
        .. ReadSectionBody(lines, LearningSection)
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => LearningLabel().Match(line))
            .Select(match => match.Success ? match.Groups["label"].Value : string.Empty),
    ];

    private static List<string> ReadSectionBody(string[] lines, string heading) =>
    [
        .. lines
            .SkipWhile(line => !line.StartsWith(heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => !string.IsNullOrWhiteSpace(line)),
    ];

    [GeneratedRegex(@"^### (?<id>F\d+) · (?<name>.+)$")]
    private static partial Regex FeatureHeading();

    [GeneratedRegex(@"^- \[(?<mark>[ x])\] (?<id>ISC-[0-9.]+): (?<text>.+)$")]
    private static partial Regex ClaimLine();

    [GeneratedRegex(@"^- (?<id>ISC-[0-9.]+) — ")]
    private static partial Regex StubLine();

    [GeneratedRegex(@"^- \*\*(?<label>[a-z-]+)\*\* — ")]
    private static partial Regex LearningLabel();

    /// <summary>A backticked span: a test name, a command, a path — the pointer part of a stub.</summary>
    [GeneratedRegex("`[^`]*`")]
    private static partial Regex Pointer();

    internal sealed record Claim(string Id, bool Closed, string Text, string Feature);

    /// <summary>
    /// A provenance stub: the claim it closes, everything after the `- ISC-N — ` prefix, and that
    /// same evidence with its pointers taken out. The split is the whole point — naming four test
    /// methods precisely is what the format asks for and costs nothing, while the sentences around
    /// them are what the size gate is over. <paramref name="Evidence"/> is kept beside the prose
    /// because a measure that only ever sees its own output cannot be checked for having read the
    /// line correctly, which is what the backtick-parity gate does with it.
    /// </summary>
    internal sealed record Stub(string Id, string Evidence, string Prose)
    {
        /// <summary>
        /// Whether the backticks close. They always have, and the gate is over the one way this
        /// measure fails open rather than shut: <c>Pointer()</c> pairs ticks, so a single dropped
        /// tick makes one span out of everything between two unrelated ones, and several hundred
        /// characters of prose stop being counted at all.
        /// </summary>
        public bool PointersClose => Evidence.Count(character => character == '`') % 2 == 0;
    }

    internal sealed class Feature(string id, string name)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string? Why { get; set; }

        public string? Board { get; set; }

        public List<Claim> Claims { get; } = [];

        public List<string> StrayBullets { get; } = [];
    }
}
