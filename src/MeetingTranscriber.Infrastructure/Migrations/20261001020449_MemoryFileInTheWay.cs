using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MemoryFileInTheWay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'extraction_refused', 'key_refused', 'memory_file_in_the_way', 'no_key_on_this_machine', 'no_summariser_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused', 'summariser_failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_processing_jobs_failure_name",
                table: "processing_jobs",
                sql: "failure IS NULL OR failure IN ('audio_missing', 'corpus_refused', 'extraction_refused', 'key_refused', 'no_key_on_this_machine', 'no_summariser_on_this_machine', 'out_of_credit', 'over_its_rate', 'provider_not_reached', 'request_refused', 'summariser_failed')");
        }
    }
}
