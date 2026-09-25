using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>
/// The corpus side of the screen that says who is who, read: the meeting's voices, everybody who
/// could be put on one, and the organizations a new person can be added to.
/// </summary>
/// <param name="Everybody">Every person in the corpus, by display name and then by id.</param>
/// <param name="Organizations">
/// The nodes at the top of the tree, for the dialogue that adds a person: only an organization is a
/// place somebody can belong to.
/// </param>
public sealed record VoicesAsHeard(
    Meeting Meeting, WhoIsWho Voices, IReadOnlyList<Person> Everybody, IReadOnlyList<Node> Organizations);

/// <summary>One voice, and who somebody put on it, or nobody.</summary>
public sealed record VoiceAnswer(string Label, Guid? PersonId);

/// <summary>
/// The corpus side of the screen that says who is who: the voices a meeting's turns carry, who is
/// on each, everybody who could be, and the one thing that screen writes back.
/// </summary>
/// <remarks>
/// Beside <see cref="MeetingReading"/> and <see cref="MeetingClassifying"/> rather than inside
/// <see cref="HumanLayer"/>, for the reason that type's own remarks give about
/// <c>MeetingClassifying</c>: this composes the human layer's writes rather than growing it with a
/// walk that has nothing to do with the invariant it exists to hold alone.
/// </remarks>
public sealed class MeetingVoices(CorpusDbContext context, TimeProvider clock)
{
    /// <summary>One meeting, as the screen that names voices needs it.</summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public VoicesAsHeard Of(Guid meetingId)
    {
        var meeting = new MeetingReading(context, clock).Row(meetingId);
        var voices = Heard(meetingId);

        var everybody = context.People
            .AsNoTracking()
            .OrderBy(person => person.DisplayName)
            .ThenBy(person => person.Id)
            .ToArray();

        var organizations = CorpusNodes.All(context)
            .Where(node => node.Kind is NodeKind.Organization)
            .OrderBy(node => node.Name)
            .ToArray();

        return new VoicesAsHeard(meeting, voices, everybody, organizations);
    }

    /// <summary>
    /// The short read, with no one to choose from — what a reading screen needs to show who spoke,
    /// and nothing a screen that offers a picker does.
    /// </summary>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    public WhoIsWho Heard(Guid meetingId)
    {
        var reading = new MeetingReading(context, clock);
        var meeting = reading.Row(meetingId);
        var turns = reading.EveryTurn(meetingId);

        var assigned = context.SpeakerAssignments
            .AsNoTracking()
            .Where(row => row.MeetingId == meetingId)
            .Join(
                context.People.AsNoTracking(),
                row => row.PersonId,
                person => person.Id,
                (row, person) => new AssignedVoice(row.SpeakerLabel, person.Id, person.DisplayName, row.AssignedBy))
            .ToArray();

        return WhoIsWho.Of(meeting.SourceProfile, turns, assigned);
    }

    /// <summary>
    /// Puts the names somebody chose on the meeting's voices, and answers how many labels it
    /// changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every answer is checked before any of them is written: a label answered twice, a label no
    /// turn of the meeting carries, or a person this corpus does not hold each refuse the whole
    /// save rather than half of it.
    /// </para>
    /// <para>
    /// One transaction, joining the caller's when one is open and opening its own otherwise —
    /// <see cref="HumanLayer.Describe"/>'s pattern, for the same reason: <see cref="NamingTheVoices"/>
    /// wraps this together with rendering the meeting again, and a caller reading it on its own
    /// still needs every row it writes to land together or not at all. Opened before the reads that
    /// judge the answers, not only around the writes — <see cref="MeetingClassifying.Save"/>'s own
    /// shape — so a person removed or a meeting re-derived into other labels between the read and
    /// the write is a race the transaction narrows rather than one this method leaves open on top of
    /// the corpus being one more than one process can open.
    /// </para>
    /// </remarks>
    /// <exception cref="MeetingStageException">There is no such meeting in this corpus.</exception>
    /// <exception cref="ArgumentException">
    /// An answer names a label twice, a label no turn of the meeting carries, or a person this
    /// corpus does not hold.
    /// </exception>
    public int Save(Guid meetingId, IReadOnlyList<VoiceAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        var human = new HumanLayer(context, clock);
        using var saving = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        var voices = Heard(meetingId);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!seen.Add(answer.Label))
            {
                throw new ArgumentException(
                    $"'{answer.Label}' was answered twice in one save, so nothing was named.",
                    nameof(answers));
            }

            if (voices.ForLabel(answer.Label) is null)
            {
                throw new ArgumentException(
                    $"Meeting {meetingId} has no voice under '{answer.Label}', so nothing was named.",
                    nameof(answers));
            }
        }

        var wanted = answers
            .Where(answer => answer.PersonId is not null)
            .Select(answer => answer.PersonId!.Value)
            .Distinct()
            .ToArray();

        var people = context.People.Where(person => wanted.Contains(person.Id)).ToDictionary(person => person.Id);

        foreach (var id in wanted.Where(id => !people.ContainsKey(id)))
        {
            throw new ArgumentException($"This corpus holds no person {id}.", nameof(answers));
        }

        var changed = 0;

        foreach (var answer in answers)
        {
            var voice = voices.ForLabel(answer.Label)!;

            if (answer.PersonId is { } personId)
            {
                if (voice.PersonId == personId && !voice.SettledByTheRecording)
                {
                    continue;
                }

                human.Assign(meetingId, answer.Label, people[personId]);
                changed++;
            }
            else if (voice.PersonId is not null)
            {
                human.Unassign(meetingId, answer.Label);
                changed++;
            }
        }

        saving?.Commit();
        return changed;
    }
}
