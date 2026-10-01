using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeptClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "template_nodes",
                columns: table => new
                {
                    template_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    node_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_nodes", x => new { x.template_id, x.node_id, x.role });
                    table.CheckConstraint("ck_template_nodes_role", "role IN ('about', 'counterpart', 'work_of')");
                    table.ForeignKey(
                        name: "fk_template_nodes_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_nodes_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_people",
                columns: table => new
                {
                    template_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_people", x => new { x.template_id, x.person_id, x.role });
                    table.CheckConstraint("ck_template_people_role", "role IN ('attended', 'subject')");
                    table.ForeignKey(
                        name: "fk_template_people_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_people_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_template_nodes_node_id",
                table: "template_nodes",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_people_person_id",
                table: "template_people",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_nodes");

            migrationBuilder.DropTable(
                name: "template_people");
        }
    }
}
