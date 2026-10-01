using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Takes <c>meetings.template_id</c> out. Nothing has written it since keeping a classification
    /// stopped stamping meetings (ISC-202.3), and nothing has shipped, so no data goes with it.
    /// Dropping it rebuilds <c>meetings</c>, which loses the table's search triggers and renumbers
    /// its rowids; the migration after this one puts both right.
    /// </summary>
    public partial class MeetingsForgetTheirTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_meetings_templates_template_id",
                table: "meetings");

            migrationBuilder.DropIndex(
                name: "ix_meetings_template_id",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "meetings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "template_id",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_template_id",
                table: "meetings",
                column: "template_id");

            migrationBuilder.AddForeignKey(
                name: "fk_meetings_templates_template_id",
                table: "meetings",
                column: "template_id",
                principalTable: "templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
