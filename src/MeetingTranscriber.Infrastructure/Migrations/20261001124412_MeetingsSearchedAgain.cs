using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Puts the three <c>meetings</c> search triggers back after the rebuild the migration before it
    /// forced. It is a migration of its own because EF emits raw SQL ahead of a rebuild it still has
    /// pending, so triggers written into that one would sit on the table about to be dropped. The
    /// <c>'rebuild'</c> is not belt and braces: <c>meetings_fts</c> is keyed on <c>rowid</c> and the
    /// rebuild gave every meeting a new one, so without it search answers with the wrong meetings.
    /// </summary>
    public partial class MeetingsSearchedAgain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER meetings_fts_after_insert AFTER INSERT ON meetings BEGIN
                    INSERT INTO meetings_fts (rowid, title, context) VALUES (new.rowid, new.title, new.context);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER meetings_fts_after_delete AFTER DELETE ON meetings BEGIN
                    INSERT INTO meetings_fts (meetings_fts, rowid, title, context) VALUES ('delete', old.rowid, old.title, old.context);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER meetings_fts_after_update AFTER UPDATE ON meetings BEGIN
                    INSERT INTO meetings_fts (meetings_fts, rowid, title, context) VALUES ('delete', old.rowid, old.title, old.context);
                    INSERT INTO meetings_fts (rowid, title, context) VALUES (new.rowid, new.title, new.context);
                END;
                """);

            migrationBuilder.Sql("INSERT INTO meetings_fts (meetings_fts) VALUES ('rebuild');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS meetings_fts_after_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS meetings_fts_after_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS meetings_fts_after_update;");
        }
    }
}
