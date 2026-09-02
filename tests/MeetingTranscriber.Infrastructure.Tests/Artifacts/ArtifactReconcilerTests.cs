using System.Text;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Artifacts;

/// <summary>
/// What start-up is allowed to conclude from what it finds, and what it has to leave for a person.
/// </summary>
public class ArtifactReconcilerTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 7, 9, 15, 0, TimeSpan.Zero));

    [Fact]
    public void A_corpus_nothing_went_wrong_in_has_nothing_to_report()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");

        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
    }

    [Fact]
    public void An_unfinished_write_is_named_for_what_it_is_and_swept()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var leftover = Drop(corpus, $"meetings/{meeting}/transcript.md.f00d{CorpusFiles.UnfinishedSuffix}", "half");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Unfinished);
        finding.RelativePath.ShouldBe(leftover);

        ArtifactReconciler.Sweep(context).Removed.ShouldBe([leftover]);
        ArtifactReconciler.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// The other file a replace can leave beside a destination, and the one the sweep may not have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A set of derived files is replaced by emptying every destination first, and what comes out
    /// of one is an artifact's bytes rather than a write's. Still there after the replace is over,
    /// it means the machine stopped inside the run of renames or the tidy-up was refused — and in
    /// the case the emptying exists for, a replace refused partway that puts the copies back, this
    /// is the file the put-back is about to move. A sweep taking it there is not a lost write; it
    /// is a derived file that stopped existing and said nothing.
    /// </para>
    /// <para>
    /// So it is named superseded and not unfinished, and the two claims are opposite: unfinished
    /// says these bytes were never an artifact, which is the whole of the sweep's licence to delete
    /// one on sight.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_copy_a_replace_set_aside_is_reported_and_survives_the_sweep()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var aside = Drop(
            corpus,
            $"meetings/{meeting}/transcript.md.f00d{CorpusFiles.SupersededSuffix}",
            "the rendering before this one");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Superseded);
        finding.RelativePath.ShouldBe(aside);

        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, aside).FullName)
            .ShouldBe("the rendering before this one");
    }

    /// <summary>
    /// Two spellings of one path name one file here, so a row and the file a scan finds under it
    /// are one artifact rather than a row and a stray.
    /// </summary>
    /// <remarks>
    /// The reconciler asks the same question the write's guard asks — which destination is this —
    /// and has to get the same answer. Asked exactly it gets a different one in one direction only:
    /// the row is fine, because looking the file up goes through the filesystem and the filesystem
    /// does not care about the case, and the same file is then reported as one nothing recorded and
    /// may be the only copy of. One intact file, named as a problem.
    /// </remarks>
    [Fact]
    public void A_recorded_file_spelled_another_way_is_still_the_file_its_row_names()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var artifact = Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");

        var stored = CorpusFiles.Locate(corpus.Root, artifact.RelativePath);
        File.Move(stored.FullName, Path.Combine(stored.Directory!.FullName, "Transcript.md"));

        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// The line the reconciler does not cross. A file with no row is what a machine dying between
    /// the rename and the insert leaves, and the file it leaves may be the response somebody was
    /// charged for — so it is reported and it is still there afterwards.
    /// </summary>
    [Fact]
    public void A_file_with_no_row_is_reported_and_survives_the_sweep()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var orphan = Drop(corpus, $"meetings/{meeting}/deepgram.json", "{\"paid\":true}");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Unrecorded);
        finding.RelativePath.ShouldBe(orphan);

        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, orphan).FullName).ShouldBe("{\"paid\":true}");
    }

    [Fact]
    public void A_row_whose_file_is_gone_is_the_corpus_claiming_what_it_does_not_have()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var artifact = Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");

        CorpusFiles.Locate(corpus.Root, artifact.RelativePath).Delete();

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Missing);
        finding.RelativePath.ShouldBe(artifact.RelativePath);
    }

    /// <summary>
    /// A truncated file is caught by the size, which costs nothing, so it is caught whether or not
    /// anybody asked for the expensive pass.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_the_length_its_row_says_is_found_without_hashing_anything()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var artifact = Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");

        File.WriteAllText(CorpusFiles.Locate(corpus.Root, artifact.RelativePath).FullName, "cut");

        ArtifactReconciler.Check(context).ShouldHaveSingleItem()
            .State.ShouldBe(ArtifactState.Changed);
    }

    /// <summary>
    /// Content that changed under a file that kept its length is the case only the hash catches,
    /// and hashing every WAV of a corpus at every start-up is the cost of catching it. So it is a
    /// pass somebody asks for, and the report says plainly which one found it.
    /// </summary>
    [Fact]
    public void A_file_the_size_of_its_row_and_not_the_content_of_it_is_found_only_when_asked()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var artifact = Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");

        File.WriteAllText(CorpusFiles.Locate(corpus.Root, artifact.RelativePath).FullName, "a rewriting");

        ArtifactReconciler.Check(context).ShouldBeEmpty();
        ArtifactReconciler.Check(context, verifyContents: true).ShouldHaveSingleItem()
            .State.ShouldBe(ArtifactState.Changed);
    }

    /// <summary>
    /// Spool blocks are the only copy of audio that was never materialised. They are reported so
    /// somebody decides, and nothing here deletes them or counts them as an artifact.
    /// </summary>
    [Fact]
    public void Blocks_of_a_recording_that_was_never_materialised_are_reported_and_left_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var block = Drop(corpus, $"spool/{meeting}/000001.block", "pcm");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Spooled);
        finding.RelativePath.ShouldBe(block);

        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
        CorpusFiles.Locate(corpus.Root, block).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A spool block that was recorded is an artifact like any other. The folder is what a block
    /// with no row means, not what every file under it is.
    /// </summary>
    [Fact]
    public void A_spool_block_the_corpus_recorded_is_not_a_loose_one()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.SpoolBlock,
            CorpusFiles.SpoolPathFor(meeting, "000001.block"),
            When,
            "pcm");

        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    [Fact]
    public void The_database_beside_the_artifacts_is_not_one_of_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Recorded(context);

        File.Exists(corpus.DatabasePath).ShouldBeTrue();
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>A file put on disk by something other than a durable write.</summary>
    private static string Drop(TemporaryCorpus corpus, string relativePath, string text)
    {
        var file = CorpusFiles.Locate(corpus.Root, relativePath);
        file.Directory!.Create();
        File.WriteAllText(file.FullName, text, new UTF8Encoding(false));
        return relativePath;
    }

    private static Artifact Written(
        CorpusDbContext context,
        TemporaryCorpus corpus,
        Guid meeting,
        string name,
        ArtifactKind kind,
        string text) =>
        DurableArtifact.WriteText(
            context, meeting, kind, CorpusFiles.PathFor(meeting, name), When, text);

    private static Guid Recorded(CorpusDbContext context)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Language = "es",
            StartedAt = When,
            SourceProfile = SourceProfile.Multichannel,
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();
        return meeting.Id;
    }
}
