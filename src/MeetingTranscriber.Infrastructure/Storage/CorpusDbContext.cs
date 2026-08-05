using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The corpus. Rules the design leans on are declared as constraints rather than left to the
/// code that writes, because a rebuild, an import and a repair all write through here.
/// </summary>
public sealed class CorpusDbContext(DbContextOptions<CorpusDbContext> options) : DbContext(options)
{
    private static readonly string JobStates = WireNames<JobState>.AsSqlList();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<CaptureRun> CaptureRuns => Set<CaptureRun>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<TranscriptionRun> TranscriptionRuns => Set<TranscriptionRun>();

    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();

    public DbSet<Utterance> Utterances => Set<Utterance>();

    public DbSet<Summary> Summaries => Set<Summary>();

    public DbSet<Decision> Decisions => Set<Decision>();

    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();

    public DbSet<SpeakerAssignment> SpeakerAssignments => Set<SpeakerAssignment>();

    public DbSet<TerminologyCorrection> TerminologyCorrections => Set<TerminologyCorrection>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<UtcTimestamp>().HaveConversion<UtcTimestampConverter>();
        builder.Properties<Duration>().HaveConversion<DurationConverter>();
        builder.Properties<AudioChannel>().HaveConversion<AudioChannelConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHumanLayer(modelBuilder);
        ConfigureMeetings(modelBuilder);
        ConfigureRuns(modelBuilder);
        ConfigureProjections(modelBuilder);
        ConfigureAppState(modelBuilder);

        ApplyEnumNames(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
    }

