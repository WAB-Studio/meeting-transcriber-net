using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Moves what a citation anchors on from a turn's id to the meeting and the turn's position
    /// in it. The ids are minted by the projection, so a rebuild deletes them and hands out new
    /// ones, and every decision and action projected again from the same extraction pointed at a
    /// turn that no longer existed. The pair is what the projection reproduces, so the reference
    /// survives the rebuild.
    /// </summary>
    /// <remarks>
    /// The dropped <c>utterance_id</c> is not carried across, and cannot be: the position it
    /// would have to become is not in the row, only in the turn it used to point at, and that
    /// turn's id is the one thing a rebuild does not preserve. This runs before any corpus
    /// exists, which is the only reason that is acceptable — every row would land on ordinal 0
    /// and cite the first turn of its meeting. Once a corpus does exist, moving the anchor again
    /// needs the data step, not just the schema step.
    /// </remarks>
    /// <inheritdoc />
    public partial class CitationAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RestoreSearch(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_action_items_utterances_utterance_id",
                table: "action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_decisions_utterances_utterance_id",
                table: "decisions");

            migrationBuilder.DropIndex(
                name: "ix_decisions_utterance_id",
                table: "decisions");

            migrationBuilder.DropIndex(
                name: "ix_action_items_utterance_id",
                table: "action_items");

            migrationBuilder.DropColumn(
                name: "utterance_id",
                table: "decisions");

            migrationBuilder.DropColumn(
                name: "utterance_id",
                table: "action_items");

            migrationBuilder.AddColumn<int>(
                name: "utterance_ordinal",
                table: "decisions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "utterance_ordinal",
                table: "action_items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_decisions_meeting_id_utterance_ordinal",
                table: "decisions",
                columns: new[] { "meeting_id", "utterance_ordinal" });

            migrationBuilder.CreateIndex(
                name: "ix_action_items_meeting_id_utterance_ordinal",
                table: "action_items",
                columns: new[] { "meeting_id", "utterance_ordinal" });

            migrationBuilder.AddForeignKey(
                name: "fk_action_items_utterances_meeting_id_utterance_ordinal",
                table: "action_items",
                columns: new[] { "meeting_id", "utterance_ordinal" },
                principalTable: "utterances",
                principalColumns: new[] { "meeting_id", "ordinal" });

            migrationBuilder.AddForeignKey(
                name: "fk_decisions_utterances_meeting_id_utterance_ordinal",
                table: "decisions",
                columns: new[] { "meeting_id", "utterance_ordinal" },
                principalTable: "utterances",
                principalColumns: new[] { "meeting_id", "ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_action_items_utterances_meeting_id_utterance_ordinal",
                table: "action_items");

            migrationBuilder.DropForeignKey(
                name: "fk_decisions_utterances_meeting_id_utterance_ordinal",
                table: "decisions");

            migrationBuilder.DropIndex(
                name: "ix_decisions_meeting_id_utterance_ordinal",
                table: "decisions");

            migrationBuilder.DropIndex(
                name: "ix_action_items_meeting_id_utterance_ordinal",
                table: "action_items");

            migrationBuilder.DropColumn(
                name: "utterance_ordinal",
                table: "decisions");

            migrationBuilder.DropColumn(
                name: "utterance_ordinal",
                table: "action_items");

            migrationBuilder.AddColumn<Guid>(
                name: "utterance_id",
                table: "decisions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "utterance_id",
                table: "action_items",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_decisions_utterance_id",
                table: "decisions",
                column: "utterance_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_items_utterance_id",
                table: "action_items",
                column: "utterance_id");

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

        /// <summary>
        /// Puts back what rebuilding <c>utterances</c> in <c>UtteranceAnchor</c> took: its FTS5
        /// triggers, which belong to the table and were dropped with it, and the index itself,
        /// which is external content keyed on rowids the rebuild renumbered. It lives here rather
        /// than there because EF emits raw SQL before a table rebuild it still has pending, so a
        /// trigger created in that migration would be created on the table about to be dropped.
        /// </summary>
        /// <remarks>
        /// <see cref="Down"/> does not undo it. These triggers are the state
        /// <c>FullTextSearch</c> established and this migration never owned; dropping them on the
        /// way back would leave search silently not indexing.
        /// </remarks>
        private static void RestoreSearch(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER utterances_fts_after_insert AFTER INSERT ON utterances BEGIN
                    INSERT INTO utterances_fts (rowid, text) VALUES (new.rowid, new.text);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER utterances_fts_after_delete AFTER DELETE ON utterances BEGIN
                    INSERT INTO utterances_fts (utterances_fts, rowid, text) VALUES ('delete', old.rowid, old.text);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER utterances_fts_after_update AFTER UPDATE ON utterances BEGIN
                    INSERT INTO utterances_fts (utterances_fts, rowid, text) VALUES ('delete', old.rowid, old.text);
                    INSERT INTO utterances_fts (rowid, text) VALUES (new.rowid, new.text);
                END;
                """);

            migrationBuilder.Sql("INSERT INTO utterances_fts (utterances_fts) VALUES ('rebuild');");
        }
    }
}
