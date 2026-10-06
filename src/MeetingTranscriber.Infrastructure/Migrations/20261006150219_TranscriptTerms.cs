using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Hand written, for the reason <c>FullTextSearch</c> gives: EF models tables, not FTS5 virtual
    /// tables, so nothing here reaches the model snapshot. This one is a view of an index and holds
    /// nothing: <c>fts5vocab</c> reads the words <c>utterances_fts</c> already stores, one row per
    /// word (<c>term</c>, <c>doc</c>, <c>cnt</c>). It is derived, so a rebuild of the index is a rebuild
    /// of it, and it is what lets search ask which words the transcripts hold without reading a turn.
    /// </summary>
    public partial class TranscriptTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE VIRTUAL TABLE utterances_fts_terms USING fts5vocab (utterances_fts, 'row');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS utterances_fts_terms;");
        }
    }
}
