using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SummaryFailuresSessionsAndCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.DropIndex(
                name: "ix_extraction_runs_job_id",
                table: "extraction_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_extraction_refusals_condition",
                table: "extraction_refusals");

            migrationBuilder.AddColumn<Guid>(
                name: "corrects_run_id",
                table: "extraction_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_id",
                table: "extraction_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'extraction_refused', 'key_refused', 'no_key_on_this_machine', 'no_summariser_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused', 'summariser_failed')");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_corrects_run_id",
                table: "extraction_runs",
                column: "corrects_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_extraction_runs_one_correction_per_job",
                table: "extraction_runs",
                column: "job_id",
                unique: true,
                filter: "corrects_run_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_extraction_refusals_condition",
                table: "extraction_refusals",
                sql: "condition IN ('another_meeting', 'cited_again_elsewhere', 'input_not_as_prepared', 'no_evidence', 'no_such_turn', 'not_the_schema', 'not_the_turn_cited', 'quote_not_in_the_turn', 'speaker_not_in_the_meeting')");

            migrationBuilder.AddForeignKey(
                name: "fk_extraction_runs_extraction_runs_corrects_run_id",
                table: "extraction_runs",
                column: "corrects_run_id",
                principalTable: "extraction_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_extraction_runs_extraction_runs_corrects_run_id",
                table: "extraction_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.DropIndex(
                name: "ix_extraction_runs_corrects_run_id",
                table: "extraction_runs");

            migrationBuilder.DropIndex(
                name: "ux_extraction_runs_one_correction_per_job",
                table: "extraction_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_extraction_refusals_condition",
                table: "extraction_refusals");

            migrationBuilder.DropColumn(
                name: "corrects_run_id",
                table: "extraction_runs");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "extraction_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'extraction_refused', 'key_refused', 'no_key_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused')");

            migrationBuilder.CreateIndex(
                name: "ix_extraction_runs_job_id",
                table: "extraction_runs",
                column: "job_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_extraction_refusals_condition",
                table: "extraction_refusals",
                sql: "condition IN ('another_meeting', 'input_not_as_prepared', 'no_evidence', 'no_such_turn', 'not_the_schema', 'not_the_turn_cited', 'quote_not_in_the_turn', 'speaker_not_in_the_meeting')");
        }
    }
}
