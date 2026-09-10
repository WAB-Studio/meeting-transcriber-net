using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Isa.Tests;

/// <summary>
/// ISC-16 as a rule rather than as a word that happens not to appear: nothing under <c>tests/</c>
/// opens a socket, so no test spends what a person has to pay for.
/// </summary>
/// <remarks>
/// <para>
/// A <c>git grep</c> for HTTP and socket types was the recorded evidence until this, and the tree's
/// first legitimate fake handler falsified it while the claim stayed true — which is what a check
/// written against an absence always does. What is written here instead is what the absence was
/// for: the only thing that may sit behind an <c>HttpClient</c> under <c>tests/</c> is a handler
/// declared under <c>tests/</c>, and a handler answers out of <c>tests/fixtures/deepgram/</c>
/// rather than off a socket.
/// </para>
/// <para>
/// It reads source text and not a compiled assembly, because this project references nothing and
/// the rule is over every suite, the ones it could not reference included. Comments come out before
/// any fact reads, so a remark discussing a socket costs nothing — a rule that reddens on prose
/// only teaches people to write around it.
/// </para>
/// <para>
/// What comes out is a <em>line</em> that is all comment, and never the tail of a line that is not.
/// That is <c>App.Tests</c>' <c>SourceLines.StandsInACommentedLine</c>'s rule, taken for its reason
/// and not for the convenience: telling a real <c>//</c> from the one inside <c>"http://…"</c> takes
/// a scanner rather than a line test, and a scanner that gets it wrong drops a real finding in
/// silence instead of reporting a false one out loud. Six files under <c>tests/</c> already carry
/// <c>://</c> in live code, and three carry <c>/*</c> inside a string literal — under a rule that
/// read the language rather than the line, one block comment appearing after any of those three
/// would stop an unbounded stretch of that file being checked, with nothing going red to say so.
/// The price is the other direction: a socket type named in a comment written after code on one
/// line reddens a fact, and that is the mistake worth making. The rule is copied rather than
/// referenced because this project references nothing, which is the whole of why it can hold a rule
/// over suites no reference from here could reach.
/// </para>
/// <para>
/// This file is the one file the walk skips, because a file that states a rule names every word the
/// rule forbids. It finds itself by <c>[CallerFilePath]</c> and not by name, so renaming it cannot
/// quietly turn the skip off, and <see cref="Code"/> refuses outright if the skip matched nothing.
/// </para>
/// </remarks>
public partial class NothingUnderTestReachesTheNetworkTests
{
    /// <summary>
    /// The types that put a real socket behind a call. The first six open one themselves; the two
    /// handlers are what an <c>HttpClient</c> ends up with when it is handed nothing else; and the
    /// last three are the ways to hold a client without the word <c>HttpClient</c> ever appearing on
    /// its own, which is what the two facts below read for.
    /// </summary>
    /// <remarks>
    /// It is a list of names, so what it cannot see is a type nobody in this tree has reached for
    /// yet. That is the honest half of what replaced a <c>git grep</c>: the rule the two facts below
    /// hold is structural and this one is an inventory, kept because a test naming
    /// <c>TcpListener</c> has no other line to fail at. Extend it when a suite needs a way out this
    /// does not name — never by taking a name off it.
    /// </remarks>
    private static readonly string[] OpensASocket =
    [
        "Socket", "TcpClient", "TcpListener", "UdpClient", "HttpListener", "ClientWebSocket",
        "HttpClientHandler", "SocketsHttpHandler",
        "WebClient", "HttpMessageInvoker", "IHttpClientFactory",
    ];

    /// <summary>
    /// This file's compile-time path. The <c>[CallerFilePath]</c> is on the private helper and not
    /// on a parameter anybody can call, which is <c>IsaDocument</c>'s rule and for its reason: a
    /// default argument binds at the call site.
    /// </summary>
    private static readonly string ThisFile = Path.GetFullPath(Here());

    /// <summary>
    /// A test that opens a socket is a test that can reach Deepgram, and every response under test
    /// comes from a fixture through a handler declared in the test tree.
    /// </summary>
    [Fact]
    public void Nothing_under_test_names_a_type_that_opens_a_socket()
    {
        var code = Code();
        var named = new List<string>();
        foreach (var file in code)
        {
            foreach (var type in OpensASocket)
            {
                if (Whole(type).IsMatch(file.Text))
                {
                    named.Add($"{file.Relative} names {type}");
                }
            }
        }

        named.ShouldBeEmpty(
            "a test that opens a socket is a test that can reach Deepgram, and every response under "
            + "test comes from `tests/fixtures/deepgram/` through a handler declared in the test "
            + "tree. If what you want is a provider that is not there, `FakeDeepgram` is it.");
    }

    /// <summary>
    /// An <c>HttpClient</c> is only ever built where the handler it is built over is declared, so
    /// there is nowhere in the tree a client can be handed something that is not a fake.
    /// </summary>
    /// <remarks>
    /// The granularity is the file and not the type, because a fake in this tree is one type in one
    /// file and the walk reads text. The two assertions before the rule are what stop it passing by
    /// reading nothing: a tree with no handler in it would have nothing to allow and would let
    /// every file through, and a tree naming no client at all would be comparing two empty lists.
    /// </remarks>
    [Fact]
    public void Every_file_that_builds_an_HttpClient_declares_the_handler_behind_it()
    {
        var code = Code();

        var handlers = code.Where(file => Handler().IsMatch(file.Text))
            .Select(file => file.Relative).ToArray();

        handlers.ShouldNotBeEmpty(
            "no file under `tests/` declares a type deriving from `HttpMessageHandler`, so this "
            + "check has nothing to allow and would pass over any file at all. "
            + "`FakeDeepgram` is the one it should be finding.");

        var names = code.Where(file => Client().IsMatch(file.Text))
            .Select(file => file.Relative).ToArray();

        names.ShouldNotBeEmpty(
            "no file under `tests/` names `HttpClient` outside a comment, so this check reads "
            + "nothing. `FakeDeepgram.Client` is the one it should be finding.");

        names.Except(handlers).ShouldBeEmpty(
            "a file that names `HttpClient` and declares no handler has nothing to put behind one, "
            + "so whatever it builds goes to the network. A fake provider owns the client over "
            + "itself — `FakeDeepgram.Client` is the shape — and a test asks the fake for one.");
    }

