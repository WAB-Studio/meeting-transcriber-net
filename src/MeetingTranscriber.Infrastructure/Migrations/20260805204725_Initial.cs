using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_me = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    legacy_id = table.Column<string>(type: "TEXT", nullable: true),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    started_at = table.Column<string>(type: "TEXT", nullable: false),
                    duration_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    source_profile = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    lifecycle_state = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meetings", x => x.id);
                    table.CheckConstraint("ck_meetings_deleted_at", "(lifecycle_state = 'active') = (deleted_at IS NULL)");
                    table.CheckConstraint("ck_meetings_duration", "duration_ms IS NULL OR duration_ms >= 0");
                    table.CheckConstraint("ck_meetings_lifecycle_state", "lifecycle_state IN ('active', 'deleted', 'deleting')");
                    table.CheckConstraint("ck_meetings_source_profile", "source_profile IN ('diarize', 'multichannel')");
                    table.ForeignKey(
                        name: "fk_meetings_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    origin = table.Column<string>(type: "TEXT", nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", nullable: false),
                    byte_size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artifacts", x => x.id);
                    table.CheckConstraint("ck_artifacts_byte_size", "byte_size >= 0");
                    table.CheckConstraint("ck_artifacts_origin", "(origin = 'source' AND kind IN ('audio', 'deepgram_response', 'extraction', 'manifest', 'spool_block')) OR (origin = 'derived' AND kind IN ('summary', 'transcript', 'utterances'))");
                    table.CheckConstraint("ck_artifacts_sha256", "length(sha256) = 64");
                    table.ForeignKey(
                        name: "fk_artifacts_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    actor = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_actor", "actor IN ('agent', 'app', 'user')");
                    table.ForeignKey(
                        name: "fk_audit_events_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "capture_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    started_at = table.Column<string>(type: "TEXT", nullable: false),
                    finished_at = table.Column<string>(type: "TEXT", nullable: true),
                    others_device_id = table.Column<string>(type: "TEXT", nullable: true),
                    others_device_name = table.Column<string>(type: "TEXT", nullable: true),
                    others_capture_mode = table.Column<string>(type: "TEXT", nullable: false),
                    others_process = table.Column<string>(type: "TEXT", nullable: true),
                    me_device_id = table.Column<string>(type: "TEXT", nullable: true),
                    me_device_name = table.Column<string>(type: "TEXT", nullable: true),
                    sample_rate = table.Column<int>(type: "INTEGER", nullable: false),
                    channel_count = table.Column<int>(type: "INTEGER", nullable: false),
                    bits_per_sample = table.Column<int>(type: "INTEGER", nullable: false),
                    drift_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    recovered = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capture_runs", x => x.id);
                    table.CheckConstraint("ck_capture_runs_others_capture_mode", "others_capture_mode IN ('full_loopback', 'process_loopback')");
                    table.ForeignKey(
                        name: "fk_capture_runs_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_participants",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_participants", x => new { x.meeting_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_meeting_participants_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_participants_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<string>(type: "TEXT", nullable: true),
                    finished_at = table.Column<string>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    next_attempt_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.id);
                    table.CheckConstraint("ck_processing_jobs_attempt", "attempt >= 0");
                    table.CheckConstraint("ck_processing_jobs_kind", "kind IN ('backup', 'capture', 'extract', 'finalize', 'render', 'transcribe')");
                    table.CheckConstraint("ck_processing_jobs_state", "state IN ('awaiting_user', 'cancelled', 'failed_permanent', 'failed_retryable', 'pending', 'running', 'succeeded')");
                    table.ForeignKey(
                        name: "fk_processing_jobs_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "speaker_assignments",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    speaker_label = table.Column<string>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assigned_by = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_speaker_assignments", x => new { x.meeting_id, x.speaker_label });
                    table.ForeignKey(
                        name: "fk_speaker_assignments_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_speaker_assignments_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "terminology_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    wrong_text = table.Column<string>(type: "TEXT", nullable: false),
                    correct_text = table.Column<string>(type: "TEXT", nullable: false),
                    match_mode = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminology_corrections", x => x.id);
                    table.CheckConstraint("ck_terminology_corrections_scope", "project_id IS NULL OR meeting_id IS NULL");
                    table.ForeignKey(
                        name: "fk_terminology_corrections_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_terminology_corrections_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utterances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    start_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    end_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    channel = table.Column<int>(type: "INTEGER", nullable: true),
                    speaker_label = table.Column<string>(type: "TEXT", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    confidence = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utterances", x => x.id);
                    table.CheckConstraint("ck_utterances_channel", "channel IS NULL OR channel IN (0, 1)");
                    table.CheckConstraint("ck_utterances_confidence", "confidence IS NULL OR (confidence >= 0.0 AND confidence <= 1.0)");
                    table.CheckConstraint("ck_utterances_ordinal", "ordinal >= 0");
                    table.CheckConstraint("ck_utterances_span", "start_ms >= 0 AND end_ms >= start_ms");
                    table.ForeignKey(
                        name: "fk_utterances_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extraction_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    job_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    provider = table.Column<string>(type: "TEXT", nullable: false),
                    provider_version = table.Column<string>(type: "TEXT", nullable: true),
                    model = table.Column<string>(type: "TEXT", nullable: true),
                    prompt_version = table.Column<string>(type: "TEXT", nullable: false),
                    schema_version = table.Column<string>(type: "TEXT", nullable: false),
                    input_hash = table.Column<string>(type: "TEXT", nullable: false),
                    raw_output_hash = table.Column<string>(type: "TEXT", nullable: true),
                    output_artifact_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    accepted_at = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraction_runs", x => x.id);
                    table.CheckConstraint("ck_extraction_runs_input_hash", "length(input_hash) = 64");
                    table.CheckConstraint("ck_extraction_runs_raw_output_hash", "raw_output_hash IS NULL OR length(raw_output_hash) = 64");
                    table.CheckConstraint("ck_extraction_runs_state", "state IN ('awaiting_user', 'cancelled', 'failed_permanent', 'failed_retryable', 'pending', 'running', 'succeeded')");
                    table.ForeignKey(
                        name: "fk_extraction_runs_artifacts_output_artifact_id",
                        column: x => x.output_artifact_id,
                        principalTable: "artifacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_extraction_runs_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_extraction_runs_processing_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "transcription_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    job_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    provider = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: true),
                    source_profile = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    audio_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    billable_config_hash = table.Column<string>(type: "TEXT", nullable: false),
                    estimated_cost_micros = table.Column<long>(type: "INTEGER", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: true),
                    price_table_version = table.Column<string>(type: "TEXT", nullable: true),
                    approved_at = table.Column<string>(type: "TEXT", nullable: true),
                    response_artifact_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    finished_at = table.Column<string>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transcription_runs", x => x.id);
                    table.CheckConstraint("ck_transcription_runs_audio_sha256", "length(audio_sha256) = 64");
                    table.CheckConstraint("ck_transcription_runs_cost", "estimated_cost_micros IS NULL OR estimated_cost_micros >= 0");
                    table.CheckConstraint("ck_transcription_runs_source_profile", "source_profile IN ('diarize', 'multichannel')");
                    table.CheckConstraint("ck_transcription_runs_state", "state IN ('awaiting_user', 'cancelled', 'failed_permanent', 'failed_retryable', 'pending', 'running', 'succeeded')");
                    table.ForeignKey(
                        name: "fk_transcription_runs_artifacts_response_artifact_id",
                        column: x => x.response_artifact_id,
                        principalTable: "artifacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_transcription_runs_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transcription_runs_processing_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "action_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    statement = table.Column<string>(type: "TEXT", nullable: false),
                    owner_person_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    utterance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    start_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    end_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    speaker_label = table.Column<string>(type: "TEXT", nullable: false),
                    quoted_text = table.Column<string>(type: "TEXT", nullable: false),
                    source_artifact_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_items", x => x.id);
                    table.CheckConstraint("ck_action_items_evidence_span", "start_ms >= 0 AND end_ms >= start_ms");
                    table.CheckConstraint("ck_action_items_state", "state IN ('done', 'dropped', 'open')");
                    table.ForeignKey(
                        name: "fk_action_items_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_action_items_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_action_items_people_owner_person_id",
                        column: x => x.owner_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_action_items_utterances_utterance_id",
                        column: x => x.utterance_id,
                        principalTable: "utterances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    statement = table.Column<string>(type: "TEXT", nullable: false),
                    decided_by_person_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    utterance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    start_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    end_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    speaker_label = table.Column<string>(type: "TEXT", nullable: false),
                    quoted_text = table.Column<string>(type: "TEXT", nullable: false),
                    source_artifact_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisions", x => x.id);
                    table.CheckConstraint("ck_decisions_evidence_span", "start_ms >= 0 AND end_ms >= start_ms");
                    table.ForeignKey(
                        name: "fk_decisions_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_decisions_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_decisions_people_decided_by_person_id",
                        column: x => x.decided_by_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_decisions_utterances_utterance_id",
                        column: x => x.utterance_id,
                        principalTable: "utterances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    @abstract = table.Column<string>(name: "abstract", type: "TEXT", nullable: true),
                    body = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_summaries_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_summaries_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_items_extraction_run_id",
                table: "action_items",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_meeting_id",
                table: "action_items",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_owner_person_id",
                table: "action_items",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_state_due_date",
                table: "action_items",
                columns: new[] { "state", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_action_items_utterance_id",
                table: "action_items",
                column: "utterance_id");

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_meeting_id_relative_path",
                table: "artifacts",
                columns: new[] { "meeting_id", "relative_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_sha256",
                table: "artifacts",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_meeting_id",
                table: "audit_events",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                table: "audit_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_capture_runs_meeting_id_started_at",
                table: "capture_runs",
                columns: new[] { "meeting_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_decided_by_person_id",
                table: "decisions",
                column: "decided_by_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_extraction_run_id",
                table: "decisions",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_meeting_id",
                table: "decisions",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_utterance_id",
                table: "decisions",
                column: "utterance_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_job_id",
                table: "extraction_runs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_meeting_id_created_at",
                table: "extraction_runs",
                columns: new[] { "meeting_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_output_artifact_id",
                table: "extraction_runs",
                column: "output_artifact_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_participants_person_id",
                table: "meeting_participants",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_meetings_legacy_id",
                table: "meetings",
                column: "legacy_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_project_id",
                table: "meetings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_meetings_started_at",
                table: "meetings",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_kind_idempotency_key",
                table: "processing_jobs",
                columns: new[] { "kind", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_meeting_id",
                table: "processing_jobs",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_state_next_attempt_at",
                table: "processing_jobs",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_name",
                table: "projects",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_speaker_assignments_person_id",
                table: "speaker_assignments",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_summaries_extraction_run_id",
                table: "summaries",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_summaries_meeting_id_extraction_run_id",
                table: "summaries",
                columns: new[] { "meeting_id", "extraction_run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminology_corrections_meeting_id",
                table: "terminology_corrections",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminology_corrections_project_id",
                table: "terminology_corrections",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_audio_sha256_billable_config_hash_state",
                table: "transcription_runs",
                columns: new[] { "audio_sha256", "billable_config_hash", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_job_id",
                table: "transcription_runs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_meeting_id",
                table: "transcription_runs",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_response_artifact_id",
                table: "transcription_runs",
                column: "response_artifact_id");

            migrationBuilder.CreateIndex(
                name: "ix_utterances_meeting_id_ordinal",
                table: "utterances",
                columns: new[] { "meeting_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_utterances_meeting_id_start_ms",
                table: "utterances",
                columns: new[] { "meeting_id", "start_ms" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_items");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "capture_runs");

            migrationBuilder.DropTable(
                name: "decisions");

            migrationBuilder.DropTable(
                name: "meeting_participants");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "speaker_assignments");

            migrationBuilder.DropTable(
                name: "summaries");

            migrationBuilder.DropTable(
                name: "terminology_corrections");

            migrationBuilder.DropTable(
                name: "transcription_runs");

            migrationBuilder.DropTable(
                name: "utterances");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "extraction_runs");

            migrationBuilder.DropTable(
                name: "artifacts");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "meetings");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
