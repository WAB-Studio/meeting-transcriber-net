using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Hand written, for the reason 20260805204749_FullTextSearch gives: EF models tables, not FTS5
    /// virtual tables or triggers. Six more external content indexes, so the text still lives in its
    /// own table and dropping any of them loses nothing.
    ///
    /// Three of these index tables that are sources rather than derivatives — meetings, nodes and
    /// people. That does not make the index a source: an external content index holds no copy of the
    /// text, so it is rebuildable whatever it indexes, and CorpusIntegrity.SearchIndexes is the list
    /// that says so. docs/corpus.md carries the same sentence.
    ///
    /// None of the six content tables has an INTEGER PRIMARY KEY, so SQLite is free to renumber
    /// their rowids during a VACUUM and search would then answer with the wrong rows and report
    /// nothing. That is why CorpusIntegrity.Compact vacuums and rebuilds, and why every index added
    /// here joins CorpusIntegrity.SearchIndexes in the same change.
    ///
    /// Each index is seeded with a 'rebuild' and that is not belt and braces. An external content
    /// index starts empty and its triggers only speak for what happens next, so without the seed
    /// every meeting, node, person and accepted claim already in the corpus would be unfindable —
    /// and CorpusIntegrity.Check would report the corpus unsound, because an empty index over a
    /// table with rows in it is exactly the disagreement its integrity-check compares for. The FTS
    /// migration above needs no seed because 20260805204725_Initial creates its tables in the same
    /// pass and there is never a row before the trigger. This one runs against corpora that already
    /// exist, over the tables that fill up first, so it does.
    /// </summary>
    /// <inheritdoc />
    public partial class EverythingSearchPromises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE meetings_fts USING fts5 (
                    title,
                    context,
                    content = 'meetings',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE nodes_fts USING fts5 (
                    name,
                    content = 'nodes',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE people_fts USING fts5 (
                    display_name,
                    content = 'people',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE decisions_fts USING fts5 (
                    statement,
                    content = 'decisions',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE action_items_fts USING fts5 (
                    statement,
                    content = 'action_items',
                    content_rowid = 'rowid'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE open_questions_fts USING fts5 (
                    question,
                    content = 'open_questions',
                    content_rowid = 'rowid'
                );
                """);

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

            migrationBuilder.Sql("""
                CREATE TRIGGER nodes_fts_after_insert AFTER INSERT ON nodes BEGIN
                    INSERT INTO nodes_fts (rowid, name) VALUES (new.rowid, new.name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER nodes_fts_after_delete AFTER DELETE ON nodes BEGIN
                    INSERT INTO nodes_fts (nodes_fts, rowid, name) VALUES ('delete', old.rowid, old.name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER nodes_fts_after_update AFTER UPDATE ON nodes BEGIN
                    INSERT INTO nodes_fts (nodes_fts, rowid, name) VALUES ('delete', old.rowid, old.name);
                    INSERT INTO nodes_fts (rowid, name) VALUES (new.rowid, new.name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER people_fts_after_insert AFTER INSERT ON people BEGIN
                    INSERT INTO people_fts (rowid, display_name) VALUES (new.rowid, new.display_name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER people_fts_after_delete AFTER DELETE ON people BEGIN
                    INSERT INTO people_fts (people_fts, rowid, display_name) VALUES ('delete', old.rowid, old.display_name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER people_fts_after_update AFTER UPDATE ON people BEGIN
                    INSERT INTO people_fts (people_fts, rowid, display_name) VALUES ('delete', old.rowid, old.display_name);
                    INSERT INTO people_fts (rowid, display_name) VALUES (new.rowid, new.display_name);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER decisions_fts_after_insert AFTER INSERT ON decisions BEGIN
                    INSERT INTO decisions_fts (rowid, statement) VALUES (new.rowid, new.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER decisions_fts_after_delete AFTER DELETE ON decisions BEGIN
                    INSERT INTO decisions_fts (decisions_fts, rowid, statement) VALUES ('delete', old.rowid, old.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER decisions_fts_after_update AFTER UPDATE ON decisions BEGIN
                    INSERT INTO decisions_fts (decisions_fts, rowid, statement) VALUES ('delete', old.rowid, old.statement);
                    INSERT INTO decisions_fts (rowid, statement) VALUES (new.rowid, new.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER action_items_fts_after_insert AFTER INSERT ON action_items BEGIN
                    INSERT INTO action_items_fts (rowid, statement) VALUES (new.rowid, new.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER action_items_fts_after_delete AFTER DELETE ON action_items BEGIN
                    INSERT INTO action_items_fts (action_items_fts, rowid, statement) VALUES ('delete', old.rowid, old.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER action_items_fts_after_update AFTER UPDATE ON action_items BEGIN
                    INSERT INTO action_items_fts (action_items_fts, rowid, statement) VALUES ('delete', old.rowid, old.statement);
                    INSERT INTO action_items_fts (rowid, statement) VALUES (new.rowid, new.statement);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER open_questions_fts_after_insert AFTER INSERT ON open_questions BEGIN
                    INSERT INTO open_questions_fts (rowid, question) VALUES (new.rowid, new.question);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER open_questions_fts_after_delete AFTER DELETE ON open_questions BEGIN
                    INSERT INTO open_questions_fts (open_questions_fts, rowid, question) VALUES ('delete', old.rowid, old.question);
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER open_questions_fts_after_update AFTER UPDATE ON open_questions BEGIN
                    INSERT INTO open_questions_fts (open_questions_fts, rowid, question) VALUES ('delete', old.rowid, old.question);
                    INSERT INTO open_questions_fts (rowid, question) VALUES (new.rowid, new.question);
                END;
                """);

            // The indexes exist and are empty; the triggers above only speak for what happens
            // next. This is what puts the rows that were already there into them.
            foreach (var index in new[]
                { "meetings_fts", "nodes_fts", "people_fts", "decisions_fts", "action_items_fts", "open_questions_fts" })
            {
                migrationBuilder.Sql($"INSERT INTO {index} ({index}) VALUES ('rebuild');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
                { "meetings", "nodes", "people", "decisions", "action_items", "open_questions" })
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
