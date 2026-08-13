using System.Text;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Artifacts;

/// <summary>
/// Putting back what the corpus records and the disk has lost, and everything that is not that.
/// </summary>
/// <remarks>
/// The bytes offered always live at the corpus root rather than under <c>meetings/</c>, which is
/// where somebody's backup of them actually is: the reconciler walks the two artifact folders and
/// nothing else, so a copy sitting beside the database is invisible to it and cannot be what makes
/// these assertions pass.
/// </remarks>
public class ArtifactRestoreTests
{
    private const string Paid = "{\"results\":\"what was said\"}";

    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 13, 11, 0, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp Later =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// The whole point: a paid response the corpus claims and does not have is there again, and the
    /// check that reported it has nothing left to say.
    /// </summary>
    [Fact]
    public void A_source_the_disk_lost_comes_back_from_the_bytes_its_row_records()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var response = Response(context, corpus, meeting);
        var kept = Kept(corpus, "backup.json", Paid);

        CorpusFiles.Locate(corpus.Root, response.RelativePath).Delete();
        ArtifactReconciler.Check(context).ShouldHaveSingleItem()
            .State.ShouldBe(ArtifactState.Missing);

        var restored = ArtifactRestore.Restore(context, kept, Later);

        restored.Sha256.ShouldBe(response.Sha256);
        restored.PutBack.ShouldBe([response.RelativePath]);
        restored.AlreadyThere.ShouldBeEmpty();
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    /// <summary>
    /// The row is not rewritten into something else on the way. It already describes these bytes —
    /// that is what found it — so the only thing a restore may move is when the file that is there
    /// now was last confirmed, which is what that column says.
    /// </summary>
    [Fact]
    public void The_row_comes_out_saying_what_it_said_but_confirmed_again()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var response = Response(context, corpus, meeting);
        var kept = Kept(corpus, "backup.json", Paid);
        CorpusFiles.Locate(corpus.Root, response.RelativePath).Delete();

        ArtifactRestore.Restore(context, kept, Later);

        var row = context.Artifacts.Single();
        row.Id.ShouldBe(response.Id);
        row.Kind.ShouldBe(ArtifactKind.DeepgramResponse);
        row.Origin.ShouldBe(ArtifactOrigin.Source);
        row.Sha256.ShouldBe(response.Sha256);
        row.ByteSize.ShouldBe(response.ByteSize);
        row.ConfirmedAt.ShouldBe(Later);
    }

    /// <summary>
    /// The refusal the whole design turns on. Bytes reach an artifact's path only where the corpus
    /// already says those exact bytes are, so there is no file a person can hand over that ends up
    /// under a paid response's row.
    /// </summary>
    [Fact]
    public void Bytes_no_row_of_this_corpus_describes_are_refused_and_nothing_is_written()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var response = Response(context, corpus, meeting);
        var stranger = Kept(corpus, "elsewhere.json", "{\"results\":\"another meeting entirely\"}");
        CorpusFiles.Locate(corpus.Root, response.RelativePath).Delete();

        var refused = Should.Throw<ArtifactRestoreException>(
            () => ArtifactRestore.Restore(context, stranger, Later));

        refused.Message.ShouldContain(stranger.FullName);
        CorpusFiles.Locate(corpus.Root, response.RelativePath).Exists.ShouldBeFalse();
        ArtifactReconciler.Check(context).ShouldHaveSingleItem()
            .State.ShouldBe(ArtifactState.Missing);
    }

    /// <summary>
    /// The sharper half of the same rule: bytes this corpus does know, offered while a different
    /// row is the one missing. There is no meeting and no path on the way in, so the only thing
    /// that can decide where they land is the corpus — and it puts them nowhere, because the row
    /// they belong under already has its file.
    /// </summary>
    [Fact]
    public void Bytes_the_corpus_records_elsewhere_do_not_land_where_another_row_is_missing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var gone = Response(context, corpus, Recorded(context));
        var other = Response(context, corpus, Recorded(context), "{\"results\":\"a different meeting\"}");
        var kept = Kept(corpus, "backup.json", "{\"results\":\"a different meeting\"}");

        CorpusFiles.Locate(corpus.Root, gone.RelativePath).Delete();

        var restored = ArtifactRestore.Restore(context, kept, Later);

        restored.PutBack.ShouldBeEmpty();
        restored.AlreadyThere.ShouldBe([other.RelativePath]);
        CorpusFiles.Locate(corpus.Root, gone.RelativePath).Exists.ShouldBeFalse();
        context.Artifacts.Single(row => row.Id == gone.Id).Sha256.ShouldBe(gone.Sha256);
    }

    /// <summary>
    /// Running it twice is the ordinary way this is used, and the second run is not an error. A file
    /// that is there is also where this stops: what is at that path may be damaged rather than
    /// right, and deciding to destroy it is a person's, so the path is named and left alone.
    /// </summary>
    [Fact]
    public void A_path_that_has_a_file_is_named_and_not_written_over()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context);
        var response = Response(context, corpus, meeting);
        var kept = Kept(corpus, "backup.json", Paid);

        var damaged = CorpusFiles.Locate(corpus.Root, response.RelativePath);
        File.WriteAllText(damaged.FullName, "half of a copy that stopped", new UTF8Encoding(false));

        var restored = ArtifactRestore.Restore(context, kept, Later);

        restored.PutBack.ShouldBeEmpty();
        restored.AlreadyThere.ShouldBe([response.RelativePath]);
        File.ReadAllText(damaged.FullName).ShouldBe("half of a copy that stopped");
    }

    /// <summary>
    /// Two rows carrying the same bytes are the same artifact twice, so there is nothing to choose
    /// between them and both are put back.
    /// </summary>
    [Fact]
    public void Bytes_two_rows_record_go_back_under_both_of_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var first = Response(context, corpus, Recorded(context));
        var second = Response(context, corpus, Recorded(context));
        var kept = Kept(corpus, "backup.json", Paid);

        CorpusFiles.Locate(corpus.Root, first.RelativePath).Delete();
        CorpusFiles.Locate(corpus.Root, second.RelativePath).Delete();

        var restored = ArtifactRestore.Restore(context, kept, Later);

        restored.PutBack.Count.ShouldBe(2);
        restored.PutBack.ShouldContain(first.RelativePath);
        restored.PutBack.ShouldContain(second.RelativePath);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_there_is_not_something_to_put_back()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var absent = new FileInfo(Path.Combine(corpus.Root.FullName, "nothing.json"));

        Should.Throw<ArtifactRestoreException>(
            () => ArtifactRestore.Restore(context, absent, Later))
            .Message.ShouldContain(absent.FullName);
    }

    /// <summary>Somebody's copy of the bytes, kept where no scan of the corpus reaches it.</summary>
    private static FileInfo Kept(TemporaryCorpus corpus, string name, string text)
    {
        var file = new FileInfo(Path.Combine(corpus.Root.FullName, name));
        File.WriteAllText(file.FullName, text, new UTF8Encoding(false));
        return file;
    }

    private static Artifact Response(
        CorpusDbContext context,
        TemporaryCorpus corpus,
        Guid meeting,
        string? paid = null) =>
        DurableArtifact.WriteText(
            context,
            meeting,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting, "deepgram.json"),
            When,
            paid ?? Paid);

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
