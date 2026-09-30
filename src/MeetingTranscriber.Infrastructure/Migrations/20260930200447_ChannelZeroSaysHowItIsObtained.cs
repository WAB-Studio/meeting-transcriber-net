using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChannelZeroSaysHowItIsObtained : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No fill for a channel 0 row written before the mode existed: nothing has shipped, so
            // a corpus holding one is a developer's and is rebuilt (docs/migrations.md, first section).
            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "capture_source_changes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_capture_source_changes_mode",
                table: "capture_source_changes",
                sql: "(channel = 0) = (mode IS NOT NULL) AND (mode IS NULL OR mode IN ('one_program', 'whole_machine'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_capture_source_changes_mode",
                table: "capture_source_changes");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "capture_source_changes");
        }
    }
}
