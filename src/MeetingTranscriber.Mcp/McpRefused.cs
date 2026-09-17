namespace MeetingTranscriber.Mcp;

/// <summary>
/// The one thing this server throws on purpose: something a person can do something about, said
/// in words rather than in whatever the layer underneath would have said.
/// </summary>
/// <remarks>
/// It reaches an agent as a tool error and never as the process dying, which is
/// <see cref="CorpusServer"/>'s whole shape: a server that stops has cost the agent every answer
/// after this one, and the commonest reason to refuse — a machine where nobody has recorded
/// anything yet — is not a reason to end a session.
/// </remarks>
internal sealed class McpRefused(string why) : Exception(why);
