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
    // Named for what it is, and not JobStates: that is the domain type holding the transitions,
    // and a field shadowing it here would make the next reference to either one a coin toss.
    private static readonly string JobStateNames = WireNames<JobState>.AsSqlList();

    private static readonly string AwaitingUser = WireNames<JobState>.Of(JobState.AwaitingUser);

    private static readonly string Organization = WireNames<NodeKind>.Of(NodeKind.Organization);

    /// <summary>The one column name the model treats as a promise. See <see cref="SealCreatedAt"/>.</summary>
    private const string CreatedAtColumn = "created_at";

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

    public DbSet<ActionItemProgress> ActionItemProgress => Set<ActionItemProgress>();

    public DbSet<Node> Nodes => Set<Node>();

    public DbSet<MeetingNode> MeetingNodes => Set<MeetingNode>();

    public DbSet<MeetingTemplate> Templates => Set<MeetingTemplate>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Affiliation> Affiliations => Set<Affiliation>();

    public DbSet<MeetingPerson> MeetingPeople => Set<MeetingPerson>();

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
        SealCreatedAt(modelBuilder);
    }

    private static void ConfigureHumanLayer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Node>(node =>
        {
            node.ToTable("nodes", table =>
            {
                table.HasCheckConstraint("ck_nodes_kind", $"kind IN ({WireNames<NodeKind>.AsSqlList()})");
                table.HasCheckConstraint("ck_nodes_depth", $"depth BETWEEN 0 AND {Node.MaxDepth}");
                // A root is exactly what has no parent, and the copy of the parent it carries
                // arrives whole or not at all.
                table.HasCheckConstraint("ck_nodes_root", "(parent_id IS NULL) = (depth = 0)");
                table.HasCheckConstraint(
                    "ck_nodes_parent",
                    "(parent_id IS NULL) = (parent_kind IS NULL) AND (parent_id IS NULL) = (parent_depth IS NULL)");
                // Exactly one level below. The foreign key below is what makes parent_depth the
                // parent's own, so together the two say what neither can say alone.
                table.HasCheckConstraint(
                    "ck_nodes_child_depth",
                    "parent_depth IS NULL OR depth = parent_depth + 1");
                table.HasCheckConstraint("ck_nodes_parent_kind", ClassOrder());
            });

            node.HasKey(entity => entity.Id);
            // What a child's copy of its parent is checked against. Two of them: the tree needs
            // the depth as well, and a person's employer needs the class on its own.
            node.HasAlternateKey(entity => new { entity.Id, entity.Kind });
            node.HasAlternateKey(entity => new { entity.Id, entity.Kind, entity.Depth });
            node.HasIndex(entity => new { entity.ParentId, entity.Name }).IsUnique();
            // SQLite counts NULLs as distinct, so the index above lets two roots share a name.
            // This one is what actually stops a second 'TechSed' at the top of the tree.
            node.HasIndex(entity => entity.Name).IsUnique().HasFilter("parent_id IS NULL");
            // Deleting a node takes what hangs under it. A tree that outlives its root is the
            // kind of orphan nothing goes looking for.
            node.HasOne<Node>().WithMany()
                .HasForeignKey(entity => new { entity.ParentId, entity.ParentKind, entity.ParentDepth })
                .HasPrincipalKey(parent => new { parent.Id, parent.Kind, parent.Depth })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingNode>(link =>
        {
            link.ToTable("meeting_nodes", table => table.HasCheckConstraint(
                "ck_meeting_nodes_role",
                $"role IN ({WireNames<MeetingNodeRole>.AsSqlList()})"));

            // The role is part of the key: one meeting can be work of a node and also about it.
            link.HasKey(entity => new { entity.MeetingId, entity.NodeId, entity.Role });
            // Everything under this node, for a listing and for the terminology that applies.
            link.HasIndex(entity => entity.NodeId);
            link.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            link.HasOne<Node>().WithMany().HasForeignKey(entity => entity.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingTemplate>(template =>
        {
            template.ToTable("templates");
            template.HasKey(entity => entity.Id);
            template.HasIndex(entity => entity.Name).IsUnique();
        });

        modelBuilder.Entity<Person>(person =>
        {
            person.ToTable("people");
            person.HasKey(entity => entity.Id);
        });

        modelBuilder.Entity<Affiliation>(affiliation =>
        {
            affiliation.ToTable("affiliations", table =>
            {
                table.HasCheckConstraint("ck_affiliations_organization", $"organization_kind = '{Organization}'");
                table.HasCheckConstraint(
                    "ck_affiliations_period",
                    "started_at IS NULL OR ended_at IS NULL OR ended_at >= started_at");
            });

            affiliation.HasKey(entity => entity.Id);
            // Somebody can be at the same organization twice — they left and came back — so the
            // pair is not the key. What cannot happen is two of them open at once: that is the
            // same fact written twice, and nothing downstream could tell which one to close.
            affiliation.HasIndex(entity => new { entity.PersonId, entity.OrganizationId })
                .IsUnique()
                .HasFilter("ended_at IS NULL");
            affiliation.HasOne<Person>().WithMany().HasForeignKey(entity => entity.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
            // The class travels with the id, so somebody said to be at a project or a ticket is
            // refused by the key rather than by whoever wrote the insert. Losing an organization
            // takes the affiliations to it and leaves the people: they are in meetings, and a
            // meeting without the people on it is not repairable. Who is at this organization needs
            // no index of its own — this key brings one, and it leads with the id.
            affiliation.HasOne<Node>().WithMany()
                .HasForeignKey(entity => new { entity.OrganizationId, entity.OrganizationKind })
                .HasPrincipalKey(node => new { node.Id, node.Kind })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingPerson>(person =>
        {
            person.ToTable("meeting_people", table => table.HasCheckConstraint(
                "ck_meeting_people_role",
                $"role IN ({WireNames<MeetingPersonRole>.AsSqlList()})"));

            // The role is part of the key, as it is for a node: somebody's own one to one is a
            // meeting they attended and are the subject of, and one column made that a choice.
            person.HasKey(entity => new { entity.MeetingId, entity.PersonId, entity.Role });
            // Every meeting somebody is on. The key leads with the meeting, so searching by person
            // is the one direction it cannot serve.
            person.HasIndex(entity => entity.PersonId);
            person.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            person.HasOne<Person>().WithMany().HasForeignKey(entity => entity.PersonId)
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

        modelBuilder.Entity<ActionItemProgress>(progress =>
        {
            progress.ToTable("action_item_progress", table =>
            {
                table.HasCheckConstraint(
                    "ck_action_item_progress_state",
                    $"state IN ({WireNames<ActionItemState>.AsSqlList()})");
                table.HasCheckConstraint("ck_action_item_progress_ordinal", "ordinal >= 0");
            });

            // The key is the extraction and the position in it, not an action's id: a rebuild
            // mints new ids, and this row has to still find its action afterwards.
            progress.HasKey(entity => new { entity.ExtractionRunId, entity.Ordinal });
            // What is still open, across the corpus. The other half of that listing — when it is
            // due — is a column of the projection, so the query joins and no one index serves it.
            progress.HasIndex(entity => entity.State);
            progress.HasOne<ExtractionRun>().WithMany().HasForeignKey(entity => entity.ExtractionRunId)
                .OnDelete(DeleteBehavior.Cascade);
            progress.HasOne<Person>().WithMany().HasForeignKey(entity => entity.OwnerPersonId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TerminologyCorrection>(correction =>
        {
            correction.ToTable("terminology_corrections", table => table.HasCheckConstraint(
                "ck_terminology_corrections_scope",
                "node_id IS NULL OR meeting_id IS NULL"));
            correction.HasKey(entity => entity.Id);
            correction.HasOne<Node>().WithMany().HasForeignKey(entity => entity.NodeId)
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
            meeting.HasIndex(entity => entity.StartedAt);
            // The list every window opens on: the meetings that are still here, newest first.
            // Equality column first, then the one being ordered on.
            meeting.HasIndex(entity => new { entity.LifecycleState, entity.StartedAt });
            meeting.HasOne<MeetingTemplate>().WithMany().HasForeignKey(entity => entity.TemplateId)
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
                table.HasCheckConstraint("ck_processing_jobs_state", $"state IN ({JobStateNames})");
                table.HasCheckConstraint("ck_processing_jobs_attempt", "attempt >= 0");
                // Waiting for a person and having failed are different things, and the column
                // somebody opens to find out what happened has to keep saying which.
                table.HasCheckConstraint(
                    "ck_processing_jobs_awaiting_reason",
                    $"(state = '{AwaitingUser}') = (awaiting_reason IS NOT NULL)");
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
            // deliberate re-transcription, which carries its own cost approval. Where each of
            // them stands is one join away, on the job, which is the only row that holds it.
            run.HasIndex(entity => new { entity.AudioSha256, entity.BillableConfigHash });
            run.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            ConfigureRunJob(run);
            run.HasOne<Artifact>().WithMany().HasForeignKey(entity => entity.ResponseArtifactId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExtractionRun>(run =>
        {
            run.ToTable("extraction_runs", table =>
            {
                table.HasCheckConstraint("ck_extraction_runs_input_hash", "length(input_hash) = 64");
                table.HasCheckConstraint(
                    "ck_extraction_runs_raw_output_hash",
                    "raw_output_hash IS NULL OR length(raw_output_hash) = 64");
            });

            run.HasKey(entity => entity.Id);
            run.HasIndex(entity => new { entity.MeetingId, entity.CreatedAt });
            run.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            ConfigureRunJob(run);
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
            // An alternate key rather than a unique index because it is what citations point at,
            // and SQLite will only accept a foreign key onto columns declared unique in the table.
            utterance.HasAlternateKey(entity => new { entity.MeetingId, entity.Ordinal });
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
                table.HasCheckConstraint("ck_action_items_ordinal", "ordinal >= 0");
                table.HasCheckConstraint("ck_action_items_evidence_span", "start_ms >= 0 AND end_ms >= start_ms");
            });

            action.HasKey(entity => entity.Id);
            action.HasIndex(entity => entity.MeetingId);
            action.HasIndex(entity => entity.DueDate);
            // Unique because it is what a person's state is pinned to. Two actions sharing a
            // position in one extraction would make that state ambiguous rather than wrong,
            // which is the harder kind of bug to see.
            action.HasIndex(entity => new { entity.ExtractionRunId, entity.Ordinal }).IsUnique();
            action.HasOne<Meeting>().WithMany().HasForeignKey(entity => entity.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            action.HasOne<ExtractionRun>().WithMany().HasForeignKey(entity => entity.ExtractionRunId)
                .OnDelete(DeleteBehavior.Cascade);
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
    /// <remarks>
    /// <para>
    /// The turn is named by the meeting and its position, not by its id, so the reference still
    /// resolves after a rebuild has thrown every turn away and projected them again. The meeting
    /// half is the owner's own <c>meeting_id</c> column, written once and read by both: a claim
    /// citing a turn of another meeting has nowhere to put the other meeting's id.
    /// </para>
    /// <para>
    /// The turn is referenced without a delete action on purpose. Cascading here made deleting
    /// utterances take every decision and action citing them, silently — and a rebuild starts by
    /// deleting utterances. NO ACTION is checked at the end of the statement, so deleting a
    /// meeting still works: the claims go in the same statement as the turns they cite. Deleting
    /// turns on their own now fails instead, which is what a projection deleted out of order is.
    /// </para>
    /// </remarks>
    private static void ConfigureEvidence<TOwner>(OwnedNavigationBuilder<TOwner, Citation> evidence)
        where TOwner : class
    {
        evidence.HasOne<Utterance>().WithMany()
            .HasForeignKey(citation => new { citation.MeetingId, citation.UtteranceOrdinal })
            .HasPrincipalKey(utterance => new { utterance.MeetingId, utterance.Ordinal })
            .OnDelete(DeleteBehavior.NoAction);
    }

    /// <summary>
    /// The job a run hangs off: required, because the job is the only row that says where the run
    /// stands, and NO ACTION so a job cannot be deleted out from under a call somebody paid for.
    /// Deleting the meeting still works — SQLite checks an immediate constraint at the end of the
    /// statement, and the cascade takes the job and the run in the same one.
    /// </summary>
    private static void ConfigureRunJob<TRun>(EntityTypeBuilder<TRun> run)
        where TRun : class =>
        run.HasOne<ProcessingJob>().WithMany()
            .HasForeignKey(nameof(TranscriptionRun.JobId))
            .OnDelete(DeleteBehavior.NoAction);

    /// <summary>
    /// Which class of node can sit under which, as one CHECK over the class a child carries of its
    /// parent. With the foreign key making that copy the parent's own, the two together say the
    /// tree goes organization, initiative, topic — and that an organization is always a root and a
    /// topic never one.
    /// </summary>
    /// <remarks>
    /// Every comparison against <c>parent_kind</c> is <c>IS</c> and not <c>=</c>, because a CHECK
    /// only fails on FALSE: a root reaches <c>'x' = NULL</c>, which is NULL, and a constraint that
    /// evaluates to NULL lets the row through. That is how a topic stood as a root against a
    /// constraint written to forbid exactly that.
    /// </remarks>
    private static string ClassOrder()
    {
        var kinds = Enum.GetValues<NodeKind>().Order().ToArray();

        // A root has no parent to be checked against, so which classes may be one is said here.
        var roots = kinds.Where(Node.CanBeRoot).Select(Quoted);
        var under = kinds
            .Where(parent => Node.Holds(parent) is not null)
            .Select(parent => $"(parent_kind IS {Quoted(parent)} AND kind = {Quoted(Node.Holds(parent)!.Value)})");

        return $"(parent_kind IS NULL AND kind IN ({string.Join(", ", roots)})) OR {string.Join(" OR ", under)}";

        static string Quoted(NodeKind kind) => $"'{WireNames<NodeKind>.Of(kind)}'";
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
    /// A column called <c>created_at</c> says when its row was created, so nothing may write one
    /// over a row that is already there. Declared on the model rather than left to each writer:
    /// the two that broke it — a speaker a person reassigned, an artifact a rerender replaced —
    /// were both keeping an honest timestamp under a name that promised another one, and neither
    /// read as wrong at the call site. A row that wants "when what it holds now was settled" gets
    /// a column that says so, and this is what makes that a compile-time-visible decision instead
    /// of a habit.
    /// </summary>
    /// <remarks>
    /// Runs after the naming pass, because the rule is about the name on disk and not about the
    /// property: <c>CreatedAt</c> is only the usual way to arrive at <c>created_at</c>.
    /// </remarks>
    private static void SealCreatedAt(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.GetColumnName() == CreatedAtColumn)
                {
                    property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
                }
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
