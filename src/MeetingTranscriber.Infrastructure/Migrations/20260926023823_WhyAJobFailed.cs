using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WhyAJobFailed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure",
                table: "processing_jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure",
                table: "processing_jobs",
                sql: "(state = 'failed_permanent') = (failure IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'key_refused', 'no_key_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure",
                table: "processing_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "failure",
                table: "processing_jobs");
        }
    }
}
