using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Turns three things the tree only asserted in comments into keys the database refuses to
    /// break.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node's copy of its parent — the id, the class and the depth — becomes one foreign key
    /// against the parent's own columns, which is what makes the copy truthful. With the CHECKs
    /// added in the migration before this one on top of it, a child at a depth that is not its
    /// parent's plus one, a topic standing as a root and an organization hanging off a topic are
    /// all refused. A person's organization is the same technique with one column: the class
    /// travels with the id, so an employer that is a project has nowhere to be written.
    /// </para>
    /// <para>
    /// Separate from that migration because SQLite rebuilds the whole table for either change, and
    /// a rebuild that added the unique constraints and the key at once would have the new table
    /// referencing the old one, which does not carry them. It fails with `foreign key mismatch`.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class TreeKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nodes_nodes_parent_id",
                table: "nodes");

            migrationBuilder.DropForeignKey(
                name: "fk_people_nodes_organization_id",
                table: "people");

            migrationBuilder.DropIndex(
                name: "ix_people_organization_id",
                table: "people");

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_id_organization_kind",
                table: "people",
                columns: new[] { "organization_id", "organization_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_nodes_parent_id_parent_kind_parent_depth",
                table: "nodes",
                columns: new[] { "parent_id", "parent_kind", "parent_depth" });

            migrationBuilder.AddForeignKey(
                name: "fk_nodes_nodes_parent_id_parent_kind_parent_depth",
                table: "nodes",
                columns: new[] { "parent_id", "parent_kind", "parent_depth" },
                principalTable: "nodes",
                principalColumns: new[] { "id", "kind", "depth" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_people_nodes_organization_id_organization_kind",
                table: "people",
                columns: new[] { "organization_id", "organization_kind" },
                principalTable: "nodes",
                principalColumns: new[] { "id", "kind" },
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nodes_nodes_parent_id_parent_kind_parent_depth",
                table: "nodes");

            migrationBuilder.DropForeignKey(
                name: "fk_people_nodes_organization_id_organization_kind",
                table: "people");

            migrationBuilder.DropIndex(
                name: "ix_people_organization_id_organization_kind",
                table: "people");

            migrationBuilder.DropIndex(
                name: "ix_nodes_parent_id_parent_kind_parent_depth",
                table: "nodes");

            migrationBuilder.CreateIndex(
                name: "ix_people_organization_id",
                table: "people",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_nodes_nodes_parent_id",
                table: "nodes",
                column: "parent_id",
                principalTable: "nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_people_nodes_organization_id",
                table: "people",
                column: "organization_id",
                principalTable: "nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
