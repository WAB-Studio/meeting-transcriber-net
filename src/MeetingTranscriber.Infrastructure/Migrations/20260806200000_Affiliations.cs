using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Gives a person as many organizations as they have, each for as long as they were there, and
    /// lets a meeting name somebody in more than one way at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single <c>organization_id</c> on a person could not hold a contractor working for two
    /// clients, and having no period could not hold the candidate you interviewed and then hired:
    /// hiring them rewrote the interview into a meeting with your own employee. Both are the same
    /// shape as the meeting links this replaces the last of — a person relates to several things,
    /// and each relation has a span.
    /// </para>
    /// <para>
    /// <c>meeting_participants</c> becomes <c>meeting_people</c> with the role in the key, because
    /// somebody's own one to one is a meeting they attended and are the subject of. One row per
    /// person made that a choice between two true things, and the name claimed attendance the
    /// subject of a meeting held without them never had.
    /// </para>
    /// <para>
    /// <b>It drops what it replaces instead of moving it, on purpose.</b> Those are human layer
    /// rows, so the choice is not free — it is safe only because the application has never shipped
    /// and no corpus holds any. The day it writes one somebody keeps is the day this stops
    /// applying to migrations written after it.
    /// </para>
    /// </remarks>
    public partial class Affiliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_people_nodes_organization_id_organization_kind",
                table: "people");

            migrationBuilder.DropTable(
                name: "meeting_participants");

            migrationBuilder.DropIndex(
                name: "ix_people_organization_id_organization_kind",
                table: "people");

            migrationBuilder.DropCheckConstraint(
                name: "ck_people_organization",
                table: "people");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "people");

            migrationBuilder.DropColumn(
                name: "organization_kind",
                table: "people");

            migrationBuilder.CreateTable(
                name: "affiliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    organization_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    organization_kind = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<string>(type: "TEXT", nullable: true),
                    ended_at = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_affiliations", x => x.id);
                    table.CheckConstraint("ck_affiliations_organization", "organization_kind = 'organization'");
                    table.CheckConstraint("ck_affiliations_period", "started_at IS NULL OR ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "fk_affiliations_nodes_organization_id_organization_kind",
                        columns: x => new { x.organization_id, x.organization_kind },
                        principalTable: "nodes",
                        principalColumns: new[] { "id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_affiliations_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_people",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_people", x => new { x.meeting_id, x.person_id, x.role });
                    table.CheckConstraint("ck_meeting_people_role", "role IN ('attended', 'subject')");
                    table.ForeignKey(
                        name: "fk_meeting_people_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_people_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_affiliations_organization_id_organization_kind",
                table: "affiliations",
                columns: new[] { "organization_id", "organization_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_affiliations_person_id_organization_id",
                table: "affiliations",
                columns: new[] { "person_id", "organization_id" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_people_person_id",
                table: "meeting_people",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "affiliations");

            migrationBuilder.DropTable(
                name: "meeting_people");

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organization_kind",
                table: "people",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "meeting_participants",
                columns: table => new
                {
                    meeting_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    person_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_participants", x => new { x.meeting_id, x.person_id });
                    table.CheckConstraint("ck_meeting_participants_role", "role IN ('attended', 'subject')");
                    table.ForeignKey(
                        name: "fk_meeting_participants_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_participants_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_id_organization_kind",
                table: "people",
                columns: new[] { "organization_id", "organization_kind" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_people_organization",
                table: "people",
                sql: "(organization_id IS NULL) = (organization_kind IS NULL) AND (organization_kind IS NULL OR organization_kind = 'organization')");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_participants_person_id",
                table: "meeting_participants",
                column: "person_id");

            migrationBuilder.AddForeignKey(
                name: "fk_people_nodes_organization_id_organization_kind",
                table: "people",
                columns: new[] { "organization_id", "organization_kind" },
                principalTable: "nodes",
                principalColumns: new[] { "id", "kind" },
                onDelete: ReferentialAction.SetNull);
        }
    }
}
