using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Replaces the one company and one project a meeting could have with a tree of nodes and as
    /// many links to it as the meeting needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A meeting covering two projects is ordinary and had nowhere to go; so did a meeting held
    /// with a client before any project existed, and one whose other side is a company that is
    /// not mine. All three are the same shape: a meeting relates to several things, and not all
    /// of them the same way. The link carries which way.
    /// </para>
    /// <para>
    /// The tree is capped at three levels. It is a classification somebody keeps in their head,
    /// not a filesystem, and with the cap everything under a node is two joins away — no
    /// recursive query, no materialised path to keep in step with the parents.
    /// </para>
    /// <para>
    /// The names of the kinds and the roles are provisional and stored under a CHECK, so changing
    /// one is another migration. That is deliberate: they are values on disk, not labels in an
    /// interface.
    /// </para>
    /// <para>
    /// <b>It drops the companies and the projects instead of moving them, on purpose.</b> Those
    /// rows are human layer — approved by a person, inferable from no artifact, recoverable from
    /// no backup of files — so the choice was not a free one. It is safe because there is no corpus
    /// to lose: the application has never shipped, and the only route into those tables was an
    /// import that now writes the tree directly. Translating them would have been code nothing
    /// could exercise against real rows, standing exactly where a person would trust it most.
    /// </para>
    /// <para>
    /// That reasoning expires the day this application writes a corpus somebody keeps, and the day
    /// it does is the day this note stops applying to migrations written after it.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class Classification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_meetings_projects_project_id", table: "meetings");
            migrationBuilder.DropForeignKey(name: "fk_people_companies_company_id", table: "people");
            migrationBuilder.DropForeignKey(
                name: "fk_terminology_corrections_projects_project_id",
                table: "terminology_corrections");

            migrationBuilder.DropTable(name: "projects");
            migrationBuilder.DropTable(name: "companies");

            migrationBuilder.DropIndex(name: "ix_meetings_project_id", table: "meetings");
            migrationBuilder.DropIndex(name: "ix_people_company_id", table: "people");
            migrationBuilder.DropIndex(
                name: "ix_terminology_corrections_project_id",
                table: "terminology_corrections");

            migrationBuilder.DropColumn(name: "project_id", table: "meetings");
            migrationBuilder.DropColumn(name: "company_id", table: "people");
            migrationBuilder.DropColumn(name: "project_id", table: "terminology_corrections");

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    parent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    depth = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nodes", x => x.id);
                    table.CheckConstraint("ck_nodes_depth", "depth BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_nodes_kind", "kind IN ('initiative', 'organization', 'topic')");
                    table.CheckConstraint("ck_nodes_root", "(parent_id IS NULL) = (depth = 0)");
                    table.ForeignKey(
                        name: "fk_nodes_nodes_parent_id",
                        column: x => x.parent_id,
                        principalTable: "nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting_nodes",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    node_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_nodes", x => new { x.meeting_id, x.node_id, x.role });
                    table.CheckConstraint("ck_meeting_nodes_role", "role IN ('about', 'counterpart', 'work_of')");
                    table.ForeignKey(
                        name: "fk_meeting_nodes_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_nodes_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<string>(
                name: "context",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "template_id",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "node_id",
                table: "terminology_corrections",
                type: "TEXT",
                nullable: true);

            // Every row that exists attended: the role is new and nothing has been asked yet.
            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "meeting_participants",
                type: "TEXT",
                nullable: false,
                defaultValue: "attended",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nodes_name",
                table: "nodes",
                column: "name",
                unique: true,
                filter: "parent_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_parent_id_name",
                table: "nodes",
                columns: new[] { "parent_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_templates_name",
                table: "templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_nodes_node_id",
                table: "meeting_nodes",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_meetings_template_id",
                table: "meetings",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_id",
                table: "people",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminology_corrections_node_id",
                table: "terminology_corrections",
                column: "node_id");

            migrationBuilder.AddForeignKey(
                name: "fk_meetings_templates_template_id",
                table: "meetings",
                column: "template_id",
                principalTable: "templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_people_nodes_organization_id",
                table: "people",
                column: "organization_id",
                principalTable: "nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_terminology_corrections_nodes_node_id",
                table: "terminology_corrections",
                column: "node_id",
                principalTable: "nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_meetings_templates_template_id", table: "meetings");
            migrationBuilder.DropForeignKey(name: "fk_people_nodes_organization_id", table: "people");
            migrationBuilder.DropForeignKey(
                name: "fk_terminology_corrections_nodes_node_id",
                table: "terminology_corrections");

            migrationBuilder.DropTable(name: "meeting_nodes");
            migrationBuilder.DropTable(name: "templates");
            migrationBuilder.DropTable(name: "nodes");

            migrationBuilder.DropIndex(name: "ix_meetings_template_id", table: "meetings");
            migrationBuilder.DropIndex(name: "ix_people_organization_id", table: "people");
            migrationBuilder.DropIndex(
                name: "ix_terminology_corrections_node_id",
                table: "terminology_corrections");

            migrationBuilder.DropColumn(name: "context", table: "meetings");
            migrationBuilder.DropColumn(name: "template_id", table: "meetings");
            migrationBuilder.DropColumn(name: "organization_id", table: "people");
            migrationBuilder.DropColumn(name: "node_id", table: "terminology_corrections");

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "meeting_participants",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

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

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "terminology_corrections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(name: "ix_companies_name", table: "companies", column: "name", unique: true);
            migrationBuilder.CreateIndex(name: "ix_projects_company_id", table: "projects", column: "company_id");
            migrationBuilder.CreateIndex(name: "ix_projects_name", table: "projects", column: "name", unique: true);
            migrationBuilder.CreateIndex(name: "ix_meetings_project_id", table: "meetings", column: "project_id");
            migrationBuilder.CreateIndex(name: "ix_people_company_id", table: "people", column: "company_id");
            migrationBuilder.CreateIndex(
                name: "ix_terminology_corrections_project_id",
                table: "terminology_corrections",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "fk_meetings_projects_project_id",
                table: "meetings",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_people_companies_company_id",
                table: "people",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_terminology_corrections_projects_project_id",
                table: "terminology_corrections",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
