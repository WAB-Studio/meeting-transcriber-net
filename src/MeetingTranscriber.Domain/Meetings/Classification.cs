using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// How a meeting is classified. The shape here is settled; the names are not — they are stored
// values with a CHECK behind them, so renaming one is a migration and not a relabelling. Which
// names these end up with is decided in "Refinar los nombres de la clasificación".

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

/// <summary>Why a person is on a meeting.</summary>
public enum ParticipantRole
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
/// <see cref="Depth"/> is stored because a CHECK cannot look at another row, let alone another
/// table. The schema can say a depth is between zero and two and that only a root has no parent;
/// that a child sits exactly one below its parent is this type's to keep.
/// </para>
/// </remarks>
public class Node
{
    /// <summary>The deepest a tree goes: a root, its work, and a subject inside that work.</summary>
    public const int MaxDepth = 2;

    public Guid Id { get; set; }

    /// <summary>Null only for a root, which is what <see cref="Depth"/> zero means.</summary>
    public Guid? ParentId { get; set; }

    public NodeKind Kind { get; set; }

    public required string Name { get; set; }

    /// <summary>Zero for a root. Always one more than the parent's.</summary>
    public int Depth { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
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
