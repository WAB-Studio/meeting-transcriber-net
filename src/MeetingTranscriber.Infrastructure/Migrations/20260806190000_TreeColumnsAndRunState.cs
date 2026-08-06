using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Takes the second answer to "where does this run stand" out of the corpus, separates waiting
    /// for a person from having failed, and gives a node the columns its parent will be checked
    /// against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>transcription_runs.state</c> and <c>extraction_runs.state</c> are gone, and the job each
    /// run belongs to is now required. They held the same seven-state vocabulary as a job, with a
    /// public setter and no transitions at all, next to a job that already had both — two rows
    /// answering whether a transcription was charged for, and only one of them recovered after a
    /// restart.
    /// </para>
    /// <para>
    /// <c>processing_jobs.awaiting_reason</c> separates why a job is waiting for a person from how
    /// its last attempt failed. A cost still to approve was being written into the error column,
    /// and that column is the one a screen reads to say what happened.
    /// </para>
    /// <para>
    /// The rest is the groundwork for the migration after this one: a node carries a copy of its
    /// parent's class and depth, a person carries the class of their organization, and
    /// <c>nodes</c> gains the unique constraints those copies are checked against. The foreign
    /// keys themselves cannot be added here — SQLite rebuilds the table, and the new table would
    /// reference an old one that does not have those constraints yet.
    /// </para>
    /// <para>
    /// Nothing is preserved and nothing needs to be: no corpus has any of these tables populated.
    /// A run that did exist with no job would fail the new key rather than be given an invented
    /// one, which is the outcome to want — a paid call whose job nobody can name is not something
    /// to paper over.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class TreeColumnsAndRunState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_extraction_runs_processing_jobs_job_id",
                table: "extraction_runs");

            migrationBuilder.DropForeignKey(
                name: "fk_transcription_runs_processing_jobs_job_id",
                table: "transcription_runs");

            migrationBuilder.DropIndex(
                name: "ix_transcription_runs_audio_sha256_billable_config_hash_state",
                table: "transcription_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transcription_runs_state",
                table: "transcription_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_extraction_runs_state",
                table: "extraction_runs");

            migrationBuilder.DropColumn(
                name: "state",
                table: "transcription_runs");

            migrationBuilder.DropColumn(
                name: "state",
                table: "extraction_runs");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "transcription_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "awaiting_reason",
                table: "processing_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organization_kind",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parent_depth",
                table: "nodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_kind",
                table: "nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "extraction_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_nodes_id_kind",
                table: "nodes",
                columns: new[] { "id", "kind" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_nodes_id_kind_depth",
                table: "nodes",
                columns: new[] { "id", "kind", "depth" });

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_audio_sha256_billable_config_hash",
                table: "transcription_runs",
                columns: new[] { "audio_sha256", "billable_config_hash" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_awaiting_reason",
                table: "processing_jobs",
                sql: "(state = 'awaiting_user') = (awaiting_reason IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_people_organization",
                table: "people",
                sql: "(organization_id IS NULL) = (organization_kind IS NULL) AND (organization_kind IS NULL OR organization_kind = 'organization')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_nodes_child_depth",
                table: "nodes",
                sql: "parent_depth IS NULL OR depth = parent_depth + 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_nodes_parent",
                table: "nodes",
                sql: "(parent_id IS NULL) = (parent_kind IS NULL) AND (parent_id IS NULL) = (parent_depth IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_nodes_parent_kind",
                table: "nodes",
                sql: "(parent_kind IS NULL AND kind IN ('organization', 'initiative')) OR (parent_kind IS 'organization' AND kind = 'initiative') OR (parent_kind IS 'initiative' AND kind = 'topic')");

            migrationBuilder.AddForeignKey(
                name: "fk_extraction_runs_processing_jobs_job_id",
                table: "extraction_runs",
                column: "job_id",
                principalTable: "processing_jobs",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_transcription_runs_processing_jobs_job_id",
                table: "transcription_runs",
                column: "job_id",
                principalTable: "processing_jobs",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_extraction_runs_processing_jobs_job_id",
                table: "extraction_runs");

            migrationBuilder.DropForeignKey(
                name: "fk_transcription_runs_processing_jobs_job_id",
                table: "transcription_runs");

            migrationBuilder.DropIndex(
                name: "ix_transcription_runs_audio_sha256_billable_config_hash",
                table: "transcription_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_awaiting_reason",
                table: "processing_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_people_organization",
                table: "people");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_nodes_id_kind",
                table: "nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_nodes_id_kind_depth",
                table: "nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_nodes_child_depth",
                table: "nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_nodes_parent",
                table: "nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_nodes_parent_kind",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "awaiting_reason",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "organization_kind",
                table: "people");

            migrationBuilder.DropColumn(
                name: "parent_depth",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "parent_kind",
                table: "nodes");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "transcription_runs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "transcription_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_id",
                table: "extraction_runs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "extraction_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_transcription_runs_audio_sha256_billable_config_hash_state",
                table: "transcription_runs",
                columns: new[] { "audio_sha256", "billable_config_hash", "state" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_transcription_runs_state",
                table: "transcription_runs",
                sql: "state IN ('awaiting_user', 'cancelled', 'failed_permanent', 'failed_retryable', 'pending', 'running', 'succeeded')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_extraction_runs_state",
                table: "extraction_runs",
                sql: "state IN ('awaiting_user', 'cancelled', 'failed_permanent', 'failed_retryable', 'pending', 'running', 'succeeded')");

            migrationBuilder.AddForeignKey(
                name: "fk_extraction_runs_processing_jobs_job_id",
                table: "extraction_runs",
                column: "job_id",
                principalTable: "processing_jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_transcription_runs_processing_jobs_job_id",
                table: "transcription_runs",
                column: "job_id",
                principalTable: "processing_jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
