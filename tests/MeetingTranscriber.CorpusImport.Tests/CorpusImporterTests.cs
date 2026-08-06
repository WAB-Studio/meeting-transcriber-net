using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.CorpusImport.Tests;

public class CorpusImporterTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public void A_meeting_arrives_with_what_the_response_and_the_human_layer_say()
    {
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var meeting = context.Meetings.Single();
        meeting.Title.ShouldBe("the one about the orchard");
        meeting.SourceProfile.ShouldBe(SourceProfile.Multichannel);
        meeting.Duration!.Value.Milliseconds.ShouldBe(1_800_500);
        meeting.Language.ShouldBe("es");
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.Context.ShouldBeNull();
        meeting.StartedAt.Value.LocalDateTime.ShouldBe(new DateTime(2026, 7, 29, 9, 35, 15));

        var link = context.MeetingNodes.Single();
        link.NodeId.ShouldBe(context.Nodes.Single(node => node.Name == "orchard").Id);
        link.Role.ShouldBe(MeetingNodeRole.WorkOf);
        context.Templates.Single(template => template.Id == meeting.TemplateId)
            .Name.ShouldBe("review");
    }

    [Fact]
    public void A_single_track_meeting_is_recorded_as_the_profile_it_is()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", channels: 1);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.Meetings.Single().SourceProfile.ShouldBe(SourceProfile.Diarize);
    }

    /// <summary>
    /// The property the whole tool hangs off. A corpus is imported over months, and an import
    /// that made a second copy of every meeting would be worse than one that never ran.
    /// </summary>
    [Fact]
    public void Importing_the_same_corpus_twice_imports_it_once()
    {
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithCorrections()
            .WithMeeting("2026-07-29 09-35-15")
            .WithMeeting("2026-08-03 08-13-17");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var importer = new CorpusImporter(context, Clock);

        var first = importer.Import(new LegacyCorpus(legacy.Directory));
        var second = importer.Import(new LegacyCorpus(legacy.Directory));

        first.MeetingsImported.ShouldBe(2);
        second.MeetingsImported.ShouldBe(0);
        second.MeetingsAlreadyThere.ShouldBe(2);

        context.Meetings.Count().ShouldBe(2);
        context.Artifacts.Count().ShouldBe(4);
        context.Nodes.Count().ShouldBe(2);
        context.MeetingNodes.Count().ShouldBe(2);
        context.People.Count().ShouldBe(2);
        context.SpeakerAssignments.Count().ShouldBe(2);
        context.MeetingParticipants.Count().ShouldBe(2);
        context.TerminologyCorrections.Count().ShouldBe(2);
    }

    /// <summary>
    /// What matching on the response rather than on the folder buys. A corpus edited by hand for
    /// months has folders that were renamed, and neither half of that is a second meeting.
    /// </summary>
    [Fact]
    public void A_meeting_whose_folder_was_renamed_is_still_the_same_meeting()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var importer = new CorpusImporter(context, Clock);

        importer.Import(new LegacyCorpus(legacy.Directory));
        Directory.Move(
            Path.Combine(legacy.Directory.FullName, "2026-07-29 09-35-15"),
            Path.Combine(legacy.Directory.FullName, "2026-07-29 09-35-16"));
        var second = importer.Import(new LegacyCorpus(legacy.Directory));

        second.MeetingsImported.ShouldBe(0);
        second.MeetingsAlreadyThere.ShouldBe(1);
        context.Meetings.Count().ShouldBe(1);
    }

    /// <summary>
    /// Two folders holding the same response are one meeting, deliberately. Byte-identical
    /// responses carry the same request id, so they are one call that was paid for once, however
    /// many places it was copied to.
    /// </summary>
    [Fact]
    public void The_same_response_in_two_folders_is_one_meeting()
    {
        using var legacy = new LegacyCorpusBuilder();
        legacy.WithMeeting("2026-07-29 09-35-15");
        File.Copy(
            Path.Combine(legacy.Directory.FullName, "2026-07-29 09-35-15", "deepgram.json"),
            Path.Combine(
                Directory.CreateDirectory(
                    Path.Combine(legacy.Directory.FullName, "2026-08-03 08-13-17")).FullName,
                "deepgram.json"));
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(1);
        report.MeetingsAlreadyThere.ShouldBe(1);
        context.Meetings.Count().ShouldBe(1);
    }

    /// <summary>
    /// Where a meeting came from is provenance, and provenance goes in the audit trail. It used
    /// to be a column of the meeting, which made the application carry the old system's
    /// identifier long after the tool that read it was deleted.
    /// </summary>
    [Fact]
    public void Where_a_meeting_came_from_is_written_down()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var audit = context.AuditEvents.Single();
        audit.Action.ShouldBe("imported");
        audit.MeetingId.ShouldBe(context.Meetings.Single().Id);
        audit.Detail.ShouldNotBeNull().ShouldContain("2026-07-29 09-35-15");
    }

    /// <summary>
    /// One way, and the corpus it reads is full of things that were paid for. Nothing in it is
    /// created, rewritten, moved or deleted, including by the option that copies.
    /// </summary>
    [Fact]
    public void The_corpus_it_reads_comes_out_exactly_as_it_went_in()
    {
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithCorrections()
            .WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var before = legacy.Fingerprint();
        var target = System.IO.Directory.CreateTempSubdirectory("imported-corpus-");

        try
        {
            new CorpusImporter(context, Clock)
                .Import(new LegacyCorpus(legacy.Directory), new ImportOptions(CopyTo: target));

            legacy.Fingerprint().ShouldBe(before);
        }
        finally
        {
            target.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_source_that_was_copied_is_the_same_file_at_the_other_end()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var target = System.IO.Directory.CreateTempSubdirectory("imported-corpus-");

        try
        {
            var report = new CorpusImporter(context, Clock)
                .Import(new LegacyCorpus(legacy.Directory), new ImportOptions(CopyTo: target));

            report.ArtifactsCopied.ShouldBe(2);
            foreach (var artifact in context.Artifacts)
            {
                var copied = new FileInfo(Path.Combine(target.FullName, artifact.RelativePath));
                copied.Exists.ShouldBeTrue(artifact.RelativePath);
                copied.Length.ShouldBe(artifact.ByteSize);
            }
        }
        finally
        {
            target.Delete(recursive: true);
        }
    }

    [Fact]
    public void Without_the_copy_option_an_artifact_points_at_where_it_already_is()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.ArtifactsCopied.ShouldBe(0);
        context.Artifacts.Select(artifact => artifact.RelativePath).ShouldBe(
            ["2026-07-29 09-35-15/deepgram.json", "2026-07-29 09-35-15/extraction.json"],
            ignoreOrder: true);
    }

    [Fact]
    public void Only_the_sources_are_registered()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.Artifacts.ShouldAllBe(artifact => artifact.Origin == ArtifactOrigin.Source);
        context.Artifacts.Select(artifact => artifact.Kind).ShouldBe(
            [ArtifactKind.DeepgramResponse, ArtifactKind.Extraction],
            ignoreOrder: true);
        report.NotImported.ShouldContain(line => line.Contains("transcript.md", StringComparison.Ordinal));
    }

    [Fact]
    public void An_artifact_carries_the_hash_of_the_file_it_names()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var fingerprint = legacy.Fingerprint();
        foreach (var artifact in context.Artifacts)
        {
            artifact.Sha256.ShouldBe(fingerprint[artifact.RelativePath]);
        }
    }

    /// <summary>
    /// A resolved label is somebody having listened, and nothing regenerates it. It is stored
    /// under the number the provider used, not the one the old renderer displayed.
    /// </summary>
    [Fact]
    public void A_speaker_somebody_resolved_arrives_under_the_label_the_provider_wrote()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var assignment = context.SpeakerAssignments.Single();
        assignment.SpeakerLabel.ShouldBe("speaker_0");
        assignment.AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
        assignment.PersonId.ShouldBe(context.People.Single(person => person.DisplayName == "Renée").Id);

        // Speaker 2 was never resolved, so nothing is invented for it.
        context.SpeakerAssignments.Count().ShouldBe(1);
        context.MeetingParticipants.Single().PersonId.ShouldBe(assignment.PersonId);
    }

    /// <summary>
    /// Channel 1 is deliberately not assigned. The old system labelled it from configuration
    /// rather than from a decision about the meeting, and a microphone can carry a room.
    /// </summary>
    [Fact]
    public void The_microphone_channel_is_not_given_to_anybody()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.SpeakerAssignments.ShouldAllBe(assignment => assignment.SpeakerLabel != "channel_1");
        context.People.ShouldAllBe(person => !person.IsMe);
    }

    [Fact]
    public void A_correction_arrives_once_per_spelling_and_only_while_it_is_switched_on()
    {
        using var legacy = new LegacyCorpusBuilder().WithCorrections();
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.TerminologyCorrections.Select(correction => correction.WrongText).ShouldBe(
            ["Orchard Company", "orchard co."],
            ignoreOrder: true);
        context.TerminologyCorrections.ShouldAllBe(correction => correction.CorrectText == "Orchard Co");
        context.TerminologyCorrections.ShouldAllBe(
            correction => correction.MatchMode == TerminologyMatchMode.IgnoreCase);
        // Global: the legacy file is one list for the whole corpus.
        context.TerminologyCorrections.ShouldAllBe(
            correction => correction.NodeId == null && correction.MeetingId == null);
    }

    [Fact]
    public void The_catalog_arrives_as_a_tree()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var acme = context.Nodes.Single(node => node.Kind == NodeKind.Organization);
        acme.Name.ShouldBe("Acme");
        acme.ParentId.ShouldBeNull();
        acme.Depth.ShouldBe(0);

        var orchard = context.Nodes.Single(node => node.Kind == NodeKind.Initiative);
        orchard.Name.ShouldBe("orchard");
        orchard.ParentId.ShouldBe(acme.Id);
        orchard.Depth.ShouldBe(1);

        context.People.Single(person => person.DisplayName == "Renée").OrganizationId.ShouldBe(acme.Id);
        // Sam has no company in the catalog, and gets none here.
        context.People.Single(person => person.DisplayName == "Sam").OrganizationId.ShouldBeNull();
    }

    [Fact]
    public void A_meeting_with_no_meta_still_arrives()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", meta: null);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var meeting = context.Meetings.Single();
        meeting.Title.ShouldBeNull();
        context.MeetingNodes.ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_whose_name_is_not_a_date_is_named_in_the_report_and_left_alone()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("not-a-recording");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.Meetings.ShouldBeEmpty();
        report.NotImported.ShouldContain(line => line.Contains("recording date", StringComparison.Ordinal));
    }

    /// <summary>
    /// The note somebody wrote so a reader who was not there understands the meeting. Nothing
    /// infers it, so losing it on the way in would lose it for good.
    /// </summary>
    [Fact]
    public void A_meeting_keeps_the_note_a_person_wrote_on_it()
    {
        const string meta = """
            title: "with a note"
            context: "talks about the issues, which gh lists"
            meeting_type: "refinement"
            """;
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", meta: meta);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var meeting = context.Meetings.Single();
        meeting.Title.ShouldBe("with a note");
        meeting.Context.ShouldBe("talks about the issues, which gh lists");
        context.Templates.Single(template => template.Id == meeting.TemplateId)
            .Name.ShouldBe("refinement");
        report.NotImported.ShouldNotContain(line => line.Contains("context", StringComparison.Ordinal));
    }

    /// <summary>
    /// The meeting held before any project existed — an interview, a first call with a client.
    /// It hangs off the organization itself, which is what a meeting could not do when it could
    /// only belong to a project.
    /// </summary>
    [Fact]
    public void A_meeting_with_an_organization_and_no_project_hangs_off_the_organization()
    {
        const string meta = """
            company: "acme"
            title: "no project"
            """;
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15", meta: meta);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var link = context.MeetingNodes.Single();
        link.NodeId.ShouldBe(context.Nodes.Single(node => node.Name == "Acme").Id);
        link.Role.ShouldBe(MeetingNodeRole.WorkOf);
        report.NotImported.ShouldNotContain(line => line.Contains("catalog", StringComparison.Ordinal));
    }

    [Fact]
    public void A_meeting_naming_something_the_catalog_does_not_have_says_so()
    {
        const string meta = """
            project: "ghost"
            title: "nowhere"
            """;
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15", meta: meta);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.MeetingNodes.ShouldBeEmpty();
        report.NotImported.ShouldContain(line => line.Contains("not in the catalog", StringComparison.Ordinal));
    }

    [Fact]
    public void A_meeting_with_no_rendered_transcript_records_the_language_it_was_told()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", transcript: false);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock)
            .Import(new LegacyCorpus(legacy.Directory), new ImportOptions(Language: "en"));

        context.Meetings.Single().Language.ShouldBe("en");
        report.NotImported.ShouldContain(line => line.Contains("language", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_corpus_imports_nothing_and_says_nothing_went_wrong()
    {
        using var legacy = new LegacyCorpusBuilder();
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(0);
        report.NotImported.ShouldBeEmpty();
        context.Meetings.ShouldBeEmpty();
    }
}
