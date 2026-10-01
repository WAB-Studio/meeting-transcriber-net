using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// ISC-157.1's corpus half: what somebody settled about a recording that ends comes back the same
/// after the application was closed and reopened, and a corpus that cannot say answers the one way
/// that spends nothing.
/// </summary>
/// <remarks>
/// Against a corpus on disk and read back through a second connection, for the reason
/// <c>MeetingWorkTests</c> gives: what is claimed is that the answer survives the process, and an
/// object still sitting in a context's tracker proves none of it.
/// </remarks>
public class CorpusSettingsTests
{
    private static readonly UtcTimestamp Chosen =
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 16, 10, 14, 0, TimeSpan.Zero));

    [Fact]
    public void A_corpus_nobody_has_answered_for_does_nothing_when_a_recording_ends()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusSettings(context).WhenARecordingEnds().ShouldBe(AfterARecording.DoNothing);
    }

    [Theory]
    [InlineData(AfterARecording.DoNothing)]
    [InlineData(AfterARecording.Transcribe)]
    [InlineData(AfterARecording.TranscribeAndSummarise)]
    public void What_was_settled_is_what_comes_back(AfterARecording settled)
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            new CorpusSettings(writing).WhenARecordingEnds(settled, Chosen);
        }

        using var reopened = corpus.Open();
        new CorpusSettings(reopened).WhenARecordingEnds().ShouldBe(settled);
    }

    [Fact]
    public void Answering_again_replaces_the_answer_rather_than_adding_one()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            var settings = new CorpusSettings(writing);
            settings.WhenARecordingEnds(AfterARecording.TranscribeAndSummarise, Chosen);
            settings.WhenARecordingEnds(AfterARecording.Transcribe, Chosen + Duration.FromSeconds(30));
        }

        using var reopened = corpus.Open();

        // One row, and it is the second answer. A preference is not a history: two rows under one
        // key is not something `settings` can hold, and a reader that found the first would have
        // this application transcribing something nobody asked it to.
        var stored = reopened.Settings.Single();
        stored.Key.ShouldBe(CorpusSettings.AfterARecordingKey);
        stored.UpdatedAt.ShouldBe(Chosen + Duration.FromSeconds(30));

        new CorpusSettings(reopened).WhenARecordingEnds().ShouldBe(AfterARecording.Transcribe);
    }

    [Fact]
    public void A_settings_row_holding_something_this_build_cannot_read_does_nothing()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            writing.Settings.Add(new Setting
            {
                Key = CorpusSettings.AfterARecordingKey,
                Value = "transcribe_and_send_it_to_everybody",
                UpdatedAt = Chosen,
            });

            writing.SaveChanges();
        }

        using var reopened = corpus.Open();

        // Answered and not thrown, and the answer is the one that spends nothing. A build that
        // threw here would take the settings screen down over a row, and one that guessed would
        // spend the user's own Deepgram credit on a word it did not understand.
        new CorpusSettings(reopened).WhenARecordingEnds().ShouldBe(AfterARecording.DoNothing);
    }

    /// <summary>
    /// The three names this preference is stored under, spelled out.
    /// </summary>
    /// <remarks>
    /// Here and not in the naming suite, and that is deliberate twice over.
    /// <c>CorpusNamingTests.Every_enum_the_model_stores_is_spelled_out_here</c> sweeps
    /// <c>context.Model.GetEntityTypes()</c>, and this is not one of them — it is a string in
    /// <c>Setting.Value</c>, so that sweep would never have reached these three whatever else was
    /// true. And the file it lives in belongs to another piece of work this batch.
    /// <para>
    /// Spelled out because <c>WireNames</c> derives them from the member names: renaming a member of
    /// <see cref="AfterARecording"/> silently changes what is on disk, and a corpus answered before
    /// the rename would come back reading as <see cref="AfterARecording.DoNothing"/> — which is the
    /// preference quietly forgetting itself rather than failing.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(AfterARecording.DoNothing, "do_nothing")]
    [InlineData(AfterARecording.Transcribe, "transcribe")]
    [InlineData(AfterARecording.TranscribeAndSummarise, "transcribe_and_summarise")]
    public void The_names_on_disk(AfterARecording settled, string stored)
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            new CorpusSettings(writing).WhenARecordingEnds(settled, Chosen);
        }

        using var reopened = corpus.Open();
        reopened.Settings.Single().Value.ShouldBe(stored);
    }

    [Fact]
    public void A_corpus_nobody_has_exported_says_so()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusSettings(context).LastExportMade().ShouldBeNull();
    }

    [Fact]
    public void The_last_export_comes_back_after_the_corpus_is_reopened()
    {
        using var corpus = new TemporaryCorpus();
        var made = new LastExport(Chosen, [ExportKind.Audio, ExportKind.HandCorrections], 3, @"C:somewheremeeting-transcriber-export-20260916-101400");

        using (var writing = corpus.OpenMigrated())
        {
            var settings = new CorpusSettings(writing);
            settings.Exported(made with { Meetings = 1 });
            settings.Exported(made);
        }

        using var reopened = corpus.Open();

        // One row, rewritten: what the screen says is the last export and not a history.
        reopened.Settings.Single().Key.ShouldBe(CorpusSettings.LastExportKey);
        var last = new CorpusSettings(reopened).LastExportMade().ShouldNotBeNull();
        last.At.ShouldBe(Chosen);
        last.Kinds.ShouldBe(made.Kinds);
        last.Meetings.ShouldBe(3);
        last.Folder.ShouldBe(made.Folder);
    }

    /// <summary>
    /// The four names an export kind is stored under, spelled out, for the reason the names above
    /// are: the kinds are stored only inside the <c>last-export</c> value, so the sweep in
    /// <c>CorpusNamingTests</c> never reaches them.
    /// </summary>
    [Fact]
    public void Each_kind_an_export_can_carry_is_stored_under_exactly_this_name()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            new CorpusSettings(writing).Exported(new LastExport(
                Chosen,
                [ExportKind.Audio, ExportKind.Transcripts, ExportKind.Summaries, ExportKind.HandCorrections],
                0,
                "folder"));
        }

        using var reopened = corpus.Open();
        using var stored = System.Text.Json.JsonDocument.Parse(reopened.Settings.Single().Value);

        stored.RootElement.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()).ToArray()
            .ShouldBe(["audio", "transcripts", "summaries", "hand_corrections"]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"kinds\":[\"tapes\"],\"meetings\":1,\"folder\":\"x\"}")]
    [InlineData("{\"kinds\":[\"audio\"],\"meetings\":\"many\",\"folder\":\"x\"}")]
    public void A_last_export_this_build_cannot_read_reads_as_none(string stored)
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            writing.Settings.Add(new Setting { Key = CorpusSettings.LastExportKey, Value = stored, UpdatedAt = Chosen });
            writing.SaveChanges();
        }

        using var reopened = corpus.Open();
        new CorpusSettings(reopened).LastExportMade().ShouldBeNull();
    }

    [Fact]
    public void A_word_said_to_be_right_comes_back_after_the_corpus_is_reopened()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            new CorpusSettings(writing).SayItIsRight("Dipgram", Chosen);
        }

        using var reopened = corpus.Open();
        new CorpusSettings(reopened).WordsSaidRight().ShouldBe(["dipgram"]);
    }

    [Fact]
    public void Nobody_having_said_anything_is_right_reads_as_none()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        new CorpusSettings(context).WordsSaidRight().ShouldBeEmpty();
    }

    [Fact]
    public void Saying_a_word_is_right_twice_keeps_it_once()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            var settings = new CorpusSettings(writing);
            settings.SayItIsRight("Dipgram", Chosen);
            settings.SayItIsRight("dipgram", Chosen + Duration.FromSeconds(30));
            settings.SayItIsRight("Nubeco", Chosen + Duration.FromSeconds(60));
        }

        using var reopened = corpus.Open();
        new CorpusSettings(reopened).WordsSaidRight().ShouldBe(["dipgram", "nubeco"]);
        reopened.Settings.Count(row => row.Key == CorpusSettings.WordsSaidRightKey).ShouldBe(1);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[1, 2]")]
    [InlineData("[null]")]
    public void A_words_said_right_row_this_build_cannot_read_reads_as_none(string stored)
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            writing.Settings.Add(new Setting { Key = CorpusSettings.WordsSaidRightKey, Value = stored, UpdatedAt = Chosen });
            writing.SaveChanges();
        }

        using var reopened = corpus.Open();
        new CorpusSettings(reopened).WordsSaidRight().ShouldBeEmpty();
    }

    [Fact]
    public void The_words_said_right_are_stored_under_exactly_this_name()
    {
        CorpusSettings.WordsSaidRightKey.ShouldBe("words-said-right");

        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            new CorpusSettings(writing).SayItIsRight("dipgram", Chosen);
        }

        using var reopened = corpus.Open();
        reopened.Settings.Single().Key.ShouldBe("words-said-right");
    }
}
