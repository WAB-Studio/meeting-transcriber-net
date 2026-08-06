using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Turns the meeting and the position of a turn into something the rest of the corpus can
    /// point at. SQLite only accepts a foreign key onto columns that are collectively unique, and
    /// a unique index is not enough for EF: it wants an alternate key, which lands as a UNIQUE
    /// table constraint. Nothing references it yet — that is <c>CitationAnchor</c>, next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is its own migration rather than the first half of that one because SQLite cannot add a
    /// constraint in place. EF rebuilds the whole table, and when several tables are rebuilt at
    /// once it does them in alphabetical order — <c>action_items</c> long before
    /// <c>utterances</c>. Every corpus opens with foreign keys on, so a child table rebuilt with
    /// a key onto a parent that has not gained its constraint yet fails on the spot with
    /// "foreign key mismatch". Two migrations is what puts the parent first.
    /// </para>
    /// <para>
    /// Rebuilding <c>utterances</c> drops it, so its FTS5 triggers go with it — they belong to
    /// the table, not to the schema EF tracks — and the rows come back under new rowids, which is
    /// what <c>utterances_fts</c> is keyed on. <c>CitationAnchor</c> puts both back, because EF
    /// emits raw SQL before a pending rebuild and there is no way to run it afterwards from here.
    /// Rolling this migration back has the same cost and no migration left to pay it: reapplying
    /// is the way forward, and the repair is the four statements <c>CitationAnchor</c> runs.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class UtteranceAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_utterances_meeting_id_ordinal",
                table: "utterances");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_utterances_meeting_id_ordinal",
                table: "utterances",
                columns: new[] { "meeting_id", "ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ak_utterances_meeting_id_ordinal",
                table: "utterances");

            migrationBuilder.CreateIndex(
                name: "ix_utterances_meeting_id_ordinal",
                table: "utterances",
                columns: new[] { "meeting_id", "ordinal" },
                unique: true);
        }
    }
}
