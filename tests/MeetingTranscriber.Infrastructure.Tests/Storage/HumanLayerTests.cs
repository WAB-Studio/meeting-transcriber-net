using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The write path for the part of the corpus nothing can produce again. Until this existed the
/// human layer had tables, constraints and a backup policy, and no code that put a row in any of
/// them outside the legacy importer — so every rule about it was a rule about a shape nobody filled.
/// </summary>
/// <remarks>
/// Most of what is asserted here the database would refuse on its own, and those assertions are
/// still worth having: they say the service reaches the constraint rather than failing earlier for
/// its own reasons. The ones that matter most are the two the schema cannot state at all — one
/// person is the user of this install, and a label somebody resolved outranks one the recording
/// guessed — because for those this class is the only thing standing between the rule and a corpus
/// that quietly breaks it.
/// </remarks>
public class HumanLayerTests
{
    /// <summary>
    /// One pass over every table <c>docs/corpus.md</c> calls the human layer. It is deliberately one
    /// test rather than nine: what is being asserted is that there is a way in for all of them, and
    /// nine passing tests with a tenth table nobody can write is the state this replaced.
    /// </summary>
    [Fact]
    public void Every_table_of_the_human_layer_has_a_way_in()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var organization = human.Root(NodeKind.Organization, "TechSed");
        var initiative = human.Under(organization, NodeKind.Initiative, "Coati");
        var template = human.Template("trabajo");
        var ada = human.Add("Ada");
        var meeting = fixture.Meeting("la daily");

        human.ThisIsMe(ada);
        human.Join(ada, organization);
        human.Link(meeting.Id, initiative, MeetingNodeRole.WorkOf);
        human.Name(meeting.Id, ada, MeetingPersonRole.Attended);
        human.Assign(meeting.Id, "ch1:speaker_0", ada);
        human.Correct("quati", "Coati", under: initiative);
        human.Describe(meeting, "la daily del equipo", "arranca el sprint");
        human.Shape(meeting, template);
        human.Mark(fixture.ExtractionRunId(meeting), ordinal: 0, ActionItemState.Done, ada);

        context.Nodes.Count().ShouldBe(2);
        context.Templates.Count().ShouldBe(1);
        context.People.Count().ShouldBe(1);
        context.Affiliations.Count().ShouldBe(1);
        context.MeetingNodes.Count().ShouldBe(1);
        context.MeetingPeople.Count().ShouldBe(1);
        context.SpeakerAssignments.Count().ShouldBe(1);
        context.TerminologyCorrections.Count().ShouldBe(1);
        context.ActionItemProgress.Count().ShouldBe(1);

