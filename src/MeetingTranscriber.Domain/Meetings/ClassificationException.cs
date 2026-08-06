namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// A node was going to be put somewhere the classification tree does not reach: at a depth that is
/// not its parent's plus one, under a class that cannot hold it, or as a root when it is not one.
/// </summary>
/// <remarks>
/// The same rules are constraints in the schema, and both are needed. The database is what stops a
/// repair script or an import writing directly; this is what stops the application from building
/// the row in the first place, where the message can still say which node and why.
/// </remarks>
public sealed class ClassificationException(string message) : InvalidOperationException(message);
