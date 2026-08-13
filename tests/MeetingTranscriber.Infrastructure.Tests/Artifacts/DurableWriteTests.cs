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
    [Fact]
    public void A_write_cut_before_it_is_put_in_place_is_an_unfinished_write_and_no_more()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var path = CorpusFiles.PathFor(meeting, "transcript.md");

        var staged = StagedArtifact.Stage(
            context, meeting, path, stream => stream.Write(Encoding.UTF8.GetBytes("a whole transcript")));

        staged.IsPending.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, path).Exists.ShouldBeFalse();
        context.Artifacts.ShouldBeEmpty();
        EveryRowReReads(context);

        var findings = ArtifactReconciler.Check(context);
        findings.Select(finding => finding.State).ShouldBe([ArtifactState.Unfinished]);
        ArtifactReconciler.Sweep(context).Count.ShouldBe(1);
        Files(corpus).ShouldBeEmpty();
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
            context, unrecorded, path, stream => stream.Write(Encoding.UTF8.GetBytes("{}")));

        Should.Throw<DbUpdateException>(() => staged.Commit(ArtifactKind.DeepgramResponse, When));

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
    public void A_path_that_is_not_this_meetings_is_refused(string shape)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();
        var meeting = Guid.NewGuid();
        var path = string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, meeting);

        Should.Throw<ArgumentException>(() => StagedArtifact.Stage(
            context, meeting, path, stream => stream.WriteByte(0)));
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
