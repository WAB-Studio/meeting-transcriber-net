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
/// It takes no arguments, and that is a decision rather than an omission: an MCP client config
/// names an executable and nothing else, so there is no <c>--corpus</c> anybody could have typed.
/// <see cref="TheCorpusHere"/> asks what the window asks.
/// </para>
/// </remarks>
internal static class Program
{
    private static Task<int> Main() => CorpusServer.RunAsync();
}
