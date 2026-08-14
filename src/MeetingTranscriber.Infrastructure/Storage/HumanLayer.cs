using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The way in for everything a person approves: the classification tree, who is on a meeting, where
/// they belong, which voice is whose, what the transcription gets wrong, and where an action stands.
/// </summary>
/// <remarks>
/// <para>
/// These are the rows <c>docs/corpus.md</c> calls the human layer, and they are the part of the
/// corpus nothing can produce again. A projection can be deleted and rendered from the artifacts;
/// none of this can, which is why it is worth one place that writes it rather than a page of
/// <c>context.Add</c> wherever an edit happens to be handled.
/// </para>
/// <para>
/// What that one place buys is the rules the schema cannot state. A CHECK holds within a row and a
/// key holds across rows that exist at once, so neither can say "exactly one person is the user of
/// this install" — moving that flag is two rows changing together, and a unique index would refuse
/// the half-second between them. Nor can either say that a label a person resolved outranks one the
/// recording guessed, because both are the same row written twice and the database sees only the
/// second. Both live here, and there is nowhere else they could.
/// </para>
/// <para>
/// One edit is one transaction: every method saves. A person's edits are separate acts and a failed
/// one should not take back the one before it, and the two rules above each move more than one row,
/// so the boundary is the method rather than the caller.
/// </para>
/// <para>
/// It reaches the corpus folder, and not because most of it writes files — only
/// <see cref="Describe"/> does. It is because a corpus is a database and a folder, and the title a
/// person types is the one thing a person can change that the folder also carries: the recovery
/// card in <see cref="MeetingManifest"/>. A human layer that could not reach the folder would be
/// one whose renames go stale on disk with nothing able to say so. The folder comes from the
/// corpus itself rather than from whoever constructs this, which is what makes it the folder these
/// rows are in and not one a caller named. A layer above this one, holding the folder and calling
/// <see cref="Describe"/> as its database half, keeps this type about rows and leaves the bug
/// where it was: <see cref="Describe"/> stays public, so the rename that changes the title and not
/// the card is still one call away.
/// </para>
/// </remarks>
public sealed class HumanLayer(CorpusDbContext context, TimeProvider clock)
{
    private UtcTimestamp Now => UtcTimestamp.From(clock.GetUtcNow());

    /// <summary>
    /// Puts a node at the top of a tree of its own — an organization, or a body of work belonging to
    /// nobody in particular.
    /// </summary>
    public Node Root(NodeKind kind, string name)
    {
        var node = Node.Root(Guid.NewGuid(), kind, name, Now);
        context.Nodes.Add(node);
        context.SaveChanges();
        return node;
    }

    /// <summary>Hangs a node one level under another.</summary>
    public Node Under(Node parent, NodeKind kind, string name)
    {
        var node = Node.Under(Guid.NewGuid(), parent, kind, name, Now);
        context.Nodes.Add(node);
        context.SaveChanges();
        return node;
    }

    /// <summary>Renaming a node moves nothing in the tree and nothing that hangs off it.</summary>
    public void Rename(Node node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        node.Name = name;
        node.UpdatedAt = Now;
        context.SaveChanges();
    }

    /// <summary>
    /// Drops a node, everything under it, and every link and affiliation onto any of them. The
    /// people stay: they are on meetings, and a meeting missing the people on it is not repairable.
    /// </summary>
    public void Remove(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        context.Nodes.Remove(node);
        context.SaveChanges();
    }

    /// <summary>
    /// Says a meeting relates to a node, and which way. A meeting has as many of these as it needs,
    /// and saying the same thing twice changes nothing rather than failing.
    /// </summary>
    public MeetingNode Link(Guid meetingId, Node node, MeetingNodeRole role)
    {
        ArgumentNullException.ThrowIfNull(node);

        var existing = context.MeetingNodes.Find(meetingId, node.Id, role);
        if (existing is not null)
        {
            return existing;
        }

        var link = new MeetingNode
        {
            MeetingId = meetingId,
            NodeId = node.Id,
            Role = role,
            CreatedAt = Now,
        };

        context.MeetingNodes.Add(link);
        context.SaveChanges();
        return link;
    }

