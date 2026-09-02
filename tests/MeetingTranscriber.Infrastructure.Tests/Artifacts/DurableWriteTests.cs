using System.Text;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Infrastructure.Tests.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Artifacts;

/// <summary>
/// The write sequence, cut at every step it can be cut at.
/// </summary>
/// <remarks>
/// <para>
/// A crash is simulated by stopping the sequence at a step boundary rather than by killing a
/// process, and that is not a shortcut: steps two, three and four leave the same thing on disk —
/// a temporary and nothing else — because reading a file and hashing it change nothing. So the
/// states a cut can leave are four, all of them reachable here: nothing, a temporary, a file the
/// corpus has no row for, and a finished artifact.
/// </para>
/// <para>
/// Every one of them is followed by <see cref="EveryRowReReads"/>, which is the invariant the
/// whole design is for and the thing that would be quietly false if the order of the last two
/// steps were ever swapped.
/// </para>
/// </remarks>
public class DurableWriteTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 7, 9, 15, 0, TimeSpan.Zero));

    [Fact]
    public void An_artifact_is_recorded_as_exactly_what_ended_up_on_disk()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        var artifact = DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Transcript, path, When, "# Reunión\n\nUna línea.\n");

        artifact.RelativePath.ShouldBe(path);
        artifact.Origin.ShouldBe(ArtifactOrigin.Derived);
        artifact.Sha256.Length.ShouldBe(64);
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe("# Reunión\n\nUna línea.\n");
        EveryRowReReads(context);
    }

    /// <summary>
    /// Written whole somewhere else and then put in place, which is the difference between a
    /// reader seeing the old artifact or the new one and a reader seeing half of one.
    /// </summary>
    [Fact]
    public void A_finished_write_leaves_nothing_beside_the_artifact()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.Transcript,
            CorpusFiles.PathFor(meeting, "transcript.md"),
            When,
            "whatever");

        Files(corpus).ShouldBe([$"meetings/{meeting}/transcript.md"]);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// Cut inside step one. Producing the content is the caller's code and it is allowed to fail
    /// halfway — a renderer that throws on the last turn, a stream that ends early — and what it
    /// must never do is leave something a later run would read as an artifact.
    /// </summary>
    [Fact]
    public void A_write_cut_while_its_content_is_produced_leaves_nothing_at_all()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        Should.Throw<InvalidOperationException>(() => DurableArtifact.Write(
            context, meeting, ArtifactKind.Transcript, path, When, stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("half a transcript"));
                throw new InvalidOperationException("the renderer gave up");
            }));

        Files(corpus).ShouldBeEmpty();
        context.Artifacts.ShouldBeEmpty();
        EveryRowReReads(context);
    }

    /// <summary>
    /// Cut at steps two, three or four: the temporary is written and the machine stops. Nothing
    /// disposes anything, which is what makes this a crash and not a handled failure.
    /// </summary>
    /// <remarks>
    /// What a sweep then does with it is not asked here, and cannot be: a crash leaves the temporary
    /// with nothing holding it, and this process is holding it for as long as the write is stopped
    /// rather than over. That half is
    /// <see cref="ArtifactReconcilerTests.An_unfinished_write_is_named_for_what_it_is_and_swept"/>,
    /// over a file on disk and no writer at all — which is what a process that died leaves. The
    /// disposal at the end is not part of the cut: everything asserted has already been asserted,
    /// and without it the handle outlives the corpus folder somebody has to delete.
    /// </remarks>
    [Fact]
    public void A_write_cut_before_it_is_put_in_place_is_an_unfinished_write_and_no_more()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        var staged = StagedArtifact.Stage(
            context,
            meeting,
            ArtifactKind.Transcript,
            path,
            stream => stream.Write(Encoding.UTF8.GetBytes("a whole transcript")));

        staged.IsPending.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, path).Exists.ShouldBeFalse();
        context.Artifacts.ShouldBeEmpty();
        EveryRowReReads(context);

        var findings = ArtifactReconciler.Check(context);
        findings.Select(finding => finding.State).ShouldBe([ArtifactState.Unfinished]);
        Files(corpus).ShouldHaveSingleItem().ShouldEndWith(CorpusFiles.UnfinishedSuffix);

        staged.Dispose();
    }

    /// <summary>
    /// A sweep running beside a write that is still being made leaves it alone, and the write it
    /// arrived in the middle of still lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a hypothetical: <c>sweep</c> is a command somebody runs in a terminal, and the
    /// application it runs beside is rendering meetings. A sweep deletes a <c>.partial</c> on sight
    /// with no age and no second thought, and the temporary of a write in flight is spelled exactly
    /// that. What separates the two is the handle — a staged artifact holds its temporary from the
    /// moment it exists until the moment it is renamed — so the sweep is refused it and leaves it.
    /// </para>
    /// <para>
    /// Taken, it would be worse than a lost render: inside a set the moves run after every
    /// destination has been emptied, and nothing puts a destination back from there.
    /// </para>
    /// <para>
    /// It says which ones it left rather than only how many it took, because this is the ordinary
    /// outcome of running the command beside a working application: reported as a count of nothing
    /// removed, a corpus whose every write is live and a corpus with nothing in it read the same.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_sweep_running_beside_a_write_leaves_the_write_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        using var staged = StagedArtifact.Stage(
            context,
            meeting,
            ArtifactKind.Transcript,
            path,
            stream => stream.Write(Encoding.UTF8.GetBytes("a whole transcript")));

        var swept = ArtifactReconciler.Sweep(context);
        swept.Removed.ShouldBeEmpty();
        swept.Left.ShouldHaveSingleItem().ShouldEndWith(CorpusFiles.UnfinishedSuffix);

        staged.Commit(When);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe("a whole transcript");
        Files(corpus).ShouldBe([path]);
        EveryRowReReads(context);
    }

    /// <summary>
    /// Cut between step five and step six, which is the one the order was chosen for. The file is
    /// in place and the corpus has not recorded it — so the corpus says less than the truth, which
    /// is recoverable, instead of more, which is not.
    /// </summary>
    [Fact]
    public void A_write_cut_after_the_file_is_in_place_leaves_a_file_and_no_row()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        // A meeting that is not in the corpus: the row cannot be written, and the foreign key is
        // what refuses it, which is the same failure a process death between the two steps is.
        var unrecorded = Guid.NewGuid();
        var path = CorpusFiles.PathFor(unrecorded, "deepgram.json");

        using var staged = StagedArtifact.Stage(
            context,
            unrecorded,
            ArtifactKind.DeepgramResponse,
            path,
            stream => stream.Write(Encoding.UTF8.GetBytes("{}")));

        Should.Throw<DbUpdateException>(() => staged.Commit(When));

        CorpusFiles.Locate(corpus.Root, path).Exists.ShouldBeTrue();
        staged.IsPending.ShouldBeFalse();

        using var reopened = corpus.Open();
        reopened.Artifacts.ShouldBeEmpty();
        EveryRowReReads(reopened);

        var findings = ArtifactReconciler.Check(reopened);
        findings.Select(finding => finding.State).ShouldBe([ArtifactState.Unrecorded]);
    }

    /// <summary>
    /// The paid artifact rule, enforced by the move itself rather than by looking first: between a
    /// check and a rename there is a window, and what would fit through it is the only copy of a
    /// response somebody was charged for.
    /// </summary>
    [Fact]
    public void A_source_is_never_written_over()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "deepgram.json");

        DurableArtifact.WriteText(
            context, meeting, ArtifactKind.DeepgramResponse, path, When, "{\"paid\":true}");

        Should.Throw<ArtifactWriteException>(() => DurableArtifact.WriteText(
            context, meeting, ArtifactKind.DeepgramResponse, path, When, "{}"));

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe("{\"paid\":true}");
        context.Artifacts.Count().ShouldBe(1);
        Files(corpus).ShouldBe([$"meetings/{meeting}/deepgram.json"]);
        EveryRowReReads(context);
    }

    /// <summary>
    /// The one source that rule does not reach, and the reason the question is asked as *can this
    /// be produced again* rather than *is this a source*. The card is regenerated from the meetings
    /// row every time it is written, so refusing to replace it would protect nothing and cost the
    /// only thing it is for: a card that can be put right, and put back when it is gone.
    /// </summary>
    [Fact]
    public void The_recovery_card_is_the_source_a_second_write_replaces()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "manifest.json");

        var first = DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Manifest, path, When, "{\"title\":\"before\"}");
        var second = DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Manifest, path, When, "{\"title\":\"after\"}");

        second.Id.ShouldBe(first.Id);
        second.Origin.ShouldBe(ArtifactOrigin.Source);
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe("{\"title\":\"after\"}");
        context.Artifacts.Count().ShouldBe(1);
        EveryRowReReads(context);
    }

    /// <summary>
    /// What keeps the replaceable kinds from reaching a file that is not theirs. Whether a write
    /// may replace is decided by the kind the caller names, and the destination is named by the
    /// same caller — so on their own the two say nothing about each other, and a manifest addressed
    /// at the response would put the corpus's regenerable file over its paid one.
    /// </summary>
    [Fact]
    public void A_write_that_calls_a_path_something_it_is_not_is_refused_before_the_file_moves()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "deepgram.json");

        DurableArtifact.WriteText(
            context, meeting, ArtifactKind.DeepgramResponse, path, When, "{\"paid\":true}");

        var refused = Should.Throw<ArtifactWriteException>(() => DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Manifest, path, When, "{\"meeting\":\"mine now\"}"));

        refused.Message.ShouldContain("DeepgramResponse");
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe("{\"paid\":true}");

        var artifact = context.Artifacts.ShouldHaveSingleItem();
        artifact.Kind.ShouldBe(ArtifactKind.DeepgramResponse);
        artifact.Origin.ShouldBe(ArtifactOrigin.Source);
        Files(corpus).ShouldBe([$"meetings/{meeting}/deepgram.json"]);
        EveryRowReReads(context);
    }

    /// <summary>
    /// A derivative is the other half of that rule: re-rendering replaces it, and the corpus keeps
    /// one row for one file because the row is what a backup and a rebuild walk.
    /// </summary>
    [Fact]
    public void A_derivative_is_replaced_and_stays_one_row()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        var first = DurableArtifact.WriteText(
            context, meeting, ArtifactKind.Transcript, path, When, "the first rendering");

        // Read off the entity now: the second write updates this same row, so afterwards there is
        // no first hash left to compare against.
        var (id, hash) = (first.Id, first.Sha256);

        var second = DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.Transcript,
            path,
            When,
            "the second rendering, with the corrections applied");

        second.Id.ShouldBe(id);
        second.Sha256.ShouldNotBe(hash);
        context.Artifacts.Count().ShouldBe(1);
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, path).FullName)
            .ShouldBe("the second rendering, with the corrections applied");
        EveryRowReReads(context);
    }

    [Fact]
    public void An_empty_artifact_is_still_an_artifact()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        var artifact = DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.Summary,
            CorpusFiles.PathFor(meeting, "summary.md"),
            When,
            string.Empty);

        artifact.ByteSize.ShouldBe(0);
        artifact.Sha256.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        EveryRowReReads(context);
    }

    [Fact]
    public void What_is_recorded_is_the_hash_of_the_bytes_that_were_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "audio.wav");
        var audio = new byte[64 * 1024];
        Random.Shared.NextBytes(audio);

        var artifact = DurableArtifact.Write(
            context, meeting, ArtifactKind.Audio, path, When, stream => stream.Write(audio));

        artifact.ByteSize.ShouldBe(audio.Length);
        File.ReadAllBytes(CorpusFiles.Locate(corpus.Root, path).FullName).ShouldBe(audio);
        EveryRowReReads(context);
    }

    /// <summary>
    /// A set whose second file cannot even be written leaves the first one where it was, because
    /// steps one to four happen for every file before step five happens for any of them.
    /// </summary>
    /// <remarks>
    /// This is the half of the window that is the whole of it in practice. Putting a file in place
    /// is one rename; producing the next one is rendering it, writing it, flushing it to the disk,
    /// hashing it and reading it back — which is where a full disk, an I/O error or a caller that
    /// throws halfway actually lands. Written one whole write after the next, all of that happened
    /// with the first file already replaced and its row already saved.
    /// </remarks>
    [Fact]
    public void A_set_whose_second_file_cannot_be_written_leaves_the_first_one_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var transcript = CorpusFiles.PathFor(meeting, "transcript.md");
        var utterances = CorpusFiles.PathFor(meeting, "utterances.jsonl");

        DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the first rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":0}"));
        var recorded = Derived(context);

        // The second file addressed at another meeting's folder, which is refused where a file is
        // written rather than where one is put in place. Any refusal from steps one to four does
        // this; this one is the only one a test can ask for.
        Should.Throw<ArgumentException>(() => DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the second rendering"),
            (ArtifactKind.Utterances, CorpusFiles.PathFor(Guid.NewGuid(), "utterances.jsonl"), "{\"turn\":1}")));

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript).FullName).ShouldBe("the first rendering");
        Derived(context).ShouldBe(recorded);
        Files(corpus).ShouldBe([$"meetings/{meeting}/transcript.md", $"meetings/{meeting}/utterances.jsonl"]);
        EveryRowReReads(context);
    }

    /// <summary>
    /// A set whose second destination cannot be taken leaves the first one where it was, and the
    /// row still describing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the sequence, and the one the set exists for. The test above stops in
    /// staging, before anything could have moved; this one gets past staging with both files
    /// written and checked, and is refused at the replace — where, one whole write after another,
    /// the first file was already in place and its row already saved.
    /// </para>
    /// <para>
    /// A program holding the file open is what a sync client, an editor or a backup does to it, and
    /// it is the condition the set is emptied against: the destinations are renamed out of the way
    /// first, so the one that cannot be renamed refuses while the one that could is put back.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_set_whose_second_destination_cannot_be_taken_leaves_the_first_one_where_it_was()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var transcript = CorpusFiles.PathFor(meeting, "transcript.md");
        var utterances = CorpusFiles.PathFor(meeting, "utterances.jsonl");

        DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the first rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":0}"));
        var recorded = Derived(context);

        using (new FileStream(
            CorpusFiles.Locate(corpus.Root, utterances).FullName,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Should.Throw<IOException>(() => DurableArtifact.WriteAllText(
                context,
                meeting,
                When,
                (ArtifactKind.Transcript, transcript, "the second rendering"),
                (ArtifactKind.Utterances, utterances, "{\"turn\":1}")));
        }

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript).FullName).ShouldBe("the first rendering");
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, utterances).FullName).ShouldBe("{\"turn\":0}");
        Derived(context).ShouldBe(recorded);
        Files(corpus).ShouldBe([$"meetings/{meeting}/transcript.md", $"meetings/{meeting}/utterances.jsonl"]);
        EveryRowReReads(context);
    }

    /// <summary>
    /// A replace stopped in the middle of its moves leaves the copy it emptied out of the way, and
    /// a sweep finishes the tidy-up the replace did not reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state the naming exists for, reached rather than described. Standing in for the machine
    /// dying inside the run of renames is a folder where the second file goes: a directory is not a
    /// file, so nothing vacates it and the move meets it and is refused — after the first file has
    /// been emptied and moved, and before the tidy-up at the end that removes what was emptied.
    /// </para>
    /// <para>
    /// So a copy of the first file's old rendering is on disk under a name a sweep does not take on
    /// sight, which is the whole point of the naming: at no moment between the emptying and the
    /// move was it safe to take. What makes it safe now is on disk beside it — the destination it
    /// came out of, holding the second rendering — and nothing is ever coming back for the copy,
    /// because the only thing that would have is the put-back, which gives up before a move.
    /// </para>
    /// <para>
    /// The row is a generation behind both, since the save never ran, and that is the half of this
    /// state a sweep is not for: `check --verify-contents` is what reports it, and a rebuild is
    /// what settles it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_replace_that_stopped_partway_leaves_a_copy_the_sweep_finishes_with()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var transcript = CorpusFiles.PathFor(meeting, "transcript.md");
        var utterances = CorpusFiles.PathFor(meeting, "utterances.jsonl");

        DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the first rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":0}"));

        CorpusFiles.Locate(corpus.Root, utterances).Delete();
        Directory.CreateDirectory(CorpusFiles.Locate(corpus.Root, utterances).FullName);

        Should.Throw<UnauthorizedAccessException>(() => DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the second rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":1}")));

        // The first file was emptied and moved before the second was refused, so its new rendering
        // is in place and the copy of the old one is beside it.
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript).FullName).ShouldBe("the second rendering");
        var aside = Files(corpus).Where(CorpusFiles.IsSuperseded).ShouldHaveSingleItem();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, aside).FullName).ShouldBe("the first rendering");
        ArtifactReconciler.Check(context)
            .ShouldContain(finding =>
                finding.State == ArtifactState.Superseded && finding.RelativePath == aside);

        var swept = ArtifactReconciler.Sweep(context);
        swept.Removed.ShouldBe([aside]);
        swept.Left.ShouldBeEmpty();
        File.Exists(CorpusFiles.Locate(corpus.Root, aside).FullName).ShouldBeFalse();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript).FullName).ShouldBe("the second rendering");

        ArtifactReconciler.Check(context)
            .ShouldNotContain(finding => finding.State == ArtifactState.Superseded);
    }

    /// <summary>
    /// A destination somebody may delete and not write is still replaced, because what asks is the
    /// rename the replace itself performs and not a look at whether the file could be written.
    /// </summary>
    /// <remarks>
    /// The two questions come apart, and a corpus restored from a backup or living under a policy
    /// is where they do. Asked as "can this be opened for writing", a deny-write rule refuses every
    /// derived file of every meeting and a rebuild reports the disk as the problem; asked as the
    /// rename, it does not come up, because deleting is what a replace needs and deleting is
    /// allowed. Standing in for the rule here is a read-only file, which Windows refuses a write
    /// handle on and renames without complaint.
    /// </remarks>
    [Fact]
    public void A_destination_that_cannot_be_opened_for_writing_is_still_replaced()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var transcript = CorpusFiles.PathFor(meeting, "transcript.md");
        var utterances = CorpusFiles.PathFor(meeting, "utterances.jsonl");

        DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the first rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":0}"));

        var standing = CorpusFiles.Locate(corpus.Root, utterances);
        File.SetAttributes(standing.FullName, FileAttributes.ReadOnly);
        Should.Throw<UnauthorizedAccessException>(
            () => new FileStream(standing.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None));

        DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "the second rendering"),
            (ArtifactKind.Utterances, utterances, "{\"turn\":1}"));

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript).FullName).ShouldBe("the second rendering");
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, utterances).FullName).ShouldBe("{\"turn\":1}");
        EveryRowReReads(context);
    }

    /// <summary>
    /// One path named twice in a set is refused before anything moves, because the save that would
    /// have caught it comes after both files are already in place.
    /// </summary>
    [Fact]
    public void One_path_named_twice_in_a_set_is_refused_before_anything_moves()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var transcript = CorpusFiles.PathFor(meeting, "transcript.md");

        var refused = Should.Throw<ArtifactWriteException>(() => DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, transcript, "one rendering"),
            (ArtifactKind.Transcript, transcript, "another rendering")));

        refused.Message.ShouldContain("destination");
        Files(corpus).ShouldBeEmpty();
        context.Artifacts.ShouldBeEmpty();
        EveryRowReReads(context);
    }

    /// <summary>
    /// Two spellings of one path are one destination, so the same refusal reaches them: the guard
    /// asks which file a path resolves to and not which string it is.
    /// </summary>
    /// <remarks>
    /// The corpus is a folder on a Windows filesystem, where these two are one file. Compared
    /// exactly they are two, so both writes would go to one destination — the second over the first
    /// — and then both rows would reach a unique index that compares them the same exact way and
    /// takes them both. That is a corpus with two artifacts recorded over one file's bytes, which
    /// is the ordering the guard exists to make unreachable, arrived at through it.
    /// </remarks>
    [Fact]
    public void Two_spellings_of_one_destination_are_refused_the_same_way()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        var refused = Should.Throw<ArtifactWriteException>(() => DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, CorpusFiles.PathFor(meeting, "transcript.md"), "one rendering"),
            (ArtifactKind.Transcript, CorpusFiles.PathFor(meeting, "Transcript.md"), "another rendering")));

        refused.Message.ShouldContain("destination");
        Files(corpus).ShouldBeEmpty();
        context.Artifacts.ShouldBeEmpty();
        EveryRowReReads(context);
    }

    /// <summary>
    /// A set that goes in whole is one save, so the rows of a meeting's derived files arrive
    /// together in whatever unit of work the caller has and not one after the other.
    /// </summary>
    [Fact]
    public void A_set_put_in_place_records_every_row_it_wrote()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);

        var written = DurableArtifact.WriteAllText(
            context,
            meeting,
            When,
            (ArtifactKind.Transcript, CorpusFiles.PathFor(meeting, "transcript.md"), "# Reunión\n"),
            (ArtifactKind.Utterances, CorpusFiles.PathFor(meeting, "utterances.jsonl"), "{\"turn\":0}\n"));

        written.Select(artifact => artifact.Kind).ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances]);
        written.ShouldAllBe(artifact => artifact.Origin == ArtifactOrigin.Derived);
        context.Artifacts.Count().ShouldBe(2);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
        EveryRowReReads(context);
    }

    /// <summary>
    /// The stored path is what a backup walks and what the reconciler scans, and both start from
    /// the meeting's folder. A file written anywhere else has a row nothing will ever look at.
    /// </summary>
    [Theory]
    [InlineData("transcript.md")]
    [InlineData("meetings/transcript.md")]
    [InlineData("/meetings/{0}/transcript.md")]
    [InlineData("meetings/{0}/../../elsewhere.md")]
    [InlineData("meetings\\{0}\\transcript.md")]
    [InlineData("meetings/{0}/transcript.md.partial")]
    [InlineData("meetings/{0}/transcript.md.superseded")]
    public void A_path_that_is_not_this_meetings_is_refused(string shape)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();
        var meeting = Guid.NewGuid();
        var path = string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, meeting);

        Should.Throw<ArgumentException>(() => StagedArtifact.Stage(
            context, meeting, ArtifactKind.Transcript, path, stream => stream.WriteByte(0)));
        Files(corpus).ShouldBeEmpty();
    }

    [Fact]
    public void Another_meetings_folder_is_not_somewhere_this_meeting_may_write()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();
        var meeting = Guid.NewGuid();

        Should.Throw<ArgumentException>(() => StagedArtifact.Stage(
            context,
            meeting,
            ArtifactKind.Transcript,
            CorpusFiles.PathFor(Guid.NewGuid(), "transcript.md"),
            stream => stream.WriteByte(0)));
    }

    /// <summary>
    /// The invariant every cut above is measured against: a row always names a file that is there
    /// and reads back as what the row says it is.
    /// </summary>
    private static void EveryRowReReads(CorpusDbContext context)
    {
        foreach (var artifact in context.Artifacts.AsNoTracking().ToList())
        {
            var file = CorpusFiles.Locate(context.Root, artifact.RelativePath);

            file.Exists.ShouldBeTrue($"{artifact.RelativePath} has a row and no file");
            file.Length.ShouldBe(artifact.ByteSize, $"{artifact.RelativePath} is not the size its row says");
            CorpusFiles.Sha256Of(file).ShouldBe(artifact.Sha256, $"{artifact.RelativePath} is not what its row says");
        }
    }

    /// <summary>Every derived row, by what it says about the file it names.</summary>
    private static List<string> Derived(CorpusDbContext context) =>
    [
        .. context.Artifacts.AsNoTracking()
            .Where(artifact => artifact.Origin == ArtifactOrigin.Derived)
            .OrderBy(artifact => artifact.RelativePath)
            .AsEnumerable()
            .Select(artifact => $"{artifact.RelativePath}|{artifact.ByteSize}|{artifact.Sha256}"),
    ];

    /// <summary>Everything under the corpus root, as stored paths, in a stable order.</summary>
    private static List<string> Files(TemporaryCorpus corpus) =>
        [.. new[] { CorpusFiles.Meetings, CorpusFiles.Spool }
            .Select(folder => new DirectoryInfo(Path.Combine(corpus.Root.FullName, folder)))
            .Where(folder => folder.Exists)
            .SelectMany(folder => folder.EnumerateFiles("*", SearchOption.AllDirectories))
            .Select(file => CorpusFiles.RelativePathOf(corpus.Root, file))
            .Order(StringComparer.Ordinal)];

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
