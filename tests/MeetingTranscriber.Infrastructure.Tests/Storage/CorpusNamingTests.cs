using System.Text.RegularExpressions;

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
public partial class CorpusNamingTests
{
    /// <summary>
    /// Every stored value, spelled out. It is also read back by
    /// <see cref="Every_enum_the_model_stores_is_spelled_out_here"/>, so an enum the model persists
    /// and this list has not heard of fails rather than going uncovered — which is how the
    /// classification, the newest and least settled of them, was the one missing.
    /// </summary>
    private static readonly (object Value, string Stored)[] Names =
    [
        (SourceProfile.Multichannel, "multichannel"),
        (SourceProfile.Diarize, "diarize"),
        (LifecycleState.Active, "active"),
        (LifecycleState.Deleting, "deleting"),
        (LifecycleState.Deleted, "deleted"),
        (JobKind.Capture, "capture"),
        (JobKind.Finalize, "finalize"),
        (JobKind.Transcribe, "transcribe"),
        (JobKind.Extract, "extract"),
        (JobKind.Render, "render"),
        (JobKind.Backup, "backup"),
        (JobState.Pending, "pending"),
        (JobState.Running, "running"),
        (JobState.AwaitingUser, "awaiting_user"),
        (JobState.Succeeded, "succeeded"),
        (JobState.FailedRetryable, "failed_retryable"),
        (JobState.FailedPermanent, "failed_permanent"),
        (JobState.Cancelled, "cancelled"),
        (ArtifactKind.SpoolBlock, "spool_block"),
        (ArtifactKind.Audio, "audio"),
        (ArtifactKind.DeepgramResponse, "deepgram_response"),
        (ArtifactKind.Extraction, "extraction"),
        (ArtifactKind.Manifest, "manifest"),
        (ArtifactKind.Transcript, "transcript"),
        (ArtifactKind.Utterances, "utterances"),
        (ArtifactKind.Summary, "summary"),
        (ArtifactOrigin.Source, "source"),
        (ArtifactOrigin.Derived, "derived"),
        (CaptureMode.ProcessLoopback, "process_loopback"),
        (CaptureMode.FullLoopback, "full_loopback"),
        (ActionItemState.Open, "open"),
        (ActionItemState.Done, "done"),
        (ActionItemState.Dropped, "dropped"),
        (SpeakerAssignmentSource.Channel, "channel"),
        (SpeakerAssignmentSource.Person, "person"),
        (TerminologyMatchMode.Exact, "exact"),
        (TerminologyMatchMode.IgnoreCase, "ignore_case"),
        (AuditActor.User, "user"),
        (AuditActor.App, "app"),
        (AuditActor.Agent, "agent"),

        // The classification vocabulary, closed against the thirteen meetings arquitectura.md §5.3
        // lists. A rename changes what is on disk and the CHECK behind it at the same time, and
        // neither the migration nor a test elsewhere would notice.
        (NodeKind.Organization, "organization"),
        (NodeKind.Initiative, "initiative"),
        (NodeKind.Topic, "topic"),
        (MeetingNodeRole.WorkOf, "work_of"),
        (MeetingNodeRole.Counterpart, "counterpart"),
        (MeetingNodeRole.About, "about"),
        (MeetingPersonRole.Attended, "attended"),
        (MeetingPersonRole.Subject, "subject"),
    ];

    public static TheoryData<object, string> StoredNames
    {
        get
        {
            var data = new TheoryData<object, string>();
            foreach (var (value, stored) in Names)
            {
                data.Add(value, stored);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StoredNames))]
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
                "title",
                "context",
                "template_id",
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
    /// Which enums to spell out is read off the model rather than remembered. The list above was
    /// hand-written, so what it missed was whatever went in last — and what went in last was the
    /// classification, the one vocabulary that had not been settled yet.
    /// </summary>
    [Fact]
    public void Every_enum_the_model_stores_is_spelled_out_here()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.Open();

        var covered = Names.Select(name => name.Value).ToHashSet();
        var stored = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType)
            .Where(type => type.IsEnum)
            // The channel is the one enum stored as its number, because that number is the
            // contract: it is what the provider answers with. It has its own tests.
            .Where(type => type != typeof(AudioChannel))
            .Distinct();

        foreach (var type in stored)
        {
            foreach (var value in Enum.GetValues(type))
            {
                covered.ShouldContain(value, $"{type.Name}.{value} is stored and spelled out nowhere");
            }
        }
    }

    /// <summary>
    /// The convention covers every column of every table, so a property added anywhere lands in
    /// snake_case without anybody remembering to name it. Checked against the convention and not
    /// against lowercase: <c>awaitingreason</c> is lowercase too, and it is not what is on disk.
    /// </summary>
    [Fact]
    public void Every_column_anywhere_is_named_by_the_convention()
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
            SnakeCase().IsMatch(table).ShouldBeTrue(table);
            foreach (var column in Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');"))
            {
                SnakeCase().IsMatch(column).ShouldBeTrue($"{table}.{column}");
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

    /// <summary>
    /// Lowercase words joined by single underscores, which is what the naming pass produces —
    /// unlike "not uppercase", which <c>awaitingreason</c> also satisfies.
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex SnakeCase();

    private static string StoredName(object value) => value switch
    {
        NodeKind kind => WireNames<NodeKind>.Of(kind),
        MeetingNodeRole role => WireNames<MeetingNodeRole>.Of(role),
        MeetingPersonRole role => WireNames<MeetingPersonRole>.Of(role),
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