    /// <summary>
    /// Takes one link off, leaving the others. Unlinking what was never linked is not an error: the
    /// caller asked for the meeting not to be work of that node, and it is not.
    /// </summary>
    public void Unlink(Guid meetingId, Node node, MeetingNodeRole role)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (context.MeetingNodes.Find(meetingId, node.Id, role) is { } link)
        {
            context.MeetingNodes.Remove(link);
            context.SaveChanges();
        }
    }

    /// <summary>Names a template. It carries only its name; what it pre-fills arrives with the UI.</summary>
    public MeetingTemplate Template(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var template = new MeetingTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = Now,
        };

        context.Templates.Add(template);
        context.SaveChanges();
        return template;
    }

    /// <summary>
    /// Somebody new. Never the user of this install: that flag moves through
    /// <see cref="ThisIsMe"/>, which is what keeps it on one person.
    /// </summary>
    public Person Add(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var now = Now;
        var person = new Person
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.People.Add(person);
        context.SaveChanges();
        return person;
    }

    public void Rename(Person person, string displayName)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        person.DisplayName = displayName;
        person.UpdatedAt = Now;
        context.SaveChanges();
    }

    /// <summary>
    /// Moves the flag naming the user of this install, taking it off whoever had it. There is one
    /// person behind the microphone, and two of them would leave <c>Speakers.Resolve</c> settling a
    /// voice onto whichever row a query happened to return first.
    /// </summary>
    /// <remarks>
    /// Not a unique index, and the reason is worth stating so nobody adds one and reverts this. The
    /// flag moving is one row losing it and another gaining it, and SQLite checks a unique index at
    /// the end of each statement rather than of the transaction — so the pair would be refused
    /// whenever the update setting it happened to run before the update clearing it, which is not
    /// something the caller can order. The invariant is real; this is the only place it can hold.
    /// </remarks>
    public void ThisIsMe(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var now = Now;
        foreach (var other in context.People.Where(candidate => candidate.IsMe).ToArray())
        {
            if (other.Id == person.Id)
            {
                continue;
            }

            other.IsMe = false;
            other.UpdatedAt = now;
        }

        person.IsMe = true;
        person.UpdatedAt = now;
        context.SaveChanges();
    }

    /// <summary>
    /// Forgets somebody, and with them every meeting that named them, every organization they were
    /// at and every voice resolved onto them. What they decided and what they own is kept and
    /// loses their name, because a decision nobody made is still a decision the meeting reached.
    /// </summary>
    public void Remove(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        context.People.Remove(person);
        context.SaveChanges();
    }

    /// <summary>
    /// Puts somebody at an organization, open ended unless a start is given. Somebody can be at two
    /// at once; what they cannot be is at the same one twice without having left it, which is the
    /// same fact written down twice and is refused by the corpus.
    /// </summary>
    public Affiliation Join(Person person, Node organization, UtcTimestamp? from = null)
    {
        var affiliation = Affiliation.At(Guid.NewGuid(), person, organization, Now, from);
        context.Affiliations.Add(affiliation);
        context.SaveChanges();
        return affiliation;
    }

    /// <summary>
    /// Closes a spell rather than deleting it, so reading a meeting from back then still says who
    /// they worked for that day.
    /// </summary>
    public void Leave(Affiliation affiliation, UtcTimestamp at)
    {
        ArgumentNullException.ThrowIfNull(affiliation);

        affiliation.Ended(at);
        context.SaveChanges();
    }

    /// <summary>
    /// Names somebody on a meeting under one role. Both roles at once are two calls and two rows:
    /// somebody's own one to one is a meeting they attended and are the subject of.
    /// </summary>
    public MeetingPerson Name(Guid meetingId, Person person, MeetingPersonRole role)
    {
        ArgumentNullException.ThrowIfNull(person);

        var existing = context.MeetingPeople.Find(meetingId, person.Id, role);
        if (existing is not null)
        {
            return existing;
        }

        var named = new MeetingPerson
        {
            MeetingId = meetingId,
            PersonId = person.Id,
            Role = role,
            CreatedAt = Now,
        };

        context.MeetingPeople.Add(named);
        context.SaveChanges();
        return named;
    }

    /// <summary>Takes one role off, leaving the other if the meeting names them under both.</summary>
    public void Unname(Guid meetingId, Person person, MeetingPersonRole role)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (context.MeetingPeople.Find(meetingId, person.Id, role) is { } named)
        {
            context.MeetingPeople.Remove(named);
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Settles a speaker label onto a person. The label stays exactly as the provider wrote it —
    /// this is what turns one into a name at render time, and it is the key a name hangs off, so
    /// the caller passes the label <c>SpeakerLabels.For</c> built and never a person's name.
    /// </summary>
    /// <param name="assignedBy">
    /// What settled it. <see cref="SpeakerAssignmentSource.Channel"/> is the recording having had
    /// nobody else it could be, and it never overwrites a person: somebody who said which voice is
    /// whose has answered a question the microphone was only guessing at. The other direction does
    /// overwrite, because that is a person correcting the guess.
    /// </param>
    public SpeakerAssignment Assign(
        Guid meetingId,
        string speakerLabel,
        Person person,
        SpeakerAssignmentSource assignedBy = SpeakerAssignmentSource.Person)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerLabel);
        ArgumentNullException.ThrowIfNull(person);

        var existing = context.SpeakerAssignments.Find(meetingId, speakerLabel);
        if (existing is not null)
        {
            if (assignedBy is SpeakerAssignmentSource.Channel
                && existing.AssignedBy is SpeakerAssignmentSource.Person)
            {
                return existing;
            }

            existing.PersonId = person.Id;
            existing.AssignedBy = assignedBy;
            existing.AssignedAt = Now;
            context.SaveChanges();
            return existing;
        }

        var assignment = new SpeakerAssignment
        {
            MeetingId = meetingId,
            SpeakerLabel = speakerLabel,
            PersonId = person.Id,
            AssignedBy = assignedBy,
            AssignedAt = Now,
        };

        context.SpeakerAssignments.Add(assignment);
        context.SaveChanges();
        return assignment;
    }

    /// <summary>Leaves the label unresolved again, which is what it was before somebody answered.</summary>
    public void Unassign(Guid meetingId, string speakerLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerLabel);

        if (context.SpeakerAssignments.Find(meetingId, speakerLabel) is { } assignment)
        {
            context.SpeakerAssignments.Remove(assignment);
            context.SaveChanges();
        }
    }

    /// <summary>
    /// A word the transcription gets wrong and what it should say. Applied when rendering and never
    /// written into the raw response, which is what a citation is checked against.
    /// </summary>
    /// <param name="under">
    /// The node it applies inside, and everything below it. A jargon term belongs to the work it
    /// comes from rather than to one meeting of it.
    /// </param>
    /// <param name="meetingId">The one meeting it applies to. Neither scope makes it global.</param>
    public TerminologyCorrection Correct(
        string wrongText,
        string correctText,
        TerminologyMatchMode matchMode = TerminologyMatchMode.Exact,
        Node? under = null,
        Guid? meetingId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrongText);
        ArgumentException.ThrowIfNullOrWhiteSpace(correctText);

        if (under is not null && meetingId is not null)
        {
            throw new ArgumentException(
                $"'{wrongText}' cannot be scoped to both '{under.Name}' and one meeting; a correction has one scope or none.",
                nameof(meetingId));
        }

        var correction = new TerminologyCorrection
        {
            Id = Guid.NewGuid(),
            NodeId = under?.Id,
            MeetingId = meetingId,
            WrongText = wrongText,
            CorrectText = correctText,
            MatchMode = matchMode,
            CreatedAt = Now,
        };

        context.TerminologyCorrections.Add(correction);
        context.SaveChanges();
        return correction;
    }

    /// <summary>Changes what a correction replaces the word with, and how it matches.</summary>
    public void Recorrect(TerminologyCorrection correction, string correctText, TerminologyMatchMode matchMode)
    {
        ArgumentNullException.ThrowIfNull(correction);
        ArgumentException.ThrowIfNullOrWhiteSpace(correctText);

        correction.CorrectText = correctText;
        correction.MatchMode = matchMode;
        context.SaveChanges();
    }

    public void Drop(TerminologyCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);

        context.TerminologyCorrections.Remove(correction);
        context.SaveChanges();
    }

    /// <summary>
    /// Where an action stands and who took it. Keyed on the extraction and the action's position in
    /// it, never on an action's id, so what somebody marked is still on the right action after a
    /// rebuild has thrown every <c>action_items</c> row away and projected new ones.
    /// </summary>
    public ActionItemProgress Mark(
        Guid extractionRunId,
        int ordinal,
        ActionItemState state,
        Person? owner = null)
    {
        var progress = context.ActionItemProgress.Find(extractionRunId, ordinal);
        if (progress is null)
        {
            progress = new ActionItemProgress { ExtractionRunId = extractionRunId, Ordinal = ordinal };
            context.ActionItemProgress.Add(progress);
        }

        progress.State = state;
        progress.OwnerPersonId = owner?.Id;
        progress.UpdatedAt = Now;
        context.SaveChanges();
        return progress;
    }

    /// <summary>
    /// The title and the note somebody wrote so a person who was not there can read the meeting.
    /// Nothing infers either, which is why they are stored beside the meeting and not projected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recovery card is rewritten here because the title is on it, and this is the only place a
    /// title moves after the meeting was filed. Leaving it to <c>rebuild</c> was what the corpus did
    /// before, and a rename is not the moment anybody runs one: the folder would go on naming the
    /// meeting by a title nobody uses, and the day that mattered would be the day the database was
    /// gone and the card was the only thing left to read.
    /// </para>
    /// <para>
    /// The two halves are one transaction, which is the only arrangement that does not recreate the
    /// bug in a narrower window. The row is saved first because the card is produced from what the
    /// corpus holds — writing it first would put a title on disk that no statement had yet agreed
    /// to — but saved outside a transaction it would stand while a card write that could not
    /// happen threw, and a rename with a stale card is exactly what this method exists to prevent.
    /// Inside one, a disk that is full or a folder that cannot be written takes the rename back
    /// with it, and the person is told the title did not change rather than told nothing and left
    /// with a folder that disagrees.
    /// </para>
    /// <para>
    /// What the transaction cannot cover is the file itself, because a filesystem does not join a
    /// SQLite transaction: a rollback after the replace has happened leaves the new card beside the
    /// old row, which <see cref="Artifacts.ArtifactReconciler"/> reports as a hash that disagrees
    /// and <c>rebuild</c> puts right. That window is the one <see cref="Artifacts.DurableArtifact"/>
    /// already chooses everywhere — a file ahead of the corpus, never a corpus ahead of the file.
    /// </para>
    /// <para>
    /// Written on every call rather than only when the title actually moved. The card is a
    /// statement of what the corpus holds, not a diff of it, so a card that is missing, emptied or
    /// left behind by a write that never finished comes back on the next edit — and a comparison
    /// that decided otherwise would be one more thing that has to be right for the folder to be.
    /// The note is not on the card and never has been: five fields, and
    /// <see cref="MeetingManifest"/> says why.
    /// </para>
    /// </remarks>
    public void Describe(Meeting meeting, string? title, string? notes)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        using var edit = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        var now = Now;
        meeting.Title = title;
        meeting.Context = notes;
        meeting.UpdatedAt = now;
        context.SaveChanges();

        MeetingManifest.Write(context, meeting.Id, now);

        edit?.Commit();
    }

    /// <summary>The shape the meeting was classified as, or none.</summary>
    public void Shape(Meeting meeting, MeetingTemplate? template)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        meeting.TemplateId = template?.Id;
        meeting.UpdatedAt = Now;
        context.SaveChanges();
    }
}
