using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// Table names, column names and stored enum values all come out of conventions, which means a
/// rename in C# silently changes what is on disk. These tests spell every one of them out, so
/// that rename shows up as a failure instead of as a corpus nobody can read.
/// </summary>
public class CorpusNamingTests
{
    [Theory]
    [InlineData(SourceProfile.Multichannel, "multichannel")]
    [InlineData(SourceProfile.Diarize, "diarize")]
    [InlineData(LifecycleState.Active, "active")]
    [InlineData(LifecycleState.Deleting, "deleting")]
    [InlineData(LifecycleState.Deleted, "deleted")]
    [InlineData(JobKind.Capture, "capture")]
    [InlineData(JobKind.Finalize, "finalize")]
    [InlineData(JobKind.Transcribe, "transcribe")]
    [InlineData(JobKind.Extract, "extract")]
    [InlineData(JobKind.Render, "render")]
    [InlineData(JobKind.Backup, "backup")]
    [InlineData(JobState.Pending, "pending")]
    [InlineData(JobState.Running, "running")]
    [InlineData(JobState.AwaitingUser, "awaiting_user")]
    [InlineData(JobState.Succeeded, "succeeded")]
    [InlineData(JobState.FailedRetryable, "failed_retryable")]
    [InlineData(JobState.FailedPermanent, "failed_permanent")]
    [InlineData(JobState.Cancelled, "cancelled")]
    [InlineData(ArtifactKind.SpoolBlock, "spool_block")]
    [InlineData(ArtifactKind.Audio, "audio")]
    [InlineData(ArtifactKind.DeepgramResponse, "deepgram_response")]
    [InlineData(ArtifactKind.Extraction, "extraction")]
    [InlineData(ArtifactKind.Manifest, "manifest")]
    [InlineData(ArtifactKind.Transcript, "transcript")]
    [InlineData(ArtifactKind.Utterances, "utterances")]
    [InlineData(ArtifactKind.Summary, "summary")]
    [InlineData(ArtifactOrigin.Source, "source")]
    [InlineData(ArtifactOrigin.Derived, "derived")]
    [InlineData(CaptureMode.ProcessLoopback, "process_loopback")]
    [InlineData(CaptureMode.FullLoopback, "full_loopback")]
    [InlineData(ActionItemState.Open, "open")]
    [InlineData(ActionItemState.Done, "done")]
    [InlineData(ActionItemState.Dropped, "dropped")]
    [InlineData(SpeakerAssignmentSource.Channel, "channel")]
    [InlineData(SpeakerAssignmentSource.Person, "person")]
    [InlineData(TerminologyMatchMode.Exact, "exact")]
    [InlineData(TerminologyMatchMode.IgnoreCase, "ignore_case")]
    [InlineData(AuditActor.User, "user")]
    [InlineData(AuditActor.App, "app")]
    [InlineData(AuditActor.Agent, "agent")]
    public void An_enum_is_stored_under_exactly_this_name(object value, string expected)
    {
        StoredName(value).ShouldBe(expected);
    }

    /// <summary>
    /// The profile is the one enum whose stored name is also published: it is what the provider
    /// is asked for. The convention has to keep agreeing with the domain's own mapping.
    /// </summary>
    [Theory]
    [InlineData(SourceProfile.Multichannel)]
    [InlineData(SourceProfile.Diarize)]
    public void The_source_profile_convention_agrees_with_the_domain(SourceProfile profile)
    {
        WireNames<SourceProfile>.Of(profile).ShouldBe(profile.ToWireName());
    }

    [Fact]
    public void A_meeting_is_stored_under_exactly_these_columns()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Sql.Strings(context, "SELECT name FROM pragma_table_info('meetings');").ShouldBe(
            [
                "id",
                "legacy_id",
                "project_id",
                "title",
                "started_at",
                "duration_ms",
                "source_profile",
                "language",
                "lifecycle_state",
                "created_at",
                "updated_at",
                "deleted_at",
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// Evidence is an owned type, so EF would prefix its columns with the navigation and call
    /// them Evidence_UtteranceOrdinal. They are the citation fields arquitectura.md §7.3 lists,
    /// named by the same pass as everything else — including any field added to a citation later.
    /// </summary>
    [Fact]
    public void Evidence_keeps_the_column_names_a_citation_is_defined_by()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        foreach (var table in new[] { "decisions", "action_items" })
        {
            var columns = Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');");

            columns.ShouldContain("utterance_ordinal");
            columns.ShouldContain("start_ms");
            columns.ShouldContain("end_ms");
            columns.ShouldContain("speaker_label");
            columns.ShouldContain("quoted_text");
            columns.ShouldContain("source_artifact_sha256");
            columns.ShouldNotContain(column => column.StartsWith("evidence", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The meeting half of a citation is the owner's own column, not a copy of it. Two would let a
    /// claim say it belongs to one meeting and cites a turn of another, and the pass that names
    /// columns is what would quietly produce the second one.
    /// </summary>
    [Fact]
    public void A_citation_shares_the_meeting_of_the_claim_it_belongs_to()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        foreach (var table in new[] { "decisions", "action_items" })
        {
            var columns = Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');");

            columns.Count(column => column == "meeting_id").ShouldBe(1, table);
        }
    }

    /// <summary>
    /// The convention covers every column of every table, so a property added anywhere lands in
    /// snake_case without anybody remembering to name it.
    /// </summary>
    [Fact]
    public void No_column_anywhere_keeps_its_csharp_casing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        // EF's own bookkeeping is out: the history table it was told to call schema_migrations,
        // and __EFMigrationsLock, which has no name to give it. Neither holds corpus data.
        var tables = Sql.Strings(
            context,
            $"""
            SELECT name FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name NOT LIKE '__EF%'
              AND name <> '{CorpusDatabase.MigrationsHistoryTable}';
            """);

        foreach (var table in tables)
        {
            foreach (var column in Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');"))
            {
                column.ShouldBe(column.ToLowerInvariant(), $"{table}.{column}");
            }
        }
    }

    /// <summary>Anything holding a Duration says so in the column name.</summary>
    [Fact]
    public void A_length_is_stored_in_a_column_that_names_its_unit()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Sql.Strings(context, "SELECT name FROM pragma_table_info('utterances');").ShouldContain("start_ms");
        Sql.Strings(context, "SELECT name FROM pragma_table_info('capture_runs');").ShouldContain("drift_ms");
        Sql.Strings(context, "SELECT name FROM pragma_table_info('meetings');").ShouldContain("duration_ms");
    }

    private static string StoredName(object value) => value switch
    {
        SourceProfile profile => WireNames<SourceProfile>.Of(profile),
        LifecycleState state => WireNames<LifecycleState>.Of(state),
        JobKind kind => WireNames<JobKind>.Of(kind),
        JobState state => WireNames<JobState>.Of(state),
        ArtifactKind kind => WireNames<ArtifactKind>.Of(kind),
        ArtifactOrigin origin => WireNames<ArtifactOrigin>.Of(origin),
        CaptureMode mode => WireNames<CaptureMode>.Of(mode),
        ActionItemState state => WireNames<ActionItemState>.Of(state),
        SpeakerAssignmentSource source => WireNames<SpeakerAssignmentSource>.Of(source),
        TerminologyMatchMode mode => WireNames<TerminologyMatchMode>.Of(mode),
        AuditActor actor => WireNames<AuditActor>.Of(actor),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Add the enum to this switch."),
    };
}
