using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChannelThatStoppedFollowing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capture_source_changes",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    at = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<int>(type: "INTEGER", nullable: false),
                    heard = table.Column<string>(type: "TEXT", nullable: false),
                    was_hearing = table.Column<string>(type: "TEXT", nullable: false),
                    device_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capture_source_changes", x => new { x.meeting_id, x.at, x.channel });
                    table.CheckConstraint("ck_capture_source_changes_channel", "channel IN (0, 1)");
                    table.CheckConstraint("ck_capture_source_changes_device", "channel <> 0 OR device_id IS NULL");
                    table.ForeignKey(
                        name: "fk_capture_source_changes_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_source_changes");
        }
    }
}
