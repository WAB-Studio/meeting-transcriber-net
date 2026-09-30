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
/// It takes one optional pair, <c>--corpus &lt;folder&gt;</c>, which an MCP client config passes
/// through its own <c>args</c> — the way a second process, or a test, points this server at a
/// corpus that is not this user's. Without it <see cref="TheCorpusHere"/> asks what the window
/// asks.
/// </para>
/// </remarks>
internal static class Program
{
    private static Task<int> Main(string[] args) => CorpusServer.RunAsync(args);
}