    private static void ConfigureHumanLayer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("projects");
            project.HasKey(entity => entity.Id);
            project.HasIndex(entity => entity.Name).IsUnique();
        });

        modelBuilder.Entity<Person>(person =>
        {
            person.ToTable("people");
            person.HasKey(entity => entity.Id);
        });

        modelBuilder.Entity<MeetingParticipant>(participant =>
        {
            participant.ToTable("meeting_participants");
            participant.HasKey(entity => new { entity.MeetingId, entity.PersonId });
            participant.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            participant.HasOne<Person>().WithMany().HasForeignKey(entity => entity.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakerAssignment>(assignment =>
        {
            assignment.ToTable("speaker_assignments");
            assignment.HasKey(entity => new { entity.MeetingId, entity.SpeakerLabel });
            assignment.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            assignment.HasOne<Person>().WithMany().HasForeignKey(entity => entity.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TerminologyCorrection>(correction =>
        {
            correction.ToTable("terminology_corrections", table => table.HasCheckConstraint(
                "ck_terminology_corrections_scope",
                "project_id IS NULL OR meeting_id IS NULL"));
            correction.HasKey(entity => entity.Id);
            correction.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            correction.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureMeetings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meeting>(meeting =>
        {
            meeting.ToTable("meetings", table =>
            {
                table.HasCheckConstraint(
                    "ck_meetings_source_profile",
                    $"source_profile IN ({WireNames<SourceProfile>.AsSqlList()})");
                table.HasCheckConstraint(
                    "ck_meetings_lifecycle_state",
                    $"lifecycle_state IN ({WireNames<LifecycleState>.AsSqlList()})");
                table.HasCheckConstraint(
                    "ck_meetings_duration",
                    "duration_ms IS NULL OR duration_ms >= 0");
                // A meeting is either here without a deletion date, or on its way out with one.
                table.HasCheckConstraint(
                    "ck_meetings_deleted_at",
                    "(lifecycle_state = 'active') = (deleted_at IS NULL)");
            });

            meeting.HasKey(entity => entity.Id);
            meeting.HasIndex(entity => entity.LegacyId).IsUnique();
            meeting.HasIndex(entity => entity.StartedAt);
            // The list every window opens on: the meetings that are still here, newest first.
            // Equality column first, then the one being ordered on.
            meeting.HasIndex(entity => new { entity.LifecycleState, entity.StartedAt });
            meeting.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Artifact>(artifact =>
        {
            artifact.ToTable("artifacts", table =>
            {
                table.HasCheckConstraint("ck_artifacts_sha256", "length(sha256) = 64");
                table.HasCheckConstraint("ck_artifacts_byte_size", "byte_size >= 0");
                // The source line, in the schema rather than only in docs/corpus.md. Moving a
                // kind across it becomes a deliberate migration instead of a typo.
                table.HasCheckConstraint("ck_artifacts_origin", SourceLine());
            });

            artifact.HasKey(entity => entity.Id);
            artifact.HasIndex(entity => new { entity.MeetingId, entity.RelativePath }).IsUnique();
            artifact.HasIndex(entity => entity.Sha256);
            // Backup walks the sources and a rebuild walks the derivatives, both across the whole
            // corpus. Without this the column the policy hangs off is the one nothing can seek.
            artifact.HasIndex(entity => entity.Origin);
            artifact.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRuns(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CaptureRun>(run =>
        {
            run.ToTable("capture_runs", table => table.HasCheckConstraint(
                "ck_capture_runs_others_capture_mode",
                $"others_capture_mode IN ({WireNames<CaptureMode>.AsSqlList()})"));
            run.HasKey(entity => entity.Id);
            run.HasIndex(entity => new { entity.MeetingId, entity.StartedAt });
            run.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessingJob>(job =>
        {
            job.ToTable("processing_jobs", table =>
            {
                table.HasCheckConstraint("ck_processing_jobs_kind", $"kind IN ({WireNames<JobKind>.AsSqlList()})");
                table.HasCheckConstraint("ck_processing_jobs_state", $"state IN ({JobStates})");
                table.HasCheckConstraint("ck_processing_jobs_attempt", "attempt >= 0");
            });

            job.HasKey(entity => entity.Id);
            job.HasIndex(entity => new { entity.Kind, entity.IdempotencyKey }).IsUnique();
            job.HasIndex(entity => new { entity.State, entity.NextAttemptAt });
            job.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranscriptionRun>(run =>
        {
            run.ToTable("transcription_runs", table =>
            {
                table.HasCheckConstraint("ck_transcription_runs_state", $"state IN ({JobStates})");
                table.HasCheckConstraint(
                    "ck_transcription_runs_source_profile",
                    $"source_profile IN ({WireNames<SourceProfile>.AsSqlList()})");
                table.HasCheckConstraint("ck_transcription_runs_audio_sha256", "length(audio_sha256) = 64");
                table.HasCheckConstraint(
                    "ck_transcription_runs_cost",
                    "estimated_cost_micros IS NULL OR estimated_cost_micros >= 0");
            });

            run.HasKey(entity => entity.Id);
            // Not unique: the same audio under the same billable configuration is repeated by a
            // deliberate re-transcription, which carries its own cost approval.
            run.HasIndex(entity => new { entity.AudioSha256, entity.BillableConfigHash, entity.State });
            run.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            run.HasOne<ProcessingJob>().WithMany().HasForeignKey(entity => entity.JobId)
                .OnDelete(DeleteBehavior.SetNull);
            run.HasOne<Artifact>().WithMany().HasForeignKey(entity => entity.ResponseArtifactId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExtractionRun>(run =>
        {
            run.ToTable("extraction_runs", table =>
            {
                table.HasCheckConstraint("ck_extraction_runs_state", $"state IN ({JobStates})");
                table.HasCheckConstraint("ck_extraction_runs_input_hash", "length(input_hash) = 64");
                table.HasCheckConstraint(
                    "ck_extraction_runs_raw_output_hash",
                    "raw_output_hash IS NULL OR length(raw_output_hash) = 64");
            });

            run.HasKey(entity => entity.Id);
            run.HasIndex(entity => new { entity.MeetingId, entity.CreatedAt });
            run.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            run.HasOne<ProcessingJob>().WithMany().HasForeignKey(entity => entity.JobId)
                .OnDelete(DeleteBehavior.SetNull);
            run.HasOne<Artifact>().WithMany().HasForeignKey(entity => entity.OutputArtifactId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Utterance>(utterance =>
        {
            utterance.ToTable("utterances", table =>
            {
                // The channel contract, restated where the rows land.
                table.HasCheckConstraint("ck_utterances_channel", "channel IS NULL OR channel IN (0, 1)");
                table.HasCheckConstraint("ck_utterances_span", "start_ms >= 0 AND end_ms >= start_ms");
                table.HasCheckConstraint("ck_utterances_ordinal", "ordinal >= 0");
                table.HasCheckConstraint(
                    "ck_utterances_confidence",
                    "confidence IS NULL OR (confidence >= 0.0 AND confidence <= 1.0)");
            });

            utterance.HasKey(entity => entity.Id);
            utterance.HasIndex(entity => new { entity.MeetingId, entity.Ordinal }).IsUnique();
            utterance.HasIndex(entity => new { entity.MeetingId, entity.Start });
            utterance.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Summary>(summary =>
        {
            summary.ToTable("summaries");
            summary.HasKey(entity => entity.Id);
            summary.HasIndex(entity => new { entity.MeetingId, entity.ExtractionRunId }).IsUnique();
            summary.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            summary.HasOne<ExtractionRun>().WithMany().HasForeignKey(entity => entity.ExtractionRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Decision>(decision =>
        {
            decision.ToTable("decisions", table => table.HasCheckConstraint(
                "ck_decisions_evidence_span",
                "start_ms >= 0 AND end_ms >= start_ms"));
            decision.HasKey(entity => entity.Id);
            decision.HasIndex(entity => entity.MeetingId);
            decision.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            decision.HasOne<ExtractionRun>().WithMany().HasForeignKey(entity => entity.ExtractionRunId)
                .OnDelete(DeleteBehavior.Cascade);
            decision.HasOne<Person>().WithMany().HasForeignKey(entity => entity.DecidedByPersonId)
                .OnDelete(DeleteBehavior.SetNull);
            ConfigureEvidence(decision.OwnsOne(entity => entity.Evidence));
        });

        modelBuilder.Entity<ActionItem>(action =>
        {
            action.ToTable("action_items", table =>
            {
                table.HasCheckConstraint("ck_action_items_state", $"state IN ({WireNames<ActionItemState>.AsSqlList()})");
                table.HasCheckConstraint("ck_action_items_evidence_span", "start_ms >= 0 AND end_ms >= start_ms");
            });

            action.HasKey(entity => entity.Id);
            action.HasIndex(entity => entity.MeetingId);
            action.HasIndex(entity => new { entity.State, entity.DueDate });
            action.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            action.HasOne<ExtractionRun>().WithMany().HasForeignKey(entity => entity.ExtractionRunId)
                .OnDelete(DeleteBehavior.Cascade);
            action.HasOne<Person>().WithMany().HasForeignKey(entity => entity.OwnerPersonId)
                .OnDelete(DeleteBehavior.SetNull);
            ConfigureEvidence(action.OwnsOne(entity => entity.Evidence));
        });
    }

    private static void ConfigureAppState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Setting>(setting =>
        {
            setting.ToTable("settings");
            setting.HasKey(entity => entity.Key);
        });

        modelBuilder.Entity<AuditEvent>(auditEvent =>
        {
            auditEvent.ToTable("audit_events", table => table.HasCheckConstraint(
                "ck_audit_events_actor",
                $"actor IN ({WireNames<AuditActor>.AsSqlList()})"));
            auditEvent.HasKey(entity => entity.Id);
            auditEvent.Property(entity => entity.Id).ValueGeneratedOnAdd();
            auditEvent.HasIndex(entity => entity.OccurredAt);
            // An audit trail that disappears with what it audited is not an audit trail.
            auditEvent.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// Evidence sits in its owner's table, so a decision and its citation are one row and there
    /// is no way to write one without the other. Its columns are named by the same pass as every
    /// other column, which is what keeps a field added here from landing as evidence_whatever.
    /// </summary>
    private static void ConfigureEvidence<TOwner>(OwnedNavigationBuilder<TOwner, Citation> evidence)
        where TOwner : class
    {
        evidence.HasOne<Utterance>().WithMany().HasForeignKey(citation => citation.UtteranceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>The kind to origin mapping of <see cref="Artifacts"/>, as one CHECK.</summary>
    private static string SourceLine()
    {
        var sources = Quote(ArtifactOrigin.Source);
        var derived = Quote(ArtifactOrigin.Derived);
        return $"(origin = 'source' AND kind IN ({sources})) OR (origin = 'derived' AND kind IN ({derived}))";

        static string Quote(ArtifactOrigin origin) => string.Join(
            ", ",
            Enum.GetValues<ArtifactKind>()
                .Where(kind => kind.OriginOf() == origin)
                .Select(kind => $"'{WireNames<ArtifactKind>.Of(kind)}'")
                .Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Every enum other than the channel is stored under its name. Doing it in a pass rather
    /// than per property is what stops a new column from silently landing as an integer.
    /// </summary>
    private static void ApplyEnumNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!type.IsEnum || type == typeof(AudioChannel))
                {
                    continue;
                }

                var converter = (ValueConverter)Activator.CreateInstance(
                    typeof(EnumWireConverter<>).MakeGenericType(type))!;
                property.SetValueConverter(converter);
            }
        }
    }

    /// <summary>
    /// Columns and tables are snake_case, and anything holding a <see cref="Duration"/> ends in
    /// _ms so the unit is visible from a query rather than only from the type. Every property is
    /// named from its own name, including the ones an owned type contributes to its owner's
    /// table: EF would prefix those with the navigation, and no column here carries a prefix.
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // An owned type shares its owner's table and its key IS the owner's key column.
                // Renaming it here would split one row into two mismatched column names.
                if (entity.IsOwned() && property.IsPrimaryKey())
                {
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var suffix = type == typeof(Duration) ? "_ms" : string.Empty;
                property.SetColumnName(WireNames.ToSnakeCase(property.Name) + suffix);
            }

            // Constraint names are EF's own — IX_utterances_meeting_id_start_ms — over columns
            // this pass already renamed, so only the prefix needs bringing into line.
            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(index.GetDatabaseName()?.ToLowerInvariant());
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.GetName()?.ToLowerInvariant());
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(foreignKey.GetConstraintName()?.ToLowerInvariant());
            }
        }
    }
}