        var stored = context.Meetings.Single();
        stored.Title.ShouldBe("la daily del equipo");
        stored.Context.ShouldBe("arranca el sprint");
        stored.TemplateId.ShouldBe(template.Id);
    }

    /// <summary>
    /// The title is on the recovery card, and it is the one of that card's five fields a person
    /// moves after the meeting was filed. So renaming writes the card, and the probe is the
    /// situation the card exists for: the corpus is closed and the file is all that is left to
    /// answer from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meeting is given its card first, the way filing one leaves it, so what is being probed
    /// is a card going stale rather than one never written. Those are different failures and only
    /// the first is what a rename does.
    /// </para>
    /// <para>
    /// Read back through <c>MeetingManifest.Read</c> rather than by looking for the new title in
    /// the text. A file holding the right characters in the wrong shape is not a card anybody
    /// recovers from, and a substring check would call it one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Renaming_a_meeting_leaves_its_card_saying_the_new_title()
    {
        using var corpus = new TemporaryCorpus();
        Guid meetingId;
        string card;

        using (var context = corpus.OpenMigrated())
        {
            var fixture = new HumanLayerFixture(context, corpus.Root);
            var meeting = fixture.Meeting("la daily");
            meetingId = meeting.Id;
            var filed = MeetingManifest.Write(context, corpus.Root, meeting.Id, HumanLayerFixture.Now);
            MeetingManifest.Read(CorpusFiles.Locate(corpus.Root, filed.RelativePath)).Title.ShouldBe("la daily");

            fixture.HumanLayer.Describe(meeting, "la daily del equipo", "arranca el sprint");

            // Still the one card and the one row: the file is replaced where it stands, which is
            // what keeps a rename out of the artifacts table as a second entry for the same path.
            var manifest = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.Manifest);
            manifest.Id.ShouldBe(filed.Id);
            manifest.Origin.ShouldBe(ArtifactOrigin.Source);
            card = CorpusFiles.Locate(corpus.Root, manifest.RelativePath).FullName;
        }

        var recovered = MeetingManifest.Read(new FileInfo(card));

        recovered.MeetingId.ShouldBe(meetingId);
        recovered.Title.ShouldBe("la daily del equipo");
    }

    /// <summary>
    /// The other half of the same promise: a rename whose card cannot be written does not happen.
    /// Saving the row on its own and letting the card write throw afterwards would leave exactly
    /// the folder this is about — a meeting renamed in the database and named the old way on disk
    /// — in a narrower window and with nobody told which of the two it got.
    /// </summary>
    /// <remarks>
    /// A directory standing where the card goes is how the write is made to fail. It is the
    /// nearest thing this suite can arrange to a full disk, and what matters is that the failure
    /// arrives from the filesystem at the moment of replacing, which is where a real one would —
    /// Windows calls that one access denied rather than an I/O error, which is why the type
    /// asserted is the one it actually throws and not the family it looks like it belongs to.
    /// The title is then read past the tracked entity: a rolled-back transaction does not undo
    /// what EF holds in memory, and what is being asserted is what the corpus kept.
    /// </remarks>
    [Fact]
    public void A_rename_whose_card_cannot_be_written_does_not_happen_at_all()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var meeting = fixture.Meeting("la daily");
        var filed = MeetingManifest.Write(context, corpus.Root, meeting.Id, HumanLayerFixture.Now);

        var card = CorpusFiles.Locate(corpus.Root, filed.RelativePath);
        card.Delete();
        Directory.CreateDirectory(card.FullName);

        Should.Throw<UnauthorizedAccessException>(() =>
            fixture.HumanLayer.Describe(meeting, "la daily del equipo", "arranca el sprint"));

        context.Meetings.AsNoTracking().Single().Title.ShouldBe("la daily");
    }

    /// <summary>
    /// The flag is on one row and moves whole. Two of them would leave <c>Speakers.Resolve</c>
    /// settling a voice onto whichever the query happened to return first, which is a wrong name on
    /// somebody's words and nothing failing.
    /// </summary>
    [Fact]
    public void Exactly_one_person_is_the_user_of_this_install()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayerFixture(context, corpus.Root).HumanLayer;

        var first = human.Add("Ada");
        var second = human.Add("Renata");
        human.ThisIsMe(first);
        human.ThisIsMe(second);

        context.People.Where(person => person.IsMe).Select(person => person.DisplayName)
            .ToArray()
            .ShouldBe(["Renata"]);
    }

    /// <summary>Saying it twice about the same person leaves them the one, not nobody.</summary>
    [Fact]
    public void Saying_it_again_about_the_same_person_changes_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayerFixture(context, corpus.Root).HumanLayer;

        var ada = human.Add("Ada");
        human.ThisIsMe(ada);
        human.ThisIsMe(ada);

        context.People.Single(person => person.IsMe).Id.ShouldBe(ada.Id);
    }

    /// <summary>
    /// The recording only ever guessed: the microphone came back with one speaker, so there was
    /// nobody else it could be. Somebody who said which voice is whose answered a question, and the
    /// answer is not overwritten by a later capture arriving at the same conclusion by default.
    /// </summary>
    [Fact]
    public void A_label_the_recording_settled_does_not_overwrite_one_a_person_resolved()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var ada = human.Add("Ada");
        var jo = human.Add("Jo");

        human.Assign(meeting.Id, "ch1:speaker_0", ada);
        human.Assign(meeting.Id, "ch1:speaker_0", jo, SpeakerAssignmentSource.Channel);

        var assignment = context.SpeakerAssignments.Single();
        assignment.PersonId.ShouldBe(ada.Id);
        assignment.AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
    }

    /// <summary>The other direction, which is a person correcting the guess and has to land.</summary>
    [Fact]
    public void A_person_resolving_a_label_overwrites_what_the_recording_settled()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var ada = human.Add("Ada");
        var jo = human.Add("Jo");

        human.Assign(meeting.Id, "ch1:speaker_0", ada, SpeakerAssignmentSource.Channel);
        human.Assign(meeting.Id, "ch1:speaker_0", jo);

        var assignment = context.SpeakerAssignments.Single();
        assignment.PersonId.ShouldBe(jo.Id);
        assignment.AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
    }

    /// <summary>
    /// Somebody's own one to one is a meeting they attended and are the subject of, so the two roles
    /// are two rows — and taking one off has to leave the other, which is what a single row per
    /// person could not do.
    /// </summary>
    [Fact]
    public void Naming_somebody_under_both_roles_keeps_the_other_when_one_comes_off()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("un 1:1");
        var jo = human.Add("Jo");

        human.Name(meeting.Id, jo, MeetingPersonRole.Attended);
        human.Name(meeting.Id, jo, MeetingPersonRole.Subject);
        // Twice under one role is one row: the caller asked for them to be on it, and they are.
        human.Name(meeting.Id, jo, MeetingPersonRole.Subject);
        context.MeetingPeople.Count().ShouldBe(2);

        human.Unname(meeting.Id, jo, MeetingPersonRole.Attended);

        context.MeetingPeople.Single().Role.ShouldBe(MeetingPersonRole.Subject);
    }

    /// <summary>The same shape one level over: a meeting is work of two projects, and loses one.</summary>
    [Fact]
    public void Unlinking_one_node_leaves_the_others()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("dos proyectos");
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var coati = human.Under(techsed, NodeKind.Initiative, "Coati");
        var huemul = human.Under(techsed, NodeKind.Initiative, "Huemul");

        human.Link(meeting.Id, coati, MeetingNodeRole.WorkOf);
        human.Link(meeting.Id, huemul, MeetingNodeRole.WorkOf);
        human.Link(meeting.Id, huemul, MeetingNodeRole.WorkOf);
        context.MeetingNodes.Count().ShouldBe(2);

        human.Unlink(meeting.Id, coati, MeetingNodeRole.WorkOf);
        // And one that was never there, which is not a failure: it is not linked, as asked.
        human.Unlink(meeting.Id, coati, MeetingNodeRole.About);

        context.MeetingNodes.Single().NodeId.ShouldBe(huemul.Id);
    }

    /// <summary>
    /// Leaving closes the spell instead of deleting it, which is what makes the interview still
    /// readable as an interview after the candidate was hired.
    /// </summary>
    [Fact]
    public void Leaving_an_organization_closes_the_spell_and_keeps_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var northwind = human.Root(NodeKind.Organization, "Northwind");
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var vikram = human.Add("Vikram");

        var was = human.Join(vikram, northwind);
        human.Leave(was, HumanLayerFixture.Hired);
        human.Join(vikram, techsed, from: HumanLayerFixture.Hired);

        context.Affiliations.Count().ShouldBe(2);
        var spells = context.Affiliations.ToArray();
        spells.Single(spell => spell.Held(HumanLayerFixture.Interviewed)).OrganizationId.ShouldBe(northwind.Id);
        spells.Single(spell => spell.Held(HumanLayerFixture.Now)).OrganizationId.ShouldBe(techsed.Id);
    }

    /// <summary>
    /// A correction applies inside a node, inside one meeting, or everywhere. Both scopes at once is
    /// a caller that has not decided which, and it is refused before it reaches the CHECK that says
    /// the same thing — so the message names the two scopes rather than the constraint.
    /// </summary>
    [Fact]
    public void A_correction_is_scoped_to_a_node_or_a_meeting_or_to_neither()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var node = human.Root(NodeKind.Organization, "TechSed");

        human.Correct("quati", "Coati", under: node);
        human.Correct("huemul", "Huemul", meetingId: meeting.Id);
        human.Correct("deepgran", "Deepgram");

        Should.Throw<ArgumentException>(() => human.Correct("x", "y", under: node, meetingId: meeting.Id));

        context.TerminologyCorrections.Count().ShouldBe(3);
    }

    /// <summary>Editing one changes what it writes, and leaves what it matches on.</summary>
    [Fact]
    public void A_correction_is_edited_without_becoming_a_second_one()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var human = new HumanLayerFixture(context, corpus.Root).HumanLayer;

        var correction = human.Correct("quati", "Coaty");
        human.Recorrect(correction, "Coati", TerminologyMatchMode.IgnoreCase);

        var stored = context.TerminologyCorrections.Single();
        stored.Id.ShouldBe(correction.Id);
        stored.WrongText.ShouldBe("quati");
        stored.CorrectText.ShouldBe("Coati");
        stored.MatchMode.ShouldBe(TerminologyMatchMode.IgnoreCase);

        human.Drop(stored);
        context.TerminologyCorrections.ShouldBeEmpty();
    }

    /// <summary>
    /// Renaming somebody leaves them the same person, which is the whole reason a name is a column
    /// and not the key: every meeting that named them still does.
    /// </summary>
    [Fact]
    public void Renaming_somebody_leaves_them_on_the_meetings_that_named_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var person = human.Add("Ada L.");
        human.Name(meeting.Id, person, MeetingPersonRole.Attended);
        var before = person.UpdatedAt;

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        human.Rename(person, "Ada Lovelace");

        var stored = context.People.Single();
        stored.Id.ShouldBe(person.Id);
        stored.DisplayName.ShouldBe("Ada Lovelace");
        stored.CreatedAt.ShouldBe(before);
        stored.UpdatedAt.ShouldBeGreaterThan(before);
        context.MeetingPeople.Single().PersonId.ShouldBe(person.Id);
    }

    /// <summary>
    /// Forgetting somebody takes what hung off them and nothing else. The organization they were at
    /// stays: it is not theirs, and other people are at it.
    /// </summary>
    [Fact]
    public void Forgetting_somebody_takes_what_hung_off_them_and_leaves_the_rest()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var ada = human.Add("Ada");
        var jo = human.Add("Jo");

        human.Join(ada, techsed);
        human.Join(jo, techsed);
        human.Name(meeting.Id, ada, MeetingPersonRole.Attended);
        human.Name(meeting.Id, jo, MeetingPersonRole.Attended);
        human.Assign(meeting.Id, "ch1:speaker_0", ada);

        human.Remove(ada);

        context.People.Single().Id.ShouldBe(jo.Id);
        context.Affiliations.Single().PersonId.ShouldBe(jo.Id);
        context.MeetingPeople.Single().PersonId.ShouldBe(jo.Id);
        context.SpeakerAssignments.ShouldBeEmpty();
        context.Nodes.Count().ShouldBe(1);
        context.Meetings.Count().ShouldBe(1);
    }

    /// <summary>
    /// Dropping a node takes the tree under it and every link onto any of it, and leaves the people
    /// who were there. A meeting missing the people on it is not something anybody can repair.
    /// </summary>
    [Fact]
    public void Removing_a_node_takes_what_hangs_under_it_and_leaves_the_people()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var coati = human.Under(techsed, NodeKind.Initiative, "Coati");
        var ada = human.Add("Ada");

        human.Join(ada, techsed);
        human.Link(meeting.Id, coati, MeetingNodeRole.WorkOf);
        human.Correct("quati", "Coati", under: coati);

        human.Remove(techsed);

        context.Nodes.ShouldBeEmpty();
        context.MeetingNodes.ShouldBeEmpty();
        context.Affiliations.ShouldBeEmpty();
        context.TerminologyCorrections.ShouldBeEmpty();
        context.People.Count().ShouldBe(1);
        context.Meetings.Count().ShouldBe(1);
    }

    /// <summary>
    /// Where an action stands is written against the extraction and the position inside it, so
    /// marking the same one twice moves it rather than writing a second row.
    /// </summary>
    [Fact]
    public void Marking_an_action_again_moves_it_rather_than_writing_a_second_row()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var fixture = new HumanLayerFixture(context, corpus.Root);
        var human = fixture.HumanLayer;

        var meeting = fixture.Meeting("una reunion");
        var run = fixture.ExtractionRunId(meeting);
        var ada = human.Add("Ada");

        human.Mark(run, ordinal: 0, ActionItemState.Open, ada);
        human.Mark(run, ordinal: 0, ActionItemState.Done, ada);
        human.Mark(run, ordinal: 1, ActionItemState.Dropped);

        var stored = context.ActionItemProgress.OrderBy(progress => progress.Ordinal).ToArray();
        stored.Length.ShouldBe(2);
        stored[0].State.ShouldBe(ActionItemState.Done);
        stored[0].OwnerPersonId.ShouldBe(ada.Id);
        stored[1].State.ShouldBe(ActionItemState.Dropped);
        stored[1].OwnerPersonId.ShouldBeNull();
    }
}

