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

    private IsaDocument(string[] lines)
    {
        Lines = lines;
        Frontmatter = ReadFrontmatter(lines);
        Features = ReadFeatures(lines);
        Claims = [.. Features.SelectMany(feature => feature.Claims)];
        VerificationStubs = ReadVerificationStubs(lines);
        Fog = ReadSectionBody(lines, FogSection);
    }

    public string[] Lines { get; }

    public IReadOnlyDictionary<string, string> Frontmatter { get; }

    public IReadOnlyList<Feature> Features { get; }

    public IReadOnlyList<Claim> Claims { get; }

    public IReadOnlyList<string> VerificationStubs { get; }

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
            }
        }

        return features;
    }

    private static List<string> ReadVerificationStubs(string[] lines) =>
    [
        .. lines
            .Select(line => StubLine().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["id"].Value),
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

    internal sealed record Claim(string Id, bool Closed, string Text, string Feature);

    internal sealed class Feature(string id, string name)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string? Why { get; set; }

        public string? Board { get; set; }

        public List<Claim> Claims { get; } = [];
    }
}
