namespace MeetingTranscriber.Mcp;

/// <summary>
/// The corpus's other read-only face: an MCP server an agent asks about meetings somebody
/// recorded, over the Windows user's own permissions and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// No port, no database on the network, no backend. An MCP client starts this executable and talks
/// to it over its own standard input and output, so what reaches the corpus is a child process of
/// whatever the person running it already trusted, reading the files that person can already read.
/// </para>
/// <para>
/// It takes two optional pairs, in either order, which an MCP client config passes through its own
/// <c>args</c>. <c>--corpus &lt;folder&gt;</c> is the way a second process, or a test, points this
/// server at a corpus that is not this user's; without it <see cref="TheCorpusHere"/> asks what
/// the window asks. <c>--record &lt;file&gt;</c> is where each request is written down, for the same
/// reason: without it the record is this user's own, and a suite must never write into that.
/// </para>
/// </remarks>
internal static class Program
{
    private static Task<int> Main(string[] args) => CorpusServer.RunAsync(args);
}
