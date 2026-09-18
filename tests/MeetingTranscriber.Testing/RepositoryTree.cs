using System.Runtime.CompilerServices;

namespace MeetingTranscriber.Testing;

/// <summary>
/// Where this repository is, and the folders under it a test reads out of.
/// </summary>
/// <remarks>
/// <para>
/// Found from this source file's compile-time path and never from the working directory, which
/// depends on where the runner was launched from. The <c>[CallerFilePath]</c> is on a private
/// overload, which is <c>AppSources</c>'s rule and for its reason: a default argument binds at the
/// call site, so a caller in another folder would silently resolve a different root.
/// </para>
/// <para>
/// <b>Five files still resolve the root their own way and may not fold onto this, and this is the
/// one place that says so.</b> <c>tests/MeetingTranscriber.App.Tests/AppSources.cs</c> is in a
/// project that references only <c>MeetingTranscriber.Presentation</c>;
/// <c>tests/MeetingTranscriber.Isa.Tests/IsaDocument.cs</c>, <c>IsaHistory.cs</c> and
/// <c>NothingUnderTestReachesTheNetworkTests.cs</c> are in one that references nothing at all; and
/// <c>tests/MeetingTranscriber.UiProbe.Tests/ProbeIsNotDrivenTests.cs</c> is in one that references
/// the probe. Each of those csproj files argues for its reference set at length, and this project
/// references <c>MeetingTranscriber.Infrastructure</c> — so folding any of them onto this would buy
/// one spelling with a boundary. <c>tools/MeetingTranscriber.CorpusFixtures/Program.cs</c> is not
/// under <c>tests/</c> at all. Said here and not five times, because five files each saying
/// <em>do not fold me</em> is five copies of one rule.
/// </para>
/// <para>
/// Two more are in projects that do reference this one and still carry their own today:
/// <c>tests/MeetingTranscriber.Mcp.Tests/ReadOnlyTests.cs</c> and
/// <c>tests/MeetingTranscriber.Infrastructure.Tests/Storage/CorpusLocationTests.cs</c>. Those two
/// are owed rather than exempt.
/// </para>
/// </remarks>
public static class RepositoryTree
{
    /// <summary>The clone this test binary was built out of.</summary>
    public static DirectoryInfo Root { get; } = Resolve();

    /// <summary>The product's projects.</summary>
    public static DirectoryInfo Src { get; } = new(Path.Combine(Root.FullName, "src"));

    /// <summary>The suites, and what they open.</summary>
    public static DirectoryInfo Tests { get; } = new(Path.Combine(Root.FullName, "tests"));

    /// <summary>One file named from the repository root, with forward or backward slashes.</summary>
    public static FileInfo At(string relative) =>
        new(Path.Combine([Root.FullName, .. relative.Split(['/', '\\'])]));

    /// <summary>Every <c>.cs</c> file under <paramref name="folder"/>, skipping <c>bin</c> and
    /// <c>obj</c>, in a fixed order.</summary>
    public static IReadOnlyList<FileInfo> SourceUnder(DirectoryInfo folder) =>
    [
        .. folder
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderBinOrObj(folder, file))
            .OrderBy(file => file.FullName, StringComparer.Ordinal),
    ];

    private static bool IsUnderBinOrObj(DirectoryInfo folder, FileInfo file) => Path
        .GetRelativePath(folder.FullName, file.FullName)
        .Split(Path.DirectorySeparatorChar)
        .Any(part => part is "bin" or "obj");

    private static DirectoryInfo Resolve([CallerFilePath] string thisFile = "") => new(
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")));
}
