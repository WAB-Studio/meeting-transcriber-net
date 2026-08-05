using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListingAndBackupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_meetings_lifecycle_state_started_at",
                table: "meetings",
                columns: new[] { "lifecycle_state", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_origin",
                table: "artifacts",
                column: "origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_meetings_lifecycle_state_started_at",
                table: "meetings");

            migrationBuilder.DropIndex(
                name: "ix_artifacts_origin",
                table: "artifacts");
        }
    }
}
