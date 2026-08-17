using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OpenQuestionsAndDecisionPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_decisions_extraction_run_id",
                table: "decisions");

            migrationBuilder.AddColumn<int>(
                name: "ordinal",
                table: "decisions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "open_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    question = table.Column<string>(type: "TEXT", nullable: false),
                    utterance_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    start_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    end_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    speaker_label = table.Column<string>(type: "TEXT", nullable: false),
                    quoted_text = table.Column<string>(type: "TEXT", nullable: false),
                    source_artifact_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_questions", x => x.id);
                    table.CheckConstraint("ck_open_questions_evidence_span", "start_ms >= 0 AND end_ms >= start_ms");
                    table.CheckConstraint("ck_open_questions_ordinal", "ordinal >= 0");
                    table.ForeignKey(
                        name: "fk_open_questions_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_open_questions_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_open_questions_utterances_meeting_id_utterance_ordinal",
                        columns: x => new { x.meeting_id, x.utterance_ordinal },
                        principalTable: "utterances",
                        principalColumns: new[] { "meeting_id", "ordinal" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_extraction_run_id_ordinal",
                table: "decisions",
                columns: new[] { "extraction_run_id", "ordinal" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_decisions_ordinal",
                table: "decisions",
                sql: "ordinal >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_open_questions_extraction_run_id_ordinal",
                table: "open_questions",
                columns: new[] { "extraction_run_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_questions_meeting_id",
                table: "open_questions",
                column: "meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_questions_meeting_id_utterance_ordinal",
                table: "open_questions",
                columns: new[] { "meeting_id", "utterance_ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "open_questions");

            migrationBuilder.DropIndex(
                name: "ix_decisions_extraction_run_id_ordinal",
                table: "decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decisions_ordinal",
                table: "decisions");

            migrationBuilder.DropColumn(
                name: "ordinal",
                table: "decisions");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_extraction_run_id",
                table: "decisions",
                column: "extraction_run_id");
        }
    }
}
