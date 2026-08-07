using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// How a meeting is classified. Both the shape and the names are settled: they are stored values
// with a CHECK behind them, so renaming one from here is a migration and not a relabelling. What
// they have to hold is the thirteen meetings arquitectura.md §5.3 lists, and
// ClassificationStoriesTests is each of them stored and found again.

/// <summary>What a node of the classification tree is.</summary>
public enum NodeKind
{
    /// <summary>
    /// An organization of any sort. Deliberately not "company": a university, a conference and a
    /// client are all one of these, and calling them companies made the name lie.
    /// </summary>
    Organization = 1,

    /// <summary>A body of work that lasts — a project, a course, a support line.</summary>
    Initiative = 2,

    /// <summary>One concrete subject: an incident, a ticket, a negotiation.</summary>
    Topic = 3,
}

/// <summary>How a meeting relates to a node it is linked to.</summary>
public enum MeetingNodeRole
{
    /// <summary>The meeting is work belonging to this node. What a project used to be.</summary>
    WorkOf = 1,

    /// <summary>The other side of the table: a client, a company interviewing, a partner.</summary>
    Counterpart = 2,

    /// <summary>What the meeting is about, without being work of it.</summary>
    About = 3,
}

/// <summary>How a person relates to a meeting they are named on.</summary>
/// <remarks>
/// Not one of two, but as many as apply, the same way a node's role is: somebody in their own one
/// to one attended it and is what it was about, and a dismissal discussed before the person is in
/// the room is the subject of a meeting they were never at. One column forced a choice between
/// those and lost whichever was not picked.
/// </remarks>
public enum MeetingPersonRole
{
    Attended = 1,

    /// <summary>The meeting is about them: a one to one, an interview, a dismissal.</summary>
    Subject = 2,
}

/// <summary>
/// One node of the classification tree: an organization, the work under it, a subject inside
/// that. A meeting hangs off nodes rather than off a single project, because a meeting that
/// covers two projects is ordinary and a meeting held before any project exists is too.
/// </summary>
/// <remarks>
/// <para>
/// Three levels at most. It is a classification a person keeps in their head, not a filesystem:
/// with the depth capped, everything under a node is two joins away and needs no recursive query
/// or materialised path to find.
/// </para>
/// <para>
/// A child carries its parent's class and depth beside the parent's id, and the three are one
/// foreign key against the parent's own columns — the same technique a citation uses to point at
/// the pair of a meeting and a turn's position. That is what makes "a child sits exactly one below
/// its parent" and "the classes go organization, initiative, topic in that order" things the
/// database refuses rather than things a comment asserts. They are the parent's values, never set
/// by a caller, which is why the factories are the only way in.
/// </para>
/// </remarks>
public class Node
{
    /// <summary>The deepest a tree goes: a root, its work, and a subject inside that work.</summary>
    public const int MaxDepth = 2;

    private Node(string name) => Name = name;

    public Guid Id { get; private set; }

    /// <summary>Null only for a root, which is what <see cref="Depth"/> zero means.</summary>
    public Guid? ParentId { get; private set; }

    public NodeKind Kind { get; private set; }

    /// <summary>Renaming a node is ordinary, and moves nothing in the tree.</summary>
    public string Name { get; set; }

    /// <summary>Zero for a root. Always one more than the parent's.</summary>
    public int Depth { get; private set; }

    /// <summary>The parent's class, carried so the foreign key can check it. Null for a root.</summary>
    public NodeKind? ParentKind { get; private set; }

    /// <summary>The parent's depth, carried for the same reason. Null for a root.</summary>
    public int? ParentDepth { get; private set; }

    public UtcTimestamp CreatedAt { get; private set; }

    public UtcTimestamp UpdatedAt { get; set; }

    /// <summary>
    /// Puts a node at the top of a tree of its own. A topic is never one: it is a subject inside
    /// some body of work, and one hanging off nothing names an incident belonging to nobody.
    /// </summary>
    public static Node Root(Guid id, NodeKind kind, string name, UtcTimestamp now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!CanBeRoot(kind))
        {
            throw new ClassificationException(
                $"'{name}' is a {kind} and cannot be a root; it belongs inside an {NodeKind.Initiative}.");
        }

        return new Node(name)
        {
            Id = id,
            Kind = kind,
            Depth = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Hangs a node under another, one level down, copying the parent's class and depth so the
    /// key can be checked against them.
    /// </summary>
    public static Node Under(Guid id, Node parent, NodeKind kind, string name, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Holds(parent.Kind) != kind)
        {
            throw new ClassificationException(
                $"A {parent.Kind} holds {Holds(parent.Kind)?.ToString() ?? "nothing"}, so '{name}' cannot be a {kind} inside it.");
        }

        if (parent.Depth + 1 > MaxDepth)
        {
            throw new ClassificationException(
                $"'{name}' would sit at depth {parent.Depth + 1}, and the tree stops at {MaxDepth}.");
        }

        return new Node(name)
        {
            Id = id,
            ParentId = parent.Id,
            Kind = kind,
            Depth = parent.Depth + 1,
            ParentKind = parent.Kind,
            ParentDepth = parent.Depth,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>The one class a node of this class can hold, or null when it holds none.</summary>
    public static NodeKind? Holds(NodeKind kind) => kind switch
    {
        NodeKind.Organization => NodeKind.Initiative,
        NodeKind.Initiative => NodeKind.Topic,
        NodeKind.Topic => null,
        _ => throw new ClassificationException($"Unknown node kind '{kind}'."),
    };

    /// <summary>
    /// Whether a node of this class can stand at the top of a tree of its own. An organization
    /// always does; work belonging to nobody in particular is ordinary and does too. A topic is
    /// a subject inside some body of work, and one hanging off nothing is a subject of nothing.
    /// </summary>
    public static bool CanBeRoot(NodeKind kind) => Holds(kind) is not null;
}

/// <summary>
/// A meeting and something it is about, with the part that says which of the two it is. A meeting
/// has as many of these as it needs: two projects, or a project and the client on the other side.
/// </summary>
public class MeetingNode
{
    public Guid MeetingId { get; set; }

    public Guid NodeId { get; set; }

    public MeetingNodeRole Role { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>
/// A named shape for a meeting — "work", "with a client", "interview", "class".
/// </summary>
/// <remarks>
/// Data and not code, so one can be added without touching the schema. Today it carries only its
/// name, which is what the Python corpus called the meeting type. What each one pre-fills — which
/// kinds of child a space has, which links it asks for — arrives with the interface that offers
/// them, and it will always only pre-fill: a template can never express what the constraints
/// forbid, and a meeting resembling none of them is classified by hand.
/// </remarks>
public class MeetingTemplate
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
