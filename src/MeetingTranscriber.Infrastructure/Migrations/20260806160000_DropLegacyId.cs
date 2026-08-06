using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Takes the Python corpus's identifier out of <c>meetings</c>. It was a column of the
    /// application carried for a tool that is deleted the day the last old corpus is read, and a
    /// unique constraint the application would have had to keep meaning something forever.
    /// </summary>
    /// <remarks>
    /// What it was for still works. The import matches a meeting on the SHA-256 of the response
    /// it was transcribed from, which is already stored and already indexed — a stronger answer
    /// than a folder name, because it does not care what the folder was called. Where a meeting
    /// came from is recorded as an audit event, which is where provenance belongs.
    /// </remarks>
    /// <inheritdoc />
    public partial class DropLegacyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_meetings_legacy_id",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "legacy_id",
                table: "meetings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legacy_id",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_legacy_id",
                table: "meetings",
                column: "legacy_id",
                unique: true);
        }
    }
}
