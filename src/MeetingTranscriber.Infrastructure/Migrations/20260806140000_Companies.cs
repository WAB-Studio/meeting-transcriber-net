using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Gives projects and people a company. One person works across several of them, so it is a
    /// column and not a prefix on a project's name: search filters on the company alone, and a
    /// name reading 'Company / project' cannot be taken apart again.
    /// </summary>
    /// <remarks>
    /// Both links are nullable and both are SET NULL. Work belonging to nobody in particular is
    /// ordinary, and a company being deleted must not take the projects — and through them the
    /// meetings, which are the corpus — with it. The link is the recoverable half.
    /// </remarks>
    /// <inheritdoc />
    public partial class Companies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_name",
                table: "companies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_company_id",
                table: "projects",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_company_id",
                table: "people",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_companies_company_id",
                table: "projects",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_people_companies_company_id",
                table: "people",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_projects_companies_company_id",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_people_companies_company_id",
                table: "people");

            migrationBuilder.DropIndex(
                name: "ix_projects_company_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_people_company_id",
                table: "people");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "people");

            migrationBuilder.DropTable(
                name: "companies");
        }
    }
}
