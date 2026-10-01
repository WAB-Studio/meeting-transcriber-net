using System.Text.Json;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Export;
using MeetingTranscriber.Processing.Rendering;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Tests.Export;

/// <summary>
/// What an export takes out of a corpus, and what it leaves. Every fact walks the folder the export
/// made and reads its files as a person with nothing installed would.
/// </summary>
public class CorpusExportTests
{
    private static readonly UtcTimestamp Then =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 30, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp Now =
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 30, 10, 0, 5, TimeSpan.Zero));

    private static readonly HashSet<ExportKind> Everything =
        Ticks(ExportKind.Audio, ExportKind.Transcripts, ExportKind.Summaries, ExportKind.HandCorrections);

    private static HashSet<ExportKind> Ticks(params ExportKind[] kinds) => [.. kinds];

    [Fact]
    public void An_export_of_only_the_transcripts_carries_not_one_byte_of_audio()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno");
        var audio = File.ReadAllBytes(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "audio.wav")).FullName);

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Transcripts), TimeZoneInfo.Utc, Now);

        var files = exported.Folder.GetFiles("*", SearchOption.AllDirectories);
        files.ShouldNotContain(file => file.Name == "audio.wav");
        files.ShouldNotContain(file => File.ReadAllBytes(file.FullName).SequenceEqual(audio));
        files.ShouldContain(file => file.Name == "deepgram.json");
        files.ShouldContain(file => file.Name == "transcript.md");
        files.ShouldNotContain(file => file.FullName.Contains("extractions", StringComparison.Ordinal));
        files.ShouldNotContain(file => file.Name == CorpusExport.HandCorrectionsName);
    }

    [Fact]
    public void Without_the_corrections_ticked_the_transcript_carries_no_name_no_corrected_word_and_no_note()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "el quati come", title: "Zoológico");

        var human = new HumanLayer(context, Then);
        var zenobia = human.Add("Zenobia");
        human.Assign(meeting, MeetingRows.SpeakerLabel, zenobia);
        var correction = human.Correct("quati", "Coati");
        human.Describe(context.Meetings.First(row => row.Id == meeting), "Zoológico", "nota privada");

        var row = context.Meetings.AsNoTracking().First(candidate => candidate.Id == meeting);
        var own = TranscriptRenderer.Render(
            new TranscriptHeader(
                meeting,
                row.StartedAt,
                row.Language,
                row.Title,
                row.Context,
                new Dictionary<string, string> { [MeetingRows.SpeakerLabel] = "Zenobia" },
                [correction]),
            new MeetingReading(context, TimeProvider.System).EveryTurn(meeting)).Markdown;
        DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Transcript, CorpusFiles.PathFor(meeting, "transcript.md"), Then, own);

        own.ShouldContain("Zenobia");
        own.ShouldContain("Coati");
        own.ShouldContain("nota privada");

        var bare = Only(CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Transcripts), TimeZoneInfo.Utc, Now), "transcript.md");
        var text = File.ReadAllText(bare.FullName);
        text.ShouldContain(MeetingRows.SpeakerLabel);
        text.ShouldContain("quati");
        text.ShouldContain("Zoológico");
        text.ShouldNotContain("Zenobia");
        text.ShouldNotContain("Coati");
        text.ShouldNotContain("nota privada");

        using var second = new TemporaryFolder();
        var copied = Only(
            CorpusExport.Into(context, second.Folder.FullName, Ticks(ExportKind.Transcripts, ExportKind.HandCorrections), TimeZoneInfo.Utc, Now),
            "transcript.md");
        File.ReadAllBytes(copied.FullName).ShouldBe(
            File.ReadAllBytes(CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "transcript.md")).FullName));
    }

    [Fact]
    public void An_export_of_everything_finds_every_meeting_with_what_it_had()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var withAudio = Recorded(context, corpus.Root, "uno", title: "Con audio");
        var without = MeetingRows.Recorded(context, Then + Duration.FromMilliseconds(60_000), ["dos"], title: "Sin audio");
        Extraction(context, withAudio, "{\"a\":1}");
        Extraction(context, without, "{\"b\":2}");

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now);

        exported.Meetings.ShouldBe(2);
        exported.LeftOut.ShouldBeEmpty();

        foreach (var meeting in new[] { withAudio, without })
        {
            var folder = exported.Folder.GetDirectories().Single(directory =>
                Card(directory).GetProperty("meeting").GetString() == meeting.ToString());

            foreach (var artifact in context.Artifacts.AsNoTracking().Where(row => row.MeetingId == meeting).ToArray())
            {
                if (artifact.Kind is ArtifactKind.Manifest or ArtifactKind.Utterances)
                {
                    continue;
                }

                var name = artifact.RelativePath[$"meetings/{meeting}/".Length..];
                File.ReadAllBytes(Path.Combine(folder.FullName, name.Replace('/', Path.DirectorySeparatorChar)))
                    .ShouldBe(File.ReadAllBytes(CorpusFiles.Locate(corpus.Root, artifact.RelativePath).FullName));
            }
        }

        exported.Folder.GetDirectories().Single(directory => directory.Name.EndsWith("Sin audio", StringComparison.Ordinal))
            .GetFiles("audio.wav").ShouldBeEmpty();
    }

    [Fact]
    public void What_an_export_holds_is_read_without_this_application()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var one = Recorded(context, corpus.Root, "uno", title: "Primera");
        var two = MeetingRows.Recorded(context, Then + Duration.FromMilliseconds(3_600_000), ["dos"], title: "Segunda | con barra");

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Transcripts), TimeZoneInfo.Utc, Now);

        var index = File.ReadAllText(Path.Combine(exported.Folder.FullName, CorpusExport.IndexName));
        index.ShouldContain("kinds: [transcripts]");
        index.ShouldContain("meetings: 2");

        foreach (var (meeting, title) in new[] { (one, "Primera"), (two, "Segunda \\| con barra") })
        {
            var row = index.Split('\n').Single(line => line.Contains(meeting.ToString(), StringComparison.Ordinal));
            var cells = row.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var folder = new DirectoryInfo(Path.Combine(exported.Folder.FullName, cells[0]));

            folder.Exists.ShouldBeTrue();
            cells[2].ShouldBe(context.Meetings.AsNoTracking().First(candidate => candidate.Id == meeting).StartedAt.ToStorage());
            var card = Card(folder);
            card.GetProperty("meeting").GetString().ShouldBe(meeting.ToString());
            card.GetProperty("started_at").GetString().ShouldBe(cells[2]);
            row.ShouldContain(title);
        }

        Card(exported.Folder.GetDirectories().Single(directory => directory.Name.Contains("Primera", StringComparison.Ordinal)))
            .GetProperty("title").GetString().ShouldBe("Primera");
    }

    [Fact]
    public void A_meeting_on_its_way_out_is_not_exported()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var staying = Recorded(context, corpus.Root, "uno", title: "Se queda");
        var leaving = MeetingRows.Recorded(context, Then + Duration.FromMilliseconds(1_000), ["dos"], title: "Se va");
        var row = context.Meetings.First(candidate => candidate.Id == leaving);
        row.LifecycleState = LifecycleState.Deleting;
        row.DeletedAt = Then;
        context.SaveChanges();

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now);

        exported.Meetings.ShouldBe(1);
        exported.Folder.GetDirectories().ShouldHaveSingleItem().Name.ShouldContain("Se queda");
        File.ReadAllText(Path.Combine(exported.Folder.FullName, CorpusExport.IndexName))
            .ShouldNotContain(leaving.ToString());
        staying.ShouldNotBe(leaving);
    }

    [Fact]
    public void What_somebody_corrected_by_hand_goes_only_when_it_was_ticked()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var second = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno", title: "Con manos");

        var human = new HumanLayer(context, Then);
        var node = human.Root(NodeKind.Organization, "Acme");
        human.Link(meeting, node, MeetingNodeRole.Counterpart);
        human.Describe(context.Meetings.First(row => row.Id == meeting), "Con manos", "la nota");
        human.Assign(meeting, MeetingRows.SpeakerLabel, human.Add("Zenobia"));
        human.Correct("quati", "Coati", under: node);

        var with = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.HandCorrections), TimeZoneInfo.Utc, Now);
        var perMeeting = File.ReadAllText(Path.Combine(
            with.Folder.GetDirectories().Single().FullName, CorpusExport.HandCorrectionsName));
        perMeeting.ShouldContain("Zenobia");
        perMeeting.ShouldContain("Acme");
        perMeeting.ShouldContain("la nota");

        var root = File.ReadAllText(Path.Combine(with.Folder.FullName, CorpusExport.HandCorrectionsName));
        root.ShouldContain("Coati");
        root.ShouldContain("Acme");
        root.ShouldContain("Zenobia");

        var without = CorpusExport.Into(context, second.Folder.FullName, Ticks(ExportKind.Transcripts), TimeZoneInfo.Utc, Now);
        without.Folder.GetFiles("*", SearchOption.AllDirectories)
            .ShouldNotContain(file => file.Name == CorpusExport.HandCorrectionsName);
    }

    /// <summary>
    /// Which summary a meeting shows is one of the things somebody settled by hand, so it goes with
    /// the rest of that file. Goes red with <c>SummaryShown</c> left out of the record.
    /// </summary>
    [Fact]
    public void The_summary_a_meeting_shows_goes_with_what_was_corrected_by_hand()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno");

        var older = MeetingRows.Extracted(context, meeting, Then, accepted: Then, "el primero");
        var laterOn = UtcTimestamp.From(Then.Value.AddHours(1));
        var newer = MeetingRows.Extracted(context, meeting, laterOn, accepted: laterOn, "el segundo");

        new HumanLayer(context, UtcTimestamp.From(Then.Value.AddHours(2))).ShowSummary(meeting, older);

        var exported = CorpusExport.Into(
            context, destination.Folder.FullName, Ticks(ExportKind.HandCorrections), TimeZoneInfo.Utc, Now);
        var corrections = File.ReadAllText(Path.Combine(
            exported.Folder.GetDirectories().Single().FullName, CorpusExport.HandCorrectionsName));

        corrections.ShouldContain(older.ToString());
        corrections.ShouldNotContain(newer.ToString());
    }

    [Fact]
    public void A_file_the_corpus_records_and_the_disk_has_lost_is_named_and_the_export_still_finishes()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno");
        var lost = CorpusFiles.PathFor(meeting, "deepgram.v2.json");
        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.DeepgramResponse,
            Origin = ArtifactKind.DeepgramResponse.OriginOf(),
            RelativePath = lost,
            ByteSize = 10,
            Sha256 = new string('f', 64),
            ConfirmedAt = Then,
        });

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now);

        exported.LeftOut.ShouldBe([lost]);
        File.ReadAllText(Path.Combine(exported.Folder.FullName, CorpusExport.IndexName))
            .ShouldContain($"## left out\n\n- {lost}");
        destination.Folder.GetDirectories().ShouldHaveSingleItem().Name
            .ShouldNotEndWith(CorpusExport.PartialSuffix);
    }

    [Fact]
    public void An_export_is_a_new_folder_and_leaves_what_was_already_there_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        Recorded(context, corpus.Root, "uno");
        var mine = Path.Combine(destination.Folder.FullName, "mis-cosas.txt");
        File.WriteAllText(mine, "de la persona");

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now);

        File.ReadAllText(mine).ShouldBe("de la persona");
        exported.Folder.Parent!.FullName.ShouldBe(destination.Folder.FullName);
        exported.Folder.Name.ShouldBe("meeting-transcriber-export-20260930-100005");
        destination.Folder.GetDirectories().ShouldHaveSingleItem().Name
            .ShouldStartWith("meeting-transcriber-export-");
        destination.Folder.GetFileSystemInfos().Length.ShouldBe(2);

        Should.Throw<IOException>(() =>
            CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now));
    }

    [Fact]
    public void Two_meetings_that_would_share_a_folder_name_get_two()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        MeetingRows.Recorded(context, Then, ["uno"], title: "Igual");
        MeetingRows.Recorded(context, Then, ["dos"], title: "Igual");

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Transcripts), TimeZoneInfo.Utc, Now);

        exported.Folder.GetDirectories().Select(folder => folder.Name).Order().ToArray()
            .ShouldBe(["2026-03-04 1430 Igual", "2026-03-04 1430 Igual (2)"]);
    }

    [Fact]
    public void The_export_is_remembered_as_the_last_one_only_once_it_is_whole()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        Recorded(context, corpus.Root, "uno");

        new CorpusSettings(context).LastExportMade().ShouldBeNull();

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Audio, ExportKind.Summaries), TimeZoneInfo.Utc, Now);

        using var other = corpus.Open();
        var last = new CorpusSettings(other).LastExportMade().ShouldNotBeNull();
        last.At.ShouldBe(Now);
        last.Kinds.ShouldBe([ExportKind.Audio, ExportKind.Summaries]);
        last.Meetings.ShouldBe(1);
        last.Folder.ShouldBe(exported.Folder.FullName);
        Directory.Exists(last.Folder).ShouldBeTrue();

        Should.Throw<ArgumentException>(() =>
            CorpusExport.Into(context, destination.Folder.FullName, new HashSet<ExportKind>(), TimeZoneInfo.Utc, Now));
    }

    [Fact]
    public void An_export_that_fails_part_way_is_never_remembered_and_never_looks_finished()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno");
        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.DeepgramResponse,
            Origin = ArtifactKind.DeepgramResponse.OriginOf(),
            RelativePath = "elsewhere/deepgram.v2.json",
            ByteSize = 1,
            Sha256 = new string('f', 64),
            ConfirmedAt = Then,
        });

        Should.Throw<InvalidOperationException>(() =>
            CorpusExport.Into(context, destination.Folder.FullName, Everything, TimeZoneInfo.Utc, Now));

        new CorpusSettings(context).LastExportMade().ShouldBeNull();
        destination.Folder.GetDirectories().ShouldHaveSingleItem().Name.ShouldEndWith(CorpusExport.PartialSuffix);
    }

    [Fact]
    public void A_copy_of_another_length_than_the_corpus_recorded_is_deleted_and_named()
    {
        using var corpus = new TemporaryCorpus();
        using var destination = new TemporaryFolder();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, "uno");
        var row = context.Artifacts.First(artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Audio);
        row.ByteSize = 999;
        context.SaveChanges();

        var exported = CorpusExport.Into(context, destination.Folder.FullName, Ticks(ExportKind.Audio), TimeZoneInfo.Utc, Now);

        exported.LeftOut.ShouldBe([row.RelativePath]);
        exported.Folder.GetFiles("audio.wav", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static Guid Recorded(CorpusDbContext context, DirectoryInfo root, string said, string? title = null)
    {
        var meeting = MeetingRows.Recorded(context, Then, [said], title: title, root: root);

        DurableArtifact.WriteText(
            context, meeting, ArtifactKind.DeepgramResponse, CorpusFiles.PathFor(meeting, "deepgram.json"), Then, "{\"paid\":true}");

        return meeting;
    }

    private static void Extraction(CorpusDbContext context, Guid meeting, string json) =>
        DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.Extraction,
            CorpusFiles.PathFor(meeting, $"extractions/{Guid.NewGuid()}.json"),
            Then,
            json);

    private static FileInfo Only(CorpusExported exported, string name) =>
        exported.Folder.GetFiles(name, SearchOption.AllDirectories).ShouldHaveSingleItem();

    /// <summary>A folder's card, read with the framework's JSON and never with <c>MeetingManifest</c>.</summary>
    private static JsonElement Card(DirectoryInfo folder) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(folder.FullName, "manifest.json"))).RootElement.Clone();
}
