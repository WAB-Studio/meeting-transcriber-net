using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Moves the state and the owner of an action out of <c>action_items</c>, which a rebuild
    /// deletes whole, and into <c>action_item_progress</c>, keyed on the extraction run and the
    /// position in it. It also drops the cascade from a citation to its turn, so deleting
    /// utterances stops taking the claims that cite them.
    /// </summary>
    /// <remarks>
    /// The two dropped columns are not carried across, and cannot be: the key the new table needs
    /// is the position inside the extraction, and rows written before this migration have no such
    /// position — every one of them would land on ordinal 0 and collide. This runs before any
    /// corpus exists, which is the only reason that is acceptable. Once one does, moving human
    /// state again needs the data step, not just the schema step.
    /// </remarks>
    /// <inheritdoc />
    public partial class HumanActionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_action_items_people_owner_person_id",
                table: "action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_action_items_utterances_utterance_id",
                table: "action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_decisions_utterances_utterance_id",
                table: "decisions");

            migrationBuilder.DropIndex(
                name: "ix_action_items_extraction_run_id",
                table: "action_items");

            migrationBuilder.DropIndex(
                name: "ix_action_items_owner_person_id",
                table: "action_items");

            migrationBuilder.DropIndex(
                name: "ix_action_items_state_due_date",
                table: "action_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_items_state",
                table: "action_items");

            migrationBuilder.DropColumn(
                name: "owner_person_id",
                table: "action_items");

            migrationBuilder.DropColumn(
                name: "state",
                table: "action_items");

            migrationBuilder.AddColumn<int>(
                name: "ordinal",
                table: "action_items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "action_item_progress",
                columns: table => new
                {
                    extraction_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    owner_person_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_item_progress", x => new { x.extraction_run_id, x.ordinal });
                    table.CheckConstraint("ck_action_item_progress_ordinal", "ordinal >= 0");
                    table.CheckConstraint("ck_action_item_progress_state", "state IN ('done', 'dropped', 'open')");
                    table.ForeignKey(
                        name: "fk_action_item_progress_extraction_runs_extraction_run_id",
                        column: x => x.extraction_run_id,
                        principalTable: "extraction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_action_item_progress_people_owner_person_id",
                        column: x => x.owner_person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_items_due_date",
                table: "action_items",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_extraction_run_id_ordinal",
                table: "action_items",
                columns: new[] { "extraction_run_id", "ordinal" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_items_ordinal",
                table: "action_items",
                sql: "ordinal >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_action_item_progress_owner_person_id",
                table: "action_item_progress",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_item_progress_state",
                table: "action_item_progress",
                column: "state");

            migrationBuilder.AddForeignKey(
                name: "fk_action_items_utterances_utterance_id",
                table: "action_items",
                column: "utterance_id",
                principalTable: "utterances",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_decisions_utterances_utterance_id",
                table: "decisions",
                column: "utterance_id",
                principalTable: "utterances",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_action_items_utterances_utterance_id",
                table: "action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_decisions_utterances_utterance_id",
                table: "decisions");

            migrationBuilder.DropTable(
                name: "action_item_progress");

            migrationBuilder.DropIndex(
                name: "ix_action_items_due_date",
                table: "action_items");

            migrationBuilder.DropIndex(
                name: "ix_action_items_extraction_run_id_ordinal",
                table: "action_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_items_ordinal",
                table: "action_items");

            migrationBuilder.DropColumn(
                name: "ordinal",
                table: "action_items");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_person_id",
                table: "action_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "action_items",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_extraction_run_id",
                table: "action_items",
                column: "extraction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_owner_person_id",
                table: "action_items",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_state_due_date",
                table: "action_items",
                columns: new[] { "state", "due_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_items_state",
                table: "action_items",
                sql: "state IN ('done', 'dropped', 'open')");

            migrationBuilder.AddForeignKey(
                name: "fk_action_items_people_owner_person_id",
                table: "action_items",
                column: "owner_person_id",
                principalTable: "people",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_action_items_utterances_utterance_id",
                table: "action_items",
                column: "utterance_id",
                principalTable: "utterances",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_decisions_utterances_utterance_id",
                table: "decisions",
                column: "utterance_id",
                principalTable: "utterances",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
