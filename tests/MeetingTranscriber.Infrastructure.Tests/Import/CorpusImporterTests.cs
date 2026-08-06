using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Import;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Import;

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
        meeting.LegacyId.ShouldBe("2026-07-29 09-35-15");
        meeting.Title.ShouldBe("the one about the orchard");
        meeting.SourceProfile.ShouldBe(SourceProfile.Multichannel);
        meeting.Duration!.Value.Milliseconds.ShouldBe(1_800_500);
        meeting.Language.ShouldBe("es");
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.ProjectId.ShouldBe(context.Projects.Single(project => project.Name == "orchard").Id);
        meeting.StartedAt.Value.LocalDateTime.ShouldBe(new DateTime(2026, 7, 29, 9, 35, 15));
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
        context.Projects.Count().ShouldBe(1);
        context.Companies.Count().ShouldBe(1);
        context.People.Count().ShouldBe(2);
        context.SpeakerAssignments.Count().ShouldBe(2);
        context.MeetingParticipants.Count().ShouldBe(2);
        context.TerminologyCorrections.Count().ShouldBe(2);
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
            correction => correction.ProjectId == null && correction.MeetingId == null);
    }

    [Fact]
    public void A_project_keeps_the_company_it_belongs_to()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var acme = context.Companies.Single();
        acme.Name.ShouldBe("Acme");
        context.Projects.Single().CompanyId.ShouldBe(acme.Id);
        context.People.Single(person => person.DisplayName == "Renée").CompanyId.ShouldBe(acme.Id);
        // Sam has no company in the catalog, and gets none here.
        context.People.Single(person => person.DisplayName == "Sam").CompanyId.ShouldBeNull();
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
        meeting.ProjectId.ShouldBeNull();
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

    [Fact]
    public void A_meeting_whose_context_note_has_nowhere_to_go_says_so()
    {
        const string meta = """
            title: "with a note"
            context: "the note nobody has a column for"
            """;
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", meta: meta);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.Meetings.Single().Title.ShouldBe("with a note");
        report.NotImported.ShouldContain(line => line.Contains("context note", StringComparison.Ordinal));
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