/// <summary>
/// The rows a human layer hangs off — a meeting, and the extraction run an action's state is keyed
/// on. Neither is the human layer's to create, so neither goes through the service.
/// </summary>
internal sealed class HumanLayerFixture
{
    public static readonly UtcTimestamp Interviewed = On(2025, 3, 4);

    public static readonly UtcTimestamp Hired = On(2025, 4, 1);

    public static readonly UtcTimestamp Now = On(2026, 8, 7);

    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly CorpusDbContext _context;

    public HumanLayerFixture(CorpusDbContext context, DirectoryInfo root)
    {
        _context = context;
        Clock = new SteppedClock(Now.Value);
        HumanLayer = new HumanLayer(context, root, Clock);
    }

    public SteppedClock Clock { get; }

    public HumanLayer HumanLayer { get; }

    public Meeting Meeting(string title)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = title,
            StartedAt = Now,
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        _context.Meetings.Add(meeting);
        _context.SaveChanges();
        return meeting;
    }

    /// <summary>
    /// An accepted extraction of that meeting, and the job that ran it. Written as SQL because
    /// neither is what is under test and both carry required columns of their own.
    /// </summary>
    public Guid ExtractionRunId(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        var job = Text(Guid.NewGuid());
        var run = Guid.NewGuid();
        var owner = Text(meeting.Id);

        Sql.Execute(_context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{job}', '{owner}', 'extract', 'succeeded', 'extract/{owner}', '{Now}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{Text(run)}', '{owner}', '{job}', 'claude_code', '1', '1', '{Sha256}', '{Now}', '{Now}');
            """);

        return run;
    }

    /// <summary>
    /// A GUID as the corpus holds it. EF writes them upper case, so a SQL literal spelled the way
    /// <see cref="Guid.ToString()"/> spells it points at nothing — and a foreign key onto a row EF
    /// wrote fails for a reason that looks nothing like a case difference.
    /// </summary>
    private static string Text(Guid id) => id.ToString().ToUpperInvariant();

    private static UtcTimestamp On(int year, int month, int day) =>
        UtcTimestamp.From(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
}

/// <summary>
/// A clock that only moves when a test moves it. The real one would make two edits in the same
/// millisecond indistinguishable, which is exactly what an assertion about a timestamp being newer
/// needs to tell apart.
/// </summary>
internal sealed class SteppedClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
