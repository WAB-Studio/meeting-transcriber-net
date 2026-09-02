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
    /// The other file a replace can leave beside a destination, and the one observation that says
    /// which of the two things it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A set of derived files is replaced by emptying every destination first, and what comes out
    /// of one is an artifact's bytes rather than a write's. Still there afterwards, it is either
    /// the last copy of a derived file — the machine stopped between the emptying and the moves, or
    /// a put-back was refused — or a copy the replace finished on top of and did not get to remove.
    /// A sweep taking the first is a derived file that stopped existing and said nothing; leaving
    /// the second is `check` standing red until somebody deletes a file by hand.
    /// </para>
    /// <para>
    /// What tells them apart is whether the file the copy came out of is back, so the two are put
    /// in one corpus and swept in one run: the suffix is identical, the meeting is the same, and
    /// only the destination differs. A sweep reading the suffix alone takes both or neither, and
    /// either way this goes red.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_copy_is_taken_only_where_the_file_that_replaced_it_is_back()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "this rendering");
        var replaced = Aside(corpus, meeting, "transcript.md", "the rendering before this one");
        var missing = Aside(corpus, meeting, "utterances.jsonl", "the turns before these");

        var reported = ArtifactReconciler.Check(context);

        reported.Select(finding => finding.RelativePath).ShouldBe([replaced, missing]);
        reported.ShouldAllBe(finding => finding.State == ArtifactState.Superseded);
        reported[0].Detail.ShouldNotBe(
            reported[1].Detail,
            "one wants a rebuild and the other wants nothing, so the report says which");

        var swept = ArtifactReconciler.Sweep(context);

        swept.Removed.ShouldBe([replaced]);
        swept.Left.ShouldBeEmpty();
        File.Exists(CorpusFiles.Locate(corpus.Root, replaced).FullName).ShouldBeFalse();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, missing).FullName)
            .ShouldBe("the turns before these");

        var left = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        left.State.ShouldBe(ArtifactState.Superseded);
        left.RelativePath.ShouldBe(missing);
    }

    /// <summary>
    /// A file ending the same way that no replace of this corpus wrote is somebody else's file, and
    /// is reported as one and left where it is.
    /// </summary>
    /// <remarks>
    /// The sweep's licence to delete a copy is that the corpus itself set it aside and the file it
    /// came out of is back, and the second half is only askable of a name the first half wrote —
    /// destination, the token that makes the copy unique, suffix. Read as "whatever stands before
    /// the last full stop", any name at all resolves to a destination, and this one resolves to a
    /// file that is on disk, so a sweep would take somebody else's file on the strength of a
    /// meeting having a transcript. Reported as a copy it would be no better: the advice under that
    /// state is to rebuild the derived file it came out of, and there is no such file. What it is
    /// is a file the corpus has no row for, which is the one state that says it may be the only one.
    /// </remarks>
    [Fact]
    public void A_file_this_corpus_did_not_set_aside_is_not_a_copy_of_anything()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "this rendering");
        var theirs = Drop(
            corpus,
            $"meetings/{meeting}/transcript.md.f00d{CorpusFiles.SupersededSuffix}",
            "somebody else's idea of a backup");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Unrecorded);
        finding.RelativePath.ShouldBe(theirs);

        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, theirs).FullName)
            .ShouldBe("somebody else's idea of a backup");
    }

    /// <summary>
    /// A copy wearing the read-only bit is still the corpus's to remove once the file that replaced
    /// it is back.
    /// </summary>
    /// <remarks>
    /// The bit is not somebody's answer about this file. It rides in on a backup medium or a policy,
    /// the corpus replaces a derivative wearing it without asking — which is what
    /// <c>DurableWriteTests.A_destination_that_cannot_be_opened_for_writing_is_still_replaced</c>
    /// settles — and the rename that sets the copy aside carries it across. Left standing it refuses
    /// the delete exactly the way a live handle does, and the sweep would report the copy as a write
    /// somebody is still making and tell the person to run the command again. Nothing has it open,
    /// running it again does the same thing, and `check` never goes green: the state this whole
    /// change exists to end, one layer further down.
    /// </remarks>
    [Fact]
    public void A_copy_the_disk_marked_read_only_is_still_taken()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        Written(context, corpus, meeting, "transcript.md", ArtifactKind.Transcript, "this rendering");
        var replaced = Aside(corpus, meeting, "transcript.md", "the rendering before this one");
        CorpusFiles.Locate(corpus.Root, replaced).IsReadOnly = true;

        var swept = ArtifactReconciler.Sweep(context);

        swept.Removed.ShouldBe([replaced]);
        swept.Left.ShouldBeEmpty("nothing has it open, so nothing is waiting on anything");
        ArtifactReconciler.Check(context).ShouldBeEmpty();
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

    /// <summary>
    /// The copy a replace would have set aside of this meeting's file, named the way a replace
    /// names one, standing in for a machine that stopped before the tidy-up.
    /// </summary>
    private static string Aside(TemporaryCorpus corpus, Guid meeting, string name, string text) =>
        Drop(
            corpus,
            $"{CorpusFiles.PathFor(meeting, name)}.{Guid.NewGuid():n}{CorpusFiles.SupersededSuffix}",
            text);

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
