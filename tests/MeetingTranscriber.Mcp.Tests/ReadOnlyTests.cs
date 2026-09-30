using System.Text.RegularExpressions;

namespace MeetingTranscriber.Mcp.Tests;

/// <summary>
/// The half of ISC-98 a build agent can hold: this server opens nothing it could write through,
/// writes no file but the record of what it was asked (ISC-100) and keeps that outside the corpus,
/// runs no SQL of its own, and puts nothing on standard output but the protocol.
/// </summary>
/// <remarks>
/// <para>
/// Sweeps over the source and not over behaviour, which is what makes them worth having here. The
/// connection is what refuses a write — <c>Mode=ReadOnly</c>, and SQLite enforces it — so a fact
/// asserting that a tool did not write would be asserting SQLite works. What these hold is the step
/// before: that nothing in this project ever asks for the other kind of connection. A write that
/// slipped in would be a write through a connection the promise was never about.
/// </para>
/// <para>
/// The shape is <c>DeepgramKeyTests</c>' two sweeps: match over every <c>.cs</c> in the project,
/// answer with the whole set of files, and assert the set. Both directions matter — a file that
/// should not be there shows up, and a file that stopped naming what it names goes missing — so a
/// sweep that read nothing cannot pass.
/// </para>
/// </remarks>
public class ReadOnlyTests
{
    /// <summary>
    /// The corpus is opened one way, and it is the way that cannot be written through.
    /// </summary>
    /// <remarks>
    /// One file names <c>OpenReadOnly</c> and no file names the other two. A second opener is a
    /// second place the mode is decided, and the one that got it wrong would be indistinguishable
    /// from this one at every call site above it.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_server_opens_a_corpus_that_can_be_written()
    {
        Naming(@"CorpusDatabase\.OpenReadOnly").ShouldBe(
            ["TheCorpusHere.cs"],
            "the corpus is opened in one place, so that one file is the whole of what has to be "
            + "read to know this server cannot write. A second opener is a second decision about "
            + "the mode, and nothing above it could tell the two apart.");

        Naming(
                @"CorpusDatabase\.Open\(|CorpusDatabase\.OpenMigrated\(|SaveChanges|ExecuteDelete"
                + @"|ExecuteUpdate|Database\.Migrate|context\.(Add|Update|Remove)\b")
            .ShouldBeEmpty(
                "every one of these either opens a corpus that can be written or writes through "
                + "one. ISC-98 promises this server never writes the corpus, and a connection opened read-only "
                + "is what makes that a property of the connection rather than of the code above it "
                + "— but a caller that asked for the other kind of connection would be outside that "
                + "promise entirely, which is what this catches.");
    }

    /// <summary>
    /// The one file this server writes is the record of what it was asked, and it is not in the
    /// corpus.
    /// </summary>
    /// <remarks>
    /// The read-only connection says nothing at all about the folder around the database. A corpus
    /// is its rows <em>and</em> its artifacts — <c>docs/corpus.md</c> is what says which of those
    /// can be obtained again and which cannot — so a tool that wrote a file beside them would be
    /// outside ISC-98's promise while every sweep above stayed green. <c>CorpusFiles</c> is on the
    /// list because it is how a path inside a corpus is built, and nothing here has a reason to
    /// build one. What ISC-100 asks for is a record of every request, so one file is written:
    /// <c>RequestRecord.cs</c>, under the user's local application data, and the second assertion
    /// is what keeps it from ever being pointed at the corpus. ISC-98's "never writes" therefore
    /// reads as "never writes the corpus", which is the only reading it can have beside ISC-100.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_server_writes_a_file_into_the_corpus()
    {
        Naming(
                @"File\.(Write|Create|Delete|Move|Copy|Append)|Directory\.(Create|Delete|Move)"
                + @"|CorpusFiles|FileStream|FileMode\.")
            .ShouldBe(
                ["RequestRecord.cs"],
                "the corpus is its rows and the artifacts beside them, and a connection opened "
                + "read-only refuses the first and says nothing about the second. This server "
                + "answers questions and writes exactly one file, the record of what it was asked, "
                + "outside the corpus. A second file that writes is a second place to decide where.");

        Naming(@"CorpusFiles|CorpusDatabase|\.Root\b", only: "RequestRecord.cs").ShouldBeEmpty(
            "the record is kept beside the user's own settings and never in a corpus, so the file "
            + "that writes it has no business naming how a corpus path is built or where a corpus "
            + "root is.");
    }

    /// <summary>
    /// There is no way to run arbitrary SQL through this server, which is the second half of what
    /// #130 asks to be proved.
    /// </summary>
    /// <remarks>
    /// The eight tools are eight questions, and an agent can ask them and nothing else. What the
    /// corpus is asked in SQL is asked by <c>MeetingTranscriber.Infrastructure</c>, where every
    /// string is written down and every value is bound — so a tool that took SQL would be handing a
    /// caller the one thing that list of questions exists instead of.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_server_writes_its_own_SQL() =>
        Naming(@"FromSqlRaw|ExecuteSqlRaw|SqliteCommand|RawSql").ShouldBeEmpty(
            "a tool that ran SQL would be a ninth tool answering any question at all, including "
            + "the ones the other eight are shaped to keep closed. The reads this server needs are "
            + "written down in Infrastructure with their parameters bound.");

    /// <summary>
    /// Standard output is the protocol and nothing else reaches it.
    /// </summary>
    /// <remarks>
    /// One stray line makes an agent's session fail as a parse error a long way from whatever wrote
    /// it, which is why the redirect is the first statement of the run and not a rule everybody has
    /// to remember. Writing to <see cref="Console.Error"/> is not what this catches and is not
    /// meant to be: that stream is where a line belongs.
    /// </remarks>
    [Fact]
    public void Standard_output_is_the_protocol_and_nothing_else()
    {
        Naming(@"Console\.SetOut\(Console\.Error\)").ShouldBe(
            ["CorpusServer.cs"],
            "stdout is redirected once, before anything that could print. Redirected and not merely "
            + "avoided: the core prints and the runtime prints, and neither reads a rule.");

        Naming(@"Console\.Write|Console\.Out").ShouldBeEmpty(
            "anything written to standard output lands in the middle of the protocol. What a "
            + "server has to say to a person goes to standard error, which is where the redirect "
            + "sends the rest.");
    }

    /// <summary>
    /// Which files of this project match, by name, in a fixed order.
    /// </summary>
    private static IReadOnlyList<string> Naming(string pattern, string? only = null)
    {
        var project = new DirectoryInfo(
            Path.Combine(RepositoryTree.Src.FullName, "MeetingTranscriber.Mcp"));

        project.Exists.ShouldBeTrue(
            $"'{project.FullName}' is the project these sweeps are about, and a sweep over a folder "
            + "that is not there reads nothing and passes.");

        return
        [
            .. project
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !Inside(file, "obj") && !Inside(file, "bin"))
                .Where(file => only is null || file.Name == only)
                .Where(file => Regex.IsMatch(File.ReadAllText(file.FullName), pattern))
                .Select(file => file.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool Inside(FileInfo file, string folder) => file.FullName.Contains(
        $"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}",
        StringComparison.Ordinal);
}
