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

    private List<string>? _paths;

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
    /// Every path in this repository the file points at, deduplicated and in the order they first
    /// appear. A stub cites its probe by path, and nothing else here says those resolve.
    /// </summary>
    /// <remarks>
    /// Worked out when it is asked for and not in the constructor, which every other member here
    /// is: this is the one that reads the disk, one document is built per test method and one per
    /// commit the history gates walk, and exactly one fact asks for it.
    /// </remarks>
    public IReadOnlyList<string> Paths => _paths ??= ReadPaths(Lines);

    /// <summary>
    /// Where this repository is, found from this source file's compile-time path — the same way
    /// <c>DeepgramFixtures</c> finds the fixture folder. A path built from the working directory
    /// would depend on where the runner was launched from.
    /// </summary>
    /// <remarks>
    /// The <c>[CallerFilePath]</c> is on the private overload and not on this one, which is
    /// <c>AppSources</c>'s rule and for its reason: a default argument binds at the call site, so a
    /// caller in another folder would silently resolve a different root. Here that would hand the
    /// two halves of the path gate two different trees — one deciding what counts as a root, the
    /// other deciding whether a path exists under it.
    /// </remarks>
    public static DirectoryInfo Root() => Here();

    private static DirectoryInfo Here([CallerFilePath] string thisFile = "") => new(
        System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisFile)!, "..", "..")));

    /// <summary>The file itself, which lives at the root.</summary>
    public static FileInfo Path() =>
        new(System.IO.Path.Combine(Root().FullName, "ISA.md"));

    public static IsaDocument Read() => new(File.ReadAllLines(Path().FullName));

    /// <summary>
    /// Read from lines instead of from the file, so a stub written to break a gate can be measured
    /// without editing the corpus the gates run over. Every number the gates compare against came
    /// off that corpus, and a mutation of it is a hand run nothing re-runs.
    /// </summary>
    internal static IsaDocument Of(params string[] lines) => new(lines);

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
                    Pointer().Replace(evidence, WhatIsNotAPointer)));
            }
        }

        return stubs;
    }

    /// <summary>
    /// The backticked spans anywhere in the file that are unambiguously a path in this repository.
    /// </summary>
    /// <remarks>
    /// Read over the whole file and not only over <c>## Verification</c>, because a claim and the
    /// header both point at files too and a pointer that has stopped resolving is the same defect
    /// wherever it sits.
    /// <para>
    /// Four conditions, and each one is a shape the section actually holds. The span carries a
    /// <c>/</c>, which is what tells a path from a name: <c>Turns.Group</c>, <c>ISA.md</c> and
    /// <c>ch1:speaker_0</c> are all things somebody could go and look up, and none of them says
    /// where. It is one whitespace-free token, which drops the recorded command lines —
    /// <c>git grep -l "class TemporaryCorpus" -- tests/</c> is a run and not a pointer, and so is
    /// <c>mklink /J</c>. It holds no wildcard or angle bracket, which drops the one glob and the
    /// one shape: <c>tests/**/*.cs</c> names a set and <c>meetings/&lt;id&gt;/manifest.json</c>
    /// names a corpus folder nobody's checkout has. And its first segment names a directory at the
    /// root, which is what makes it a path in <em>this</em> repository rather than a relative one
    /// whose base is a sentence — <c>MeetingTranscriber.Testing/DeepgramFixtures.cs</c> is what a
    /// <c>git grep</c> run inside <c>tests/</c> printed, and resolving it would mean guessing the
    /// directory it was run from.
    /// </para>
    /// <para>
    /// What the last condition lets through, said rather than left to be found. A root directory
    /// that is gone makes every pointer under it unreadable rather than red, because the segment
    /// stops naming a root and the span stops being read as a path at all — the one way this fails
    /// open, and it takes deleting <c>docs/</c> or <c>tests/</c> whole. The same condition drops
    /// the only two source files `ISA.md` cites, both of them what a `git grep` inside <c>tests/</c>
    /// printed, and those are the pointers most likely to rot: a file rooted nowhere resolves only
    /// against a directory a sentence beside it names, which is a reading and not a rule.
    /// </para>
    /// </remarks>
    private static List<string> ReadPaths(string[] lines)
    {
        var roots = Root().EnumerateDirectories()
            .Select(directory => directory.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. lines
                .SelectMany(line => Pointer().Matches(line))
                .Select(span => span.Value.Trim('`'))
                .Where(span => IsAPath(span, roots))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>The four conditions, in the order the remark above states them.</summary>
    private static bool IsAPath(string span, HashSet<string> roots) =>
        span.IndexOf('/', StringComparison.Ordinal) > 0
        && !span.Any(char.IsWhiteSpace)
        && span.IndexOfAny(NotInAPath) < 0
        && roots.Contains(span[..span.IndexOf('/', StringComparison.Ordinal)]);

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

    /// <summary>
    /// What a path in this repository cannot hold: a wildcard, which makes the span a set, and an
    /// angle bracket, which makes it a shape. Both are answered by a different question than the
    /// one <see cref="Paths"/> is asked. Nothing else is here — a quote and a pipe are illegal in a
    /// Windows path too, and every span in the file that carries one already carries whitespace, so
    /// listing them would be two conditions nothing has ever reached.
    /// </summary>
    private static readonly char[] NotInAPath = ['*', '?', '<', '>'];

    /// <summary>
    /// The longest a backticked span is free for. Past it the span is priced as prose, one
    /// character for one character.
    /// </summary>
    /// <remarks>
    /// Measured over the 437 spans in the section on 2026-09-01, and every length here is
    /// <c>Match.Length</c> — the span with its two backticks, which is what the comparison below
    /// gets. The longest is 135, ISC-158.1's UI probe walk, then ISC-171's at 121 and ISC-126's
    /// fully-qualified test name at 115. One more reaches 110 and nothing else does. So 150 is
    /// above every pointer anybody has written, with room for a longer walk than any that exists.
    /// </remarks>
    private const int LongestPointer = 150;

    /// <summary>
    /// What a backticked span costs the size gate: nothing up to <see cref="LongestPointer"/>,
    /// and everything after that.
    /// </summary>
    /// <remarks>
    /// Ticks around a span are what makes it free, and until this the freedom was unbounded —
    /// so the whole of a stub's prose passed the gate by being wrapped in one pair of them, and
    /// the parity check below claimed to cover the only way the measure failed open while that
    /// stayed wide. It is not treated as a dodge somebody plots. The section already backticks
    /// English sentences as evidence, so a writer following the neighbours arrives at it by the
    /// same route they arrive at everything else this gate exists to stop, and pricing the excess
    /// rather than failing the line keeps an honestly long walk from going red for being long.
    /// </remarks>
    private static string WhatIsNotAPointer(Match span) =>
        span.Length <= LongestPointer ? string.Empty : span.Value[LongestPointer..];

    internal sealed record Claim(string Id, bool Closed, string Text, string Feature);

    /// <summary>
    /// A provenance stub: the claim it closes, everything after the `- ISC-N — ` prefix, and that
    /// same evidence with its pointers taken out. The split is the whole point — naming four test
    /// methods precisely is what the format asks for and costs nothing, while the sentences around
    /// them are what the size gate is over. <paramref name="Evidence"/> is kept beside the prose
    /// because a stub that spends its whole budget inside backticks measures nothing at all in
    /// <paramref name="Prose"/>, so the gate reads both: what a stub says in the open, and what it
    /// carries in total. Their difference is what the pointers were let off for being pointers.
    /// </summary>
    internal sealed record Stub(string Id, string Evidence, string Prose)
    {
        /// <summary>
        /// Whether the backticks close. They always have, and it is one of the three ways the
        /// prose measure fails open rather than shut: <c>Pointer()</c> pairs ticks, so a single
        /// dropped tick makes one span out of everything between two unrelated ones, and several
        /// hundred characters of prose stop being counted at all.
        /// </summary>
        /// <remarks>
        /// The second is a span that pairs correctly and is a paragraph, which
        /// <see cref="LongestPointer"/> prices at everything past 150 — a 600-character span is
        /// charged 450, which is a price and not a refusal, so whether that stub goes red depends
        /// on what else it says. The third is that paragraph cut into spans that are each short,
        /// which no per-span price reaches at all: fourteen ticked spans of 140 discount 1960
        /// between them.
        ///
        /// None of the three is shut, and saying what each still lets through is the point of
        /// writing them down. What shuts the question they share is not here but in
        /// <c>IsaStructureTests</c>, which reads <paramref name="Evidence"/> as well as
        /// <paramref name="Prose"/>: however a stub arranges its ticks, everything it carries past
        /// the two budgets together is charged, so no stub can be arbitrarily long and measure
        /// short. What it can still do is spend the whole of both on pointers, which is the prose
        /// ceiling more than it is a defect — a stub of nothing but test names is what the format
        /// asks for.
        /// </remarks>
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
