using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MeetingsArchivedAndAudioRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "archived_at",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_removed_at",
                table: "meetings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "audio_removed_at",
                table: "meetings");
        }
    }
}
