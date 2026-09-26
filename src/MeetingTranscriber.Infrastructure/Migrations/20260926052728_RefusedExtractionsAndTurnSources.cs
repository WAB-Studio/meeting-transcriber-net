using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefusedExtractionsAndTurnSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.CreateTable(
                name: "extraction_refusals",
                columns: table => new
                {
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    condition = table.Column<string>(type: "TEXT", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    statement = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraction_refusals", x => new { x.extraction_run_id, x.ordinal });
                    table.CheckConstraint("ck_extraction_refusals_condition", "condition IN ('another_meeting', 'input_not_as_prepared', 'no_evidence', 'no_such_turn', 'not_the_schema', 'not_the_turn_cited', 'quote_not_in_the_turn', 'speaker_not_in_the_meeting')");
                    table.CheckConstraint("ck_extraction_refusals_ordinal", "ordinal >= 0");
                    table.ForeignKey(
                        name: "fk_extraction_refusals_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "turn_sources",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    response_artifact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    projected_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turn_sources", x => x.meeting_id);
                    table.ForeignKey(
                        name: "fk_turn_sources_artifacts_response_artifact_id",
                        column: x => x.response_artifact_id,
                        principalTable: "artifacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_turn_sources_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'extraction_refused', 'key_refused', 'no_key_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused')");

            migrationBuilder.CreateIndex(
                name: "ix_turn_sources_response_artifact_id",
                table: "turn_sources",
                column: "response_artifact_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extraction_refusals");

            migrationBuilder.DropTable(
                name: "turn_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'key_refused', 'no_key_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused')");
        }
    }
}