    /// <summary>
    /// The one case the fact above lets through: a client with nothing behind it, written inside
    /// the very file that declares the handler it should have been handed.
    /// </summary>
    [Fact]
    public void No_HttpClient_under_test_is_built_with_nothing_behind_it()
    {
        Code().Where(file => Bare().IsMatch(file.Text)).Select(file => file.Relative).ShouldBeEmpty(
            "`new HttpClient()` with nothing in the brackets builds its own handler, and that one "
            + "has a socket behind it. Hand it the fake instead — `new(this)` inside the handler is "
            + "what `FakeDeepgram.Client` does.");
    }

    /// <summary>A type declared here with a message handler behind it — the only fake that may.</summary>
    [GeneratedRegex(@"\bclass\s+\w+\s*:[^\r\n{]*\b(HttpMessageHandler|DelegatingHandler)\b")]
    private static partial Regex Handler();

    /// <summary>The type itself. <c>HttpClientHandler</c> is a different word and is the first fact's.</summary>
    [GeneratedRegex(@"\bHttpClient\b")]
    private static partial Regex Client();

    /// <summary>
    /// An <c>HttpClient</c> built over nothing, spelled either way: <c>new HttpClient()</c>, and the
    /// target-typed <c>new()</c> that a member or a local declared as <c>HttpClient</c> gets — which
    /// is how this tree spells the one construction it has, so a rule reading only the first form
    /// would find nothing at all. The second alternative cannot cross a <c>;</c>, <c>{</c> or
    /// <c>}</c>, so it reads one declaration and never runs on into the next.
    /// </summary>
    /// <remarks>
    /// It does cross a line break, because this tree's one construction does — the member is
    /// declared on one line and its <c>new</c> is on the next. Two things follow, neither of them
    /// today's: a member whose <em>parameter</em> is an <c>HttpClient</c> and whose body is a
    /// target-typed <c>new()</c> of something else reddens this and is told to hand it the fake,
    /// which is wrong advice; and a client declared on one line and assigned on another is missed,
    /// because a <c>;</c> sits between them. Telling those apart is a parser, and the noisy half is
    /// the half a person answers rather than never hears.
    /// </remarks>
    [GeneratedRegex(@"\bnew\s+HttpClient\s*\(\s*\)|\bHttpClient\b[^;{}]*\bnew\s*\(\s*\)")]
    private static partial Regex Bare();

    /// <summary>
    /// A line that is all comment, which is the whole of what is taken out. It matches from the
    /// start of a line through leading whitespace to <c>//</c>, <c>/*</c> or the <c>*</c> that
    /// continues or closes a block — never a <c>//</c> with code in front of it, for the reason the
    /// remarks on this class give.
    /// </summary>
    [GeneratedRegex(@"^[ \t]*(//|/\*|\*).*$", RegexOptions.Multiline)]
    private static partial Regex Comment();

    /// <summary>
    /// One of the forbidden names, matched as a whole word and with case: <c>\bSocket\b</c> does not
    /// fire inside <c>SocketsHttpHandler</c> or <c>ClientWebSocket</c>, which is why both are named
    /// in their own right, and <c>socket</c> in a sentence is not <c>Socket</c> in code. The pattern
    /// is interpolated rather than generated, so <c>SYSLIB1045</c> does not fire on it.
    /// </summary>
    private static Regex Whole(string type) => new($@"\b{type}\b");

    /// <summary>
    /// Every hand-written <c>*.cs</c> under <c>tests/</c> with its comments taken out, this file
    /// left out, named relative to the repository root so a failure says where to look.
    /// </summary>
    private static IReadOnlyList<Source> Code()
    {
        var root = IsaDocument.Root();
        var found = new DirectoryInfo(Path.Combine(root.FullName, "tests"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !Built(file))
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .ToArray();

        // Without this the skip below can stop matching — a tree built on one machine and run on
        // another, say — and all three facts go red on the one file that is allowed to name every
        // word they forbid, saying nothing about why.
        found.ShouldContain(
            file => IsThisFile(file),
            "the file holding this rule was not found under `tests/`, so the skip below reads "
            + "nothing and this check is about to fail on itself.");

        Source[] read =
        [
            .. found
                .Where(file => !IsThisFile(file))
                .Select(file => new Source(
                    Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/'),
                    Comment().Replace(File.ReadAllText(file.FullName), " "))),
        ];

        // Asked here rather than in each fact, so a walk that found nothing says so once and every
        // rule over it is answered by the same sentence.
        read.ShouldNotBeEmpty("no `*.cs` file was found under `tests/`, so this check reads nothing.");

        return read;
    }

    private static bool Built(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsThisFile(FileInfo file) =>
        string.Equals(file.FullName, ThisFile, StringComparison.OrdinalIgnoreCase);

    private static string Here([CallerFilePath] string file = "") => file;

    /// <summary>One file: where it is, and what it says once the comments are out.</summary>
    private sealed record Source(string Relative, string Text);
}
