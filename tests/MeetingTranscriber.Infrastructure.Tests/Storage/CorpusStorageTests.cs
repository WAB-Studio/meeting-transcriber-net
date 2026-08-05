using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// What the domain types actually become on disk. Reading the columns back as raw values is the
/// point: a round trip through EF alone would pass even if both directions were wrong.
/// </summary>
public class CorpusStorageTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 5, 14, 30, 12, 345, TimeSpan.Zero));

    [Fact]
    public void A_meeting_lands_in_the_columns_the_contract_promises()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        context.Meetings.Add(NewMeeting());
        context.SaveChanges();

        Sql.Scalar(context, "SELECT started_at FROM meetings;").ShouldBe("2026-08-05T14:30:12.345Z");
        Sql.Scalar(context, "SELECT duration_ms FROM meetings;").ShouldBe(7_200_000L);
        Sql.Scalar(context, "SELECT source_profile FROM meetings;").ShouldBe("multichannel");
        Sql.Scalar(context, "SELECT lifecycle_state FROM meetings;").ShouldBe("active");
    }

    [Fact]
    public void A_meeting_comes_back_as_the_types_it_went_in_as()
    {
        using var corpus = new TemporaryCorpus();
        var id = Guid.NewGuid();

        using (var writing = corpus.OpenMigrated())
        {
            var meeting = NewMeeting();
            meeting.Id = id;
            writing.Meetings.Add(meeting);
            writing.SaveChanges();
        }

        using var reading = corpus.Open();
        var stored = reading.Meetings.Single(meeting => meeting.Id == id);

        stored.StartedAt.ShouldBe(When);
        stored.Duration.ShouldBe(Duration.FromMilliseconds(7_200_000));
        stored.SourceProfile.ShouldBe(SourceProfile.Multichannel);
        stored.LifecycleState.ShouldBe(LifecycleState.Active);
    }

    [Fact]
    public void A_turn_stores_its_channel_as_the_number_the_contract_fixes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var meeting = NewMeeting();
        context.Meetings.Add(meeting);
        context.Utterances.Add(NewUtterance(meeting.Id, ordinal: 0, AudioChannel.Others));
        context.Utterances.Add(NewUtterance(meeting.Id, ordinal: 1, AudioChannel.Me));
        context.Utterances.Add(NewUtterance(meeting.Id, ordinal: 2, channel: null));
        context.SaveChanges();

        Sql.Scalar(context, "SELECT channel FROM utterances WHERE ordinal = 0;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT channel FROM utterances WHERE ordinal = 1;").ShouldBe(1L);
        Sql.Scalar(context, "SELECT channel FROM utterances WHERE ordinal = 2;").ShouldBe(DBNull.Value);
    }

    [Fact]
    public void A_turn_stores_its_offsets_as_whole_milliseconds()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var meeting = NewMeeting();
        context.Meetings.Add(meeting);
        context.Utterances.Add(NewUtterance(meeting.Id, ordinal: 0, AudioChannel.Others));
        context.SaveChanges();

        Sql.Scalar(context, "SELECT start_ms FROM utterances;").ShouldBe(1_500L);
        Sql.Scalar(context, "SELECT end_ms FROM utterances;").ShouldBe(4_250L);
    }

    private static Meeting NewMeeting() => new()
    {
        Id = Guid.NewGuid(),
        Language = "es",
        StartedAt = When,
        Duration = Duration.FromMilliseconds(7_200_000),
        SourceProfile = SourceProfile.Multichannel,
        CreatedAt = When,
        UpdatedAt = When,
    };

    private static Utterance NewUtterance(Guid meetingId, int ordinal, AudioChannel? channel) => new()
    {
        Id = Guid.NewGuid(),
        MeetingId = meetingId,
        Ordinal = ordinal,
        Start = Duration.FromMilliseconds(1_500),
        End = Duration.FromMilliseconds(4_250),
        Channel = channel,
        SpeakerLabel = channel is null ? "speaker_0" : $"channel_{(int)channel}",
        Text = "the drift budget is fifty milliseconds",
    };
}
