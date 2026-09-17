using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChannelZeroHasNoDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_capture_runs_others_capture_mode",
                table: "capture_runs");

            migrationBuilder.DropColumn(
                name: "others_device_id",
                table: "capture_runs");

            migrationBuilder.DropColumn(
                name: "others_device_name",
                table: "capture_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_capture_runs_others_capture_mode",
                table: "capture_runs",
                sql: "others_capture_mode IN ('one_program', 'whole_machine')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_capture_runs_others_process",
                table: "capture_runs",
                sql: "(others_capture_mode = 'one_program') = (others_process IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_capture_runs_others_capture_mode",
                table: "capture_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_capture_runs_others_process",
                table: "capture_runs");

            migrationBuilder.AddColumn<string>(
                name: "others_device_id",
                table: "capture_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "others_device_name",
                table: "capture_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_capture_runs_others_capture_mode",
                table: "capture_runs",
                sql: "others_capture_mode IN ('full_loopback', 'process_loopback')");
        }
    }
}
