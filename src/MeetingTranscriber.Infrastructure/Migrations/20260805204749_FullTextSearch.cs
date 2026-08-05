using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Hand written, and it has to be. EF models tables, not FTS5 virtual tables or triggers, so
    /// nothing here came from the model and nothing here reaches the model snapshot. The
    /// practical consequence: rename a column indexed below and EF will happily generate the
    /// migration for the table without noticing that these triggers went stale.
    ///
    /// Both indexes are external content — the text stays in its own table and FTS5 holds only
    /// the index — so dropping and rebuilding either one loses nothing.
    ///
    /// External content keys on rowid, and neither indexed table has an INTEGER PRIMARY KEY, so
    /// SQLite is free to renumber those rowids during a VACUUM. Search would then answer with
    /// the wrong rows and never report an error. Anything that vacuums the corpus has to follow
    /// it with INSERT INTO utterances_fts (utterances_fts) VALUES ('rebuild'), and the same for
    /// summaries_fts.
    /// </summary>
    /// <inheritdoc />
    public partial class FullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE utterances_fts USING fts5 (
                    text,
                    content = 'utterances',
                    content_rowid = 'rowid'
                );
                """);

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

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE summaries_fts USING fts5 (
                    abstract,
                    body,
                    content = 'summaries',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER summaries_fts_after_insert AFTER INSERT ON summaries BEGIN
                    INSERT INTO summaries_fts (rowid, abstract, body) VALUES (new.rowid, new.abstract, new.body);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER summaries_fts_after_delete AFTER DELETE ON summaries BEGIN
                    INSERT INTO summaries_fts (summaries_fts, rowid, abstract, body) VALUES ('delete', old.rowid, old.abstract, old.body);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER summaries_fts_after_update AFTER UPDATE ON summaries BEGIN
                    INSERT INTO summaries_fts (summaries_fts, rowid, abstract, body) VALUES ('delete', old.rowid, old.abstract, old.body);
                    INSERT INTO summaries_fts (rowid, abstract, body) VALUES (new.rowid, new.abstract, new.body);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "utterances", "summaries" })
            {
                foreach (var moment in new[] { "insert", "delete", "update" })
                {
                    migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {table}_fts_after_{moment};");
                }

                migrationBuilder.Sql($"DROP TABLE IF EXISTS {table}_fts;");
            }
        }
    }
}
