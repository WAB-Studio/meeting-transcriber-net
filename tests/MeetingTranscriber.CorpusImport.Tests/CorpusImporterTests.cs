using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

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

        // Five each: the two sources copied in, the card, and the two files rendered here.
        context.Artifacts.Count().ShouldBe(10);
        context.Nodes.Count().ShouldBe(2);
        context.MeetingNodes.Count().ShouldBe(2);
        context.People.Count().ShouldBe(2);
        context.SpeakerAssignments.Count().ShouldBe(2);
        context.MeetingPeople.Count().ShouldBe(2);
        context.Affiliations.Count().ShouldBe(1);
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

        // The folder was written and read back a moment ago, so this rename carries the hazard
        // #81 measured and Folders explains. It has never been seen refused here — this is the
        // same shape as the rename that was, taken through the same helper rather than left to
        // be found the hard way.
        Folders.MoveWaitingOutWhoeverHasIt(
            new DirectoryInfo(Path.Combine(legacy.Directory.FullName, "2026-07-29 09-35-15")),
            new DirectoryInfo(Path.Combine(legacy.Directory.FullName, "2026-07-29 09-35-16")));
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

        var audit = context.AuditEvents.AsEnumerable()
            .Single(entry => entry.Detail!.Contains("folder", StringComparison.Ordinal));
        audit.Action.ShouldBe("imported");
        audit.MeetingId.ShouldBe(context.Meetings.Single().Id);
        audit.Detail.ShouldNotBeNull().ShouldContain("2026-07-29 09-35-15");
    }

    /// <summary>
    /// Everything the old corpus knew about the run that produced its summary, in the row a
    /// projected decision or action hangs off. Without it the extraction is a file nothing points
    /// at, and a claim read out of it would have no run to belong to.
    /// </summary>
    [Fact]
    public void An_imported_extraction_arrives_with_the_run_it_came_out_of()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.ExtractionRunsImported.ShouldBe(1);
        var run = context.ExtractionRuns.Single();
        var meeting = context.Meetings.Single();
        var output = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.Extraction);
        var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);

        run.MeetingId.ShouldBe(meeting.Id);
        run.Provider.ShouldBe("claude_code");
        run.Model.ShouldBe("claude-opus-5[1m]");
        run.PromptVersion.ShouldBe("31d0d27-dirty");
        // The Python system had one shape and never versioned it; this names that shape.
        run.SchemaVersion.ShouldBe("legacy");

        // What it produced, and what it ultimately came out of. The transcript the model was
        // actually given is this application's to render, so the response is as far back as the
        // corpus can still name.
        run.RawOutputHash.ShouldBe(output.Sha256);
        run.OutputArtifactId.ShouldBe(output.Id);
        run.InputHash.ShouldBe(response.Sha256);

        // On the timeline where it ran, not where it was imported, and accepted then: the old
        // system had no acceptance step and rendered from the extraction it wrote.
        run.CreatedAt.Value.LocalDateTime.ShouldBe(new DateTime(2026, 7, 29, 10, 58, 55));
        run.AcceptedAt.ShouldBe(run.CreatedAt);

        // It ran once and it landed, which is what having a summary means.
        var job = context.ProcessingJobs.Single(entry => entry.Id == run.JobId);
        job.Kind.ShouldBe(JobKind.Extract);
        job.State.ShouldBe(JobState.Succeeded);
        job.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// The half of this the run exists for. A decision and an action read out of that extraction
    /// hang off it, and so does the state a person later gives the action, which is keyed on the
    /// run rather than on an action's id.
    /// </summary>
    [Fact]
    public void A_decision_and_an_action_projected_from_it_hang_off_that_run()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var run = context.ExtractionRuns.Single();
        var meeting = context.Meetings.Single();
        var when = run.CreatedAt;

        // The turn cited below is one the import rendered. It used to be written here, from a
        // time when an import produced no turns of its own.
        context.Utterances.Any(turn => turn.MeetingId == meeting.Id && turn.Ordinal == 0).ShouldBeTrue();

        context.Decisions.Add(new Decision
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting.Id,
            ExtractionRunId = run.Id,
            Statement = "el presupuesto sube",
            Evidence = Cited(meeting.Id),
            CreatedAt = when,
        });
        context.ActionItems.Add(new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting.Id,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Statement = "mandar el presupuesto",
            Evidence = Cited(meeting.Id),
            CreatedAt = when,
        });
        context.ActionItemProgress.Add(new ActionItemProgress
        {
            ExtractionRunId = run.Id,
            Ordinal = 0,
            State = ActionItemState.Done,
            UpdatedAt = when,
        });
        context.SaveChanges();

        context.Decisions.Single().ExtractionRunId.ShouldBe(run.Id);
        context.ActionItems.Single().ExtractionRunId.ShouldBe(run.Id);
        context.ActionItemProgress.Single().ExtractionRunId.ShouldBe(run.Id);
    }

    /// <summary>
    /// Repeatable like the rest of the tool, and matched on what the run produced rather than on
    /// anything this corpus minted.
    /// </summary>
    [Fact]
    public void Importing_the_same_extraction_twice_leaves_one_run()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var importer = new CorpusImporter(context, Clock);

        importer.Import(new LegacyCorpus(legacy.Directory));
        var second = importer.Import(new LegacyCorpus(legacy.Directory));

        second.ExtractionRunsImported.ShouldBe(0);
        context.ExtractionRuns.Count().ShouldBe(1);
        context.ProcessingJobs.Count().ShouldBe(1);
    }

    /// <summary>
    /// A folder nobody ever summarised gets no run, rather than an empty one standing for work
    /// that never happened.
    /// </summary>
    [Fact]
    public void A_meeting_with_no_extraction_gets_no_run()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", extraction: null);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.ExtractionRunsImported.ShouldBe(0);
        context.ExtractionRuns.ShouldBeEmpty();
        context.ProcessingJobs.ShouldBeEmpty();
        context.Meetings.Count().ShouldBe(1);
    }

    /// <summary>
    /// The prompt version is the one thing here a real file might not carry. What goes in then is
    /// said out loud rather than looking like a version somebody chose.
    /// </summary>
    [Fact]
    public void An_extraction_that_names_no_prompt_version_says_so_and_still_arrives()
    {
        const string thin = """{"model": "claude-opus-5[1m]", "response": {"abstract": "x"}}""";
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15", extraction: thin);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        var run = context.ExtractionRuns.Single();
        run.PromptVersion.ShouldBe("unknown");
        report.Assumed.ShouldContain(line => line.Contains("prompt version", StringComparison.Ordinal));
        report.Assumed.ShouldContain(line => line.Contains("run at import time", StringComparison.Ordinal));
    }

    /// <summary>
    /// A corpus edited by hand for months has a file somebody truncated. It is named, and the
    /// meeting it belongs to still arrives with everything else the folder holds.
    /// </summary>
    [Fact]
    public void An_extraction_that_cannot_be_read_is_named_and_the_meeting_still_arrives()
    {
        using var legacy = new LegacyCorpusBuilder()
            .WithMeeting("2026-07-29 09-35-15", extraction: """{"model": "claude""");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.Meetings.Count().ShouldBe(1);
        context.ExtractionRuns.ShouldBeEmpty();
        report.NotImported.ShouldContain(line => line.Contains("extraction.json", StringComparison.Ordinal));

        // The file itself is still registered: it was paid for in credits and it is still the only
        // copy of that summary, whatever this tool could make of its contents.
        context.Artifacts.ShouldContain(artifact => artifact.Kind == ArtifactKind.Extraction);
    }

    /// <summary>
    /// The Claude Code session the summary came out of. No column holds it — it is the provider's
    /// own handle — and it is the only thread back to the conversation that produced the summary,
    /// so it is written where provenance goes rather than dropped.
    /// </summary>
    [Fact]
    public void The_session_an_extraction_came_out_of_is_written_down()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        context.AuditEvents.ShouldContain(entry =>
            entry.Detail!.Contains("02132d9f-69ba-47c3-ab5b-ca6b4c739408", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of not importing the Python system's rendered files: the meeting gets those
    /// files anyway, produced here from the response that was just imported. Without them a legacy
    /// meeting arrives with nothing to search and no turn a claim could resolve against.
    /// </summary>
    [Fact]
    public void An_imported_meeting_arrives_with_its_derivatives_generated_here()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock)
            .Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsRendered.ShouldBe(1);
        report.TurnsProjected.ShouldBeGreaterThan(0);

        var meeting = context.Meetings.Single().Id;
        context.Utterances.Count(turn => turn.MeetingId == meeting).ShouldBe(report.TurnsProjected);

        foreach (var kind in new[] { ArtifactKind.Transcript, ArtifactKind.Utterances })
        {
            var artifact = context.Artifacts.Single(row => row.Kind == kind);
            artifact.Origin.ShouldBe(ArtifactOrigin.Derived);
            CorpusFiles.Locate(corpus.Root, artifact.RelativePath).Exists.ShouldBeTrue(artifact.RelativePath);
        }

        // Rendered here means rendered by this application: a speaker the old corpus resolved is a
        // name in the transcript, and a correction it carried has been applied.
        var transcript = File.ReadAllText(CorpusFiles.Locate(
            corpus.Root,
            context.Artifacts.Single(row => row.Kind == ArtifactKind.Transcript).RelativePath).FullName);
        transcript.ShouldContain("## Renée");
    }

    /// <summary>
    /// A response the .NET parser rejects: the import names the meeting and finishes, rather than
    /// ending on an exception with the report that says what the run left behind never printed.
    /// </summary>
    /// <remarks>
    /// Ordinary input here rather than an exotic case. A <c>deepgram.json</c> is copied and hashed,
    /// and only its metadata is read on the way in — which is where the profile comes from — so the
    /// first thing that ever puts one through the parser is the render at the end, by which point
    /// the meeting and its sources are already in the corpus.
    /// </remarks>
    [Fact]
    public void A_response_the_parser_cannot_read_is_named_and_the_rest_still_imports()
    {
        const string Broken = "2026-07-29 09-35-15";
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithMeeting(Broken, response: ClaimsTwoChannelsAndCarriesOne(Broken))
            .WithMeeting("2026-08-03 08-13-17");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(2);
        report.MeetingsRendered.ShouldBe(1);
        report.NotImported.ShouldHaveSingleItem()
            .ShouldContain("it claims 2 channel(s) and carries 1", Case.Sensitive);
        report.NotImported.ShouldHaveSingleItem().ShouldContain(Broken, Case.Sensitive);
    }

    /// <summary>
    /// The rule itself, as against the probe above, which only pins one refusal the parser happens
    /// to raise.
    /// </summary>
    /// <remarks>
    /// A speaker numbered below zero. The parser range checks the channel of an utterance and not
    /// its speaker, so this reaches <c>SpeakerLabels.For</c> and comes back as the domain's speaker
    /// contract refusing — an <see cref="ArgumentOutOfRangeException"/>, which reads like somebody's
    /// bug rather than a refusal and which no list of what a render may throw would have carried.
    /// Narrow the catch to a longer list of render exceptions and the probe above stays green while
    /// this one goes red, which is the whole reason it is here.
    /// </remarks>
    [Fact]
    public void A_refusal_no_list_would_have_carried_is_named_and_the_rest_still_imports()
    {
        const string Impossible = "2026-07-29 09-35-15";
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithMeeting(Impossible, response: WithSpeakerBelowZero(Impossible))
            .WithMeeting("2026-08-03 08-13-17");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(2);
        report.MeetingsRendered.ShouldBe(1);
        report.NotImported.ShouldHaveSingleItem()
            .ShouldContain("A provider numbers speakers from zero", Case.Sensitive);
        report.NotImported.ShouldHaveSingleItem().ShouldContain(Impossible, Case.Sensitive);
    }

    /// <summary>
    /// A meeting whose files the folder refuses keeps no turns of a render that did not happen, and
    /// the import still finishes.
    /// </summary>
    /// <remarks>
    /// The refusal that lands after the turns have been written rather than before them, which is
    /// what makes the render its own transaction here. A directory standing where
    /// <c>transcript.md</c> goes is what a folder half synced from elsewhere comes to, and without
    /// the transaction this meeting would be left holding turns nothing counted, under a line in
    /// the report saying it could not be finished.
    /// </remarks>
    [Fact]
    public void A_meeting_whose_files_the_folder_refuses_keeps_no_half_of_a_render()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var importer = new CorpusImporter(context, Clock);

        // Imported once to learn which meeting the response became, then put back into the state a
        // meeting has before it has ever been rendered: the turns thrown away, and a directory
        // standing where the transcript goes. The second run matches the same response by its
        // sha256 and renders it again, which is the render this is about.
        importer.Import(new LegacyCorpus(legacy.Directory));
        var meeting = context.Meetings.Single().Id;
        context.Utterances.Where(turn => turn.MeetingId == meeting).ExecuteDelete();
        var transcript = CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "transcript.md"));
        transcript.Delete();
        Directory.CreateDirectory(transcript.FullName);

        var report = importer.Import(new LegacyCorpus(legacy.Directory));

        report.NotImported.ShouldHaveSingleItem().ShouldContain("2026-07-29 09-35-15", Case.Sensitive);

        // The turns went back with the files. Without the transaction they would be sitting there,
        // projected by a render the report has just said it could not finish and counted by
        // nothing — the corpus holding turns the run does not know it wrote.
        report.TurnsProjected.ShouldBe(0);
        context.Utterances.Count(turn => turn.MeetingId == meeting).ShouldBe(0);
    }

    /// <summary>
    /// A response whose metadata says two channels and whose body carries one. The metadata is what
    /// the importer reads on the way in, so the meeting is filed as multichannel and the two halves
    /// disagreeing only surfaces when the parser is finally asked to read the whole thing.
    /// </summary>
    private static string ClaimsTwoChannelsAndCarriesOne(string id) =>
        LegacyCorpusBuilder.Response(id, channels: 1)
            .Replace(@"""channels"":1", @"""channels"":2", StringComparison.Ordinal);

    /// <summary>A response whose speakers are numbered below zero, and unaltered otherwise.</summary>
    private static string WithSpeakerBelowZero(string id) =>
        LegacyCorpusBuilder.Response(id)
            .Replace(@"""speaker"":0", @"""speaker"":-1", StringComparison.Ordinal);

    /// <summary>
    /// Repeatable, like everything else here. A second run finds the same meeting and re-renders to
    /// the same bytes rather than to a second file or a different one.
    /// </summary>
    [Fact]
    public void Importing_again_does_not_duplicate_or_rewrite_the_derivatives()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var importer = new CorpusImporter(context, Clock);

        importer.Import(new LegacyCorpus(legacy.Directory));
        var before = context.Artifacts
            .Where(artifact => artifact.Origin == ArtifactOrigin.Derived)
            .OrderBy(artifact => artifact.RelativePath)
            .Select(artifact => artifact.RelativePath + "|" + artifact.Sha256)
            .ToArray();
        var turns = context.Utterances.Count();

        importer.Import(new LegacyCorpus(legacy.Directory));

        context.Artifacts
            .Where(artifact => artifact.Origin == ArtifactOrigin.Derived)
            .OrderBy(artifact => artifact.RelativePath)
            .Select(artifact => artifact.RelativePath + "|" + artifact.Sha256)
            .ToArray()
            .ShouldBe(before);
        context.Utterances.Count().ShouldBe(turns);
    }

    /// <summary>
    /// An imported meeting is recognisable in its folder without the database, the same as one
    /// filed from the command line and from the same writer. The old corpus had no card of any
    /// kind, so this one is written here rather than carried across.
    /// </summary>
    [Fact]
    public void An_imported_meeting_arrives_with_the_card_that_names_it()
    {
        using var legacy = new LegacyCorpusBuilder().WithCatalog().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock)
            .Import(new LegacyCorpus(legacy.Directory));

        report.ManifestsWritten.ShouldBe(1);

        var meeting = context.Meetings.Single();
        var manifest = context.Artifacts.Single(row => row.Kind == ArtifactKind.Manifest);
        manifest.Origin.ShouldBe(ArtifactOrigin.Source);

        var card = MeetingManifest.Read(CorpusFiles.Locate(corpus.Root, manifest.RelativePath));
        card.MeetingId.ShouldBe(meeting.Id);
        card.StartedAt.ShouldBe(meeting.StartedAt);
        card.Profile.ShouldBe(meeting.SourceProfile);
        card.Language.ShouldBe(meeting.Language);
        card.Title.ShouldBe(meeting.Title);
    }

    private static Citation Cited(Guid meetingId) => new()
    {
        MeetingId = meetingId,
        UtteranceOrdinal = 0,
        Start = Duration.Zero,
        End = Duration.FromMilliseconds(1000),
        SpeakerLabel = "ch0:speaker_0",
        QuotedText = "sube el presupuesto",
        SourceArtifactSha256 = new string('0', 64),
    };

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

        new CorpusImporter(context, Clock)
            .Import(new LegacyCorpus(legacy.Directory));

        legacy.Fingerprint().ShouldBe(before);
    }

    [Fact]
    public void A_source_that_was_copied_is_the_same_file_at_the_other_end()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock)
            .Import(new LegacyCorpus(legacy.Directory));

        report.ArtifactsCopied.ShouldBe(2);
        foreach (var artifact in context.Artifacts)
        {
            // Under the corpus the rows were written into, which is the only folder there is: the
            // copy used to name its own, and a run could put the files anywhere the rows were not.
            var copied = CorpusFiles.Locate(corpus.Root, artifact.RelativePath);
            copied.Exists.ShouldBeTrue(artifact.RelativePath);
            copied.Length.ShouldBe(artifact.ByteSize);
        }
    }

    /// <summary>
    /// Every source lands under the meeting's own folder in the corpus the rows are in. There used
    /// to be an option that registered them where the Python corpus already had them, and what it
    /// produced was rows naming files that are not where the corpus says they are.
    /// </summary>
    [Fact]
    public void An_imported_source_is_stored_where_this_corpus_keeps_that_meetings_files()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.ArtifactsCopied.ShouldBe(2);
        var meeting = context.Meetings.Single().Id;
        context.Artifacts
            .Where(artifact => artifact.Origin == ArtifactOrigin.Source)
            .Select(artifact => artifact.RelativePath)
            .ShouldBe(
                [
                    CorpusFiles.PathFor(meeting, "deepgram.json"),
                    CorpusFiles.PathFor(meeting, "extraction.json"),
                    CorpusFiles.PathFor(meeting, MeetingManifest.FileName),
                ],
                ignoreOrder: true);
    }

    /// <summary>
    /// Only the sources come across. The corpus does hold derived files afterwards, and they are
    /// this application's renders rather than the Python system's — registering those would claim
    /// a rebuild reproduces bytes nothing here wrote.
    /// </summary>
    [Fact]
    public void Only_the_sources_are_imported_and_the_rendered_files_are_produced_here()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.SkippedByDesign["transcript.md"].ShouldBe(1);
        report.SkippedByDesign["utterances.jsonl"].ShouldBe(1);
        report.ArtifactsRegistered.ShouldBe(2);

        context.Artifacts.Select(artifact => artifact.Kind).ShouldBe(
            [
                ArtifactKind.DeepgramResponse,
                ArtifactKind.Extraction,
                ArtifactKind.Manifest,
                ArtifactKind.Transcript,
                ArtifactKind.Utterances,
            ],
            ignoreOrder: true);

        // Rendered here: the bytes are not the ones the Python corpus holds under the same names.
        var legacyFiles = legacy.Fingerprint()
            .ToDictionary(file => Path.GetFileName(file.Key), file => file.Value, StringComparer.Ordinal);
        foreach (var derived in context.Artifacts.Where(row => row.Origin == ArtifactOrigin.Derived))
        {
            derived.Sha256.ShouldNotBe(legacyFiles[Path.GetFileName(derived.RelativePath)]);
        }
    }

    /// <summary>
    /// The three lists are three because they are three different things, and one of them is the
    /// reason the report exists. Run over a real corpus the files rendered here are three lines
    /// per meeting; what had nowhere to go is a handful in total, and used to sit underneath them.
    /// </summary>
    [Fact]
    public void What_is_left_behind_on_purpose_is_not_mixed_with_what_had_nowhere_to_go()
    {
        const string nowhere = """
            project: "ghost"
            title: "nowhere"
            """;
        using var legacy = new LegacyCorpusBuilder()
            .WithCatalog()
            .WithMeeting("2026-07-29 09-35-15")
            .WithMeeting("2026-08-03 08-13-17", meta: nowhere, transcript: false);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        // Rendered here, so never news: counted by name, and named nowhere else.
        report.SkippedByDesign.Values.Sum().ShouldBe(2);

        // Came in, with something the corpus did not say and the import chose.
        report.Assumed.ShouldHaveSingleItem().ShouldContain("language", Case.Sensitive);

        // Did not come in. One line, and it is the only thing in the list.
        report.NotImported.ShouldHaveSingleItem().ShouldContain("not in the catalog", Case.Sensitive);
    }

    /// <summary>
    /// One transaction for the whole import meant a single row the corpus refused cost every
    /// meeting behind it — and the report with them, which is printed at the end and is the only
    /// record of what a run left behind.
    /// </summary>
    [Fact]
    public void A_meeting_the_corpus_refuses_does_not_take_the_others_with_it()
    {
        const string refused = """
            title: "the one this corpus will not have"
            """;
        using var legacy = new LegacyCorpusBuilder()
            .WithMeeting("2026-07-29 09-35-15", meta: null)
            .WithMeeting("2026-08-03 08-13-17", meta: refused)
            .WithMeeting("2026-08-04 13-23-25", meta: null);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        // One row this corpus refuses, standing in for whatever a corpus edited by hand for months
        // turns out to hold. A trigger rather than a stub in the importer: what is under test is
        // that the write is per meeting, and a fake that never reaches SQLite would not show it.
        context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER refuse_one BEFORE INSERT ON meetings
            WHEN NEW.title = 'the one this corpus will not have'
            BEGIN SELECT RAISE(ABORT, 'this corpus refuses that meeting'); END;
            """);

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(2);
        context.Meetings.Count().ShouldBe(2);
        report.NotImported.ShouldHaveSingleItem().ShouldContain("2026-08-03 08-13-17", Case.Sensitive);
    }

    /// <summary>
    /// A response the domain has no profile for used to escape the read and take the whole run
    /// with it, before a single meeting had been written. A corpus edited by hand for months is
    /// the one place a file like that lives.
    /// </summary>
    [Fact]
    public void A_response_that_matches_no_profile_is_named_and_the_rest_still_arrive()
    {
        using var legacy = new LegacyCorpusBuilder()
            .WithMeeting("2026-07-29 09-35-15", meta: null)
            .WithMeeting("2026-08-03 08-13-17", channels: 3, meta: null);
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var report = new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        report.MeetingsImported.ShouldBe(1);
        report.NotImported.ShouldHaveSingleItem().ShouldContain("2026-08-03 08-13-17", Case.Sensitive);
    }

    [Fact]
    public void An_artifact_carries_the_hash_of_the_file_it_names()
    {
        using var legacy = new LegacyCorpusBuilder().WithMeeting("2026-07-29 09-35-15");
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusImporter(context, Clock).Import(new LegacyCorpus(legacy.Directory));

        // Keyed by file name: the legacy corpus knows its own paths and the corpus stores its own,
        // and what has to agree is the bytes under each name.
        var fingerprint = legacy.Fingerprint()
            .ToDictionary(file => Path.GetFileName(file.Key), file => file.Value, StringComparer.Ordinal);

        foreach (var artifact in context.Artifacts
            .Where(row => row.Kind == ArtifactKind.DeepgramResponse || row.Kind == ArtifactKind.Extraction))
        {
            var name = Path.GetFileName(artifact.RelativePath);
            artifact.Sha256.ShouldBe(fingerprint[name], name);
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
        // The channel is in the label because the label is what a rendered turn carries: a bare
        // speaker_0 on a two channel meeting names nobody and fails at nothing.
        assignment.SpeakerLabel.ShouldBe("ch0:speaker_0");
        assignment.AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
        assignment.PersonId.ShouldBe(context.People.Single(person => person.DisplayName == "Renée").Id);

        // Speaker 2 was never resolved, so nothing is invented for it.
        context.SpeakerAssignments.Count().ShouldBe(1);

        // Attended, and only that: what a meeting was about is not in the legacy corpus.
        var named = context.MeetingPeople.Single();
        named.PersonId.ShouldBe(assignment.PersonId);
        named.Role.ShouldBe(MeetingPersonRole.Attended);
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

        context.SpeakerAssignments.ShouldAllBe(assignment => !assignment.SpeakerLabel.StartsWith("ch1:"));
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

        var renée = context.People.Single(person => person.DisplayName == "Renée");
        var affiliation = context.Affiliations.Single(entry => entry.PersonId == renée.Id);
        affiliation.OrganizationId.ShouldBe(acme.Id);
        // Open at both ends: the catalog carries no dates, and inventing one is not this tool's.
        affiliation.StartedAt.ShouldBeNull();
        affiliation.EndedAt.ShouldBeNull();

        // Sam has no company in the catalog, and gets no affiliation rather than an empty one.
        var sam = context.People.Single(person => person.DisplayName == "Sam");
        context.Affiliations.ShouldNotContain(entry => entry.PersonId == sam.Id);
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
        report.Assumed.ShouldContain(line => line.Contains("language", StringComparison.Ordinal));
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
