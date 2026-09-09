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
    /// Blocks with no row may be the only copy of that audio. They are reported so somebody
    /// decides, and nothing here deletes them or counts them as an artifact. What the finding may
    /// not say is that the recording was never materialised or never filed — the reconciler knows
    /// neither, and a saved meeting's spool folder is where both are false.
    /// </summary>
    [Fact]
    public void Blocks_in_the_spool_with_no_row_are_reported_and_left_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var block = Drop(corpus, $"spool/{meeting}/loopback.blocks", "pcm");

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

    /// <summary>
    /// The card. A spool folder holds the blocks and the working files a recording writes beside
    /// them, and only some of those are audio somebody could lose.
    /// </summary>
    [Fact]
    public void A_spool_folders_own_working_files_are_not_a_recording_to_recover()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        var carrying = new[] { "loopback.blocks", "microphone.blocks", "manifest.json", "changes.jsonl" }
            .Select(name => Drop(corpus, $"spool/{meeting}/{name}", "written"))
            .ToArray();

        foreach (var mark in new[] { "capture.mark", "reading.mark", "saving.mark" })
        {
            Drop(corpus, $"spool/{meeting}/{mark}", string.Empty);
        }

        var findings = ArtifactReconciler.Check(context);

        findings.Select(finding => finding.RelativePath).ShouldBe(carrying, ignoreOrder: true);
        findings.ShouldAllBe(finding => finding.State == ArtifactState.Spooled);
    }

    /// <summary>
    /// <c>audio.wav</c> beside its blocks is the recording those blocks were already poured into,
    /// which is the exact opposite of one that was never made. Two files, two sentences.
    /// </summary>
    [Fact]
    public void The_recording_poured_out_of_the_blocks_is_not_called_one_that_was_never_made()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var blocks = Drop(corpus, $"spool/{meeting}/loopback.blocks", "pcm");
        var poured = Drop(corpus, $"spool/{meeting}/audio.wav", "riff");

        var findings = ArtifactReconciler.Check(context);

        findings.ShouldAllBe(finding => finding.State == ArtifactState.Spooled);
        var aboutBlocks = findings.Single(finding => finding.RelativePath == blocks).Detail;
        var aboutPoured = findings.Single(finding => finding.RelativePath == poured).Detail;

        aboutPoured.ShouldNotBe(aboutBlocks);
        aboutPoured.ShouldContain("poured out of");
        aboutPoured.ShouldNotContain("only copy");
        aboutBlocks.ShouldContain("only copy of that audio");
    }

    /// <summary>
    /// The card says which meeting the blocks are and the changes file cannot: its records carry an
    /// instant, a channel and a device and no meeting id at all. One sentence over both would be
    /// false of one of them.
    /// </summary>
    [Fact]
    public void What_a_recording_wrote_about_itself_is_not_called_its_audio()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var card = Drop(corpus, $"spool/{meeting}/manifest.json", "{}");
        var changes = Drop(corpus, $"spool/{meeting}/changes.jsonl", "{}");

        var findings = ArtifactReconciler.Check(context);

        findings.ShouldAllBe(finding => finding.State == ArtifactState.Spooled);
        var aboutCard = findings.Single(finding => finding.RelativePath == card).Detail;
        var aboutChanges = findings.Single(finding => finding.RelativePath == changes).Detail;

        aboutCard.ShouldContain("holds no audio");
        aboutChanges.ShouldContain("holds no audio");
        aboutCard.ShouldNotContain("only copy");
        aboutChanges.ShouldNotContain("only copy");
        aboutCard.ShouldContain("which meeting");
        aboutChanges.ShouldNotContain("which meeting");
        aboutChanges.ShouldContain("what somebody moved");
    }

    /// <summary>
    /// Somebody else's file under the spool is somebody else's file. Being under that folder is not
    /// what makes something part of a recording; the name is.
    /// </summary>
    [Fact]
    public void A_file_nothing_recorded_is_still_a_file_with_no_row_under_the_spool()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var stray = Drop(corpus, $"spool/{meeting}/notes.txt", "what I have to ask about");

        var finding = ArtifactReconciler.Check(context).ShouldHaveSingleItem();
        finding.State.ShouldBe(ArtifactState.Unrecorded);
        finding.RelativePath.ShouldBe(stray);
        finding.Detail.ShouldContain("it may be the only copy");
    }

    /// <summary>
    /// What the silence is and is not. It is not the product cleaning the folder up: nothing does,
    /// docs/corpus.md says so, and deleting it by hand stays the only thing that ends it. What
    /// changed is that <c>check</c> no longer offers a decision about a recording whose owner
    /// pressed Discard and which nothing offers again.
    /// </summary>
    [Fact]
    public void A_recording_somebody_threw_away_is_not_reported_at_all()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var thrownBlocks = Drop(corpus, $"spool/.removing-{meeting}/{meeting}/loopback.blocks", "pcm");
        var thrownCard = Drop(corpus, $"spool/.removing-{meeting}/{meeting}/manifest.json", "{}");
        var waiting = Drop(corpus, $"spool/{meeting}/loopback.blocks", "pcm");

        var findings = ArtifactReconciler.Check(context);

        findings.ShouldHaveSingleItem().RelativePath.ShouldBe(waiting);
        findings.ShouldNotContain(
            finding => finding.RelativePath.Contains(".removing-", StringComparison.Ordinal));

        ArtifactReconciler.Sweep(context).Removed.ShouldBeEmpty();
        CorpusFiles.Locate(corpus.Root, thrownBlocks).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, thrownCard).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// The boundary of that silence, and both of the two states that cross it. A write that never
    /// finished and a copy a replace set aside are swept wherever they are, so <c>check</c> has to
    /// go on naming them — a sweep taking a file nothing ever reported is the two disagreeing. Both
    /// arms are here because the skip has to sit below <em>both</em> branches, and one of them alone
    /// leaves the other free to move.
    /// </summary>
    [Fact]
    public void A_write_that_never_finished_under_a_discard_is_still_swept()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var thrown = $"spool/.removing-{meeting}/{meeting}";
        var half = Drop(corpus, $"{thrown}/audio.wav{CorpusFiles.UnfinishedSuffix}", "half");

        // A copy of a file a replace of this corpus set aside, whose destination is back, which is
        // what makes a sweep take it.
        var back = Drop(corpus, $"{thrown}/loopback.blocks", "pcm");
        var aside = Drop(
            corpus,
            $"{thrown}/loopback.blocks.{Guid.NewGuid():n}{CorpusFiles.SupersededSuffix}",
            "the copy before this one");

        var findings = ArtifactReconciler.Check(context);

        findings.Single(finding => finding.RelativePath == half)
            .State.ShouldBe(ArtifactState.Unfinished);
        findings.Single(finding => finding.RelativePath == aside)
            .State.ShouldBe(ArtifactState.Superseded);

        // And the blocks beside them stay silent, which is what says the skip is still under both.
        findings.ShouldNotContain(finding => finding.RelativePath == back);

        ArtifactReconciler.Sweep(context).Removed.ShouldBe([half, aside], ignoreOrder: true);
        CorpusFiles.Locate(corpus.Root, half).Exists.ShouldBeFalse();
        CorpusFiles.Locate(corpus.Root, aside).Exists.ShouldBeFalse();
    }

    /// <summary>
    /// Being under the spool is not what makes a file part of a recording, and neither is being
    /// under it somewhere. A recording's files sit directly in its own folder, which is the only
    /// shape a row may be stored at, so anything deeper is a file with no row and is told that
    /// rather than that it came out of blocks that are not there.
    /// </summary>
    [Fact]
    public void A_file_further_down_than_a_recordings_folder_is_not_one_of_its_files()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var restored = Drop(corpus, $"spool/{meeting}/before-the-reinstall/loopback.blocks", "pcm");
        var loose = Drop(corpus, "spool/audio.wav", "riff");

        var findings = ArtifactReconciler.Check(context);

        findings.Select(finding => finding.RelativePath).ShouldBe([loose, restored], ignoreOrder: true);
        findings.ShouldAllBe(finding => finding.State == ArtifactState.Unrecorded);
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
